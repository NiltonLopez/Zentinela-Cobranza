# /diagnostico-servidor

## Descripción

Diagnostica la **ocupación / carga del SQL Server corporativo** (`10.0.0.89,1433` / `DBCallCenter`) y entrega un informe estructurado: si el servidor está saturado, **qué query lo tiene detenido**, **quién (qué máquina) la está ejecutando**, si hay bloqueos, y si la app Zentinela está sana o fugando conexiones. Termina recomendando una acción (típicamente matar la sesión culpable) y, **con confirmación del usuario**, la ejecuta.

> Nace de un incidente real (2026-06-02): un `SELECT * FROM ani.Persona` corrido a mano en SSMS desde otra máquina tuvo el servidor ahogado ~20 min y el export del Creador "nunca se generaba". Con el servidor libre, esa misma consulta pasó de 16s → 1,2s. Esta skill automatiza ese diagnóstico.

> Modelo recomendado: **Sonnet 4.6** (consulta + síntesis rutinaria; sube a Opus solo si hay que razonar una cadena de bloqueos compleja).

## Uso

```
/diagnostico-servidor
```

No requiere argumentos. Opcionalmente, el usuario puede pedir "y mata la sesión X" — en ese caso, ejecutar el bloque de remediación tras confirmar.

## Conexión a la BD (cómo conectarse sin exponer credenciales)

- El **MCP `write_query` es DML-only** (no ejecuta `KILL` ni siempre las DMVs) — ver `project_mcp_db_config.md`. Para `KILL` **siempre** se usa `sqlcmd`. Las DMVs de lectura se pueden correr por `sqlcmd` o por el MCP `read_query`; por consistencia esta skill usa `sqlcmd` para todo.
- `sqlcmd` v16+ está en: `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd`.
- **La contraseña real de `sa` NO está en `appsettings.json`** (placeholder `__DB_PASSWORD__`), sino en **`src/Zentinela.API/appsettings.Development.json`** → `ConnectionStrings:CallCenterConnection`.
- **Nunca imprimir la contraseña.** Extraerla a una variable de entorno de proceso:

```bash
cd "C:\Agentic Environment\Zentinela"
export DBPW=$(grep -o '"CallCenterConnection": "[^"]*"' src/Zentinela.API/appsettings.Development.json | grep -o 'Password=[^;]*' | sed 's/Password=//')
SQLCMD="/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd"
# Patrón de invocación (las DMVs son a nivel de instancia, conectar a DBCallCenter basta):
"$SQLCMD" -S "10.0.0.89,1433" -U sa -P "$DBPW" -d DBCallCenter -C -b -W -Q "<query>"
```

## Pasos

> Ejecutar secuencialmente. Si una query falla por conexión, reportar (servidor inalcanzable es en sí un diagnóstico). No inventar resultados.

### 1. Panorama de carga (instancia)

```sql
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
     WHERE s.is_user_process=1 AND r.status='running')                                         AS requests_corriendo,
  (SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id<>0)                     AS sesiones_bloqueadas,
  (SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process=1)                          AS sesiones_usuario,
  (SELECT ISNULL(SUM(r.cpu_time)/1000,0) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
     WHERE s.is_user_process=1)                                                                AS cpu_seg_en_vuelo;
```

### 2. Requests activos largos + bloqueo (el corazón del diagnóstico)

```sql
SET NOCOUNT ON;
SELECT TOP 20
       r.session_id, s.host_name, s.program_name, s.login_name,
       r.status, r.command,
       DATEDIFF(SECOND, r.start_time, GETDATE()) AS seg,
       r.cpu_time/1000 AS cpu_seg, r.reads, r.logical_reads,
       r.wait_type, r.blocking_session_id,
       DB_NAME(r.database_id) AS bd,
       SUBSTRING(t.text, 1, 200) AS sql_frag
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE s.is_user_process = 1 AND r.status <> 'background' AND r.session_id <> @@SPID
ORDER BY seg DESC;
```

`is_user_process = 1 AND status <> 'background'` es **clave**: excluye las sesiones internas de SQL Server (workers de Service Broker, HADR, XE, diagnósticos) que aparecen con `seg` enorme (= uptime de la instancia, p. ej. ~1.000.000s) y `host_name`/`program_name` NULL. **No son queries de usuario; ignorarlas.** Si ves filas con esos waits (`ONDEMAND_TASK_QUEUE`, `BROKER_*`, `XE_TIMER_EVENT`, `SP_SERVER_DIAGNOSTICS_SLEEP`…) y host NULL, son ruido del sistema.

Umbral práctico: una query de usuario corriendo **> 30s** con muchos `reads`/`cpu_seg` es sospechosa; **> 2 min** es casi siempre un `SELECT *` o un reporte mal filtrado.

### 3. Cadenas de bloqueo (quién bloquea a quién)

```sql
SET NOCOUNT ON;
SELECT  b.session_id  AS bloqueador_sid,  b.host_name AS bloqueador_host, b.program_name AS bloqueador_prog,
        v.session_id  AS victima_sid,     v.host_name AS victima_host,
        DATEDIFF(SECOND, rv.start_time, GETDATE()) AS victima_seg_esperando,
        rv.wait_type
FROM sys.dm_exec_requests rv
JOIN sys.dm_exec_sessions v ON v.session_id = rv.session_id
JOIN sys.dm_exec_sessions b ON b.session_id = rv.blocking_session_id
WHERE rv.blocking_session_id <> 0;
```

### 4. Desglose de sesiones — app vs humanos (¿la app fuga conexiones?)

```sql
SET NOCOUNT ON;
SELECT
  CASE WHEN program_name LIKE '%SqlClient%'          THEN 'App Zentinela (pool)'
       WHEN program_name LIKE '%Management Studio%'  THEN 'SSMS (humano)'
       WHEN program_name LIKE '%SQLCMD%'             THEN 'SQLCMD'
       ELSE ISNULL(NULLIF(program_name,''),'(desconocido)') END AS origen,
  status, COUNT(*) AS n
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
GROUP BY CASE WHEN program_name LIKE '%SqlClient%'          THEN 'App Zentinela (pool)'
              WHEN program_name LIKE '%Management Studio%'  THEN 'SSMS (humano)'
              WHEN program_name LIKE '%SQLCMD%'             THEN 'SQLCMD'
              ELSE ISNULL(NULLIF(program_name,''),'(desconocido)') END, status
ORDER BY n DESC;
```

Lectura: las sesiones `SqlClient` son la app (pool de EF Core, ~10 dormidas es **normal**). Muchas `SSMS`/`SQLCMD` son personas con ventanas abiertas — no son fuga. Solo preocuparse si las `SqlClient` *activas* (no `sleeping`) crecen sin parar.

### 5. Identidad del ofensor

Tomar el `session_id` del peor request del paso 2 y sacar su ficha:

```sql
SET NOCOUNT ON;
SELECT s.session_id, s.login_name, s.original_login_name,
       s.nt_user_name, s.nt_domain,
       s.host_name, c.client_net_address,
       s.login_time, s.last_request_start_time
FROM sys.dm_exec_sessions s
LEFT JOIN sys.dm_exec_connections c ON c.session_id = s.session_id
WHERE s.session_id = <SID_DEL_OFENSOR>;
```

**Cómo identificar "quién":**
- Si `login_name = sa` (autenticación SQL, el login compartido del proyecto) → `nt_user_name`/`nt_domain` vienen **NULL**. SQL Server **no sabe la persona**; la única pista es **`host_name`** (nombre del equipo).
- `client_net_address` puede ser el gateway (p. ej. `10.0.0.1`) si hay NAT/VPN → no localiza; priorizar `host_name`.
- Para mapear máquina→persona: preguntar a quien administre los equipos / inventario de la oficina. **No** afirmar un nombre que no salga de los datos.
- Comparar `host_name` contra la máquina local (`$env:COMPUTERNAME`) para descartar/confirmar que sea el propio usuario.

### 6. Entregar el diagnóstico (formato del informe)

Sintetizar en un informe en español con estas secciones:

1. **Estado general** — ¿ocupado o no? (basado en pasos 1-2). Una frase clara.
2. **Culpable principal** — tabla con `session_id`, query (`sql_frag`), `host_name`, BD, segundos corriendo, CPU, lecturas. Si no hay ofensor, decirlo: "servidor libre, sin queries largas".
3. **Bloqueos** — si hay cadenas (paso 3), describir bloqueador→víctima; si no, "sin bloqueos".
4. **Quién** — máquina + login; explicar la limitación del `sa` compartido si aplica; decir explícitamente si NO es la máquina del usuario.
5. **Salud de la app** — del paso 4: confirmar si la app está sana o si hay indicios de fuga de conexiones.
6. **Recomendación** — acción concreta (normalmente: matar la sesión N), con su justificación de riesgo (ver paso 7).

### 7. Remediación (matar sesión) — SOLO con confirmación

> **Nunca matar una sesión automáticamente.** `KILL` es una acción sobre un servidor corporativo compartido: confirmar con el usuario primero. Riesgo: un `KILL` sobre un `SELECT` (solo lectura) es seguro — solo cancela el escaneo. Un `KILL` sobre una transacción con escrituras dispara **rollback** (puede ser largo y pesado); en ese caso, advertirlo antes.

Procedimiento seguro (los IDs se reciclan → re-verificar antes de matar):

```bash
# 1) Reconfirmar que el SID sigue siendo la MISMA query ofensora
"$SQLCMD" -S "10.0.0.89,1433" -U sa -P "$DBPW" -d DBCallCenter -C -b -W -Q "
SET NOCOUNT ON;
SELECT r.session_id, DATEDIFF(SECOND,r.start_time,GETDATE()) AS seg, SUBSTRING(t.text,1,80) AS sql_frag
FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id = <SID>;"

# 2) Si coincide → matar
"$SQLCMD" -S "10.0.0.89,1433" -U sa -P "$DBPW" -d DBCallCenter -C -b -Q "KILL <SID>;"

# 3) Confirmar que murió (no debe devolver filas)
"$SQLCMD" -S "10.0.0.89,1433" -U sa -P "$DBPW" -d DBCallCenter -C -b -W -Q "
SET NOCOUNT ON;
SELECT session_id, status FROM sys.dm_exec_sessions WHERE session_id = <SID>;"
```

Si entre la detección y el `KILL` la sesión ya no existe (terminó sola), **reportarlo con honestidad** ("la sesión desapareció antes de matarla, no la maté yo") — no atribuirse la acción.

Opcional para confirmar el alivio: re-medir una query representativa (p. ej. el export del Creador) y comparar el antes/después.

## Criterios de éxito

- El informe identifica correctamente si el servidor está ocupado y, si lo está, **la sesión y query exactas** que lo causan, con métricas (segundos, CPU, lecturas).
- Indica `host_name` del ofensor y explica la limitación de trazabilidad del login `sa` compartido — **sin inventar el nombre de una persona**.
- Distingue sesiones de la app (pool sano) de sesiones humanas (SSMS/SQLCMD).
- Si hay bloqueos, los describe como cadena bloqueador→víctima.
- La contraseña **nunca** aparece en la salida ni en el chat.
- No se mata ninguna sesión sin confirmación explícita del usuario.

## Anti-patrones (no hacer)

- ❌ **Imprimir la contraseña** (extraerla a `$DBPW`, nunca echo-arla; redactar cualquier connection string que se muestre).
- ❌ **Matar una sesión sin confirmar** — es un servidor compartido; siempre preguntar primero.
- ❌ **Matar por SID sin re-verificar** que sigue siendo la misma query (los `session_id` se reciclan).
- ❌ **Afirmar "fue Fulano"** cuando el login es `sa` (SQL auth) — el dato real es la máquina (`host_name`), no la persona.
- ❌ **Atribuirse un `KILL` que no ocurrió** porque la sesión terminó sola — reportar el hecho tal cual.
- ❌ **Confundir el pool de la app con una fuga** — ~10 sesiones `SqlClient` dormidas es operación normal de EF Core.
- ❌ **Usar el MCP `write_query` para `KILL`** — es DML-only; usar `sqlcmd`.
- ❌ **Hardcodear la contraseña** en la skill o en cualquier archivo (regla global #3 / #4).
