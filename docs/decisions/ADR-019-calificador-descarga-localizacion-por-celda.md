# ADR-019 — Descarga por celda en "Porcentajes de Localización"

**Fecha:** 2026-05-25
**Estado:** Aceptado
**Módulos:** Cartera → Calificador

---

## Contexto

La tabla *Porcentajes de Localización* del Calificador muestra, para cada una
de las 15 fuentes de localización, la cantidad de titulares en tres estados:
**Encontrado**, **No encontró** y **Falta por buscar**. Los líderes necesitaban
una forma de bajar la lista detrás de cada cifra (cédula, nombre y última
consulta) sin tener que armar la consulta a mano contra la base de datos.

El requisito visual fue replicar el hover del módulo *Gestión de Personal* —
ícono que aparece solo en hover, click ejecuta la acción — pero por **celda**
(no por fila): cada una de las 45 intersecciones (15 fuentes × 3 categorías)
es un punto de descarga independiente.

---

## Decisión

### 1. Predicados espejo del result set 9

Los nuevos endpoints replican exactamente los predicados del result set 9 del
batch principal de `CalificadorCarteraService` (los `CASE WHEN` de
`SqlAgregados`). La metadata por fuente vive en el dict `Fuentes` del servicio
y se compone de:

- `SelectDataCols` — proyección de las columnas de la vista al `#t` temporal.
- `PredicadoEncontrado` — condición que define la celda "Encontrado".

Las otras dos categorías se derivan negando el predicado de "Encontrado" y
combinándolo con la existencia en la tabla fuente:

| Categoría | Predicado |
|---|---|
| Encontrado | `<PredicadoEncontrado>` |
| No encontrado | `NOT (<PredicadoEncontrado>) AND src.Existe IS NOT NULL` |
| Falta | `NOT (<PredicadoEncontrado>) AND src.Existe IS NULL` |

**Por qué espejo, no nuevos predicados**: si se escriben dos veces, el conteo
de la pantalla y la cantidad de filas descargadas pueden divergir cuando se
ajuste uno y se olvide el otro. Tener una única fuente lógica (la metadata
por fuente) garantiza coincidencia con el conteo visible.

### 2. Detección de existencia con flag `bit`, no con `MAX(fecha)`

La CTE `src` agrega por `TitularID`:

```sql
SELECT TitularID,
       MAX(FechaConsulta) AS UltimaConsulta,
       CAST(1 AS bit)     AS Existe
FROM dbo.<TablaFuente>
GROUP BY TitularID
```

La existencia se decide con `src.Existe IS NOT NULL` (no con
`src.UltimaConsulta IS NOT NULL`). Razón: **DatosClaro** es la única fuente
cuya columna de fecha (`FechaInsercion`) es nullable; las demás 14 tienen
`FechaConsulta NOT NULL` (ver `docs/schema.md`).

Si para un titular todas sus filas en `DatosClaro` tienen `FechaInsercion
NULL`, `MAX` retorna NULL y un check basado en la fecha lo clasificaría
incorrectamente como **Falta** cuando en realidad es **No encontrado**. El
flag `Existe` espeja el `dc.TitularID IS NOT NULL` que el result set 9 usa
para Claro.

### 3. Endpoint con `liderID` por query string

```
GET /api/calificador/{carteraID}/localizacion/{fuente}/{categoria}?liderID={N}
```

Se prefirió `?liderID=` (query) sobre un segmento adicional para evitar
ambigüedades con la ruta pre-existente `{carteraID:int}/{liderID:int?}`.
Cuando no hay líder (vista "Cartera Completo" de Admin/Director), el query
se omite y el TVF `fn_BaseMadrePorCartera(@CarteraID, NULL)` evalúa toda la
cartera.

### 4. CSV streaming, no XLSX

Sigue el patrón de `CreadorZentinelaService.EscribirCsvAsync` (BOM UTF-8 +
separador `;` + tildes para Excel ES-CO). Una cartera grande con 190k
titulares puede tener una celda "Falta por buscar" cercana al total; el
streaming evita materializar todo en memoria.

### 5. Hover por celda (no por fila)

`TablaLocalizacion` mantiene un único estado `celdaHover` con la clave
`"{fuente}|{categoria}"`. Cada `<td>` numérico es ahora un `CeldaCifra` que:

- Muestra el ícono `IconoDescarga` (SVG inline, currentColor) solo cuando la
  clave de la celda coincide con `celdaHover`.
- Cambia el cursor a `pointer` solo cuando la fuente está en `FUENTE_SLUG`
  **y** el valor de la celda es `> 0` (celdas en cero no descargan).
- Mientras corre la descarga muestra `…` en lugar del ícono y bloquea
  clicks adicionales.

Es el mismo enfoque que `IconoLapiz` en `GestionPersonalPage` (hover de la
fila revela el lápiz; aquí hover de la celda revela el ícono de descarga).

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Re-escribir predicados específicos por endpoint | Dos fuentes de verdad para la misma lógica. El conteo de pantalla y la lista descargada divergirían apenas se ajuste uno. |
| Excel (.xlsx) con ClosedXML | Materializa todo en memoria. Riesgo con "Falta por buscar" en carteras de 190k+ titulares (la categoría que típicamente arrastra el total). |
| Modal de confirmación previo a la descarga | El usuario pidió "descarga directa". Coherente con los botones de descarga del Creador, que tampoco confirman. |
| Botón único por fila (descargar las 3 categorías) | El usuario fue explícito: descargar la celda donde se hace click, no la fila completa. |
| `MAX(fecha) IS NOT NULL` como check de existencia | Falla en DatosClaro (ver §2). |

---

## Consecuencias

**Positivas**

- El conteo visible y la lista descargada quedan garantizados a coincidir
  (mismo predicado).
- 45 puntos de descarga sin duplicar HTML — `CeldaCifra` reutilizable.
- CSV streaming escala a carteras del tamaño máximo conocido.

**Negativas / aceptadas**

- Cada descarga reabre el TVF `fn_BaseMadrePorCartera`. En carteras pesadas
  (P99 ~190k) el cold start del TVF pesa 1–3s — aceptable porque la descarga
  es esporádica. No tiene caché (a diferencia de `CalificarAsync`).
- `Modelo` (int del RGM) entra al `#t` con su tipo nativo y el predicado lo
  trata como numérico (`!= 0`); las demás columnas son varchar y usan
  `NULLIF(NULLIF(...,''),'0')`. La asimetría es heredada del result set 9 y
  no se uniformó para no divergir.

**Operativas**

- Si se agrega una nueva fuente al Calificador, hay que añadirla en dos
  lugares: (i) el batch principal `SqlAgregados` (result set 9 + columnas
  del #t), y (ii) el diccionario `Fuentes` de este endpoint. Idealmente
  unificar ambos en una sola lista en un refactor futuro.
- El mapa `FUENTE_SLUG` del frontend traduce la etiqueta visible al slug del
  URL. Si se renombra una fuente en pantalla (no es lo usual) hay que
  actualizar también el slug.
