# Spike 2c — resultado

Fecha: 2026-05-06. Sujetos: P50 (24, 7) 31.051 filas, P90 (8, 7) 59.430 filas, P99 (4, 3) 190.296 filas. DB: DBCallCenter.

## Veredicto

**Q_D válido — 2.5× mejor en P90, ~17× mejor en P99**. Implementable como SQL raw vía `_db.Database.SqlQuery<BaseMadreRow>(...)` o como inline TVF reusable. Sin tocar la vista, sin tocar las tablas, sin DDL.

## Tres variantes evaluadas

| Variante | Estrategia | Resultado P90 (elapsed) |
|----------|------------|------------------------:|
| Q_A (baseline) | `SELECT * FROM vw_BaseMadre WHERE CarteraID=… AND LiderID=…` | **6.218 ms + 3.719 ms compilación** |
| Q_C  | `OUTER APPLY (TOP 1 *)` × 14, sin pasar por la vista | **242.927 ms** ← descartada |
| Q_D  | Réplica de la vista, pero con `WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)` añadido a cada CTE `Ultimo*` | **2.065 ms + 1.912 ms compilación** |

Por qué Q_C resultó 40× peor: cada `OUTER APPLY (SELECT TOP 1 * …)` rompe el batch mode de la vista, fuerza Key Lookup × 14 fuentes × 59k filas ≈ 832k seeks + lookups, y la Worktable spilea con 35M lecturas lógicas. El plan de la vista existente, aunque escanea las tablas completas, las accede en row-store batch mode que es eficiente para cardinalidades grandes.

Por qué Q_D funciona: las CTEs pre-restringidas le dan al optimizador una estimación de cardinalidad **real** (~59k titulares) en lugar de inflada (millones). Eso elimina los Hash Match con worktables grandes y mantiene el batch mode. **Lecturas lógicas son idénticas a Q_A** — la mejora viene de eliminar spills, no de leer menos.

## Equivalencia funcional

`Q_D EXCEPT vista` = 21.512 filas; `vista EXCEPT Q_D` = 21.940 filas. Aparenta no equivalencia, pero **la vista misma es no-determinista** (`vista EXCEPT vista` = 21.266 filas). Causa: ties en `(TitularID, FechaConsulta)` en las tablas fuente — ROW_NUMBER asigna `rn=1` a una fila distinta cada ejecución cuando hay empates.

Ties detectados (cartera P90):

| Tabla            | Titulares con ties |
|------------------|-------------------:|
| DatosExternos    | 78.676             |
| SIMIT            | 630                |
| DatosClaro       | 414                |
| ARLSura          | 152                |
| Camara           | 41                 |
| Positiva         | 13                 |
| PositivaEmpresas | 5                  |
| Cajacopi         | 4                  |
| Fosyga           | 3                  |
| Resto            | 0                  |

La diferencia entre Q_D y vista (~21k) es del mismo orden que el ruido propio de la vista (~21k). Para los **agregados COUNT/SUM** que ejecuta el Calificador, el orden de los registros es irrelevante — los conteos son idénticos. La equivalencia funcional se preserva.

Si en el futuro se requiere determinismo, se agrega un tiebreaker estable a la vista y a Q_D simultáneamente — por ejemplo `ORDER BY FechaConsulta DESC, ID DESC`. Fuera de scope del spike.

## Caminos de implementación

### Opción A — SQL raw inline en `CalificadorCarteraService`

Reemplazar `_db.BaseMadreRows.Where(b => b.CarteraID == carteraID && b.LiderID == liderID).ToListAsync()` (línea 82-85 del servicio) con `_db.Database.SqlQuery<BaseMadreRow>($"""…Q_D…""").ToListAsync()`. Mantiene EF Core, requiere mapear todas las columnas a `BaseMadreRow` (ya están — la entidad mapea contra `vw_BaseMadre` directamente).

Pro: cambio puntual, reversible por commit.
Con: duplicación parcial de la vista en SQL embebido.

### Opción B — Inline TVF `dbo.fn_BaseMadrePorCartera(@C, @L)`

Crear una iTVF que devuelve la misma proyección de la vista pero con el filtro CarteraID/LiderID empujado dentro. EF Core la consume con `_db.Database.SqlQuery` o un mapping a un `DbSet<BaseMadreRow>` parametrizado.

Pro: la mejora queda **disponible para Comparador y Creador** sin reescribir nada en sus servicios. Una sola fuente de verdad.
Con: cambio de schema (DDL) — requiere migración o script aplicado manualmente. Si se acepta DDL, este es el camino más limpio.

### Opción C — Mantener la vista + agregar otra vista filtrable

Crear `vw_BaseMadre_PorCartera` con `WHERE EXISTS (SELECT 1 FROM Titular_Cartera WHERE …)`. **NO funciona** — no podemos pasar parámetros a la vista, así que el filtro tendría que estar en el WHERE externo, lo que reproduce el problema actual.

**Recomendación**: **Opción A primero** (rápido, reversible, no toca DB). Si tras Fase 2 completa y validación se quiere amortizar para Comparador/Creador, migrar a Opción B en un commit aparte.

## Bonus para Fase 2

- **Quitar `SetCommandTimeout(5 minutes)`** — innecesario con Q_D. Vuelve al default de 30s.
- **`OPTION (RECOMPILE)`** en la query: el plan se recompila en cada ejecución. Esto evita el plan-cache caliente entre carteras de tamaños distintos. Como el costo de compilación bajó a ~250 ms (vs 3.7s), es asumible y previene parameter sniffing.
- La query de localización (Q_B) también pasa por la vista. **Pre-restringir las 14 LEFT JOIN DISTINCT con `WHERE TitularID IN (…)` no es necesario** — esos joins ya operan sobre el set filtrado de la vista. Pero si vamos por Opción A, podemos consolidar Q_D con la lógica de Q_B en una **única query raw** que devuelva el dataset + los agregados de localización en el mismo round-trip. Eso es Fase 2b y vale la pena medirlo.

## Artefactos

- `_artifacts/07_vw_basemadre_def.sql` — definición de la vista capturada con sp_helptext.
- `_artifacts/08_q_c_replica.sql`, `_artifacts/09_smoke_q_c.sql` — Q_C (descartada).
- `_artifacts/10_medir_q_c_p90.sql` + `.out.txt` — medición Q_C P90 (242s).
- `_artifacts/11_medir_q_d_p90.sql` + `.out.txt` — medición Q_D P90 (2s).
- `_artifacts/12_diff_q_d_vs_view.sql` + `.out.txt` — diff Q_D vs vista en P90.
- `_artifacts/13_vista_autoconsistencia.sql` + `.out.txt` — auto-EXCEPT de la vista.
- `_artifacts/14_q_d_p50_p99.sql` + `.out.txt` — medición Q_D en P50 y P99.
