# ADR-026 — Gestión de Asignaciones: cargue masivo por cartera completa (LiderID por fila)

**Estado:** Aceptado
**Fecha:** 2026-07-10
**Módulos afectados:** Administración → Gestión de Asignaciones

---

## Contexto

Gestión de Asignaciones permitía cargar titulares por par (Cartera, Líder) eligiendo la
combinación en un `<select>` que reutiliza las opciones del Calificador. Ese dropdown incluye
entradas agregadas **"{Cartera} Completo"** con `LiderID = null`. Al seleccionarlas, la página
armaba el `value` como `${carteraID}_null` y `Number("null") = NaN`: la opción "Completo"
quedaba **rota** (no había líder válido para asignar).

Se necesitaba poder cargar **toda una cartera de una sola vez**, repartiendo cada titular a su
líder desde el propio archivo.

---

## Decisión

Al seleccionar una opción **"… Completo"** se activa el modo **Cargue Masivo**:

1. La plantilla exige una **3ª columna obligatoria `LiderID`** (además de `NumeroDocumento`,
   `Nombre`). Cada fila lleva el líder al que se asigna el titular.
2. Aparece un **panel de apoyo** con los `LiderID` válidos (activos) de esa cartera —
   `[ID] Nombre`, copiable— siguiendo el patrón de "Gestión de Histórico de Virtualidad".
3. Validación por fila: el `LiderID` debe pertenecer a un `Lider_Cartera` **activo** de la
   cartera; vacío/inválido/inactivo ⇒ error. Dedup por par `(documento, líder)` (la PK de
   `Titular_Cartera` es `TitularID+LiderID+CarteraID`, así que un titular puede repartirse a
   varios líderes, pero no repetirse en el mismo líder).
4. Al confirmar se **reemplaza toda la cartera** (`DELETE WHERE CarteraID`, todos los líderes)
   y se hace bulk insert de los pares `(TitularID, LiderID)` del archivo.

El modo por líder (una cartera + un líder, reemplazo de ese líder) queda intacto.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Reemplazar solo los líderes presentes en el archivo | El usuario pidió "cargue de esa cartera en específico" completo; se decidió reemplazo total de la cartera |
| Columna con el **nombre** del líder | Se pidió explícitamente el campo `LiderID` numérico; el panel de apoyo muestra `ID — Nombre` para copiar el ID correcto |

---

## Implementación

- Backend: `AsignacionMasivaService.ValidarExcelCompletoAsync` / `ConfirmarCargueCompletoAsync`,
  `ExcelExporter.GenerarPlantillaAsignacionesCompleto` / `ParsearFilasAsignacionCompleto`,
  `AsignacionesController` (`plantilla?completo`, `validar` con `liderID` nullable, `confirmar`
  ramificado por `LiderID` nulo).
- Frontend: `GestionAsignacionesPage.tsx` (panel de apoyo, plantilla/validación por modo).
