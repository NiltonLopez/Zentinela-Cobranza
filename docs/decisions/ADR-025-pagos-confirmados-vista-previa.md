# ADR-025 — Pagos Confirmados: vista previa de 10 registros por defecto

**Estado:** Aceptado  
**Fecha:** 2026-07-06  
**Módulos afectados:** Dashboard Líder → Pagos Confirmados · Dashboard Director → Pagos Confirmados

---

## Contexto

El sub-módulo "Pagos Confirmados" (páginas React `PagosConfirmadosLiderPage.tsx` y `PagosConfirmadosDirectorPage.tsx`) renderiza la lista de pagos del período. Sin filtro de líder ni cartera, un mes puede traer **más de 13.000 pagos**, que se pintaban todos en el DOM con un `.map()` sobre el arreglo completo, generando un desplazable infinito y una página pesada.

Ambas páginas son SPA React que consumen la Web API (`/api/dashboard/{lider|director}/pagos`); la tabla, el ordenamiento y las tarjetas de totales se calculan client-side.

---

## Decisión

Por defecto la tabla muestra solo una **vista previa de 10 registros** (`PREVIEW_LIMIT = 10`). Se despliega la lista completa únicamente cuando el usuario aplica un filtro de ámbito:

- **Líder:** al seleccionar una cartera.
- **Director:** al seleccionar un líder o una cartera.

El **período** (ciclo por defecto) NO dispara la vista completa — solo líder/cartera. Extiende la regla global del proyecto *"Vista previa — máximo 10 registros en cargues"* al sub-módulo de pagos.

### Detalles de implementación

1. `enModoPreview` = sin cartera (Líder) / sin líder ni cartera (Director).
2. `filasVisibles = enModoPreview ? sorted.slice(0, 10) : sorted` — solo la tabla se recorta.
3. **Los totales (Total pagos / Recaudo / Honorarios) se siguen calculando sobre el universo completo** (`reduce` sobre todo el arreglo). Las tarjetas muestran los ~13K reales aunque la tabla enseñe 10.
4. Aviso *"Vista previa — Mostrando 10 de N pagos. Filtra por [cartera / líder o cartera] para ver el detalle completo"*, visible solo en preview y cuando hay más de 10 filas.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| `Take(10)` en el backend | Rompería los totales (se calculan client-side sobre el arreglo); exigiría un endpoint de agregados aparte |
| Paginación / scroll infinito server-side | Mayor complejidad; el despliegue es on-premise en LAN y el recorte en cliente resuelve el dolor real (el DOM). Queda como evolución futura si el payload de red molesta |
| Recortar también por período | El usuario pidió explícitamente que solo líder/cartera muestren el detalle |

---

## Nota de rendimiento

El recorte es client-side: el backend aún transfiere todos los pagos del período. Es aceptable en LAN on-premise y preserva la exactitud de los totales. Si a futuro el volumen de red se vuelve un problema, el siguiente paso es paginación server-side con agregados calculados en SQL.
