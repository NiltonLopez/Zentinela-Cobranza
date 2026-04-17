# ADR-005 — Resolución de agente por UsuarioControlNext en todos los cargues

**Estado:** Aceptado  
**Fecha:** 2026-04-17  
**Módulos afectados:** Cargue de Reportes, Cargue de Pagos

---

## Contexto

El sistema ControlNext identifica a los agentes por un **username de texto** (`UsuarioControlNext`), no por su número de cédula. Los archivos CSV y Excel exportados desde ControlNext incluyen ese username en la columna de agente.

Cargue de Pagos ya resolvía correctamente el agente haciendo un lookup `Agente.UsuarioControlNext = valor_del_archivo → AgenteID`. Sin embargo, Cargue de Reportes hacía un `int.TryParse()` directo sobre el mismo campo, esperando un número — lo que fallaba en producción para todos los agentes cuyo username no es numérico.

## Decisión

**Unificar el mecanismo de resolución de agente en todos los cargues:** leer el campo como `string` (UsuarioControl), hacer un batch lookup contra `Agente.UsuarioControlNext` (OrdinalIgnoreCase) y persistir el `AgenteID` resuelto.

## Consecuencias

- La plantilla de Cargue de Reportes muestra ahora un ejemplo con username textual (`jperez`) en lugar de un número.
- `FilaReporteRaw` tiene dos campos separados: `UsuarioControl` (raw del archivo) y `AgenteID` (long?, resuelto en validación).
- El enum `AgenteIDInvalido` se renombró a `AgenteNoEncontrado` para reflejar semánticamente el nuevo flujo.
- El INSERT a `ReporteEstadoAuxiliar` sigue recibiendo el `AgenteID` numérico (ya resuelto), sin cambios en BD.

## Alternativas descartadas

- **Aceptar ambos formatos (número y username):** descartado por complejidad innecesaria. Los archivos de ControlNext siempre usan username.
- **Usar cédula del agente como identificador:** descartado porque `Agente.AgenteID` = cédula no siempre coincide con lo que ControlNext exporta.
