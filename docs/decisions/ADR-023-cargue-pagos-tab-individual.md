# ADR-023 — Cargue de Pagos: tab "Pago individual" reusando la resolución del masivo

**Estado:** Aceptado
**Fecha:** 2026-06-11
**Módulos afectados:** Administración → Cargue de Pagos (frontend + backend `api/pagos`)

---

## Contexto

El módulo "Cargue de Pagos" solo permitía cargar pagos en lote vía CSV/XLSX. Registrar un pago suelto obligaba a armar un archivo de una fila — engorroso para correcciones o casos puntuales. Toda la lógica de negocio (resolución automática de `LiderID` desde `Titular_Cartera`, resolución de `AgenteControlID`, manejo de BASE GENERAL y desempate multi-líder vía `Cartera_Agente` — ADR-003, ADR-018, y detección de duplicados) ya estaba implementada y probada para el flujo masivo. Duplicarla para un registro individual habría sido riesgoso y divergente.

---

## Decisión

### 1. El módulo se divide en 2 tabs

- **"Pago individual"** (tab por defecto): formulario para registrar un pago a la vez.
- **"Cargue masivo"**: el flujo CSV/XLSX existente, **sin cambios funcionales** (extraído verbatim a un sub-componente bajo el tab).

`CarguePagosPage.tsx` queda como shell (encabezado + barra de tabs, patrón de `AsignacionMetasPage`); el flujo masivo vive en `cargue-pagos/CargueMasivo.tsx` y el formulario en `cargue-pagos/PagoIndividualForm.tsx`. Misma ruta `/administracion/pagos` y mismo gate de roles (Admin, Auxiliar de Sistemas).

### 2. El pago individual reutiliza la MISMA resolución del masivo

En `CarguePagosService` se extrajo el bloque de resolución+dedup a un método privado **`ResolverFilasAsync(List<PagoFilaDto> filas, int carteraID)`** que devuelve un `ValidarPagosResponse`. `ValidarArchivoAsync` ahora solo parsea el archivo y delega — el comportamiento del masivo es idéntico (el bloque de lógica no cambió, solo se le antepuso un límite de método).

`RegistrarPagoIndividualAsync` modela el pago como **una fila de CSV** (`PagoFilaDto`) y corre por esa misma `ResolverFilasAsync`. Así, cédula no asignada, asesor no encontrado, ambigüedad de líder y duplicados se detectan con la lógica exacta del masivo. Es un flujo de un paso (valida + inserta); si la fila no resuelve, lanza `PagoIndividualException` que el controller traduce a 400 con el mensaje exacto. BASE GENERAL se envía como `esBaseGeneral: true` (la UI es una casilla) y el servicio lo mapea a la palabra clave `"BASE GENERAL"` para que la resolución no tenga un segundo camino.

### 3. Endpoints de apoyo del formulario

- `POST /api/pagos/registrar-individual` — registra el pago; devuelve el líder resuelto.
- `GET /api/pagos/asesores?carteraID&q` — autocomplete de asesores de la cartera (agentes con `UsuarioControlNext`; nombres desde DBRecursosHumanos, mismo patrón cross-DB que `AsignacionMetasService`). El valor enviado al registrar es el `usuarioControl`, no el `AgenteID`, para no introducir un camino de resolución alterno.
- `GET /api/pagos/titular?carteraID&cedula` — autocompleta el nombre del cliente; `existe=false` si la cédula no está asignada a la cartera (pre-valida que habrá líder).

### 4. UX del formulario

Cartera → cédula (al digitarla, autocompleta el nombre desde la BD; si no existe en la cartera, lo avisa y bloquea) → valor/honorarios (formato COP) → fecha → asesor (autocomplete escribible) o casilla **BASE GENERAL**. Al confirmar muestra el líder resuelto.

---

## Consecuencias

- Cero duplicación de la lógica de negocio: un solo punto de verdad (`ResolverFilasAsync`) para masivo e individual.
- El cargue masivo queda intacto (extracción verbatim + un límite de método). Riesgo principal del refactor: la equivalencia entre `filas.Where(EsValida)` y el conjunto resuelto — se sostiene porque la resolución solo cambia `EsValida` de true→false y nunca agrega filas.
- 3 endpoints nuevos bajo `api/pagos`; sin cambios de esquema en la tabla `Pago`.
- El claim `userName` del JWT sigue siendo requisito (ADR-020): usuarios con token viejo deben re-login.

---

## Referencias

- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CarguePagosService.cs` — `ResolverFilasAsync`, `RegistrarPagoIndividualAsync`, `BuscarAsesoresAsync`, `BuscarTitularAsync`.
- `src/Zentinela.API/Controllers/PagosController.cs` — endpoints `registrar-individual`, `asesores`, `titular`.
- `src/Zentinela.Application/DTOs/Pagos/` — `RegistrarPagoIndividualRequest/Response`, `AsesorCarteraDto`, `TitularEnCarteraDto`, `PagoIndividualException`.
- `src/Zentinela.Web/src/pages/administracion/CarguePagosPage.tsx` + `cargue-pagos/{CargueMasivo,PagoIndividualForm}.tsx`.
- ADR-003 (lookup automático del líder), ADR-018 (BASE GENERAL + desempate), ADR-020 (UsuarioRegistro desde JWT).
