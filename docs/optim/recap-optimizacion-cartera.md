# Recap de la optimización del módulo Cartera

> Documento didáctico. No es referencia de la implementación final (eso vive
> en `baseline-cartera-2026-05.md`, `spike-2c-resultado.md`,
> `fase-2c-implementado.md`, `opcion-b-implementada.md`). Aquí hilamos los
> errores y aciertos para aprender de ellos.

---

## Estado al cierre

| Sub-módulo | ¿Optimizado? | Latencia esperada | Commit clave |
|---|---|---|---|
| **Calificador** | ✅ Sí — validado runtime | ~6 s end-to-end (reportado por el usuario) | `cdfc9ef` (Fase 2b) sobre `a0efa93` (TVF) + `95ea28a` (localización vía TVF) |
| **Creador** | ✅ Sí — pendiente smoke runtime tras reinicio | <3 s preview, <5 s admin "Completo" | `3758804` (TOP + filtros en SQL) |
| **Comparador** | ⛔ No — fuera de scope | n/a | UltimoEstadoTitular está **vacía**: el módulo es operativamente inservible hasta que se repopule. La optimización estructural (TVP, CTE única) está pendiente como Fase 4 del plan original. |

Bonus aparte de optimización (commit `73bc757`): el selector del Creador
ahora se construye desde `Lider_Cartera` con entradas `Cartera — Líder` y
una agregada `Cartera Completo` para Admin/Director. No es perf, es feature
que aprovecha que el TVF acepta `LiderID = NULL` como wildcard.

---

## Cronología de los commits

```
fab2770  (estado base, antes de empezar)
└─ a0efa93  perf(cartera): TVF dbo.fn_BaseMadrePorCartera reemplaza vw_BaseMadre
└─ 937ecf3  docs(cartera): baseline + spike 2c + Opción B
└─ 95ea28a  perf(cartera): ObtenerLocalizacionAsync usa el TVF
└─ 7f0e7f2  docs(cartera): smoke + perf de localización
└─ cdfc9ef  perf(cartera): Fase 2b — agregados del Calificador en SQL
└─ 3758804  fix(creador): timeout 30s — TOP y filtros en SQL, sin composición LINQ
└─ 73bc757  feat(creador): selector desde Lider_Cartera con entrada "Completo"
```

---

## Las cinco lecciones que aprendimos

### Lección 1 — Mide antes de tocar. La hipótesis a priori puede estar 100% mal.

El plan original (`~/.claude/plans/optimizacion-cartera-modulo.md`) atribuía
el cuello del Calificador a los **14 `LEFT JOIN (SELECT DISTINCT TitularID FROM Tabla)`**
que aparecen en la query de localización. Hipótesis razonable mirando el código.

La **medición real** (Fase 1, `baseline-cartera-2026-05.md`) mostró otra
cosa: las 14 `DISTINCT` apenas suman cardinalidad (~70k filas estimadas) y
usan los índices `IX_*_TitularID_Fecha` correctamente. El verdadero
culpable era **`vw_BaseMadre`** misma:

- Tiene **15 SORT + 15 ROW_NUMBER + 15 Filter** internos (uno por fuente).
- Genera **10 Hash Match con cardinalidades estimadas de 7-17 millones de filas**
  ANTES de filtrar por `CarteraID/LiderID`.
- El optimizador **aborta la búsqueda de plan por timeout**
  (`StatementOptmEarlyAbortReason="TimeOut"`) y entrega "el primer plan
  razonable que encuentre".

**Si hubiéramos seguido el plan a ciegas, habríamos optimizado el lado
correcto del 5% del problema.** La regla "mide antes de tocar" del plan
nos salvó porque la fase 1 estaba antes que cualquier cambio de código.

**Llevarse**: cuando un experto (o tú mismo) dice "el problema es X", ese
es el punto de PARTIDA de la investigación, no la conclusión. Captura
`STATISTICS TIME ON`, `STATISTICS IO ON` y un plan XML antes de tocar
nada. Es 30 minutos de trabajo y a veces redirige todo.

---

### Lección 2 — CTEs pre-restringidas vs OUTER APPLY: ninguno es universal.

En el spike 2c probamos dos formas de evitar `vw_BaseMadre`:

**Q_C — `OUTER APPLY (TOP 1 *)` × 14 fuentes**

```sql
SELECT *
FROM Titular_Cartera TC
JOIN Titular Ti ...
OUTER APPLY (SELECT TOP 1 * FROM Cajacopi   WHERE TitularID = TC.TitularID ORDER BY FechaConsulta DESC) CP
OUTER APPLY (SELECT TOP 1 * FROM Camara     WHERE TitularID = TC.TitularID ORDER BY FechaConsulta DESC) C
... (14 APPLYs) ...
WHERE TC.CarteraID = @C AND TC.LiderID = @L
```

Sobre la cartera P90 completa (60k titulares): **242 segundos**. 40× peor
que la vista. El plan rompe el batch mode y hace ~840k Key Lookups (60k
filas × 14 fuentes).

**Q_D — CTEs `Ultimo*` pre-restringidas al universo de la cartera**

```sql
WITH TitularesCartera AS (
    SELECT TitularID FROM Titular_Cartera WHERE CarteraID=@C AND LiderID=@L
),
UltimoCajacopi AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
    FROM Cajacopi WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)
),
... (14 CTEs)
SELECT ... FROM TitularesCartera ... LEFT JOIN UltimoCajacopi ... AND rn=1
```

P50 0.86 s, **P90 2.07 s** (vs 6.22 s de la vista, ~3×), P99 1.14 s
(vs ~18-20 s estimado, ~17×). **Las lecturas lógicas son idénticas a la
vista** — la mejora viene de eliminar spills al worktable porque el
optimizador ahora estima cardinalidades reales (~60k) en lugar de
infladas (millones).

Pero después, en el Creador con `TOP 10`, **Q_D fue mala**: el TVF
materializa las 60-395k filas de las CTEs antes de aplicar `TOP`, así que
hace todo el trabajo para devolver 10 filas.

**Mientras que ahí `OUTER APPLY` brilla**: para `TOP 10` hace 10 × 14 = 140
seeks, <1 ms.

**Llevarse**: el patrón óptimo depende del **factor de selectividad final**.
- "Necesito todo el universo" (Calificador) → CTEs pre-restringidas.
- "Necesito una muestra pequeña" (Creador preview) → OUTER APPLY (TOP 1) si
  el outer SELECT también limita; o TVF + TOP en SQL aceptando que paga
  el costo del universo (lo que termina haciendo el Creador hoy — sigue
  siendo <5 s porque la CTE es eficiente).

No hay un "mejor patrón" universal.

---

### Lección 3 — EF Core composición sobre `FromSql(TVF)` rompe garantías que parecen obvias.

El primer commit del Creador (`a0efa93`) hizo esto:

```csharp
var query = _context.BaseMadreRows
    .FromSql($"SELECT * FROM dbo.fn_BaseMadrePorCartera({carteraID}, {liderID})");

if (!string.IsNullOrWhiteSpace(ultimoPerfil))
    query = query.Where(b => b.UltimoPerfil == ultimoPerfil);
// ... más .Where() ...

var todasLasFilas = await query.AsNoTracking().ToListAsync();
// luego .Take(10) en C#
```

Estructura razonable, **funciona** en sintaxis. EF Core 9 wrappea como:

```sql
SELECT [t].* FROM (SELECT * FROM dbo.fn_BaseMadrePorCartera(@p0, @p1)) AS [t]
WHERE [t].[Ultimo Perfil] = @p2
```

**El usuario nos reportó timeout de 30 segundos**. Diagnóstico:
- El `Take(10)` se aplicaba en C# DESPUÉS de un `ToListAsync` que
  materializaba **todas** las filas (60-395k filas, 50-200 MB de payload).
- Las CTEs internas del TVF se restringen al universo COMPLETO de la
  cartera. El `TOP` exterior no se propaga a esas CTEs.
- 30s de transfer + map + take = timeout.

**Solución (`3758804`)**: dejar de componer LINQ. Construir el SQL crudo
con todos los filtros embebidos y `TOP` directo en SQL:

```csharp
var sql = $"""
    SELECT TOP ({limite}) *
    FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
    WHERE [Ultimo Perfil] = @UltimoPerfil  -- o lo que aplique
      AND TRY_CAST([Saldo] AS decimal(18,2)) >= @SaldoMinimo
      AND TRY_CONVERT(date, [Fecha Promesa], 103) >= CAST(GETDATE() AS date)
      AND ...
    """;

var filas = await _context.BaseMadreRows
    .FromSqlRaw(sql, parameters)  // sin .Where() encima
    .AsNoTracking()
    .ToListAsync();
```

Sólo se transfieren `limite` filas (≤10 para preview).

**Llevarse**:
- Cuando tu fuente es un TVF, **no compongas LINQ encima** si necesitas
  pushdown de `TOP` o filtros traducibles. Construye el SQL completo y
  pásalo a `FromSqlRaw` (o `SqlQueryRaw` para escalares).
- Aprovecha que SQL Server tiene primitivas que C# replica torpemente:
  `TRY_CAST`, `TRY_CONVERT(date, ..., 103)` traducen `decimal.TryParse(InvariantCulture)`
  y `DateOnly.TryParseExact("dd/MM/yyyy")` exactos. Si tus columnas son
  string serializadas con cultura conocida, agrégalas al WHERE.
- Para los nombres de columnas en `WHERE` cuando vienen de input del
  usuario (fuentes celular, fuentes email), usa **whitelist estática**
  con `switch`. Nunca concatenes input crudo:
  ```csharp
  static string? ColumnaCelular(string fuente) => fuente switch
  {
      "Cajacopi"   => "[Celular Cajacopi]",
      "Camara"     => "[Celular Camara]",
      // ...
      _            => null,  // input desconocido → ignorado
  };
  ```

---

### Lección 4 — Cuando hay que devolver muchos agregados y el ORM mapea filas costosamente, ADO.NET multi-result-set le gana.

El Calificador tras Fase 2c hacía **dos** queries:
1. `SELECT * FROM dbo.fn_BaseMadrePorCartera(@C, @L)` → 60-190k filas.
2. La query de localización con 14 LEFT JOIN DISTINCT.

Y luego **9 bloques de C#** que iteraban sobre las 60-190k filas en
memoria para calcular `Count`, `Sum`, `Average`, `GroupBy`.

Costo dominante en producción: la transferencia + EF Core mapping de
60-190k entidades + iteración LINQ. Especialmente para P99 (190k filas =
~190 MB de payload).

Fase 2b (`cdfc9ef`) consolida los 9 bloques C# en un único batch SQL que
emite **8 result sets** sobre una `#temp` materializada una sola vez:

```sql
SELECT [Saldo], [Fecha Promesa], ... TRY_CAST([Saldo] AS decimal(18,2)) AS _Saldo, ...
INTO #t FROM dbo.fn_BaseMadrePorCartera(@C, @L);

-- Result set 1: escalares (1 fila con 17 columnas)
SELECT @Total AS Total, ISNULL(SUM(_Saldo),0) AS SaldoTotal, ... FROM #t;

-- Result set 2: desgloseRotacion (5 filas máx)
SELECT Etiqueta, COUNT(*) AS Cantidad, ... FROM ... GROUP BY ... ORDER BY ...;

-- ... 6 result sets más ...

DROP TABLE #t;
```

**El problema**: EF Core no soporta multi-result-set sobre `FromSql`.
**La solución**: bajar a ADO.NET puro pero usando la conexión que EF
Core ya gestiona:

```csharp
var conn = _db.Database.GetDbConnection();
var wasOpen = conn.State == ConnectionState.Open;
if (!wasOpen) await conn.OpenAsync();
try
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sqlBatch;
    AddParam(cmd, "@CarteraID", carteraID);
    AddParam(cmd, "@LiderID", liderID);

    await using var reader = await cmd.ExecuteReaderAsync();

    // Result set 1
    await reader.ReadAsync();
    dto.SaldoTotal = reader.GetDecimal(reader.GetOrdinal("SaldoTotal"));
    // ... etc

    // Result set 2
    await reader.NextResultAsync();
    dto.DesgloseRotacion = await ReadDesgloseAsync(reader);

    // Result sets 3..8
    await reader.NextResultAsync(); dto.DesglosePerfil = await ReadDesgloseAsync(reader);
    // ... etc
}
finally { if (!wasOpen) await conn.CloseAsync(); }
```

Resultado: en lugar de transferir 60-190k filas y mapearlas, se
transfieren ~50 filas pre-cocinadas y el DTO se hidrata directo. La clase
`CalificadorCarteraService` pasó de 591 a ~300 líneas.

**Llevarse**:
- ORMs son geniales para CRUD estándar. Para "dame un dashboard con 8
  agregados sobre el mismo dataset", saca la queja directa a SQL.
- ADO.NET no es retroceso — es la herramienta correcta cuando necesitas
  multi-result-set. EF Core te presta la conexión sin problemas.
- Pre-parsear columnas string a tipos numéricos UNA VEZ en `#t` (con
  `TRY_CAST`, `TRY_CONVERT`) ahorra tiempo en cada uno de los agregados
  que las usan después.

---

### Lección 5 — Una función SQL bien diseñada amortiza varios consumidores.

Tras el spike 2c teníamos dos opciones:
- **Opción A**: SQL inline en cada servicio que consuma la base de cartera.
- **Opción B**: encapsular en un iTVF `dbo.fn_BaseMadrePorCartera(@C, @L)`.

Empezamos con A (commit `a0efa93`). Cuando preguntaste por el Creador,
nos dimos cuenta de que la misma reescritura iba a tener que duplicarse.
Pasamos a B (commit `937ecf3` re-aplica el patrón sobre el TVF).

Hoy el TVF lo usan:
- `CalificadorCarteraService.CalificarAsync` (Fase 2c)
- `CalificadorCarteraService.ObtenerLocalizacionAsync` (commit `95ea28a`)
- `BaseMadreRepository.ObtenerAsync` (Creador, commit `3758804`)
- `CreadorZentinelaService.ObtenerOpcionesAsync` (indirectamente — el
  selector aprovecha que el TVF acepta `@LiderID = NULL` para "Cartera
  Completo")

**Cuatro consumidores con un solo punto de mantenimiento**.

**Llevarse**: cuando una optimización de SQL crudo va a aplicar a >1
consumidor, conviene encapsularla. Un **inline TVF** (`RETURNS TABLE`,
sin `BEGIN/END`) se inlinea en el plan del caller, así que no paga
overhead. **Stored procedure** o **multi-statement TVF** sí pagarían y
no se beneficiarían del optimizador del caller.

Convención que aprendimos sobre la marcha:
- `@LiderID int = NULL` con `WHERE (@LiderID IS NULL OR LiderID = @LiderID)`
  permite usar el mismo TVF para "todos los líderes" y "uno específico".
- Documentar la migración SQL en `docs/sql/migrations/AAAA-MM-DD_<nombre>.sql`,
  idempotente (`DROP IF EXISTS + CREATE`), reversible (`DROP FUNCTION`).
- Documentar el objeto en `docs/schema.md` para que el siguiente que
  llegue al proyecto sepa que existe.

---

## Lo que NO se optimizó (y por qué)

### Comparador

Bloqueado funcionalmente. La tabla `UltimoEstadoTitular` está **vacía**
(0 filas, último update reciente — se trunca y no se repopula). Sin
datos no hay nada que comparar. Los cambios estructurales del plan
(TVP en lugar de `IN (@p0...@p9999)`, CTE única en lugar de 4 queries)
siguen pendientes como Fase 4 — implementables hoy aunque no medibles
hasta que la tabla se repopule.

### Tests rotos

`tests/Zentinela.API.Tests/` tiene **12 errores de compilación
pre-existentes** desde el commit `c81b8c1 refactor: Clean Architecture
multi-proyecto`. No los introdujimos nosotros y no se reparan con esta
serie de commits. Deuda separada.

### `vw_BaseMadre` indexada

La opción "vista indexada con `SCHEMABINDING` + clustered index" la
dejamos WONTFIX. La vista la consumen procesos externos (ETL); cualquier
cambio se discute aparte. Y con el TVF ya tenemos el rendimiento que
necesitábamos sin tocar la vista.

---

## Cómo verificar

### Calificador (validado runtime, 2026-05-07)
1. `dotnet run --project src/Zentinela.API`.
2. Login y abrir Calificador con cartera (8, 7) DELMAR AGORA / ANDRES.
3. Esperado: ~6 s end-to-end (incluye localización).

### Creador (pendiente smoke runtime)
1. Reiniciar API.
2. **Como Líder** (ANDRES): dropdown muestra solo sus carteras con
   etiqueta `NombreCartera`. Preview en <3 s.
3. **Como Admin/Director** sobre cartera Bertal (4): dropdown muestra
   4 entradas individuales (`Bertal — Julio Mora`, `— Daniela Herrera`,
   `— Nora Escobar`, `— Base General`) **+ una `Bertal Completo`**.
   - Seleccionar `Bertal — Julio Mora` → preview limita a sus
     titulares (LiderID=3).
   - Seleccionar `Bertal Completo` → preview trae todos los titulares
     de Bertal.
   - Verificar que carteras sin líderes asignados **NO aparecen** con:
     ```sql
     SELECT NombreCartera FROM Cartera c
     WHERE c.Activa = 1
       AND NOT EXISTS (SELECT 1 FROM Lider_Cartera lc WHERE lc.CarteraID = c.ID);
     ```

---

## Cómo revertir todo

Cada commit es revertible aisladamente con `git revert <sha>`. Si se
quiere bajar **todo** y volver al estado pre-optimización:

```powershell
# Revertir los commits de optimización Cartera (en orden inverso)
git revert 73bc757 3758804 cdfc9ef 95ea28a a0efa93

# Drop del TVF en BD
sqlcmd -S "10.0.0.89,1433" -U sa -P "..." -d DBCallCenter -C -Q `
  "IF OBJECT_ID('dbo.fn_BaseMadrePorCartera','IF') IS NOT NULL DROP FUNCTION dbo.fn_BaseMadrePorCartera"
```

(Los commits de docs `937ecf3` y `7f0e7f2` son sólo prosa, no
necesitan revertir si quieres conservar el historial técnico.)

---

## Recursos

- **Plan original**: `~/.claude/plans/optimizacion-cartera-modulo.md`
- **Baseline (Fase 1)**: `docs/optim/baseline-cartera-2026-05.md`
- **Spike 2c**: `docs/optim/spike-2c-resultado.md`
- **Implementación inline (Fase 2c intermedia)**: `docs/optim/fase-2c-implementado.md`
- **Opción B (TVF compartido)**: `docs/optim/opcion-b-implementada.md`
- **Migración SQL**: `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`
- **Schema**: `docs/schema.md` sección "Funciones (Functions)"
- **Artefactos crudos** (21 SQLs ejecutados + outputs + plan XML):
  `docs/optim/_artifacts/`

---

## Si en seis meses te preguntas "¿qué hicimos exactamente?"

Lee este archivo primero. Cada lección apunta al commit y al artefacto
que la justifica. Si necesitas más detalle, los docs en `docs/optim/`
tienen las mediciones crudas. Si necesitas el código exacto que se
ejecuta hoy, mira:

- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs`
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Repositories/BaseMadreRepository.cs`
- `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`

Y si necesitas el código que estuvo brevemente y se descartó (Q_C con
OUTER APPLY sobre 60k titulares, Fase 2c con SQL inline antes del TVF),
está en el historial git — revisa `937ecf3..3758804` con `git log -p`.
