# Opción B implementada — TVF compartido por Calificador y Creador

Fecha: 2026-05-06.

## Resumen

Tras Fase 2c (SQL inline en Calificador), se promovió la query Q_D a un **inline TVF** `dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)` aplicado a DBCallCenter. **Calificador y Creador ahora consumen el mismo TVF**. La duplicación que tenía el SQL inline desaparece, y el Comparador en futuro lo podrá llamar también.

## Migración aplicada

Archivo: `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`

- DDL: `CREATE FUNCTION dbo.fn_BaseMadrePorCartera (@CarteraID int, @LiderID int = NULL) RETURNS TABLE AS RETURN (...)`
- Tipo: **Inline TVF** (RETURNS TABLE, sin BEGIN/END). El optimizador la inlinea como si su cuerpo se copiara al call site → preserva batch mode y cardinalidades reales.
- `@LiderID` admite NULL → wildcard (caso admin/director consultando una cartera sin restricción de líder).
- Idempotente (DROP IF EXISTS + CREATE), reversible (DROP FUNCTION).
- Aplicada en DBCallCenter el 2026-05-06; documentada en `docs/schema.md` sección "Funciones".

## Métricas medidas con el TVF

| Caso | Filas | Compilación | Ejecución elapsed |
|------|------:|------------:|------------------:|
| P50 (24, 7)              | 31.051  | 1.95 s | **1.75 s** |
| P90 (8, 7)               | 59.430  | 1.89 s | **2.30 s** |
| P99 (4, 3)               | 190.296 | 1.95 s | **2.43 s** |
| Cartera 22 admin (NULL líder, todos los líderes) | 395.159 | 4.04 s | **3.87 s** |

Las cifras son ligeramente peores (~10-30%) que Q_D inline porque ejecuté con `OPTION (RECOMPILE)` para invalidar plan cache. En producción los planes se cachean y subsecuentes llamadas evitan el costo de compilación. Vista actual (sin TVF): P90 ~6.2 s + 3.7 s compilación; P99 estimado ~18-20 s.

## Cambios C#

### `CalificadorCarteraService.CalificarAsync` (líneas 73-84)

**Antes** (~95 líneas de SQL inline replicando la vista):
```csharp
var filas = await _db.BaseMadreRows.FromSql($"""
    ;WITH TitularesCartera AS (...), UltimoARL AS (...), ... 14 CTEs ...
    SELECT TC.CarteraID, ..., RG.Placa
    FROM dbo.Titular_Cartera TC
    INNER JOIN dbo.Titular Ti ...
    LEFT JOIN UltimoEstadoTitular UST ...
    LEFT JOIN UltimoARL A ... AND A.rn = 1
    ... 14 LEFT JOIN ...
    WHERE TC.CarteraID = {carteraID} AND TC.LiderID = {liderID}
    """).AsNoTracking().ToListAsync();
```

**Ahora**:
```csharp
var filas = await _db.BaseMadreRows
    .FromSql($"SELECT * FROM dbo.fn_BaseMadrePorCartera({carteraID}, {liderID})")
    .AsNoTracking()
    .ToListAsync();
```

### `BaseMadreRepository.ObtenerAsync` (líneas 61-67)

**Antes**:
```csharp
var query = _context.BaseMadreRows
    .Where(bm => bm.CarteraID == carteraID);

if (liderID.HasValue)
    query = query.Where(bm => bm.LiderID == liderID.Value);
```

**Ahora**:
```csharp
var query = _context.BaseMadreRows
    .FromSql($"SELECT * FROM dbo.fn_BaseMadrePorCartera({carteraID}, {liderID})");
```

El resto del repositorio (8+ filtros adicionales: `ultimoPerfil`, `mejorPerfil`, `tipoAfiliado`, `estadoFosyga`, fuentes celular, fuentes email, etc.) **se mantiene intacto**. EF Core compone los `.Where(...)` posteriores como un `SELECT * FROM (TVF) AS t WHERE ...` automáticamente — la ergonomía LINQ se preserva.

Los filtros C# residuales (saldoMinimo, rotacionMaxima, promesasVigentes, coincidencias) tampoco cambian. Su optimización a SQL es **Fase 3a, fuera de scope de este commit**.

## Validación

| Item | Estado |
|---|---|
| TVF compila + smoke (4 carteras) devuelve filas correctas | ✓ |
| Equivalencia funcional con Q_D inline (mismas cardinalidades) | ✓ |
| `dotnet build src/Zentinela.API/Zentinela.API.csproj` | ✓ 0 warnings, 0 errors |
| `BaseMadreRowMapping` no requiere cambios (el TVF respeta los nombres con espacios) | ✓ |
| Build solución completa | ⚠ Tests rotos PRE-EXISTENTE (commit `c81b8c1` los rompió antes; ver "Deuda de tests" abajo) |
| Smoke runtime end-to-end | **pendiente — la API debe iniciarse** |

## Deuda de tests detectada (no introducida por este commit)

`tests/Zentinela.API.Tests/` contiene 12 errores de compilación pre-existentes:
- `CarterasControllerTests.cs`: usa `ICarteraRepository` donde el ctor actual espera `ICarteraService`
- `AuthServiceTests.cs`: usa `IConfiguration` donde el ctor actual espera `IJwtTokenGenerator`; usa `LoginRequest.Email` que ya no existe

Estos son rot del refactor `c81b8c1 refactor: Clean Architecture multi-proyecto`. Quedan fuera del scope de la optimización Cartera. Recomendación: o se actualizan los tests, o se quita el csproj de la solución hasta que se actualicen. Decisión queda al usuario.

## Plan de activación en producción

1. La migración SQL ya quedó aplicada en DBCallCenter — **no hace falta repetirla**.
2. Detener el proceso `Zentinela.API` (si quedó corriendo de la sesión previa).
3. `dotnet run --project src/Zentinela.API`.
4. Smoke test:
   - **Calificador**: cartera (8, 7) DELMAR AGORA / ANDRES ROJAS → debería completar en ~2-3 s.
   - **Creador**: cartera (8, 7) y luego (22, *) administrador (sin líder seleccionado) → ambos deberían cargar sin freezing perceptible al mover filtros.

## Reversión completa

Si algo falla:

```powershell
# 1) Revertir cambios C# (vuelve a usar vw_BaseMadre directa con timeout 5min)
git checkout HEAD -- `
    src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs `
    src/Infrastructure/Zentinela.Infrastructure.SQLServer/Repositories/BaseMadreRepository.cs

# 2) Drop del TVF (BD)
sqlcmd -S "10.0.0.89,1433" -U sa -P "..." -d DBCallCenter -C -Q `
  "IF OBJECT_ID('dbo.fn_BaseMadrePorCartera','IF') IS NOT NULL DROP FUNCTION dbo.fn_BaseMadrePorCartera"
```

Sin pérdida de datos, sin tocar la vista existente.

## Lo que queda fuera (próximos commits)

- **Fase 2b**: mover los 9 bloques C# del Calificador (`filas.Count(...)`, `filas.Sum(...)`, GroupBy) a SQL agregado dentro o además del TVF. Eliminaría el round-trip de 60-190k filas a la app.
- **Fase 3a**: mover los filtros string del Creador (saldoMinimo TRY_CAST, rotacionMaxima TRY_CAST, promesasVigentes TRY_CONVERT date) a SQL. Hoy se aplican en C# tras el `ToListAsync` (BaseMadreRepository líneas 117-145).
- **Fase 3b**: aplicar `Take(LimitePreview)` en SQL en lugar de `.ToListAsync()` completo.
- **Fase 4**: TVP + CTE única en Comparador. El TVF ya está disponible para usarlo cuando llegue el momento.

## Artefactos

- DDL aplicado: `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`
- Smoke + perf TVF: `docs/optim/_artifacts/17_tvf_smoke_y_perf.sql` + `.out.txt`
- Schema actualizado: `docs/schema.md` sección "Funciones (Functions)"
- Spike y baseline: `docs/optim/spike-2c-resultado.md`, `docs/optim/baseline-cartera-2026-05.md`
- Implementación 2c original (sustituida): `docs/optim/fase-2c-implementado.md`
