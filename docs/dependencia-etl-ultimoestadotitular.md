# Dependencia externa: el ETL que alimenta `UltimoEstadoTitular` e `HistoricoEstadoTitular`

**Diagnóstico:** 2026-09-23
**Alcance:** este ETL **no es código de Zentinela** — es un paquete SSIS en el catálogo del
servidor. Se documenta acá porque varios módulos del producto dependen de él y su falla se
manifiesta *dentro* de Zentinela, donde nadie la relaciona con su causa.

---

## Qué hace el paquete, exactamente

Verificado el 2026-09-23 leyendo el `.dtsx`, que está en
`\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\ETL_UltimoEstadoTitular\`
(el proyecto fuente vive en el recurso compartido, no hace falta extraerlo del catálogo).

| # | Tarea | Qué hace realmente |
|---|---|---|
| 1 | Tarea Ejecutar SQL | `TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular` |
| 2 | Tarea Ejecutar proceso | `py.exe Conversor_CargaUltimoP.py` |
| 3 | Contenedor Foreach Por Cartera | Carga cada Excel al staging |
| 4 | Tarea Ejecutar SQL → Insertar | `EXEC sp_Insertar_UltimoEstadoTitular` + `EXEC sp_Insertar_HistoricoEstadoTitular` |
| 5 | Tarea Ejecutar proceso | `py.exe EliminarExcel.py` |

**El verdadero origen no son los Excel.** Son 21 CSV en
`\\10.0.0.89\bases\SISTEMAS\Apps\ReporteClientes\*.csv` (~800 MB), que el conversor de Python
transforma a `.xlsx` partiendo en dos los que son muy grandes. Los Excel son intermedios y el paso 5
los borra. Por eso una corrida "sin archivos" no significa que falte el insumo: significa que el
conversor no produjo nada.

Dos consecuencias prácticas:

- **El paso 1 vacía el staging al arrancar.** O sea que dejar filas ahí entre corridas no contamina
  la siguiente. (Conviene igual no dejarlas, pero no es peligroso.)
- **El paso 4 es literalmente lo que reescribió ADR-038.** Los dos procedimientos nuevos son lo que
  el paquete ejecuta, sin intermediarios.

## Qué depende de estas tablas

| Tabla | Quién la consume en Zentinela |
|---|---|
| `UltimoEstadoTitular` | **Comparador de Carteras** (es su punto de partida), Calificador de Cartera |
| `HistoricoEstadoTitular` | Comparador (mejor teléfono por titular), Calificador |

Si el ETL falla, estos módulos no dan error: dan **resultados vacíos o desactualizados**, que es
peor porque parece que funcionan.

> Nota: Zentinela **también** escribe estas tablas por su cuenta, desde *Administración → Cargue de
> Reportes* (`CargueReporteService`, `#TempReporte` + `MERGE`, ver ADR-011). Son **dos vías** que
> alimentan las mismas tablas. Al diagnosticar datos raros, hay que considerar ambas.

## El incidente del 2026-09-23

Estado encontrado:

| Tabla | Filas | Última actualización | Atraso |
|---|---|---|---|
| `UltimoEstadoTitular` | **0** | — | vacía |
| `HistoricoEstadoTitular` | 3.319.333 | 2026-09-11 | 12 días |

El Comparador de Carteras estaba operativamente inservible y nadie lo había atribuido al ETL.

> ⚠️ **Corrección del 2026-09-23 (misma fecha, más tarde).** La primera versión de este documento
> culpaba al `/X86` de la pérdida de datos. **Estaba mal.** El `/X86` explica por qué el ETL viene
> fallando, pero una corrida que revienta por memoria ni siquiera llega a tocar la tabla final.
> Los datos los destruyó una corrida **exitosa**. Ver la sección siguiente y ADR-038.

### Causa de la pérdida de datos: una corrida exitosa sin archivos

Detalle por tarea de la ejecución 11218 (`SSISDB.catalog.executable_statistics`):

| Tarea | Duración |
|---|---|
| Tarea Ejecutar SQL | 1,4 s |
| Tarea Ejecutar proceso | 2,2 s |
| Contenedor Foreach Por Cartera | **0,047 s** |
| Tarea Ejecutar SQL → Insertar en tablas principales | **19,5 s** |
| Tarea Ejecutar proceso - Eliminar Excel | 0,1 s |

El Foreach iteró **cero veces**: no había ningún Excel. Y el paso siguiente corrió igual, llamando
a un `sp_Insertar_UltimoEstadoTitular` que empezaba con `TRUNCATE TABLE` incondicional, sin
transacción y sin mirar el staging. Truncó la tabla entera — del orden de **2,3 millones de filas**,
a juzgar por el snapshot del 11-sep en el histórico —, insertó cero, y el paquete terminó con
`execution_result = 0`: **éxito**.

El modo de falla peligroso no era el error, era el **éxito falso**. Corregido en ADR-038
(reemplazo acotado + compuertas + auditoría propia), sin tocar el `.dtsx`.

### Por qué el ETL viene fallando: 32 bits y el techo de 2 GB

Del propio error de SSIS:

```
Memoria física:  34.089.324.544 bytes (31,7 GB) — 8,7 GB LIBRES
Memoria virtual:  2.147.352.576 bytes  (2,0 GB) — 60 MB libres   ← el techo
Un búfer no pudo asignar 2.089.152 bytes
Código de error: 0x8007000E  (E_OUTOFMEMORY)
```

`2.147.352.576` son exactamente **2 GB**: el espacio de direcciones de un proceso de 32 bits. **No
falta RAM** — sobran 8,7 GB físicos. El proceso agotó su propio techo. Revienta en el componente
"Unión de todo" (entrada 3), que es semi-bloqueante y retiene búferes.

Historial de ejecuciones (`SSISDB.catalog.executions`):

| execution_id | Fecha | Estado | 32 bits | Duración | Foreach |
|---|---|---|---|---|---|
| 11190 | 08 sep | OK | sí | 2.730 s | 28:03 |
| 11198 | 11 sep | OK | sí | 1.751 s | 15:10 |
| 11212 | 17 sep | **FALLA** | sí | 1.472 s | — |
| 11216 | 18 sep | **FALLA** | sí | 785 s | — |
| 11218 | 18 sep | OK | **no** | 31 s | **00:00.047** ⚠ |
| 11225 | 21 sep | **FALLA** | sí | 941 s | — |
| 11229 | 22 sep | **FALLA** | sí | 902 s | — |
| 11231 | 22 sep | **FALLA** | sí | 706 s | — |

Patrón: el volumen creció hasta no caber en 2 GB. Las corridas que funcionaban ya tardaban 15-28
minutos peleando con la paginación.

> ⚠ **Trampa al leer esta tabla.** La ejecución 11218 aparece como OK en 64 bits y tienta a
> concluir "64 bits funciona, y 56× más rápido". **Es un falso positivo**: su Foreach duró **47
> milisegundos**, o sea iteró **cero veces** — no había archivos Excel. Esa corrida nunca tocó el
> origen Excel.
>
> Y no fue inofensiva: **es la corrida que vació la tabla.** Un "OK" en esta tabla no significa que
> el ETL haya hecho su trabajo. Antes de concluir nada sobre una ejecución, mirar la duración del
> Foreach.
>
> ⚠️ **Y ojo con el prerequisito de 64 bits: verifiqué el proveedor equivocado.** Comprobé que
> `Microsoft.ACE.OLEDB.12.0` está registrado en 64 bits y di el tema por cerrado. Pero el paquete
> **no usa el 12.0: usa `Microsoft.ACE.OLEDB.16.0`** (parámetro
> `CM.Administrador de conexiones con Excel.ConnectionString`), y `sp_enum_oledb_providers` —que
> corre dentro de SQL Server, que es de 64 bits— **solo reporta el 12.0**.
>
> O sea que **quitar `/X86` a secas rompería el paquete** con "proveedor no registrado", en vez de
> arreglarlo. Antes hay que instalar en el servidor el *Access Database Engine 2016
> redistributable de 64 bits* (o repuntar la conexión al 12.0 y probar que lee los Excel que
> genera el conversor). Eso explica, además, por qué nadie lo había quitado.

### Dónde está configurado el 32 bits

En el **modificador `/X86`** de la línea de comandos del paso del trabajo del Agente:

- `Ejecutar_ETL_UltimoEstadoTitular`, paso 1 `EjecutarPaquete`
- `EjecutarETL_ListaNegra`, paso 1 `EjecutarETL` — mismo problema

Arreglo (quita solo el modificador; el resto del comando, incluidos sus parámetros, queda intacto):

```sql
USE msdb;
DECLARE @jid uniqueidentifier, @sid int, @cmd nvarchar(max);
SELECT @jid = j.job_id, @sid = s.step_id, @cmd = s.command
FROM sysjobs j JOIN sysjobsteps s ON s.job_id = j.job_id
WHERE j.name = 'Ejecutar_ETL_UltimoEstadoTitular' AND s.command LIKE '%/X86%';

EXEC sp_update_jobstep @job_id = @jid, @step_id = @sid,
     @command = @cmd;   -- reemplazando ' /X86' por '' en @cmd
```

Reversible: volver a insertar ` /X86`.

## Problemas de fondo que el flag NO resuelve

**1. El paquete dejaba la tabla principal vacía. ✅ RESUELTO (ADR-038, 2026-09-23.)** El patrón
*vaciar → cargar → insertar* se reemplazó por *validar → reemplazar solo lo que vino → auditar*,
dentro de los stored procedures. **No hizo falta tocar el `.dtsx`**: el daño estaba en el SQL, no
en el paquete. Ver `docs/sql/migrations/2026-09-23_rediseno_etl_estado_titular.sql`.

**2. Contraseña de `sa` en texto plano.** El comando del paso del Agente lleva la contraseña como
parámetro literal, guardada en `msdb.dbo.sysjobsteps.command` y legible por cualquiera con acceso a
`msdb`. Debería ser un **parámetro sensible del proyecto SSIS** o una credencial del Agente, y la
contraseña rotarse al sacarla de ahí. **Pendiente.**

**3. Falla en silencio para el negocio. ⏳ PARCIAL.** Ya existe del lado de la base:
`EtlEstadoTitular_Ejecucion` registra cada corrida y `vw_EtlEstadoTitularEstado` resume la frescura
en una fila. **Falta consumirla en la UI** del Comparador y el Calificador ("datos al {fecha};
última carga hace N días") en vez de mostrar cero resultados sin explicación.

## Cómo diagnosticar esto la próxima vez

**Empezar por acá** (desde ADR-038 esta es la fuente de verdad, no SSISDB):

```sql
-- Una fila: cuándo se cargó bien por última vez y cómo terminó el último intento.
SELECT * FROM DBCallCenter.dbo.vw_EtlEstadoTitularEstado;

-- El detalle, con el motivo exacto de cada aborto.
SELECT TOP 20 EjecucionID, Paso, Origen, Resultado, Motivo,
       FilasStaging, CarterasStaging, FilasInsertadas, FechaInicio, FechaFin
FROM DBCallCenter.dbo.EtlEstadoTitular_Ejecucion
ORDER BY EjecucionID DESC;
```

Un `ABORTADA` no es una avería del rediseño: es el rediseño haciendo su trabajo. El `Motivo` dice
qué faltó. Antes de forzar nada, revisar por qué el staging vino incompleto.

Las consultas de abajo siguen sirviendo para el lado de SSIS:

```sql
-- 1. Frescura de los datos
SELECT 'UltimoEstadoTitular' t, COUNT(*) filas, MAX(FechaActualizacion) ult FROM DBCallCenter.dbo.UltimoEstadoTitular
UNION ALL SELECT 'Historico', COUNT(*), MAX(FechaActualizacion) FROM DBCallCenter.dbo.HistoricoEstadoTitular;

-- 2. Ejecuciones del ETL, con el flag de 32 bits
SELECT TOP 10 execution_id, status, use32bitruntime, start_time,
       DATEDIFF(SECOND, start_time, end_time) seg
FROM SSISDB.catalog.executions WHERE project_name = 'ETL_UltimoEstadoTitular'
ORDER BY execution_id DESC;

-- 3. Si una corrida dice OK sospechosamente rápido, revisar si el Foreach hizo algo
SELECT message FROM SSISDB.catalog.event_messages
WHERE operation_id = <id> AND message LIKE '%Foreach%Finalizado%';
```
