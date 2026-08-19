# ADR-032 — Calificador: `OPTION (HASH JOIN)` reemplaza el umbral de dos funciones

**Estado:** Aceptado
**Fecha:** 2026-08-14
**Módulos afectados:** Cartera → Calificador de Cartera (métricas + descarga de Localización)
**Reemplaza:** el mecanismo de umbral introducido el 2026-07-14 (sin ADR propio; vivía como
comentario en `CalificadorCarteraService.cs`)

---

## Contexto

Una líder reportó que calificar **SG+ / VANESA ARANGO** "se demora demasiado y a veces saca
error". El error real era `Execution Timeout Expired` a los 420 s, intermitente porque el caché
de 10 min ocultaba el problema entre corridas.

El Calificador elegía entre dos funciones SQL equivalentes según el tamaño del combo:

```csharp
var nombreFuncion = titulares.Rows.Count <= UmbralCarteraChica   // 36.000
    ? "dbo.fn_BaseMadreParaTitulares_CarteraChica"               // HASH forzado en 6 CTEs
    : "dbo.fn_BaseMadreParaTitulares";                           // JOIN plano
```

SG+ tiene **36.197 titulares** — 197 por encima del umbral. Caía en la función NORMAL, donde el
optimizador armaba un plan de Nested Loops O(n²) que no terminaba nunca.

### Por qué se rompió ahora

El umbral se calibró el **2026-07-14** sobre 15 fuentes. Después, tres migraciones recrearon los
TVF agregando CTEs: `2026-08-04_tigoune`, `2026-08-04_coosalud`, `2026-08-05_saviasalud`. Cada
fuente nueva cambia el espacio de planes del optimizador, y **el cruce se movió**. Los cuatro
puntos de la calibración original describían un sistema que ya no existía.

Esto no era un caso aislado: al medir, **toda cartera ≥ 133k titulares también daba timeout** por
la función NORMAL. El mecanismo estaba roto por ambos lados y nadie lo había reportado.

---

## Decisión

**Eliminar el umbral y el branching.** Se fuerza `HASH JOIN` a nivel de la sentencia que
materializa `#t`, en `SqlAgregados`:

```sql
OPTION (RECOMPILE, MAXDOP 4, HASH JOIN);
```

Una sola función (`dbo.fn_BaseMadreParaTitulares`) para todos los tamaños.

### Evidencia (medido 2026-08-14, materialización `SELECT * INTO #t`, servidor sin carga)

| Titulares | NORMAL | CHICA | **HASH en sentencia** |
|---|---|---|---|
| 36.197 (SG+, el incidente) | ❌ timeout >150 s | 5,4 s | **2,9 s** |
| 38.500 | ❌ timeout >150 s | 7,4 s | — |
| 39.700 | 5,2 s | 6,9 s | — |
| 40.903 | 6,9 s | ❌ 133,0 s | **2,8 s** |
| 53.474 | 6,9 s | ❌ 122,8 s | **3,1 s** |
| 133.178 | ❌ timeout >150 s | — | **14,8 s** |
| 251.241 | ❌ timeout >150 s | — | **8,1 s** |
| 526.934 | ~222 s (histórico) | — | **13,3 s** |

El hint gana en **todo** el rango, incluso contra cada función en su zona buena.

### Consecuencia clave

Desaparece el acantilado. Ya no hay un número mágico que re-calibrar cada vez que se agregue una
fuente — que fue exactamente la omisión que causó el incidente.

`dbo.fn_BaseMadreParaTitulares_CarteraChica` queda en la BD **sin uso**, candidata a `DROP` en una
migración futura. No se borra ahora: revertir el commit restaura el mecanismo anterior intacto.

---

## Descarga de "Porcentajes de Localización"

Ruta distinta (`EscribirLocalizacionCsvAsync`), optimizada aparte:

1. **`OPTION (RECOMPILE)`** — hay 45 textos de query (15 fuentes × 3 categorías) y sin RECOMPILE
   cada uno cachea el plan de la *primera* cartera que lo ejecuta. Medido con `sp_executesql`:
   BERTAL (251k) heredando el plan de OMEGAKID (31k) tardaba **6,8 s** contra **3,8 s** con plan
   propio.
2. **Buffer de escritura 1 KB → 64 KB** y `IsDBNull` sincrónico (la fila ya está en memoria; la
   versión async solo agregaba overhead ×2 por fila × 250k filas).

> ⚠ **`HASH JOIN` NO se aplica aquí.** Se midió y **empeora**: BERTAL 251k × Claro pasó de
> **3,8 s natural → 10,8 s con hint → 13,1 s** con el `src` filtrado. Esta query tiene otra forma
> (agregado `MAX` + doble `LEFT JOIN`) y el optimizador la resuelve mejor sola. La regla del batch
> principal **no** se generaliza.

Techo medido con la query final: MERCADEOS PERSONAL PROPIA "Completo" (551k) × Claro (3,5 M filas)
× "falta por buscar" (639k filas de salida) → **8,9 s**.

---

## Descarga múltiple en ZIP

Se agrega selección por casillas en la tabla de Localización y un botón que empaqueta las celdas
marcadas.

**Por qué ZIP y no gzip:** gzip comprime *un* stream y no tiene concepto de entradas — no puede
contener varios archivos. Para N CSVs hace falta un contenedor. ZIP además lo abre Windows con
doble clic.

- Endpoint: `POST /api/calificador/{carteraID}/localizacion/zip` (POST y no GET porque la
  selección puede llegar a 60 celdas y no cabe cómoda en un query string).
- **Streaming real**: `ZipArchive` en modo Create sobre un stream no-buscable (`Response.Body`);
  cada CSV va del `DataReader` a su entrada fila por fila. Nada se materializa en memoria.
- Celdas **en serie**, no en paralelo: comparten conexión y cada consulta ya usa `MAXDOP 4`.
- Validación de todas las celdas **antes** de abrir el ZIP, para devolver 400 limpio en vez de un
  archivo a medio escribir con el header ya enviado.
- El botón exige **2 o más** celdas: con una sola, el click directo sobre la cifra ya descarga ese
  CSV y un ZIP de un archivo no aporta nada.

Verificado en aislamiento reproduciendo el patrón exacto sobre un stream sin `CanSeek`:

| Verificación | Resultado |
|---|---|
| ZIP sobre stream no-buscable | ✅ (usa data descriptors) |
| Compresión | 8,3 MB → 751 KB (**91 % menos**) |
| Integridad | 50.001 y 120.001 líneas exactas |
| BOM UTF-8 por entrada | ✅ `EF BB BF` confirmado a nivel de byte |
| Tildes | ✅ `Cédula` = `43 C3 A9 64 75 6C 61` |

El BOM se verificó byte a byte a propósito: `StreamReader` lo consume automáticamente, así que
leerlo con un reader da un falso negativo. Sin BOM, Excel en español muestra `CÃ©dula`.

### ⚠ `AllowSynchronousIO` es obligatorio (incidente 2026-08-19)

La primera versión desplegada **descargaba 0 bytes en producción** pese a que el test aislado
pasaba. Causa: el stream de prueba permitía escrituras síncronas; `Response.Body` **no**.

ASP.NET Core prohíbe I/O síncrono sobre el cuerpo de la respuesta desde 3.0
(`AllowSynchronousIO = false`), y **`ZipArchive` no implementa `IAsyncDisposable`** — al cerrarse
escribe el directorio central con `Stream.Write()` síncrono. La excepción se lanzaba *después* de
enviar los headers, así que el navegador recibía **200 OK con un archivo vacío**.

Reproducido con un stream que replica la restricción real:

| Caso | Writes síncronos | Resultado |
|---|---|---|
| CSV individual | **0** (15 async) | ✅ nunca estuvo en riesgo |
| ZIP sin el flag | **3** → lanza en la primera | ❌ **0 bytes** |
| ZIP con el flag | 76 sync + 24 async | ✅ 177 KB íntegro |

Fix: activar `IHttpBodyControlFeature.AllowSynchronousIO = true` **por request** en el endpoint del
ZIP, antes de escribir nada. No es global. La ruta del CSV individual es 100 % asíncrona y no lo
necesita.

**Lección para futuros streams binarios:** un stream de prueba que acepta `Write()` síncrono no
modela `Response.Body`. Si se agrega otra descarga que use `ZipArchive`, `GZipStream` u otro
componente sin `DisposeAsync`, hay que repetir este flag y probar contra un stream que prohíba
escrituras síncronas.

---

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Subir el umbral a 39.700 (el cruce medido hoy) | Se implementó y funcionaba, pero deja el acantilado en pie: la próxima fuente vuelve a moverlo. El hint lo elimina de raíz. |
| Forzar HASH dentro de las funciones (más CTEs con hint) | Requiere DDL y volver a decidir por CTE cada vez. El hint de sentencia cubre todo sin tocar la BD. |
| Aplicar `HASH JOIN` también a la descarga de Localización | **Medido: empeora 3×.** Ver arriba. |
| gzip para la descarga múltiple | No soporta múltiples entradas. |

---

## Verificación

1. Calificar **SG+ — VANESA ARANGO** (28/14): antes timeout, ahora ~4-6 s en frío, <100 ms con
   caché.
2. Calificar cualquier cartera grande (LUMEN 133k, BERTAL 251k, DELMAR EXPERTOS "Completo" 527k):
   todas < 20 s.
3. Combos que estaban rotos en silencio y deben funcionar: **SERVIPLATA / SILVIA CARDONA** (37.503)
   y **MERCADEOS PERSONAL PROPIA / SAMUEL PARRA** (39.123).
4. Descargar una celda individual de Porcentajes de Localización → CSV con tildes correctas.
5. Marcar 2+ casillas → botón se habilita → ZIP con un CSV por celda, abrible con doble clic.
6. Marcar 1 sola casilla → botón deshabilitado con tooltip explicativo.

---

## Pendientes

- `DROP FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica` cuando se confirme estabilidad.
- La lista de titulares del TVP se arma **sin `DISTINCT`**: en modo "Cartera Completo" un titular
  con dos líderes en la misma cartera entra duplicado (la descarga de la cartera 22 devuelve 639k
  filas para 551k titulares). Revisar si `dbo.TitularIDList` tiene PK que lo absorba.
- Las 3 fuentes nuevas (TigoUne, Coosalud, SaviaSalud) entraron a los TVF **sin** hint HASH ni en
  la variante protegida. Con el hint de sentencia ya no importa para el Calificador, pero sí para
  cualquier otro consumidor de esas funciones.
