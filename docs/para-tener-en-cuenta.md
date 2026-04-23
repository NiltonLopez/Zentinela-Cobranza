# Para tener en cuenta

Hallazgos de la revisión general del proyecto (2026-04-20).

---

## Alta prioridad

- [ ] **Bug: badge `FilaAgente` — lógica de precedencia incorrecta**
  La expresión `tieneMeta && !(...) || guardado` tiene ambigüedad de operadores. Además `FilaAgente` solo considera `recaudo` para detectar cambios, mientras `FilaLider` considera `recaudo + honorarios`. Inconsistencia visual garantizada.

- [ ] **Bug: inputs no se resetean tras `invalidateQueries`**
  Si otra sesión guarda una meta y el query se refresca, los inputs locales de la fila siguen mostrando el valor viejo aunque el badge diga "Con meta". Solución: usar `key` ligada al valor actual de la meta para forzar remount, o `useEffect` para sincronizar cuando cambian los props.

- [ ] **Partir `InformesRecaudoPage.tsx`**
  Más de 1.300 líneas con constantes, helpers, hooks, componentes y cuatro tabs en un solo archivo. Estructura sugerida:
  ```
  pages/informes/recaudo/
    InformesRecaudoPage.tsx     ← solo el shell con tabs
    tabs/TabAgentes.tsx
    tabs/TabLideres.tsx
    tabs/TabCarteras.tsx
    tabs/TabComparativo.tsx
    components/KpiCard.tsx
    components/ThSort.tsx
    hooks/useSortable.ts
    utils/formatters.ts         ← cop, pct
    constants.ts                ← MESES, paleta, fechas
  ```

---

## Media prioridad

- [ ] **CORS hardcodeado en `Program.cs`**
  Mover origins a `appsettings.json` / `appsettings.Development.json`:
  ```json
  "Cors": { "Origins": ["http://localhost:5173", "http://localhost:5174"] }
  ```
  En producción solo cambia config, no código.

- [ ] **`AsNoTracking()` inconsistente en `AsignacionMetasService`**
  Todos los queries de solo lectura deberían tenerlo. Revisar `ObtenerEstadoMesActualAsync` y derivados.

- [ ] **Validación de negocio en el controlador**
  Reglas como `MetaRecaudo <= 0` están en el controlador. Deben moverse al servicio o a un validador dedicado.

- [ ] **Manejo global de errores en el frontend**
  No hay interceptor Axios centralizado. Falta redirect automático en 401 y toast global en 500.

- [ ] **Alinear versión de Node entre máquinas**
  El `package-lock.json` deriva. Agregar `.nvmrc` o campo `engines` en `package.json`.

---

## Baja prioridad

- [ ] **Dividir `ExcelExporter` e `IExcelExporter`**
  Mezcla BaseMadre, Asignaciones y Reportes en una sola clase e interfaz. Partir por responsabilidad:
  `IBaseMadreExporter`, `IReporteExporter`, `IAsignacionesExporter`.

- [ ] **Patrón de módulos en `DependencyInjection.cs`**
  Con 15+ registros en un método, cada módulo nuevo lo modifica. Escalar con extensiones por módulo:
  `services.AddInformesModule()`, `services.AddCargueModule()`, etc.

- [ ] **`AsignacionMetasService` recibe dos `DbContext` directamente**
  Viola DIP. Considerar `IAgenteLookupService` para encapsular el acceso a RRHH. Cuando cambie la fuente de nombres, hoy hay que tocar el servicio de metas.

- [ ] **Lógica de período duplicada**
  `diasDelMes`, `diasTranscurridos` y mes/año actual se calculan por separado en `InformesRecaudoService` y `AsignacionMetasService`. Candidato a value object `PeriodoMesActual` en Application layer.

- [ ] **Join en memoria sin documentar en `ObtenerFilasAgenteAsync`**
  El patrón carga en batch y hace join en memoria (correcto, sin N+1), pero sin un comentario que lo explique el próximo dev lo va a "optimizar" rompiéndolo.

- [ ] **Agentes retirados aparecen en Metas**
  `ObtenerFilasAgenteAsync` no filtra por estado activo del empleado en RRHH. Definir si es intencional o agregar filtro.
