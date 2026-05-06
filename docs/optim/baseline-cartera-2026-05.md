# Baseline — Optimización módulo Cartera (mayo 2026)

Mediciones realizadas el 2026-05-06 contra DBCallCenter (10.0.0.89,1433, SQL Server 2022 Enterprise) usando `sqlcmd` con `OPTION (RECOMPILE)` para invalidar plan cache. Sólo SELECT — cero DML/DDL.

Carteras de referencia derivadas de `vw_BaseMadre` (1.269.421 filas, 35 combinaciones Cartera×Líder):

| Percentil | Filas    | Cartera × Líder                       |
|-----------|---------:|---------------------------------------|
| P50       | 31.051   | (24, 7)  OMEGAKID / ANDRES ROJAS     |
| P90       | 54.714*  | (8, 7)   DELMAR AGORA / ANDRES ROJAS → **59.430 filas** (sujeto P90) |
| P99       | 172.479* | (4, 3)   BERTAL / JULIO MORA → **190.296 filas** |

*P90 y P99 son interpolados; la cartera de medición es la combinación real más próxima.

Artefactos crudos en `_artifacts/` (SQL ejecutado + .out.txt + .sqlplan).

---

## 1. El cuello de botella **NO** son los 14 LEFT JOIN DISTINCT

El plan unificado original (`~/.claude/plans/optimizacion-cartera-modulo.md`, Fase 2a) hipotetizaba que los `LEFT JOIN (SELECT DISTINCT TitularID FROM Tabla)` materializaban ~10M filas y rompían la cobertura de los índices `IX_*_TitularID_Fecha`. **La medición lo desmiente**.

### 1.1 Calificador end-to-end sobre P90 (CarteraID=8, LiderID=7)

| Query                                                                | Compilación CPU | Ejecución CPU | Ejecución elapsed |
|----------------------------------------------------------------------|-----------------:|---------------:|-------------------:|
| Q_A `SELECT * FROM vw_BaseMadre WHERE CarteraID=8 AND LiderID=7`     | 3.719 ms         | 43.780 ms      | **6.218 ms**       |
| Q_B Localización (14 LEFT JOIN DISTINCT, agregados SUM)              | 4.709 ms         | 41.970 ms      | **5.996 ms**       |

CPU >> elapsed indica paralelismo (DOP ~7). **La compilación sola consume 3-5 segundos por query** — el optimizador aborta por timeout y entrega el primer plan razonable, no el óptimo.

Lecturas lógicas Q_B (cartera P90, ~59k filas):

| Tabla               | Lecturas  | Notas                                               |
|---------------------|----------:|-----------------------------------------------------|
| **DatosExternos**   | 165.016   | Tocada **por la vista**, no por los DISTINCTs       |
| **Persona**         | 0 en Q_B / 115.479 en Q_A | Sólo aparece cuando la vista proyecta columnas de Persona |
| Fosyga              | 38.730    | 1.5x lo que reporta Q_A (la vista la toca también)  |
| Titular             | 30.599    | Igual en Q_A y Q_B                                  |
| Positiva            | 22.964    | 2x respecto a Q_A                                   |
| Camara              | 16.418    | 1.5x respecto a Q_A                                 |
| PositivaEmpresas    | 16.418    | 2x respecto a Q_A                                   |
| SaludTotal          | 12.303    | 1.9x                                                |
| ARLSura             | 11.755    | 1.65x                                               |
| RGM                 | 11.608    | 2x                                                  |
| Tigo                | 8.010     | 1.7x                                                |
| DatosClaro          | 8.188     | 1.6x                                                |
| Cajacopi            | 7.420     | 2x                                                  |
| RUES                | 7.308     | 2x                                                  |
| SOS                 | 1.902     | 4.4x                                                |
| Compensados         | 1.781     | 1.9x                                                |
| SIMIT               | 1.930     | 2x                                                  |
| Titular_Cartera     | 299       | Trivial                                             |

Total Q_B ≈ 360k lecturas lógicas; total Q_A ≈ 423k. **Los 14 LEFT JOIN DISTINCT añaden poco — no hay materialización monstruosa**. Su sobrecosto se explica por el segundo paso (cada tabla se lee 18 veces vs 9 — paralelismo + un segundo scan por el LEFT JOIN), pero el orden de magnitud sigue dominado por la vista.

### 1.2 Plan XML (`05_plan_localizacion.sqlplan`)

- `StatementOptmEarlyAbortReason="TimeOut"` — el optimizador se rinde antes de explorar todo el espacio.
- `StatementSubTreeCost="201.392"` (estimado, plan elegido) — alto.
- **15 SORT + 15 Window Aggregate (`ROW_NUMBER`) + 15 Filter** internos en la vista. Cada fuente (14 + DatosExternos) recibe el patrón `Sort(TitularID, FechaConsulta DESC) → ROW_NUMBER → WHERE rn=1` para "el último por titular".
- 16 Hash Match (10 Right Outer + 5 Left Outer + 1 Aggregate). Los Right Outer son joins **internos** de la vista con `EstimateRows = 17M, 15M, 8.5M, 7.8M, 6M, 3.6M, 2.3M, 2.2M, 2.1M`. Es ahí donde el optimizador estima cardinalidades infladas; los 5 Left Outer (los 14 DISTINCTs míos, parcialmente alcanzados antes del timeout) tienen `EstimateRows = 70k, 44k, 39k, 36k, …` — **dos órdenes de magnitud menos**.
- 9 Index Scan + 6 Table Scan. Las 6 tablas con Table Scan son **Cajacopi, DatosExternos, PositivaEmpresas, RGM, RUES, SIMIT** — la vista necesita columnas (celular, email, etc.) que no están en `IX_*_TitularID_Fecha`, por eso degrada a Scan. Ningún Index Scan/Seek se hace sobre el lado de los DISTINCTs — el optimizador re-fórmula esos joins después del filtro CarteraID/LiderID, ya pequeños.

**Conclusión correctiva**: el costo dominante es **`vw_BaseMadre`**, específicamente sus 15 SORT/ROW_NUMBER + 10 Right Outer Hash Match con 7-17M filas estimadas. Atacar los 14 DISTINCTs (Fase 2a original) tiene techo bajo.

---

## 2. Tamaños y uso de índices

### 2.1 Tablas (`_artifacts/02_tamanos_tablas.out.txt`)

Filas reales (clustered/heap), DBCallCenter:

| Tabla               | Filas       | MB     |
|---------------------|------------:|-------:|
| Fosyga              | 2.050.497   | 302    |
| Titular             | 2.150.879   | 236    |
| Positiva            | 2.064.892   | 178    |
| PositivaEmpresas    | 1.856.446   | 64     |
| Camara              | 1.860.429   | 128    |
| RGM                 | 1.519.108   | 111    |
| ARLSura             | 1.469.937   | 92     |
| Titular_Cartera     | 1.269.422   | 191    |
| Tigo                | 1.026.333   | 63     |
| DatosClaro          | 955.888     | 63     |
| Cajacopi            | 954.510     | 73     |
| SaludTotal          | 927.095     | 94     |
| RUES                | 736.031     | 65     |
| SIMIT               | 340.927     | 17     |
| Compensados         | 167.892     | 14     |
| SOS                 | 116.967     | 7      |
| Cartera             | 29          | 0,07   |
| **UltimoEstadoTitular** | **0**   | 0      |

### 2.2 Uso de índices (`_artifacts/03_uso_indices.out.txt`)

- Los 14 `IX_*_TitularID_Fecha` están **todos en uso reciente** (`last_user_seek` de hoy, varios `user_seeks` por tabla).
- `IX_TitularCartera_Lider_Cartera`: 5.781 seeks, 1.575 scans — saludable.
- `IX_UltimoEstado_FechaPromesa`: **0 seeks, 0 scans desde reinicio** — tabla vacía, no hay nada que medir todavía. Decisión sobre este índice queda aplazada.

---

## 3. Comparador — operativamente roto hoy

`UltimoEstadoTitular` está **vacía** (0 filas, 0 titulares, 0 carteras). Su `last_user_update` fue **2026-05-05 14:46** — la tabla se modifica regularmente pero hoy está sin datos, probablemente por un truncate sin repopulación posterior.

Consecuencias medidas (`_artifacts/06_singestion_comparador.out.txt`):

- Toda la columna `vw_BaseMadre.[Ultimo Perfil]` es `'0'` en las 1.269.421 filas.
- 100% Sin Gestión, 0 Gestionados en cada cartera.
- El IN(...) que `Comparador.titularIDs.Contains(...)` enviaría rondaría:
  - 190.000 IDs (P99 BERTAL)
  - 395.000 IDs (combinando todos los líderes de Mercadeos Personal Propia)
- Esto **viola el límite duro de 2.100 parámetros** de SQL Server — la query falla antes de ejecutarse, o el ORM la fragmenta perdiendo el plan cache.

**Implicación**: la Fase 4a (TVP) sigue justificada **incluso si la tabla se repopula**, porque las cardinalidades reales del set Sin Gestión exceden el límite de parámetros. Hasta que UltimoEstadoTitular se repopule, las mediciones de Fase 5 sobre Comparador serán degeneradas (todo es Sin Gestión).

---

## 4. Reformulación de Fase 2 (Calificador)

El plan original sigue válido en filosofía pero la priorización cambia:

| Original | Estado tras baseline                                                                       |
|----------|--------------------------------------------------------------------------------------------|
| **2a — Reescribir los 14 LEFT JOIN DISTINCT** (alternativas A/B/C)                          | **Bajar prioridad** — costo marginal, no es el cuello. Mantener "B" (CTE `Presencias` con `UNION ALL` agregado por `MAX(CASE)`) sólo como cleanup; cambia poco la latencia. |
| **2b — Mover los 9 bloques C# a una sola query SQL agregada**                              | Sigue válido y de alto impacto: evita `ToListAsync` de 59k–190k filas, y permite reescribir Q_A sin pasar por la vista. |
| _(nueva)_ **2c — Evitar `vw_BaseMadre` en el Calificador**                                 | **Nueva máxima prioridad**. La vista hace 15 ROW_NUMBER+SORT internos sobre tablas grandes y el optimizador hace timeout. Reescribir con CTE/APPLY sobre las tablas base, filtrando por `CarteraID/LiderID` ANTES de hacer los joins, debería bajar el plan a ~20-50× menos costo y eliminar la compilación de 4-5s. |
| _(nueva)_ **2d — Memoización de la vista**                                                | Si 2c es demasiado intrusivo, alternativa: marcar `vw_BaseMadre` con `WITH SCHEMABINDING` + crear índice clustered sobre la vista (vista indexada). Materializa el resultado, una sola pasada amortizada. Riesgo: cualquier UPDATE a las 14 tablas dispara mantenimiento del índice. WONTFIX hasta validar tasa de cambio. |

Recomendación de secuencia para Fase 2:

1. **Spike 2c**: reescribir Q_A como CTE manual (sin la vista), validar tiempo y resultado bit-a-bit contra la vista actual sobre carteras P50/P90/P99/vacía. Si baja a <1s con cardinalidades correctas → seguir adelante.
2. **2b**: aplicar agregados SQL.
3. **2a (lite)**: reemplazar los 14 LEFT JOIN DISTINCT por la CTE `Presencias` única, sólo para limpieza de código. Sin medir como objetivo principal.

---

## 5. Reformulación de Fase 4 (Comparador)

La validación funcional queda **bloqueada hasta que UltimoEstadoTitular se repopule** — sin datos no hay caso de prueba diferencial. Mientras tanto:

1. **4a (TVP)** — implementable y correcto sin necesidad de datos. La firma del servicio no depende de datos. Tests unitarios pueden validar la mecánica del TVP con dataset sintético en setUp.
2. **4b (CTE única)** — implementable por la misma razón.
3. **4c (eliminar bug Preview vs Excel)** — implementable; pasa por construcción.

Acción operativa fuera de scope: **avisar al usuario** que UltimoEstadoTitular se está vaciando — es deuda mayor del módulo y bloquea la utilidad del Comparador en producción.

---

## 6. Decisiones para WONTFIX que se mantienen

| Item | Justificación tras baseline |
|---|---|
| Cambiar `vw_BaseMadre` a tipos fuertes | Sigue WONTFIX por integración externa. Si vamos por 2c (no usar la vista) y queda inalterable, ni siquiera la modificamos. |
| Cache distribuido / Redis | Sin evidencia tras baseline. P90 a 6s es alto pero acotable con 2c. Reevaluar tras Fase 5. |
| Streaming Excel real | Independiente de Fase 1; el chunked export sigue siendo la primera línea. |
| Tabla `TitularPresencia` materializada (alt C de 2a) | WONTFIX confirmado — los DISTINCTs no son el cuello. |
| Crear índices nuevos | Los 14 IX están en uso. Sólo se justificaría un índice de cobertura en alguna tabla fuente si vamos por 2d (vista indexada) — fuera de scope hoy. |

---

## 7. Datos para Fase 5 (validación diferencial)

Carteras a usar en tests diferenciales del Calificador:

| Etiqueta       | (CarteraID, LiderID) | Filas    |
|----------------|----------------------|---------:|
| Vacía          | (999, 999)           | 0        |
| P50            | (24, 7)              | 31.051   |
| P90            | (8, 7)               | 59.430   |
| P99            | (4, 3)               | 190.296  |
| 100% SinGestión | cualquiera (hoy)     | varía    |

`100% SinGestión` cambiará si UltimoEstadoTitular se repopula — el test debe construirse con un fixture explícito en lugar de basarse en el estado actual de la BD.

---

## 8. Cómo reproducir

```powershell
$args = @("-S","10.0.0.89,1433","-U","sa","-P","<clave>",
          "-d","DBCallCenter","-C","-l","30","-t","900","-W","-s","|")
sqlcmd @args -i "docs\optim\_artifacts\01_distribucion_carteras.sql" -o "01.out.txt"
sqlcmd @args -i "docs\optim\_artifacts\02_tamanos_tablas.sql"        -o "02.out.txt"
sqlcmd @args -i "docs\optim\_artifacts\03_uso_indices.sql"           -o "03.out.txt"
sqlcmd @args -i "docs\optim\_artifacts\04_calificador_p90.sql"       -o "04.out.txt"
sqlcmd @args -i "docs\optim\_artifacts\06_singestion_comparador.sql" -o "06.out.txt"

# Plan XML (sin -W, con -y 0 -Y 0)
$argsPlan = @("-S","10.0.0.89,1433","-U","sa","-P","<clave>",
              "-d","DBCallCenter","-C","-l","30","-t","900","-y","0","-Y","0")
sqlcmd @argsPlan -i "docs\optim\_artifacts\05_plan_localizacion.sql" `
                 -o "docs\optim\_artifacts\05_plan_localizacion.sqlplan"
```

Cualquier re-medición debe registrarse en este mismo archivo con sello de fecha.
