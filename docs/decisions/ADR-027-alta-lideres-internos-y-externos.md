# ADR-027 — Gestión de Líderes y Carteras: alta de líderes internos y externos

**Estado:** Aceptado
**Fecha:** 2026-07-10
**Módulos afectados:** Administración → Gestión de Líderes y Carteras

---

## Contexto

La tabla `Lider` (DBCallCenter) se llenaba manualmente (SQL). No había forma desde la app de
dar de alta un líder. Se necesitaban dos casos:

- **Interno:** empleados de `DBRecursosHumanos.Empleado` con cargo de líder.
- **Externo:** personas de agencias que **no** son empleados (sin registro en `Empleado`) pero
  que operan carteras externas y necesitan una fila en `Lider` (a la que apuntan
  `Lider_Cartera`, `Titular_Cartera`, `Pago`, `Meta`).

Hallazgo de esquema: `Lider.ID` **no es IDENTITY** (contradecía `schema.md`) y `Lider` tiene
columna `NumeroDocumento bigint NULL`. Ver `schema.md` (tabla Lider).

---

## Decisión

Botón **"+ Agregar Líder"** con un modal de doble modo:

**Interno** — se elige un empleado elegible y se da de alta con sus datos de RRHH.
- Cargos de líder: `CargoRoles.CargosLider = {2 Líder de Cobranza, 7 Coordinador de Cartera,
  8 Director Operativo}`.
- Elegibles: empleados activos con esos cargos que aún no sean líder activo. Si existía un
  líder inactivo con esa cédula, se **reactiva** en vez de duplicar.

**Externo** (checkbox "Es líder externo") — Nombre (obligatorio), Documento/NIT (opcional),
Email (opcional). Se crea **atómicamente**:
1. Fila en `Lider` (`Activo=1`, `NumeroDocumento` nullable).
2. Usuario Zentinela (Identity) con rol **`Líder Externo`**, vinculado a ese `LiderID`.
   - **Contraseña inicial:** el documento si viene (≥6 dígitos); si no, una **temporal generada**
     que se muestra **una sola vez** al administrador. `RequiereCambioPassword = true` en ambos.
   - **Usuario:** `nombre.apellido` + (últimos 3 del documento, o el `LiderID` si no hay
     documento), garantizando unicidad.
   - Como `Lider` (DBCallCenter) y el usuario (DBZentinela) viven en bases distintas, no hay
     transacción compartida: ante cualquier fallo se **revierte** el usuario y el líder creados.

`Lider.ID` se asigna `MAX(ID)+1` (no es IDENTITY); el mapping usa `ValueGeneratedNever()`.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Modelar la externalidad en la entidad `Lider` | La externalidad ya vive en `Lider_Cartera.EsExterna` (la relación, no la persona). Basta una fila `Lider` con documento nullable |
| Exigir documento para el usuario externo | El usuario pidió documento opcional; se resuelve con contraseña temporal generada cuando falta |

---

## Implementación

- Backend: `GestionCarterasService.ListarEmpleadosElegiblesAsync` / `CrearLiderAsync` /
  `CrearLiderExternoAsync` (inyecta `UserManager`), DTOs `EmpleadoElegibleDto`, `CrearLiderDto`,
  `CrearLiderExternoDto/Response`, `LiderMapping.ValueGeneratedNever()`, endpoints
  `GET empleados-elegibles`, `POST lideres`, `POST lideres-externos`.
- Frontend: `GestionCarterasPage.tsx` (modal con checkbox externo + panel de credenciales).
