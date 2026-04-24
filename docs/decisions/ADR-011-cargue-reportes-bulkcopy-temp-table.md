# ADR-011 — Cargue de Reportes: SqlBulkCopy + tabla temporal + MERGE

**Estado:** Aceptado  
**Fecha:** 2026-04-24  
**Módulos afectados:** Administración → Cargue de Reportes

---

## Contexto

El módulo de Cargue de Reportes actualiza `UltimoEstadoTitular` (UPSERT) e `HistoricoEstadoTitular` (acumulativo) desde archivos CSV/XLSX generados por el call center. La implementación original dependía de:

1. Una tabla de staging permanente `ReporteEstadoAuxiliar` que **no existía en producción**.
2. Dos stored procedures (`sp_ProcesarUltimoEstado`, `sp_ProcesarHistoricoEstado`) también inexistentes.
3. INSERT en chunks con SQL raw concatenado — inviable para 900K+ filas y propenso a inyección.
4. Dependencia de `IExcelExporter` para parsear archivos — acoplamiento innecesario.

El volumen real de los archivos puede superar 900.000 filas con 13 columnas cada una.

---

## Decisiones

### 1. Tabla temporal + SqlBulkCopy (reemplaza staging permanente + SPs)

Se elimina toda dependencia de tablas permanentes de staging y stored procedures. El flujo de confirmación es:

```
BeginTransaction
  ├── INSERT INTO Titular (titulares nuevos, dentro de la misma tx)
  ├── CREATE TABLE #TempReporte (tabla temporal, vive solo en la conexión)
  ├── SqlBulkCopy → #TempReporte (BatchSize = 10.000)
  ├── DELETE duplicados de #TempReporte (conserva FechaUltimaGestion más reciente por TitularID+CarteraID)
  ├── MERGE UltimoEstadoTitular ON (TitularID, CarteraID)
  │     WHEN MATCHED     → UPDATE todos los campos
  │     WHEN NOT MATCHED → INSERT
  └── MERGE HistoricoEstadoTitular ON (TitularID, CarteraID, AgenteID, Rotacion)
        WHEN MATCHED     → UPDATE FechaActualizacion
        WHEN NOT MATCHED → INSERT registro histórico nuevo
CommitTransaction / RollbackTransaction
```

**Por qué tabla temporal `#TempReporte` y no `ReporteEstadoAuxiliar`:**  
La tabla temporal es local a la conexión y se destruye automáticamente. No requiere permisos de creación de tablas permanentes, no hay race condition entre cargues concurrentes y no es necesario limpiarla manualmente. Es el patrón recomendado para bulk UPSERT en SQL Server.

### 2. Parser self-contained (reemplaza IExcelExporter)

El servicio resuelve columnas por nombre normalizado (sin tildes, sin espacios, minúsculas) con un diccionario de alias que cubre los nombres reales que llegan del call center:

| Campo canónico | Aliases reconocidos |
|---|---|
| `titularid` | titularid, titular, cedula, numerodocumento, documento, identificacion |
| `usuariocontrol` | **agenteid**, agente, **ultusugestion**, usuariocontrol, usuariocontrolnext |
| `saldototal` | saldototal, saldo, totalapagar, inicobro365, saldocapital, vlrsaldototal, pagototal |
| `numeromarcado` | numeromarcado, marcado |
| `motivonopago` | motivonopago, motivo, **motvnopago** |

En negrita los aliases críticos que los archivos reales usan y que no son obvios.

Las celdas XLSX de tipo `DateTime` se convierten a ISO (`yyyy-MM-dd`) antes de parsear — ClosedXML devuelve cadena vacía en `GetString()` para celdas de fecha.

### 3. PK real de UltimoEstadoTitular es (TitularID, CarteraID)

La entidad EF mapea la clave como `(TitularID, CarteraID, AgenteID)` pero la PK real en SQL Server es `(TitularID, CarteraID)`. El MERGE hace JOIN sobre esos dos campos y el `AgenteID` se actualiza como columna normal. La deduplicación previa al MERGE garantiza que la tabla temporal no tenga dos filas con el mismo par.

### 4. Unicidad del HistoricoEstadoTitular basada en Rotacion

`Rotacion` es el contador de veces que el titular fue gestionado. Es el discriminador semántico correcto para el histórico:

- **Misma Rotacion** → mismo evento de gestión → `UPDATE FechaActualizacion` solamente.
- **Rotacion nueva** → nueva gestión → `INSERT` del estado completo.

`UltimoEstadoTitular` ya maneja la actualización del snapshot via MERGE; el histórico solo necesita saber si hay una gestión nueva.

### 5. FechaActualizacion = DateTime.Now del servidor

`FechaActualizacion` en ambas tablas representa "cuándo procesó Zentinela este cargue", no la fecha del call center. Se fija siempre a `DateTime.Now` del servidor .NET (desplegado on-premise en Colombia) para garantizar hora local correcta.

### 6. Validaciones de campo NumeroMarcado

Los archivos del call center incluyen números de teléfono que pueden tener formatos inválidos (más de 10 dígitos, prefijos internacionales, etc.). La validación aplica:
- Extraer solo dígitos.
- Validar exactamente 10 dígitos iniciando en `3` (celular colombiano).
- Si no cumple → `null` (no falla la fila, solo limpia el campo).

---

## Scripts SQL requeridos en DBCallCenter

```sql
-- Eliminar FechaMejorPerfil (campo sin valor operativo)
ALTER TABLE UltimoEstadoTitular   DROP COLUMN FechaMejorPerfil;
ALTER TABLE HistoricoEstadoTitular DROP COLUMN FechaMejorPerfil;

-- Agregar FechaActualizacion al histórico
ALTER TABLE HistoricoEstadoTitular
    ADD FechaActualizacion DATETIME NOT NULL DEFAULT GETDATE();
```

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Staging permanente + SPs | Tabla no existía; race condition en cargas concurrentes; lógica repartida |
| EF Core `AddRange` | Inviable a 900K filas; genera batches de INSERTs individuales |
| INSERT raw en chunks | Propenso a errores de concatenación SQL; límite de 1000 parámetros |
| Tabla global temp `##temp` | Visible entre sesiones; race condition en concurrencia |
