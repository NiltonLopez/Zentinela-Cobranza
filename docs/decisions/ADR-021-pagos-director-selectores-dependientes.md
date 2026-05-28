# ADR-021 — Pagos Confirmados Director: selectores Líder ↔ Cartera dependientes

**Estado:** Aceptado
**Fecha:** 2026-05-28
**Módulos afectados:** Home → Pagos Confirmados (perfil Director/Admin)

---

## Contexto

El sub-módulo "Pagos Confirmados" del Director (ADR-013) filtra por Líder y Cartera con dos selectores. Estos venían como **dropdowns independientes**: la lista de Líder mostraba todos los líderes y la de Cartera todas las carteras internas, sin relación entre sí. Eso permitía elegir combinaciones vacías (un líder y una cartera que no le corresponde) y obligaba a revisar mentalmente qué carteras tiene cada líder.

La operación pidió que ambos selectores se **filtren mutuamente**: al elegir una cartera, el selector de Líder solo debe ofrecer los líderes de esa cartera (y la tabla, los pagos de esa cartera); y a la inversa, al elegir un líder, el selector de Cartera solo debe ofrecer sus carteras.

---

## Decisión

### 1. Dependencia bidireccional resuelta en el frontend

Ambos selectores siguen **visibles siempre** (no es cascada ni selector fusionado — ver historial abajo). Lo que cambia es el **conjunto de opciones** de cada uno según la selección del otro:

- Cartera elegida ⇒ Líder lista solo los líderes vinculados a esa cartera.
- Líder elegido ⇒ Cartera lista solo las carteras de ese líder.
- "Todos / Todas" en uno reabre la lista completa del otro.

La relación se evalúa en el cliente con un `Set` de pares `"liderID-carteraID"`, sin round-trips por cada cambio de selección.

### 2. Endpoint de vínculos en vez de cascada server-side

Se descartó el patrón "pedir las carteras del líder X / los líderes de la cartera Y" al backend en cada cambio (dos endpoints, dos estados de carga, parpadeo). En su lugar, un endpoint único entrega **todos los pares internos** una sola vez y el filtrado es instantáneo:

- `DashboardDirectorService.ObtenerVinculosLiderCarteraAsync()` → `Lider_Cartera` con `!EsExterna`, `Distinct`.
- DTO `LiderCarteraVinculoDto { int LiderID; int CarteraID; }`.
- `GET /api/dashboard/director/vinculos` — `[Authorize(Roles = Director, Admin)]`.
- Cliente `obtenerVinculosLiderCartera` en `dashboardApi.ts`, cacheado con `staleTime` de 10 min.

Solo se incluyen relaciones **internas** (`!EsExterna`), coherente con el universo administrativo del resto del módulo.

### 3. Reseteo de la selección incompatible

Al cambiar un selector, si la selección del otro deja de ser válida se **resetea a "Todos/Todas"** (en vez de quedar en un estado vacío). Lógica en los handlers:

- `handleCambiarLider`: si `carteraID` no está en `vinculoSet` para el nuevo líder ⇒ `setCarteraID(null)`.
- `handleCambiarCartera`: si `liderID` no está en `vinculoSet` para la nueva cartera ⇒ `setLiderID(null)` (+ limpia el nombre).

Así nunca se llega por UI a una combinación líder/cartera sin pagos.

### 4. Backend de pagos sin cambios

`DashboardDirectorService.ObtenerPagosAsync(int? liderID, int? carteraID, …)` ya filtraba cada dimensión por separado y soporta todas las combinaciones:

- Líder=Todos + Cartera=X ⇒ cartera completa (universo interno).
- Líder=Y + Cartera=X ⇒ relación Líder_Cartera.
- Líder=Y + Todas ⇒ todo lo del líder.
- Todos + Todas ⇒ global.

---

## Historial de iteración

cascada (cartera oculta hasta elegir líder) → 2 dropdowns independientes → selector fusionado estilo Creador (se quitó el dropdown de Líder) → volver a 2 dropdowns independientes → **2 dropdowns dependientes bidireccionales (ambos visibles)** ← estado actual.

La dependencia es solo de **opciones**, no de visibilidad: no se reintroduce el selector fusionado ni se oculta/elimina ninguno de los dos dropdowns.

---

## Consecuencias

- El usuario no puede construir combinaciones vacías desde la UI.
- Un endpoint extra (`/director/vinculos`) que se consulta una vez por sesión; el payload es liviano (solo pares de enteros) y se cachea.
- El perfil Líder (`PagosConfirmadosLiderPage.tsx`) no cambia — ya filtra solo sus carteras vía `GET /api/dashboard/lider/carteras`.

---

## Referencias

- `src/Zentinela.Application/DTOs/Dashboard/DashboardDirectorDto.cs` — `LiderCarteraVinculoDto`.
- `src/Zentinela.Application/Interfaces/IDashboardDirectorService.cs` — `ObtenerVinculosLiderCarteraAsync`.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/DashboardDirectorService.cs` — implementación.
- `src/Zentinela.API/Controllers/DashboardController.cs` — `GET /api/dashboard/director/vinculos`.
- `src/Zentinela.Web/src/api/dashboardApi.ts` + `src/Zentinela.Web/src/types/dashboard.ts` — cliente y tipo.
- `src/Zentinela.Web/src/pages/home/PagosConfirmadosDirectorPage.tsx` — `vinculoSet`, `lideresFiltrados`, `carterasFiltradas`, handlers con reseteo.
- ADR-013 (sub-módulo Pagos Confirmados Director) — antecedente directo.
