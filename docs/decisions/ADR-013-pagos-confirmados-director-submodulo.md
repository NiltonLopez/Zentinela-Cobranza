# ADR-013 — Pagos Confirmados Director: sub-módulo con filtros y tabla ordenable

**Estado:** Aceptado  
**Fecha:** 2026-04-24  
**Módulos afectados:** Home → Dashboard Director

---

## Contexto

El sub-módulo "Pagos Confirmados" del Asesor muestra solo los pagos propios. El Director necesita visibilidad total de todos los pagos de la empresa, con capacidad de filtrar por Líder, Cartera y rango de fechas. Además, dado que el volumen de pagos puede ser alto, el módulo debe iniciar con un filtro activo (ciclo actual) para evitar cargas innecesarias.

---

## Decisiones

### 1. Endpoint dedicado GET /api/dashboard/director/pagos

Se añade un nuevo endpoint al `DashboardController` con cuatro query params opcionales: `liderID`, `carteraID`, `fechaDesde`, `fechaHasta`. Sin parámetros devuelve todos los pagos de la empresa.

El servicio `DashboardDirectorService.ObtenerPagosAsync`:
- Filtra `Pagos` en DBCallCenter.
- JOIN con `Carteras` y `Lideres` (mismo DbContext) para nombres.
- Lookup separado en `DBRecursosHumanos.Empleados` para nombres de asesores (no se puede hacer JOIN cross-DB en EF Core).

### 2. PagoDirectorDto incluye NombreLider y NombreAsesor resueltos

El frontend no realiza lookups adicionales. El backend entrega la fila completa con todos los nombres ya resueltos.

### 3. Tabla con ordenamiento por columna (useSortable)

Se reutiliza el hook `useSortable<T>` y el componente `ThSort` ya existentes en `InformesRecaudoPage`. Las 8 columnas de la tabla son ordenables: Cliente, Asesor, Líder, Cartera, Fecha de pago, Valor pagado, Honorarios, Registrado.

### 4. Ambos sub-módulos de Pagos inician filtrados por el ciclo actual

Por decisión de diseño, tanto el sub-módulo del Asesor como el del Director inician con `fechaDesde = primer día del mes actual` y `fechaHasta = hoy`. Esto evita cargar el historial completo de pagos al abrir el módulo, lo cual es crítico cuando el volumen acumulado es alto.

El Asesor recibe los mismos parámetros opcionales `fechaDesde`/`fechaHasta` via `GET /api/dashboard/asesor/pagos`.

---

## Impacto en navegación

El sub-módulo se registra en `navigation.ts` con `rolesPermitidos: ['Director', 'Admin']` — invisible para Asesores y Líderes. La ruta es `/home/pagos-director`.

---

## Nota sobre la serialización de enums

Se detectó que `AddControllers()` sin configuración adicional serializa enums como enteros (`0`, `1`, `2`...). El frontend comparaba `estado === 'Ok'` (string) contra el integer `0` — siempre falso. Se agregó `JsonStringEnumConverter` globalmente en `Program.cs`. Esto afecta a todos los módulos que usan enums en DTOs.
