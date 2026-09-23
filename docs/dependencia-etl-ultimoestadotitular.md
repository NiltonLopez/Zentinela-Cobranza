# Dependencia externa: el ETL que alimenta `UltimoEstadoTitular` e `HistoricoEstadoTitular`

**Diagnóstico:** 2026-09-23
**Alcance:** este ETL **no es código de Zentinela** — es un paquete SSIS en el catálogo del
servidor. Se documenta acá porque varios módulos del producto dependen de él y su falla se
manifiesta *dentro* de Zentinela, donde nadie la relaciona con su causa.

---

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

### Causa: el paquete corre en 32 bits y se topa con el techo de 2 GB

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
> milisegundos**, o sea iteró **cero veces** — no había archivos Excel. Los 31 s fueron solo el
> `INSERT` de lo que ya estaba en staging. Esa corrida nunca tocó el origen Excel.
>
> Que 64 bits sirve se verificó **por otro lado**: `Microsoft.ACE.OLEDB.12.0` **sí está registrado
> en 64 bits** en ese servidor (lo enumera `sp_enum_oledb_providers` desde SQL Server, que es
> 64 bits). Ese era el único prerequisito.

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

**1. El paquete deja la tabla principal vacía cuando falla.** Es lo más grave del incidente: un
error técnico se convirtió en pérdida total de datos operativos. El patrón *vaciar → cargar →
insertar* no tiene red. Debería cargar a staging, validar, y **solo entonces** reemplazar la tabla
final — el mismo patrón que ya usa el Cargue de Reportes de Zentinela (`#TempReporte` + `MERGE`,
ADR-011). Requiere editar el `.dtsx` en SSDT.

**2. Contraseña de `sa` en texto plano.** El comando del paso del Agente lleva la contraseña como
parámetro literal, guardada en `msdb.dbo.sysjobsteps.command` y legible por cualquiera con acceso a
`msdb`. Debería ser un **parámetro sensible del proyecto SSIS** o una credencial del Agente, y la
contraseña rotarse al sacarla de ahí.

**3. Falla en silencio para el negocio.** Zentinela no avisa cuando estas tablas están vacías o
rancias. Mejora candidata: un chequeo de frescura visible en el Comparador y el Calificador
("datos al {fecha}; última carga hace N días") en vez de mostrar cero resultados sin explicación.

## Cómo diagnosticar esto la próxima vez

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
