# ADR-024 — Agencias Externas: métricas completas, metas administrables y módulo de informe compartido

**Estado:** Aceptado
**Fecha:** 2026-06-30
**Módulos afectados:** Informes de Recaudo → Agencias Externas; Administración → Asignación de Metas; front-end de Informes de Recaudo (Tab Carteras/Líderes/Agentes)

---

## Contexto

El sub-módulo **Agencias Externas** (relaciones `Lider_Cartera.EsExterna = 1`, ver ADR-016) mostraba
un informe reducido: solo recaudo, honorarios y asignaciones por (líder, cartera externa), con un
gráfico de barras apiladas y **sin descarga**. Deliberadamente quedaba fuera del cálculo de metas
(`GetInformeLideresAsync` excluye `EsExterna` en tres puntos) y de la asignación administrativa de
metas (`AsignacionMetasService` filtra `!lc.EsExterna`).

Dirección pidió que Agencias Externas **espeje** el informe líder-cartera (mismas métricas, misma
tabla, mismo gráfico y el mismo botón "Descargar Informe" con modal de columnas), y que las agencias
externas **puedan tener metas** administrables como cualquier líder interno.

El problema secundario: toda la maquinaria del informe (KPIs, semáforo, chips, ordenamiento, filtros,
generación de PDF + modal de columnas) vivía **embebida y privada** en `InformesRecaudoPage.tsx`.
Replicarla en `AgenciasExternasPage.tsx` habría duplicado ~450 líneas.

---

## Decisión

### 1. Métricas completas en el backend, reutilizando las fórmulas del informe líder-cartera

`GetInformeAgenciasExternasAsync` ahora calcula, por relación externa, las mismas métricas que
`GetInformeLideresAsync`: `TotalAsesores` (Cartera_Agente), `ValorMeta`/`TipoMeta`/`TieneMeta`
(`Meta_Lider_Cartera_Mes`), `DeberiaLlevarHoy` y `ProyeccionCierre`, reutilizando **verbatim** los
helpers de fecha `CalcularDias`/`CalcularDiasHabiles` y las fórmulas de ADR-006/ADR-008. Se agregan
`TotalMeta` a la respuesta y los campos nuevos a `FilaAgenciaExternaDto`.

**No hay cambio de esquema de BD**: las metas de externas se leen/escriben en la tabla existente
`Meta_Lider_Cartera_Mes` (misma que los líderes internos). El universo externo se criba en memoria
por par `(LiderID, CarteraID)`, igual que el recaudo y las asignaciones ya existentes.

### 2. Metas de externas administrables — nuevo tab, mismo destino de guardado

`EstadoMetasMesDto` gana `LiderCarterasExternas`, poblado por `ObtenerFilasLiderCarteraAsync`
parametrizado (`externas: bool` → filtro `lc.EsExterna == externas`). El front agrega un tercer tab
**"Agencias Externas"** en Asignación de Metas que **reutiliza `FilaLider` sin cambios**: el guardado
va por el mismo `POST /api/metas/guardar-lider` (`GuardarMetaLiderAsync` es agnóstico a `EsExterna`).

Se conserva la restricción existente de **una meta por (líder, cartera, mes)** — el dedup ignora
`TipoMeta`, así que cambiar el tipo sobrescribe la fila (mismo comportamiento que internos).

### 3. Módulo compartido `components/informes/reporteCompartido.tsx`

Se extrae a un único módulo lo reutilizable del informe: paleta `C`, `cop`, `MESES`, `KpiCard`,
`Semaforo`, `ChipTipoMeta`, `Cargando`, `ErrorMsg`, `useSortable`/`ThSort`/`TH`/`TD`,
`FiltroSelect`, `TooltipCOP`, `LeyendaGrafico`, y toda la pila de descarga
(`buildExportarPDF`, `filtrarColumnas`, `ModalColumnas` y sus tipos). `InformesRecaudoPage` y
`AgenciasExternasPage` importan de ahí — **cero duplicación**. La página 2 del PDF (gráfico) pasa a
ser **opcional** (`refGrafico?`), habilitando informes de 1 página (Agentes) o con gráfico.

`AgenciasExternasPage` se rediseña para espejar el Tab Carteras: KPIs (incl. "Total Meta" con
desglose Recaudo/Honorarios cuando hay ambos tipos), tabla completa ordenable, **el mismo gráfico
agrupado** (métrica · debería hoy · meta, con controles "Ordenar por" y línea de promedio), y el
botón "Descargar Informe" con modal de columnas.

---

## Consecuencias

- Agencias Externas queda al mismo nivel que el informe líder-cartera; el botón de descarga y el modal
  de columnas son literalmente el mismo componente.
- Las agencias externas **hoy no tienen metas cargadas** (0 filas): las columnas de meta muestran "—"
  hasta que se asignen desde el nuevo tab. El backend ya está listo para cuando existan.
- Deuda saldada: la lógica del informe deja de estar duplicada; futuros informes reutilizan el módulo.
- Riesgo: cualquier ajuste de estilo/PDF ahora impacta ambas páginas — es el objetivo, pero exige
  probar las dos al tocar el módulo compartido.

---

## Referencias

- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/InformesRecaudoService.cs` — `GetInformeAgenciasExternasAsync` extendido.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/AsignacionMetasService.cs` — `ObtenerFilasLiderCarteraAsync(externas)`.
- `src/Zentinela.Application/DTOs/InformesRecaudo/InformeAgenciasExternasResponse.cs`, `.../Metas/AsignacionMetasDto.cs` — DTOs.
- `src/Zentinela.Web/src/components/informes/reporteCompartido.tsx` — módulo compartido.
- `src/Zentinela.Web/src/pages/informes/AgenciasExternasPage.tsx`, `.../administracion/AsignacionMetasPage.tsx` — consumidores.
- ADR-006 (honorarios/meta), ADR-008 (debería hoy / días hábiles), ADR-016 (líderes/asesores externos), ADR-007 (metas del mes inline).
