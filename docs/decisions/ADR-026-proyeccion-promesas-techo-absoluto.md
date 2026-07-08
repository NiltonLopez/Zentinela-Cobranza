# ADR-026 — Proyección de promesas: techo absoluto por registro

**Estado:** Aceptado  
**Fecha:** 2026-07-08  
**Módulos afectados:** Dashboard Director → Métricas globales · Dashboard Líder → Métricas globales · Desglose por líder · Desglose por asesor

---

## Contexto

El home de Director/Líder (mismo endpoint, resuelto por `DashboardDirectorService.ObtenerDashboardAsync`) mostraba una "Proyección de promesas" inflada por valores de `ValorPromesa` claramente erróneos en `UltimoEstadoTitular` — se encontró a mano un registro con `ValorPromesa = $438.242.696.059.667,00`.

El campo se puebla vía `CargueReporteService.Dec()` (`CargueReporteService.cs:693`), que reformatea separadores de miles/decimales pero no valida la magnitud del resultado. Un error de cargue (celda mal formateada, copia/pegado duplicando dígitos) produce un `money` técnicamente válido pero absurdo para una promesa de pago individual, y no hay tabla de auditoría que conserve el string crudo del Excel para rastrear el origen.

Se consideró comparar cada `ValorPromesa` contra el `SaldoTotal` de la misma fila (una promesa no debería superar la deuda), pero se descartó: `SaldoTotal` se ha visto corrupto en las mismas filas, así que esa comparación no da una garantía real y exige propagar `SaldoTotal` hasta las tuplas de los desgloses por líder y por asesor — complejidad sin beneficio claro.

---

## Decisión

Se aplica un **techo absoluto configurable por registro** (`ProyeccionPromesasSettings.TechoAbsolutoPorTitular`, 25.000.000 COP por defecto). Cualquier `ValorPromesa` que lo supere se descarta de la suma (cuenta como 0), sin comparar contra ningún otro campo de la fila.

El valor se calibró verificando contra la base real (`UltimoEstadoTitular`, `DBCallCenter`): con 14.209 promesas activas, el máximo legítimo observado es ~$16.4M. 25M da un margen de ~50% sobre ese máximo sin dejar pasar el rango amplio (16M-90M) que un techo más laxo habría ignorado.

### Detalles de implementación

1. `Zentinela.Application.Settings.ProyeccionPromesasSettings` — POCO nuevo, mismo patrón que `JwtSettings`, cargado vía `IOptions<T>` desde la sección `ProyeccionPromesas` de `appsettings.json`.
2. `Zentinela.Application.Utils.ProyeccionPromesasCalculator.ValorPromesaSano(decimal? valorPromesa, decimal techoAbsoluto)` — función pura, reemplaza `r.ValorPromesa ?? 0m` en los 3 `Sum()` de `DashboardDirectorService.cs` (métrica global, desglose por líder, desglose por asesor).
3. `DashboardDirectorService` inyecta `IOptions<ProyeccionPromesasSettings>` y cachea el valor en `_techoProyeccion` en el constructor.

No se tocan los DTOs (`MetricasDirectorDto`, `FilaLiderDto`, `FilaAsesorDto` conservan `ProyeccionPromesas` como `decimal`) ni el frontend — el valor ya sano llega igual que antes y se muestra con `formatearPesos()`.

---

## Alcance

Cubre `DashboardDirectorService.cs` (Director y Líder comparten el mismo servicio — `DashboardController` resuelve `/dashboard/lider` fijando `liderID` desde el JWT sobre `IDashboardDirectorService`).

**Pendiente, fuera de este ADR:** `DashboardAsesorService.ObtenerRendimientoAsync` (`ProyeccionCiclo`) tiene la misma vulnerabilidad, pero ahí la suma se traduce directo a SQL vía `SumAsync` sobre `IQueryable` — aplicar el mismo techo requiere una expresión traducible a SQL, no la reutilización directa de `ProyeccionPromesasCalculator`. No se incluyó porque no hay confirmación de que esa vista muestre el valor inflado que motivó este cambio.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Clamp contra `SaldoTotal` de la misma fila | `SaldoTotal` también se ha visto corrupto — no da garantía real y exige propagar el campo hasta los desgloses |
| Detección estadística de outliers (percentiles/IQR por cartera) en tiempo real | Costosa de recalcular en cada carga del home; más apropiada como reporte de calidad de datos aparte, no como filtro de un KPI que debe responder rápido |
| Corregir el parser `Dec()` en `CargueReporteService` para rechazar valores absurdos al cargar | Ataca la causa raíz (deseable a futuro) pero no arregla los datos ya sucios en `UltimoEstadoTitular`/`HistoricoEstadoTitular`; se deja como mejora independiente |

---

## Nota

Esto es un blindaje de lectura, no una limpieza de datos. Las filas con `ValorPromesa` corrupto siguen en la base; identificarlas y corregirlas (o corregir el cargue) es trabajo aparte.
