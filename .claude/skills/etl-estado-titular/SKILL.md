---
name: etl-estado-titular
description: Verifica, diagnostica y repara el ETL que alimenta UltimoEstadoTitular e HistoricoEstadoTitular, y avisa por WhatsApp cuando los datos están rancios. Úsala cuando el Comparador de Carteras o el Calificador devuelvan resultados vacíos o desactualizados, cuando el trabajo Ejecutar_ETL_UltimoEstadoTitular falle, o como chequeo programado de frescura.
---

# ETL de estado de titulares — verificación, diagnóstico y aviso

Esta skill existe porque el 18 de septiembre de 2026 el ETL **vació la tabla principal y reportó
éxito**. Durante doce días nadie lo notó: el Comparador de Carteras no daba error, daba cero
resultados. Todo lo que sigue está diseñado alrededor de esa lección — **un ETL puede estar roto
mientras dice que anda bien**, así que nunca se le cree al estado del trabajo, se le cree a los datos.

Contexto completo: [ADR-038](../../../docs/decisions/ADR-038-etl-estado-titular-reemplazo-acotado-y-compuertas.md)
y [docs/dependencia-etl-ultimoestadotitular.md](../../../docs/dependencia-etl-ultimoestadotitular.md).

---

## Qué mira, y en qué orden

Siempre en este orden. Cada paso solo tiene sentido si el anterior salió limpio.

```
1. Frescura de los datos      ← la verdad está acá, no en SSIS
2. Auditoría del rediseño     ← por qué abortó, si abortó
3. Estado del trabajo y SSIS  ← solo para entender el cómo
4. Insumos en disco           ← si no hay CSV, no hay nada que cargar
5. Salud del servidor         ← descarta causa externa
```

## Configuración

**Credenciales de base.** Nunca escribirlas ni imprimirlas. Salen de
`src/Zentinela.API/appsettings.Development.json` (ignorado por git; el `appsettings.json`
versionado solo tiene el placeholder `__DB_PASSWORD__`):

```bash
cd /c/AgenticWorkspace/Zentinela
export SQLCMDPASSWORD=$(grep -o '"CallCenterConnection": "[^"]*"' src/Zentinela.API/appsettings.Development.json | grep -o 'Password=[^;]*' | sed 's/Password=//')
SQLCMD="/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd"
```

`SQLCMDPASSWORD` evita que la contraseña aparezca en la línea de comandos, donde quedaría
registrada. **Nunca usar `-P`.**

**EvolutionAPI.** En `.claude/skills/etl-estado-titular/config.local.json`, que está ignorado por
git. Si falta, copiar `config.ejemplo.json` y completarlo. Sin ese archivo la skill funciona igual:
diagnostica y no avisa, diciéndolo.

## Umbrales

| Estado | Condición | Qué hacer |
|---|---|---|
| **Al día** | `HorasDesdeCargaOk` ≤ 26 | Nada |
| **Atención** | 26 < horas ≤ 42 | Diagnosticar. Avisar solo si además el último intento falló |
| **Rancio** | **horas > 42** | Diagnosticar y **avisar por WhatsApp siempre** |
| **Crítico** | `UltimoEstadoTitular` con 0 filas, o < 50 % de la última carga buena | Avisar de inmediato, marcar como incidente |

> El ETL corre una vez al día, así que 26 horas es "se saltó una corrida" y 42 es "ya van dos".
> El pedido original era 3,5 horas; ese umbral sirve para **vigilar una corrida en progreso**
> (arranca ~15:10 y nunca tardó más de 46 min), no para la frescura diaria. Los dos casos están
> contemplados: `--modo corrida` usa 3,5 h, `--modo diario` usa la tabla de arriba.

---

## Paso 1 — Frescura

```sql
SELECT * FROM DBCallCenter.dbo.vw_EtlEstadoTitularEstado;

SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras,
       MAX(FechaActualizacion) AS dato_mas_nuevo
FROM DBCallCenter.dbo.UltimoEstadoTitular;
```

⚠️ La vista devuelve **cero filas** si nunca corrió nada desde el rediseño. Eso no es un fallo:
es "todavía no hay historial". Tratarlo aparte de "está rancio".

Si `filas = 0` → **incidente crítico**, saltar directo al Paso 5 y avisar.

## Paso 2 — Auditoría

```sql
SELECT TOP 10 EjecucionID, Paso, Origen, Resultado, Motivo,
       FilasStaging, CarterasStaging, FilasBorradas, FilasInsertadas, FilasDespues,
       FechaInicio, FechaFin
FROM DBCallCenter.dbo.EtlEstadoTitular_Ejecucion
ORDER BY EjecucionID DESC;
```

**`ABORTADA` no es una avería: es el rediseño funcionando.** Significa que el ETL trajo datos
insuficientes y se negó a publicarlos. El `Motivo` dice exactamente qué faltó. La tabla de datos
está intacta. La urgencia es media, no alta.

`ERROR` sí es una avería del procedimiento. `EN CURSO` de hace más de una hora es una corrida
colgada: revisar bloqueos antes de concluir nada.

## Paso 3 — Trabajo y paquete

```sql
SELECT TOP 5 execution_id, status, use32bitruntime,
       DATEDIFF(SECOND, start_time, ISNULL(end_time, SYSDATETIMEOFFSET())) AS segundos, start_time
FROM SSISDB.catalog.executions
WHERE project_name = 'ETL_UltimoEstadoTitular' ORDER BY execution_id DESC;
```

`status`: 2 = corriendo · 3 = cancelado · 4 = falló · 7 = correcto.

⚠️ **Un `7` no prueba nada.** Mirar siempre la duración del Foreach:

```sql
SELECT e.executable_name, es.execution_duration/1000 AS seg, es.execution_result
FROM SSISDB.catalog.executable_statistics es
JOIN SSISDB.catalog.executables e
  ON e.executable_id = es.executable_id AND e.execution_id = es.execution_id
WHERE es.execution_id = <id> ORDER BY es.start_time;
```

Si `Contenedor Foreach Por Cartera` duró menos de 10 segundos, **iteró cero veces y la corrida no
hizo nada**, sin importar que diga correcto. Fue exactamente el caso del 18-sep.

Errores del paquete:

```sql
SELECT TOP 10 message_time, LEFT(message, 400)
FROM SSISDB.catalog.event_messages
WHERE operation_id = <id> AND message_type IN (120, 130)
ORDER BY message_time DESC;
```

## Paso 4 — Insumos

El origen real **no son los Excel** (son intermedios que el paquete genera y borra), sino los CSV:

```bash
find "//10.0.0.89/bases/SISTEMAS/Apps/ReporteClientes" -maxdepth 1 -iname "*.csv" \
     -printf "%-55f %12s bytes  %TY-%Tm-%Td %TH:%TM\n" | sort
```

Referencia: **21 archivos, ~800 MB**, regenerados a diario. Si están viejos o faltan, el problema
es de quien los produce y **el ETL no tiene nada que hacer**. Esto se verifica antes de culpar al
ETL, no después.

### El chequeo que más veces va a salvar el día

Cada CSV tiene que tener una cartera registrada en `dbo.Cartera.NombreArchivo`. El paquete traduce
nombre de archivo → `CarteraID` con un `Lookup` **configurado para fallar si no encuentra**, así
que **un solo CSV sin cartera registrada mata toda la carga**, incluidas las 20 que sí estaban bien.

```bash
find "//10.0.0.89/bases/SISTEMAS/Apps/ReporteClientes" -maxdepth 1 -iname "*.csv" -printf "%f\n" \
  | sed 's/^Reporte Clientes de //; s/\.csv$//' | sort > /tmp/csv.txt
"$SQLCMD" -S 10.0.0.89,1433 -U sa -C -I -h -1 -d DBCallCenter \
  -Q "SET NOCOUNT ON; SELECT DISTINCT NombreArchivo FROM dbo.Cartera WHERE NombreArchivo IS NOT NULL;" \
  | sed 's/[[:space:]]*$//' | grep -v '^$' | sort > /tmp/cartera.txt

# Lo que aparece acá rompe el ETL:
comm -23 /tmp/csv.txt /tmp/cartera.txt
```

Si la lista sale vacía, el insumo está sano. Si sale algo, **esa es la causa** y no hay que seguir
buscando.

> Caso real (2026-09-23): aparecieron `celucredit` y `delmar_peru` en los CSV sin estar registradas
> en `Cartera`. El Foreach recorre alfabético: cargó `Bertal` (241.475 filas, 152 s), llegó a
> `celucredit` y voló el flujo entero con `0xC0209029`. Doce minutos de corrida tirados y 19
> carteras sanas sin cargar por culpa de una.

## Paso 5 — Servidor

Si las consultas tardan o los logins se caen, el servidor está saturado. Señal barata:

```bash
ping -n 3 10.0.0.89 | tail -4
```

En LAN debería dar menos de 5 ms. Cientos de milisegundos = servidor ahogado. Para el detalle,
usar `/diagnostico-servidor`.

> El propio ETL satura el servidor mientras corre, porque el paquete de 32 bits pagina contra su
> techo de 2 GB. Es esperable durante la ventana de ejecución, no en otro momento.

---

## Causas conocidas

De la más frecuente a la más rara. Todas vistas de verdad.

| Síntoma | Causa | Qué hacer |
|---|---|---|
| Paquete falla con `0xC0209029` en **"Búsqueda CarteraID"** · *"La fila no produjo ninguna coincidencia"* | Un CSV sin cartera registrada en `Cartera.NombreArchivo` | **El chequeo del Paso 4.** Ver "Cartera sin registrar" abajo |
| `ABORTADA` · "staging está vacío" | El conversor no produjo Excel, casi siempre porque faltan los CSV | Paso 4. Si faltan, avisar a quien los genera. **No forzar** |
| `ABORTADA` · "trae N cartera(s)" | El paquete murió a mitad de carga, típicamente por memoria | Revisar errores del paquete. Reintentar con los CSV completos |
| Paquete falla con `0x8007000E` | Techo de 2 GB del runtime de 32 bits (`/X86`) | Ver "Los 64 bits" abajo. Mientras tanto, reintentar suele bastar |
| SSIS dice correcto pero los datos no se movieron | Foreach de 0 iteraciones | Paso 4. **Hoy las compuertas lo frenan**; antes vaciaba la tabla |
| `EN CURSO` colgado | Bloqueo o corrida realmente larga | Ver bloqueos. No matar sin confirmación |
| Todo sano, datos rancios | El trabajo no arrancó: Agente detenido o schedule desactivado | `SELECT enabled FROM msdb.dbo.sysjobs WHERE name='Ejecutar_ETL_UltimoEstadoTitular'` |
| `ABORTADA` y el universo cambió de verdad | Se retiró una cartera y ahora son menos | Recalibrar con `@MinimoCarteras`, **no** con `@Forzar` |

### Cartera sin registrar

Es el fallo más probable, porque basta con que alguien empiece a exportar una cartera nueva. Tres
salidas, en orden de preferencia:

1. **Registrarla**, si es una cartera real del negocio. Es decisión del área, no técnica: hay que
   saber el nombre correcto y si va activa.

   ```sql
   INSERT INTO dbo.Cartera (NombreCartera, Activa, NombreArchivo)
   VALUES ('NOMBRE OFICIAL', 1, 'nombre_del_csv_sin_extension');
   ```

   `NombreArchivo` es el nombre del CSV **sin** el prefijo `Reporte Clientes de ` y sin `.csv`.
   Varias carteras pueden compartir `NombreArchivo` (pasa con `Delmar_Cast` y `grupo_ceiba`), así
   que verificar cómo discrimina el paquete antes de asumir que es uno a uno.

2. **Sacar el CSV de la carpeta**, si no debía estar ahí. Desbloquea la corrida de inmediato sin
   tocar la base.

3. **Arreglar el paquete** para que redirija las filas sin coincidencia a una salida de error en
   vez de matar el flujo. Es el arreglo de fondo — hoy una cartera nueva tira abajo las otras
   veinte — pero requiere SSDT.

### Los 64 bits

El `/X86` en el paso del trabajo es la causa de fondo de los fallos por memoria. Pero **quitarlo a
secas rompe el paquete**: usa `Microsoft.ACE.OLEDB.16.0` y en 64 bits ese servidor solo tiene
registrado el 12.0. Orden correcto:

1. Instalar el *Access Database Engine 2016 redistributable de 64 bits*.
2. Confirmar: `EXEC master.dbo.sp_enum_oledb_providers` debe listar el 16.0.
3. Recién entonces quitar ` /X86` del comando del paso.

Nunca hacer el 3 sin el 1 y el 2.

---

## Remediación

### Lanzar el ETL a mano

```sql
EXEC msdb.dbo.sp_start_job @job_name = N'Ejecutar_ETL_UltimoEstadoTitular';
```

Seguro de repetir: el primer paso del paquete vacía el staging, y si algo sale mal las compuertas
protegen la tabla. **Antes de lanzarlo, verificar el Paso 4**: sin CSV frescos es una corrida
perdida que además ahoga el servidor media hora.

### Reconstruir desde el histórico

Solo si `UltimoEstadoTitular` quedó vacía o muy incompleta y no hay CSV para recargar:

```
docs/sql/migrations/2026-09-23_recuperar_ultimoestadotitular.sql
```

Deja los datos con la antigüedad del histórico, no al día. Entra por las mismas compuertas y queda
auditado como `Origen = 'Recuperacion'`.

### Forzar una publicación

```sql
EXEC dbo.sp_Insertar_UltimoEstadoTitular @Forzar = 1, @Origen = 'Manual';
```

**Último recurso.** Saltea las compuertas y puede publicar datos parciales, que es peor que no
publicar nada porque *parecen* válidos. Solo con confirmación explícita del usuario y solo cuando
se entendió por qué abortó. Si la causa es que el universo de carteras cambió de verdad, la
herramienta correcta es `@MinimoCarteras`, no `@Forzar`.

---

## Aviso por WhatsApp

```bash
powershell -NoProfile -File .claude/skills/etl-estado-titular/notificar.ps1 -Mensaje "<mensaje>"
powershell -NoProfile -File .claude/skills/etl-estado-titular/notificar.ps1 -Probar
powershell -NoProfile -File .claude/skills/etl-estado-titular/notificar.ps1 -VerConfig
```

Salida: `0` enviado · `1` falló el envío · `2` problema de configuración. Un `2` **no** es un
incidente del ETL: es que falta o está mal `config.local.json`.

> Está en PowerShell y no en bash a propósito. Armar el JSON escapando comillas con `sed` se rompe
> con los acentos y los emoji, y el cuerpo hay que mandarlo como bytes UTF-8 o el mensaje llega
> ilegible al teléfono.

El mensaje tiene que servirle a alguien que mira el teléfono y no tiene el contexto. Siempre:

1. **Qué pasa**, en una línea, con la antigüedad concreta.
2. **Si hay pérdida de datos o no.** Es lo primero que quiere saber quien lee.
3. **La causa**, si se identificó.
4. **Qué se necesita de una persona**, o "no requiere acción".

Ejemplo:

```
⚠️ ETL de estado de titulares — datos de hace 51 h (última carga buena: 21-sep 15:47)

SIN pérdida de datos: UltimoEstadoTitular conserva 2.708.747 filas en 24 carteras.

Causa: las dos últimas corridas abortaron con "el staging trae 4 cartera(s) y se
esperaban al menos 20". El paquete muere por memoria a mitad de carga.

Requiere acción: instalar el Access Database Engine 2016 de 64 bits para poder
quitar el /X86. Mientras tanto, reintentar la corrida a veces alcanza.
```

**No avisar dos veces por lo mismo.** Antes de enviar, revisar si ya se avisó por ese incidente:

```sql
SELECT TOP 1 * FROM DBCallCenter.dbo.EtlEstadoTitular_Aviso ORDER BY AvisoID DESC;
```

Se avisa de nuevo solo si cambió el estado o pasaron más de 12 horas. Después de enviar, registrar
con `sp_EtlEstadoTitular_RegistrarAviso`. Una alerta que se repite cada media hora deja de leerse,
y entonces el día que importe tampoco se va a leer.

---

## Reglas

1. **Nunca imprimir la contraseña**, ni en consultas, ni en volcados de `sysjobsteps.command`, ni
   en mensajes de WhatsApp.
2. **Nunca `@Forzar = 1` sin confirmación explícita** del usuario en la conversación.
3. **Nunca matar una sesión ni detener el trabajo automáticamente.** Proponer y esperar.
4. **Nunca `TRUNCATE` de `UltimoEstadoTitular`.** Esa es literalmente la línea que causó el
   incidente que originó todo esto.
5. **No confiar en el estado del trabajo.** Verificar siempre los datos y la duración del Foreach.
6. **No culpar al ETL sin mirar los CSV** (Paso 4).
7. Si algo no encaja con ninguna causa conocida, **decirlo** en vez de forzar el diagnóstico en la
   casilla más parecida.

## Cómo se dispara sola

Una skill solo corre cuando alguien la invoca; no es un demonio. Para el chequeo automático hay
dos piezas, y conviene tener las dos:

- **Vigía del servidor** (`vigia-sql-agent.sql`): un trabajo del Agente cada 30 minutos que
  consulta la vista de frescura y, si está rancia, manda el WhatsApp. Es tonto pero **corre
  siempre**, aunque no haya nadie con la app abierta. Es el que garantiza que el aviso llegue.
- **Esta skill**, para el diagnóstico con contexto: qué pasó, por qué, y qué hacer. Se invoca a
  mano al recibir el aviso, o de forma programada desde Claude Code.

El vigía avisa. La skill explica.
