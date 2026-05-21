# ADR-018 — Pagos BASE GENERAL: AgenteControlID nullable + resolución de líder por unicidad

**Fecha:** 2026-05-21
**Estado:** Aceptado
**Módulos:** Cargue de Pagos (`CarguePagosService`), Informes Recaudo (`InformesRecaudoService`), Dashboard Director (`DashboardDirectorService`), schema `dbo.Pago`

---

## Contexto

En la operación entran a veces pagos que **no provienen de la gestión de un asesor específico** — el cliente paga directamente, vía portal, transferencia espontánea, etc. Estos pagos no deben contar para ninguna meta individual de asesor (`Meta_Agente_Mes`), pero sí para la meta del líder de la cartera (`Meta_Lider_Cartera_Mes`).

Antes de este cambio el modelo no permitía expresar "pago sin asesor": `dbo.Pago.AgenteControlID` era `bigint NOT NULL` y `CarguePagosService` obligaba a resolver un agente válido en cada fila (`Agente.UsuarioControlNext = UsuarioControl`).

Adicionalmente, había una bomba silenciosa en el lookup de líder. El código resolvía `LiderID` desde `Titular_Cartera` con `DistinctBy(TitularID)`, lo que silenciosamente tomaba la primera asignación si una cédula estaba registrada con múltiples líderes en la misma cartera. Esto puede pasar legítimamente en carteras compartidas (varios líderes con su propio set de titulares dentro de la misma cartera).

---

## Decisión

### 1. AgenteControlID nullable

Se hace `dbo.Pago.AgenteControlID` nullable (`bigint NULL`). NULL significa explícitamente "pago sin asesor responsable" — equivalente conceptual de BASE GENERAL.

Migración: `docs/sql/migrations/2026-05-21_pago_agentecontrolid_nullable.sql`. Sin downtime, reversible mientras no existan filas con NULL en la tabla.

### 2. Palabra clave BASE GENERAL en el cargue

`CarguePagosService` detecta `UsuarioControl == "BASE GENERAL"` (case-insensitive, trim) y marca la fila como `EsBaseGeneral = true`. Para esas filas:

- Se **omite** el lookup en `Agente.UsuarioControlNext`. `AgenteControlID` queda en NULL.
- Se aplica una **verificación estricta** del líder: la cédula debe tener exactamente UN líder distinto en `Titular_Cartera` para la cartera del cargue.
  - 0 líderes → error "Cédula X no está asignada a ningún líder en esta cartera"
  - >1 líder → error "Cédula X (BASE GENERAL) está asignada a N líderes en esta cartera — no se puede determinar a cuál atribuir el pago"
  - 1 líder → asignar

### 3. Desempate explícito en pagos normales

Para pagos NO BASE GENERAL con cédula en múltiples líderes dentro de la cartera, el código deja de tomar el primero silenciosamente. Ahora desempata vía `Cartera_Agente.LiderID` del agente resuelto:

- Si el agente del pago está asignado a uno de esos líderes en `Cartera_Agente` → usar ese líder.
- Si el agente no tiene asignación en `Cartera_Agente` para la cartera, o el líder no coincide con ninguno de los de la cédula → error explícito.

Esto resuelve el caso real: en una cartera compartida, el asesor A gestiona los titulares del líder X, y el asesor B gestiona los del líder Y; el pago se atribuye al líder correcto basándose en quién es el asesor responsable.

### 4. Consumidores de AgenteControlID

Tres servicios consumen el campo y se ajustaron para manejar NULL:

- **`DashboardAsesorService`**: filtra `WHERE AgenteControlID == @agenteID`. SQL evalúa `NULL == @p` como falso → BASE GENERAL queda fuera del dashboard del asesor (correcto: no es mérito suyo). Sin cambios.
- **`InformesRecaudoService.GetInformeAgentesAsync`**: pre-filtra `AgenteControlID.HasValue` antes de agrupar. BASE GENERAL no aparece en el informe por agente (no tiene agente).
- **`DashboardDirectorService.ObtenerPagosAsync`**: lista pagos individuales con nombre del asesor. Filas con `AgenteControlID == NULL` muestran literal `"BASE GENERAL"` como `NombreAsesor`.
- **`InformesRecaudoService.GetInformeLideresAsync`**: agrupa por `(LiderID, CarteraID)` sin tocar `AgenteControlID`. Los pagos BASE GENERAL ya contaban para el líder; ahora siguen contando. Sin cambios.

---

## Consecuencias

### Inmediatas

- **Plantilla del cargue** incluye una segunda fila de ejemplo con `BASE GENERAL` como Usuario Control para que el operador aprenda el valor mágico.
- **UI del preview** chip azul "BASE GENERAL" en lugar del texto plano para diferenciar visualmente.
- **Errores explícitos** cuando una cédula tiene múltiples líderes y el agente no desempata — antes era una asignación silenciosamente arbitraria.

### A futuro

- Si aparecen más palabras clave especiales (p. ej. "DOMICILIARIO", "PORTAL WEB"), el patrón es claro: una constante por keyword + branch en el lookup. Si llegan a ser muchas, conviene mover a una tabla de configuración.
- La meta del líder (`Meta_Lider_Cartera_Mes`) ya incluye estos pagos sin esfuerzo adicional. La meta del agente (`Meta_Agente_Mes`) no — por diseño.

### Reversión

```sql
-- Si existen pagos BASE GENERAL ya cargados:
UPDATE dbo.Pago SET AgenteControlID = 0 WHERE AgenteControlID IS NULL;
ALTER TABLE dbo.Pago ALTER COLUMN AgenteControlID BIGINT NOT NULL;
```

Más `git revert` del commit del código.

---

## Referencias

- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CarguePagosService.cs` — detección + verificación + desempate.
- `src/Zentinela.Core/Entities/Pago.cs` — `AgenteControlID` ahora `long?`.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Mappings/PagoMapping.cs` — `.IsRequired()` removido.
- `src/Zentinela.Application/DTOs/Pagos/PagoFilaDto.cs` — `AgenteControlID` nullable + `EsBaseGeneral`.
- `src/Zentinela.Web/src/pages/administracion/CarguePagosPage.tsx` — chip de UI.
- `docs/sql/migrations/2026-05-21_pago_agentecontrolid_nullable.sql` — migración.
- ADR-003 (Cargue de Pagos — lookup automático líder) — antecedente.
