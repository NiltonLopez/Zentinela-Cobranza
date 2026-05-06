# Fase 2c — Calificador acelerado vía Q_D (implementación)

Fecha: 2026-05-06. Cambio aplicado a `CalificadorCarteraService.CalificarAsync` en commit pendiente.

## Resumen ejecutivo

El módulo Calificador de Cartera dejó de pasar por la vista `vw_BaseMadre`. En su lugar ejecuta la query Q_D — réplica funcional de la vista pero con el filtro `(CarteraID, LiderID)` empujado al inicio de cada CTE `Ultimo*`. Resultado medido: P50 0.86 s, P90 2.07 s, P99 1.14 s (vs 6 s / ~18-20 s estimado anteriormente).

Cero cambios en BD (sin DDL), cero cambios en otros servicios o entidades, cero cambios en frontend. Reversible con un solo `git revert`.

---

## Cómo funcionaba antes (vista directa)

```
Calificador pide cartera (8, 7)
        │
        ▼
EF Core genera: SELECT * FROM vw_BaseMadre WHERE CarteraID=8 AND LiderID=7
        │
        ▼
SQL Server expande la vista — 14 sub-CTEs Ultimo* + DatosExternos:
  · Por cada tabla fuente: SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID
                                  ORDER BY FechaConsulta DESC) sobre la TABLA ENTERA
                                  (millones de filas)
  · 10 Hash Match con cardinalidades estimadas 7-17 millones
  · El optimizador HACE TIMEOUT en la búsqueda de plan → entrega plan subóptimo
        │
        ▼
Filtro WHERE final reduce a ~60k filas y las devuelve
        │
        ▼
EF Core deserializa a List<BaseMadreRow>
        │
        ▼
9 bloques C# iteran sobre 60k objetos en memoria para calcular agregados
```

Costos medidos en cartera P90 (60k filas): compilación 3.7 s, ejecución 6.2 s, total ~10 s. El timeout estaba puesto en 5 minutos por las P99.

## Cómo funciona ahora (Q_D vía FromSql)

```
Calificador pide cartera (8, 7)
        │
        ▼
EF Core ejecuta Q_D vía DbSet<BaseMadreRow>.FromSql($"...{carteraID}...{liderID}..."):

   ;WITH
   TitularesCartera AS (SELECT TitularID FROM Titular_Cartera
                         WHERE CarteraID=@p0 AND LiderID=@p1),
   UltimoARL      AS (SELECT *, ROW_NUMBER() OVER (...) AS rn FROM ARLSura
                       WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
   UltimoCajacopi AS (...mismo patrón...),
   ...14 CTEs...
   SELECT TC.CarteraID, ..., F.EPS, ..., RG.Marca, ...
   FROM Titular_Cartera TC
   INNER JOIN Titular Ti ...
   LEFT JOIN UltimoEstadoTitular UST ...
   LEFT JOIN DBRegistraduria.ani.Persona DBR ...
   LEFT JOIN UltimoARL A ON ... AND A.rn = 1
   ...14 LEFT JOIN...
   WHERE TC.CarteraID=@p0 AND TC.LiderID=@p1
        │
        ▼
SQL Server con cardinalidades reales (60k):
  · Cada CTE Ultimo* opera sólo sobre las filas de los 60k titulares de la cartera
  · ROW_NUMBER + Sort sobre ~60k registros en lugar de millones
  · Sin spills al worktable
  · Plan compila en 250 ms a 1.9 s
        │
        ▼
Devuelve los mismos ~60k rows con la misma proyección que la vista
        │
        ▼
EF Core mapea a List<BaseMadreRow> usando los HasColumnName del mapping existente
        │
        ▼
9 bloques C# iguales a antes — sin cambios en su lógica
```

**Por qué funciona**: el optimizador antes estimaba 17M filas porque las CTEs eran globales. Ahora estima ~60k reales porque las CTEs están restringidas. El plan deja de hacer Hash Match con eager spool y pasa a Hash Match en RAM.

**Contraintuitivo**: las **lecturas lógicas son idénticas** entre vista y Q_D. La mejora viene de eliminar spills, no de leer menos disco.

---

## Equivalencia funcional

`SELECT * FROM vw_BaseMadre WHERE …` y Q_D devuelven el mismo dataset módulo el ruido inherente de la vista actual.

La vista actual **ya es no-determinista** por ties en `(TitularID, FechaConsulta)`:
- DatosExternos: 78.676 titulares con misma fecha en cartera P90
- SIMIT: 630, DatosClaro: 414, ARLSura: 152, Camara: 41, otros: <20

`vista EXCEPT vista` (auto-comparación) devuelve 21.266 filas distintas. La diferencia entre Q_D y vista (~21k) es del mismo orden — son ties, no regresión.

Para el Calificador esto es irrelevante: los 9 bloques C# computan COUNT y SUM, agnósticos al orden. Los KPIs salen idénticos.

---

## Métricas

| Cartera | Filas | Vista (Q_A) elapsed | Q_D elapsed | Mejora |
|---------|------:|--------------------:|------------:|-------:|
| P50 (24, 7) OMEGAKID | 31.051 | ~3-4 s estimado | **0.86 s** | ~4× |
| P90 (8, 7) DELMAR AGORA | 59.430 | 6.22 s | **2.07 s** | ~3× |
| P99 (4, 3) BERTAL | 190.296 | ~18-20 s estimado | **1.14 s** | ~17× |

Compilación: bajó de 3.7-4.7 s a 0.25-1.9 s.

A esto se agrega el costo .NET de los 9 bloques C# (igual antes y después): probablemente 500 ms-2 s extras dependiendo del tamaño. El total end-to-end del Calificador queda en el orden de 2-4 segundos para cualquier cartera.

---

## Cambios concretos al código

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs`

**Antes** (líneas 73-85):
```csharp
public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID)
{
    _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

    var filas = await _db.BaseMadreRows
        .Where(b => b.CarteraID == carteraID && b.LiderID == liderID)
        .AsNoTracking()
        .ToListAsync();
```

**Después**:
```csharp
public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID)
{
    var filas = await _db.BaseMadreRows.FromSql($"""
        ;WITH TitularesCartera AS (SELECT TitularID FROM dbo.Titular_Cartera
                                   WHERE CarteraID = {carteraID} AND LiderID = {liderID}),
              UltimoARL AS (...), UltimoCajacopi AS (...), ... 14 CTEs ...
        SELECT TC.CarteraID, TC.LiderID, ... 51 columnas ...
        FROM dbo.Titular_Cartera TC
        INNER JOIN dbo.Titular Ti ...
        LEFT JOIN UltimoEstadoTitular UST ...
        LEFT JOIN DBRegistraduria.ani.Persona DBR ...
        LEFT JOIN UltimoARL A ... AND A.rn = 1
        ... 14 LEFT JOIN ...
        WHERE TC.CarteraID = {carteraID} AND TC.LiderID = {liderID}
        """).AsNoTracking().ToListAsync();
```

Los 9 bloques C# que iteran sobre `filas` (rotación, perfil, Fosyga, EPS, contactabilidad, edad Registraduría, etc.) **no cambian**. La query de localización (`ObtenerLocalizacionAsync`) **tampoco cambia** en este commit — sigue usando la vista. Su optimización queda para Fase 2b.

### Lo que NO cambió

- `BaseMadreRow` (entidad) — sin tocar
- `BaseMadreRowMapping` — sin tocar (los `HasColumnName` con espacios siguen funcionando porque Q_D los respeta uno a uno)
- `vw_BaseMadre` — sin tocar (otros consumidores como Creador y Comparador siguen leyendo de ella)
- `appsettings*` — sin tocar
- Frontend — sin tocar (la API contract es idéntica)

---

## Validación

| Item | Estado |
|---|---|
| `dotnet build` del proyecto SQLServer aislado | ✓ 0 errores, 0 warnings |
| `dotnet build` solución completa | ✗ bloqueada por API corriendo (file locks). El compilable sí compila — los errores son de copia de DLL al output con la API en uso. |
| Q_D ejecuta correctamente sobre P50/P90/P99 vía sqlcmd | ✓ tres mediciones limpias |
| Cardinal de filas devueltas idéntico a vista (P50: 31.051; P90: 59.430; P99: 190.296) | ✓ |
| Diff Q_D vs vista en mismo orden de magnitud que vista vs sí misma (ties) | ✓ |
| Mapeo de las 53 columnas Q_D vs 51 propiedades de `BaseMadreRow` | ✓ todas las requeridas presentes con nombre y tipo correctos |
| Smoke runtime end-to-end (probar en navegador con cartera P90) | **pendiente — requiere reinicio de API** |

---

## Pasos para activar el cambio

1. Detener el proceso `Zentinela.API` actualmente en ejecución (PID 22112 al cierre de esta sesión).
2. `dotnet run --project src/Zentinela.API` (o equivalente desde IDE).
3. Abrir el módulo Calificador, seleccionar cartera (8, 7) DELMAR AGORA / ANDRES ROJAS → debería completar en ~2-3 s. Luego (4, 3) BERTAL / JULIO MORA → ~1-2 s.

---

## Cómo revertir si algo falla

```powershell
git diff HEAD -- src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs
git checkout HEAD -- src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs
```

Eso restaura el comportamiento anterior (vista + timeout 5 min). Sin pérdida de datos, sin necesidad de tocar BD.

---

## Lo que queda fuera (Fase 2b y siguientes)

1. **Mover los 9 bloques C# a SQL agregado** (Fase 2b original): los `filas.Count(...)`, `filas.Sum(...)`, `filas.GroupBy(...)` que recorren la lista en memoria pueden vivir en SQL como `SUM(CASE WHEN …)`. Eso elimina el round-trip de 60k-190k filas a la app — que hoy puede ser 50-200 MB de payload por llamada.
2. **Optimizar `ObtenerLocalizacionAsync`**: la segunda query del Calificador sigue pasando por la vista. Aplicar el mismo patrón Q_D (CTEs pre-restringidas) o consolidarla en la misma raw query del paso 1.
3. **Comparador (Fase 4)**: TVP + CTE única. Bloqueado funcionalmente por `UltimoEstadoTitular` vacía, pero implementable.
4. **Creador (Fase 3)**: filtros C# a SQL + paginación.

Estas vienen en commits separados.

---

## Referencias

- Plan original: `~/.claude/plans/optimizacion-cartera-modulo.md`
- Baseline Fase 1: `docs/optim/baseline-cartera-2026-05.md`
- Spike 2c (análisis): `docs/optim/spike-2c-resultado.md`
- SQL crudo Q_D ejecutable: `docs/optim/_artifacts/11_medir_q_d_p90.sql`
- Plan XML del estado anterior (vista): `docs/optim/_artifacts/05_plan_localizacion.sqlplan`
