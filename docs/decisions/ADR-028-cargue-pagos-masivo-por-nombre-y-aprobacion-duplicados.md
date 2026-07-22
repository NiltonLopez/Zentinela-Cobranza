# ADR-028 — Cargue de Pagos: modo masivo por Líder/Cartera y aprobación de duplicados

**Estado:** Aceptado
**Fecha:** 2026-07-10
**Módulos afectados:** Administración → Cargue de Pagos (Cargue masivo)

---

## Contexto

El cargue masivo de pagos operaba con **una cartera** elegida en la UI y resolvía el `LiderID`
de cada pago **automáticamente** (desde `Titular_Cartera` / `Cartera_Agente`). Dos necesidades:

1. Cargar pagos de **varias carteras/líderes en un solo archivo**, indicando Líder y Cartera
   por fila (nombres exactos).
2. Los pagos detectados como **duplicados** (misma cédula, fecha, valor —y cartera— ya
   cargados o repetidos en el archivo) se omitían siempre; se necesitaba poder **aprobar**
   algunos para ingresarlos de todos modos.

---

## Decisión

### Modo "Cargue Masivo" (por Líder/Cartera)

Opción **"Cargue Masivo"** en el desplegable de cartera. Al elegirla:
- La cartera **no** se selecciona en la UI — viene por fila del archivo.
- La plantilla exige columnas **`Lider`** y **`Cartera`** (nombres exactos); `UsuarioControl`
  es **opcional** (si viene, resuelve el asesor; si no, `AgenteControlID` queda nulo).
- Panel de ayuda **idéntico** al de "Histórico de Virtualidad" (`PanelNombresValidos`,
  extraído a componente compartido) con los nombres exactos copiables.
- Cada fila resuelve `Lider`+`Cartera` → IDs por nombre normalizado (sin tildes/espacios/case)
  validando que el par exista y esté **activo** en `Lider_Cartera`; si no coincide ⇒ error.
- Dedup por `(cédula, fecha, valor, cartera)`. `ConfirmarCargueAsync` usa el `CarteraID` por
  fila cuando `Masivo=true`.

### Aprobación de duplicados (ambos modos de cargue masivo)

En la vista previa, cada duplicado trae un **checkbox "Aprobar"** para ingresarlo aunque
coincida con uno ya cargado, más un botón **"Aprobar/Desmarcar todos"**. Los aprobados se
suman a las filas válidas al confirmar. Los no aprobados se siguen mostrando como advertencia
y se omiten. No requirió cambio de backend: los duplicados ya vienen con `LiderID`/`CarteraID`
resueltos y `ConfirmarCargueAsync` inserta lo que recibe en `FilasValidas`.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Resolver Líder/Cartera por **ID** en el archivo | Se pidió por nombre exacto + ayuda de nombres, como en Histórico de Virtualidad |
| Un desplegable de "modo" aparte | Se reutilizó el `<select>` de cartera con una entrada "Cargue Masivo" (mismo patrón que "Completo" de Asignaciones) |
| Dedup ignorando cartera en modo masivo | En masivo la cartera varía por fila; la clave de dedup la incluye |

---

## Implementación

- Backend: `CarguePagosService.GenerarPlantillaMasiva` / `ValidarArchivoMasivoAsync` /
  `ResolverFilasMasivoAsync`, `PagoFilaDto` (+`CarteraID`/`NombreCartera`/`NombreLider`),
  `ConfirmarPagosRequest.Masivo`, `PagosController` (`plantilla?masivo`, `validar` con `masivo`).
- Frontend: `components/PanelNombresValidos.tsx` (compartido, reutilizado también en Histórico
  de Virtualidad), `CargueMasivo.tsx` (opción de modo, panel, aprobación de duplicados).
