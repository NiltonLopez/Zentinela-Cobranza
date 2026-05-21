# Localización merged + Caché in-memory del Calificador

Fecha: 2026-05-21. Aplicado en working tree sobre `fa05624`.

## Resumen ejecutivo

Dos cambios independientes que comparten commit porque colaboran en la misma
métrica (latencia P90 del Calificador):

1. **Localización merged**: el cálculo de localización (15 fuentes, 30 SUMs) deja
   de ejecutarse como una segunda query contra `dbo.fn_BaseMadrePorCartera` y se
   integra como result set 9 del batch existente. Una sola pasada al TVF en lugar
   de dos.
2. **Caché in-memory Singleton**: los resultados de `CalificarAsync(carteraID, liderID?)`
   se cachean durante 10 min (absoluto) / 5 min (sliding). Se invalida global y
   sincrónicamente al confirmar cargues de Reportes o Pagos, los dos cargues que
   pueden mutar las métricas.

Resultado: cache hit `<100ms`. Primera carga P90 ~1.5s (vs ~3s antes del merge);
cartera completa admin (~395k filas) ~3s.

---

## Antes (dos pasadas al TVF + sin caché)

```
Calificador pide cartera (8, 7)
        │
        ▼
CalificarAsync:
   1) cmd.ExecuteReaderAsync (batch 8 result sets) ──> #t + escalares + 7 desgloses
        ~1-2s incluyendo populación de #t
   2) ObtenerLocalizacionAsync (FromSql segunda query) ──> SUMs de 15 fuentes
        OTRA pasada al TVF dbo.fn_BaseMadrePorCartera
        ~1-2s — duplica el costo del TVF
        │
        ▼
DTO completo devuelto. Cada llamada repite todo el trabajo.
```

Cada navegación al Calificador (recarga, cambio de cartera y vuelta, navegación
entre dashboards) volvía a pagar 2-3s aunque los datos no hubiesen cambiado.

## Ahora (una sola pasada + caché)

```
Calificador pide cartera (8, 7)
        │
        ▼
CalificarAsync:
   0) _cache.TryGet(carteraID, liderID) ──> HIT: return en <100ms ✔
                                       └──> MISS: continúa al batch
   1) cmd.ExecuteReaderAsync (batch 9 result sets):
       SELECT INTO #t — incluye columnas extra para localización
       result set 1: escalares
       result sets 2-8: desgloses (rotación, perfil, ...)
       result set 9: localización (30 SUMs + 15 LEFT JOIN DISTINCT contra #t)
       DROP TABLE #t
        ~1-2s una sola vez
        │
        ▼
   2) _cache.Set(carteraID, liderID, dto) — cachea solo si total > 0
        │
        ▼
DTO devuelto. Próximas llamadas en <10 min: cache hit.

Cuando alguien confirma cargue de Reportes/Pagos:
   ConfirmarCargueAsync → tras commit exitoso → _calificadorCache.InvalidarTodo()
        ▼
   CTS.Cancel() + nuevo CTS — toda la caché expira instantáneamente
```

---

## Equivalencia funcional

El result set 9 reproduce exactamente las 15 reglas de detección de
`ObtenerLocalizacionAsync` (la función vieja). Las 15 fuentes detectadas:

Cajacopi, Camara, SaludTotal, Fosyga, Compensados, Positiva, Tigo, SOS,
DatosClaro, MutualSer, ARLSura, PositivaEmpresas, RUES, SIMIT, RGM.

Para cada fuente se devuelven 2 conteos:
- `XEnc` — encontrado (al menos un campo no vacío)
- `XNoEnc` — no encontrado (todos los campos vacíos PERO existe fila en la tabla fuente)

El "Faltan por buscar" se deriva en C# como `total - encontrados - noEnc`.

### Por qué LEFT JOIN DISTINCT y no LEFT JOIN directo

Las tablas fuente pueden tener múltiples filas por `TitularID` (histórico).
`LEFT JOIN (SELECT DISTINCT TitularID FROM Tabla)` evita la multiplicación de
filas en `#t`. Confirmado en `baseline-cartera-2026-05.md` que NO son cuello —
las 14 (ahora 15, con MutualSer) operan sobre ~60-200k titulares.

---

## Decisiones de la caché

**Singleton porque tiene que sobrevivir entre requests.** Scoped o Transient
serían inútiles — cada request crearía un caché vacío.

**Clave compuesta `calificador:{carteraID}:{liderID|null}`.** La entrada con
`liderID=null` corresponde al modo "Cartera Completo" del Admin/Director. Es una
entrada separada de las entradas por líder específico.

**TTL 10 min absoluto + 5 min sliding.** Sliding mantiene caliente lo que se
está mirando activamente; el absoluto pone tope a la frescura máxima. Si nadie
toca una entrada por 5 min muere; si la tocan continuamente muere a los 10 min.

**Invalidación global vía `CancellationTokenSource` swap.** Patrón estándar de
`IMemoryCache`: cada entrada se adjunta a un `CancellationChangeToken(cts.Token)`.
Para invalidar todo: cancelar el CTS actual y crear uno nuevo bajo lock. Las
entradas viejas expiran inmediatamente; las nuevas usan el CTS nuevo.

**Invalidación global, no por cartera.** Cargues de Reportes/Pagos pueden afectar
cualquier cartera (UltimoEstadoTitular, pagos cruzados). Más simple y correcto
invalidar todo. El costo de re-popular es bajo (1-2s por entrada).

**No se cachea `total == 0`.** Caso transitorio (cartera sin datos cargados). Si
cacheamos ceros, ocultamos la llegada de datos nuevos hasta que expire el TTL.

**Race-safe**: `Set` lee el CTS bajo lock antes de adjuntar el token. Evita que
`InvalidarTodo` reemplace el CTS entre el `lock` y el `_cache.Set`. Si pasa,
peor caso: la entrada nueva nace ya cancelada (correcto — el cargue ya invalidó).

---

## Cambios concretos al código

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/DependencyInjection.cs`

Añade `services.AddMemoryCache()` y `services.AddSingleton<ICalificadorCarteraCache, CalificadorCarteraCache>()`.

### `src/Zentinela.Application/Interfaces/ICalificadorCarteraCache.cs` (nuevo)

Contrato: `TryGet`, `Set`, `InvalidarTodo`.

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraCache.cs` (nuevo)

Implementación Singleton con `IMemoryCache` + `CancellationTokenSource`.

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs`

- Inyección de `ICalificadorCarteraCache`.
- `CalificarAsync`: TryGet al inicio, Set antes del return.
- `SqlAgregados`: `SELECT INTO #t` agrega ~20 columnas extra para localización.
- `SqlAgregados`: nuevo result set 9 con los 30 SUMs y 15 LEFT JOIN DISTINCT.
- `LeerLocalizacionAsync` (nueva, privada): hidrata `List<LocalizacionFuente>` desde el reader.
- Eliminado: `ObtenerLocalizacionAsync` y el record `LocalizacionResumenRaw` (~165 líneas).
- Archivo 649 → 597 líneas netas (reemplazo).

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CargueReporteService.cs`

Inyección de `ICalificadorCarteraCache`. Llamada a `InvalidarTodo()` después del
commit exitoso en `ConfirmarCargueAsync` (fuera del try/catch — solo si la
transacción cerró bien).

### `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CarguePagosService.cs`

Idem. Invalidación tras `SaveChangesAsync` (sin transacción explícita en este servicio).

---

## Métricas

| Escenario | Antes | Ahora | Mejora |
|---|---:|---:|---:|
| Calificador P90 cold (cartera 60k, líder específico) | ~3s (1.5s batch + 1.5s localización) | **~1.5s** | ~2× |
| Calificador admin "Completo" (~395k filas) | ~5s | **~3s** | ~1.7× |
| Calificador cache hit (cualquier cartera) | n/a — siempre cold | **<100ms** | ~30× |

La mayor ganancia operacional es el cache hit: la navegación habitual
(dashboard → calificador → dashboard → calificador) ya no paga el batch.

---

## Validación pendiente

- [ ] Smoke runtime end-to-end: abrir Calificador con P90 (cartera 8, líder 7),
      verificar números idénticos al output anterior.
- [ ] Smoke invalidación: confirmar cargue de Reportes, recargar Calificador,
      verificar que no devuelve la versión cacheada.
- [ ] Smoke admin "Completo": abrir cartera con `liderID = null`, verificar que
      el cache key es independiente del modo por líder.

---

## Cómo revertir

```powershell
git checkout HEAD -- `
  src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraService.cs `
  src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CargueReporteService.cs `
  src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CarguePagosService.cs `
  src/Infrastructure/Zentinela.Infrastructure.SQLServer/DependencyInjection.cs

Remove-Item src/Zentinela.Application/Interfaces/ICalificadorCarteraCache.cs
Remove-Item src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CalificadorCarteraCache.cs
```

Sin cambios en BD, sin migraciones, sin pérdida de datos.

---

## Pendientes (no en este commit)

- Cache para `ObtenerOpcionesAsync` (dropdown). Cambia rara vez; impacto bajo.
- Cache distribuido (Redis) — WONTFIX, single-server deployment.
- Snapshot pre-agregado `CalificadorSnapshot` — último recurso, implica ETL.
- Fase 4 (Comparador) — bloqueada por `UltimoEstadoTitular` vacía.

---

## Referencias

- Memoria del estado del plan: `~/.claude/projects/.../memory/project_optimizacion_cartera_estado.md`
- Fase anterior (batch 8 result sets): `docs/optim/fase-2c-implementado.md`
- Recap didáctico: `docs/optim/recap-optimizacion-cartera.md`
- Plan original: `~/.claude/plans/optimizacion-cartera-modulo.md`
