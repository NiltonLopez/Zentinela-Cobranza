# ADR-022 — Lider_Cartera: soft-delete vía `Activo` (preservar histórico)

**Estado:** Aceptado
**Fecha:** 2026-06-11
**Módulos afectados:** Administración → Gestión de Líderes y Carteras; consumidores de `Lider_Cartera` (informes, dashboards, metas, calificador, comparador, selectores de cargue)

---

## Contexto

El módulo "Gestión de Líderes y Carteras" permite eliminar físicamente una relación `Lider_Cartera` con el botón X. Hoy esa eliminación está **bloqueada por pre-validación** (`GestionCarterasService.EliminarRelacionAsync`) cuando la relación tiene dependientes: pagos, asesores, metas, mensajería o IVR. Es decir, una relación con histórico **no se puede borrar** — pero tampoco se puede **retirar**: queda activa para siempre, apareciendo en selectores operativos, dashboards y generación de bases aunque el líder ya no trabaje esa cartera.

El problema de fondo: un `Pago` queda anclado al par `(LiderID, CarteraID)` (ver `Pago.cs`), no a una asignación operativa. Borrar la relación `Lider_Cartera` rompería la trazabilidad histórica del recaudo (quién recaudó, en qué cartera) para informes, honorarios y metas. Por eso **el borrado físico es inaceptable** cuando hay histórico.

Falta un mecanismo para **retirar** una relación: sacarla de lo operativo conservando intacto su histórico.

---

## Decisión

### 1. Nueva columna `Lider_Cartera.Activo BIT NOT NULL DEFAULT 1`

Soft-delete (borrado lógico). Una relación retirada se marca `Activo = 0` en vez de eliminarse. El flag vive en la relación (como `EsExterna`), no en la cartera ni en el líder: una cartera puede tener una relación activa con un líder y otra inactiva con un líder distinto.

### 2. Dos acciones distintas, una sola X "inteligente" en la UI

El botón X **decide solo** según el estado de la relación, sin que el usuario tenga que elegir el mecanismo:

- **Relación con histórico** (pagos, metas, mensajería, IVR, o `TotalAsignaciones > 0`) ⇒ **Inactivar** (soft): vacía `Titular_Cartera` (asignación operativa, no histórica) y pone `Activo = 0`. Conserva pagos, metas, mensajería e IVR intactos. **Reversible.**
- **Relación 100% limpia** (sin ningún dependiente) ⇒ **Eliminar** (hard): el comportamiento actual de `EliminarRelacionAsync`. Salida de escape para un alta creada por error. **Irreversible**, pero no destruye nada porque no hay nada.

La UI determina la rama con los contadores que ya devuelve `RelacionLiderCarteraDto` (`TotalAsignaciones`, `TotalAsesores`) más un indicador de histórico; el backend revalida antes de actuar (no confía en el cliente).

### 3. Reactivación

Las relaciones inactivas se **listan** en el módulo con un badge gris ("Inactiva") y un botón **Reactivar** que pone `Activo = 1`. Caso de uso: un líder retoma una cartera que ya trabajó antes — se reactiva la relación existente y su histórico vuelve a empalmar con el nuevo trabajo, en vez de crear un par duplicado.

### 4. `Activo` NO es un filtro global — se clasifica por consumidor

El punto crítico. El sentido del soft-delete es que **lo operativo oculta las inactivas** pero **lo histórico las sigue viendo**. Aplicar `.Where(lc => lc.Activo)` a ciegas en todos lados rompería los informes históricos. Clasificación de los ~9 servicios que consumen `LiderCarteras`:

| Servicio / consumidor | Filtra por `Activo` | Razón |
|---|---|---|
| `GestionCarterasService.ListarRelacionesAsync` | **Ambas** (con flag) | El admin ve activas e inactivas para poder reactivar |
| `CarteraRepository` (generar base / selectores Líder) | ✅ Solo activas | Operativo |
| `CalificadorCarteraService` | ✅ Solo activas | Operativo |
| `ComparadorCarterasService` | ✅ Solo activas | Operativo |
| `AsignacionMetasService` | ✅ Solo activas | No asignar metas a relaciones retiradas |
| `DashboardDirectorService` | ✅ Solo activas | KPIs del presente |
| `AdminService` (carteras del agente) | ✅ Solo activas | Operativo |
| Selector de Cargue de Pagos | ✅ Solo activas | No cargar pagos nuevos a relación retirada |
| **`InformesRecaudoService`** | ❌ **NO filtrar** | **Histórico — debe seguir reportando el recaudo de relaciones ya inactivadas** |

`InformesRecaudoService` es el consumidor que justifica todo el ADR: si filtrara por `Activo`, el recaudo histórico del líder retirado desaparecería de los informes — el escenario exacto que esta decisión evita.

### 5. Coexistencia con `EsExterna`

`Activo` y `EsExterna` son ortogonales. Una relación puede ser `{ EsExterna = 1, Activo = 0 }` (agencia externa que dejó de operar la cartera). Los filtros se componen con AND donde aplique.

---

## Consecuencias

- El histórico de pagos/metas/mensajería/IVR queda **garantizado**: ninguna relación con dependientes se elimina jamás.
- Costo de implementación: 1 migración idempotente + columna en entidad/mapping + auditar ~30 sitios de query para clasificar el filtro (no es buscar-y-reemplazar) + endpoints Inactivar/Reactivar + UI con badge y X inteligente.
- El `EliminarRelacionAsync` actual **se conserva** para la rama hard (relación vacía); no se elimina lógica existente.
- Riesgo principal: olvidar el filtro `Activo` en un consumidor operativo (mostraría relaciones retiradas) o aplicarlo de más en `InformesRecaudoService` (ocultaría histórico). Mitigación: la tabla de clasificación de §4 es la checklist de implementación y revisión.

---

## Referencias

- `src/Zentinela.Core/Entities/LiderCartera.cs` — nueva propiedad `Activo`.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Mappings/LiderCarteraMapping.cs` — mapeo de columna.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/GestionCarterasService.cs` — `InactivarRelacionAsync`, `ReactivarRelacionAsync`, `EliminarRelacionAsync` (existente, rama hard).
- `docs/sql/migrations/2026-06-11_lider_cartera_activo.sql` — migración.
- `docs/decisions/ADR-016-lideres-asesores-externos.md` y migración `2026-05-20_lider_cartera_es_externa.sql` — precedente directo del patrón "flag en la relación".
- ADR-006 (informes de recaudo / honorarios) — consumidor histórico que NO filtra por `Activo`.
