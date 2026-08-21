# ADR-033 — El exportador Excel blinda las fechas fuera del rango de Excel

**Estado:** Aceptado
**Fecha:** 2026-08-19
**Módulos afectados:** Cartera → Comparador de Carteras · Cartera → Creador de Bases (misma ruta de exportación)

---

## Contexto

Descargar el comparativo de **MERCADEOS PERSONAL PROPIA (cartera 22) "Completa"** devolvía
**Error 500**. Reproducido ejecutando el servicio real contra los datos de producción en un harness
aislado:

```
System.OverflowException: Not a legal OleAut date.
  at ClosedXML.Excel.DateTimeExtensions.ToSerialDateTime(DateTime dateTime)
  at ClosedXML.Excel.XLCellValue.op_Implicit(DateTime dateTime)
  at ExcelExporter.GenerarComparativoCompleto(...)
```

`UltimoEstadoTitular` tiene **6 filas con `FechaPromesa` anterior a 1900** — años mal digitados:

| TitularID | CarteraID | FechaPromesa |
|---|---|---|
| 1046276005 | 4 | 0433-09-10 |
| 1124062534 | 4 | 1541-08-13 |
| 1078755217 | 8 | 0025-12-10 |
| 1124369609 | 8 | 0025-09-01 |
| 59095026 | 8 | 0005-02-15 |

El formato de fecha de Excel (*OLE Automation date*) arranca en **1899-12-30**; cualquier valor
anterior hace que ClosedXML lance `OverflowException`. Como el comparativo "Completa" cruza la
cartera seleccionada contra **todas** las demás, el de la cartera 22 topa con esas filas de las
carteras 4 y 8. Por eso fallaba en esa cartera y no en otras: no es su tamaño, es a qué carteras
alcanza.

**Una sola fila con un dato sucio tumbaba el export completo de 107.080 filas.**

## Decisión

### 1. `EscribirFecha` blinda el rango en el exportador

```csharp
private static readonly DateTime MinimoExcel = new(1900, 1, 1);

private static void EscribirFecha(IXLCell celda, DateTime fecha)
{
    if (fecha < MinimoExcel)
    {
        celda.Value = fecha.ToString("yyyy-MM-dd") + " (fecha inválida)";
        celda.Style.Font.FontColor = XLColor.FromHtml("#B45309");
        return;
    }
    celda.Value = fecha;
    celda.Style.NumberFormat.Format = "DD/MM/YYYY";
}
```

**Se escribe como TEXTO, no se omite.** El dato es basura pero es información real: ocultarlo
dejaría al usuario sin saber que hay algo que corregir en el origen. En naranja para que salte a
la vista en la hoja.

### 2. Se aplica a las CUATRO escrituras de fecha, no solo a la que reventó

`GenerarComparativoCartera` (2), `GenerarComparativoCompleto` (2) y la ruta genérica
`EstablecerValor(TipoColumna.Fecha)` — que usa el **Creador de Bases**, que lee las mismas tablas y
tenía el mismo fallo latente esperando a que alguien exportara la cartera equivocada.

### 3. Mejoras de rendimiento encontradas de paso

Mientras se descartaban hipótesis se hallaron dos derroches reales en `ComparadorCarterasService`:

- **`ObtenerMejoresTelefonosAsync` materializaba la entidad completa.** `HistoricoEstadoTitular`
  tiene 14 columnas y el método usa 8; se traían las 14 de ~981.000 filas. Ahora proyecta antes de
  `ToListAsync`.
- **`ids.Contains(...)` con 534.406 claves.** EF Core 9 serializa esa lista como un parámetro JSON
  de ~5 MB y la traduce a `OPENJSON`. Reemplazado por un **semi-join** (`_db.TitularCarteras.Any(...)`),
  que deja el filtro dentro de SQL Server.

> ⚠ El semi-join devuelve un **superconjunto** de `baseComparativo` (incluye los "Ya pago" que
> `FiltrarBase` descarta). Como el resultado se indexaba con `baseComparativo[...]`, se agregó un
> `.Where(y => baseComparativo.ContainsKey(...))` **antes** del indexador: sin él, el cambio
> introduciría un `KeyNotFoundException`.

## Cómo se diagnosticó (y qué NO era)

Tres hipótesis descartadas **con medición**, no por intuición:

| Hipótesis | Medición | Veredicto |
|---|---|---|
| Límite de filas de Excel (1.048.576) | 285.833 filas | ❌ no era |
| Timeout de SQL (default 30 s) | `OPENJSON` real: 1,6 + 1,6 + 5,3 s | ❌ no era |
| `OutOfMemory` en ClosedXML | 286.656 filas × 13 col: 61 s, pico 565 MB, sin OOM | ❌ no era |

Recién descartadas las tres se **ejecutó el servicio real** (referenciando los proyectos de
Infrastructure) contra los datos de producción, y la excepción apareció con su stack completo.

**Lección de método:** cuando tres hipótesis plausibles fallan, dejar de inferir y reproducir. Un
harness que instancia el servicio real con la connection string de producción cuesta minutos y da
el stack exacto; seguir razonando sobre el código da respuestas que suenan bien y son falsas.

## Verificación

Harness contra producción, cartera 22 sin líder:

| | Antes | Después |
|---|---|---|
| `ObtenerTodosParaExcelAsync` | (no llegaba a fallar acá) | OK — 107.080 filas |
| `GenerarComparativoCompleto` | ❌ `OverflowException` a los 3,1 s | ✅ OK — xlsx de 8 MB |

Smoke en producción: descargar el comparativo de MERCADEOS PERSONAL PROPIA "Completa" y confirmar
que baja el xlsx. Las celdas de las 5 filas afectadas deben mostrar el texto naranja
`0025-12-10 (fecha inválida)` en vez de una fecha.

## Pendiente

Las **6 filas siguen sucias en la BD**. El código ya no revienta, pero el dato malo se va a ver
marcado en cada export. Corregirlas es un `UPDATE` sobre `UltimoEstadoTitular` en la BD del sistema
de cobranza (propiedad de otro equipo) y requiere decidir el año correcto de cada una — p. ej. si
`0025-12-10` era `2025-12-10`. No se toca sin acordarlo.

Vale la pena además revisar si el cargue de reportes debería **rechazar** fechas fuera de rango en
la validación, en vez de dejarlas entrar y que cada consumidor se defienda.
