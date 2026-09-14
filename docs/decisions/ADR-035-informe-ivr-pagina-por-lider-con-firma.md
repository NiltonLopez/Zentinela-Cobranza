# ADR-035 — Informe IVR en PDF: barra dibujada y página por líder con aceptación firmada

**Estado:** Aceptado
**Fecha:** 2026-09-14
**Módulos afectados:** Informes → Informes de Virtualidad → Análisis IVR (exportación PDF)
**Archivo:** `src/Zentinela.Web/src/pages/informes/virtualidad/exportarAnalisisIvr.ts`

---

## Contexto

Tras generar el primer informe IVR real, el usuario reportó dos cosas sobre el PDF:

1. La barra de progreso del embudo se veía como `#######.....` — basura visual.
2. Pedía que cada líder tuviera **su propia página** con un **apartado para firmar** aceptando los
   resultados que la herramienta está midiendo sobre sus carteras.

## 1. La barra: dibujar, no escribir

### Por qué salían `#`

El panel en pantalla pinta la barra con los caracteres `█` y `░`. Las **fuentes estándar de jsPDF**
(Helvetica, la única sin embeber archivos de fuente) **no tienen esos glifos**. La versión original
del exportador lo sabía y puso `#` y `.` como sustituto de texto — legible como concepto, pésimo
como resultado.

Cambiar el caracter no arregla nada de fondo: cualquier glifo de bloque falla igual con Helvetica,
y embeber una fuente con esos glifos suma cientos de KB al bundle para pintar una barra.

### Decisión

**La barra se dibuja con rectángulos**, no con texto. La celda de la tabla queda vacía y en el hook
`didDrawCell` de autoTable se pintan dos `rect`: una pista gris de ancho completo y un relleno azul
IVR proporcional al porcentaje.

```ts
didDrawCell: (data) => {
  if (data.section !== 'body' || data.column.index !== 1) return;
  const p = porcentajes[data.row.index];
  pdf.setFillColor(226, 232, 240); pdf.rect(x, yb, w, h, 'F');            // pista
  pdf.setFillColor(...AZUL);       pdf.rect(x, yb, (w * p) / 100, h, 'F'); // relleno
}
```

Igual que en el panel, el **primer paso del embudo va siempre al 100 %** (es la base sobre la que se
calcula el resto).

**Regla derivada:** en los PDF con fuentes estándar, todo elemento gráfico (barras, indicadores,
semáforos) se **dibuja** con primitivas de jsPDF. Nunca con caracteres Unicode fuera de Latin-1.

## 2. Página por líder con bloque de aceptación

### Decisión

Cada líder **arranca en página nueva** y su sección cierra con un bloque **anclado al pie**:

```
─────────────────────────────────────────────────────
ACEPTACIÓN DE RESULTADOS

He revisado los resultados de la herramienta IVR correspondientes a mis
carteras en el período {desde} al {hasta} y acepto la información aquí
presentada como reflejo de la gestión realizada.

_____________________________________        ________________
Firma del líder                              Fecha
{NOMBRE DEL LÍDER}
```

La intención es que **la hoja del líder sea un entregable en sí misma**: se imprime, el líder revisa
sus cifras (embudo, desenlaces, detalle por cartera, apreciación) y firma abajo. Queda constancia
física de que acepta los resultados que la herramienta está midiendo.

### Detalles que importan

- **Anclado al pie, no a continuación del contenido.** El bloque se posiciona en
  `LIMITE_Y - 46 mm` de la última página del líder, así siempre queda en el mismo lugar de la hoja
  y la firma no "flota" a distinta altura según cuánto contenido haya arriba.
- **Nunca se parte.** Si el contenido del líder dejó menos de 46 mm, el bloque pasa entero a una
  página nueva. Una firma cortada entre dos hojas no sirve para nada.
- **El período va interpolado** en el texto legal — cada firma dice exactamente qué rango de fechas
  está aceptando.
- **El nombre va impreso bajo la línea**, para que no haya que escribirlo a mano ni quede ambiguo
  quién firmó.
- La **nota metodológica va en página aparte**, para no compartir hoja con la última firma.

### Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Firma solo al final del documento (una para todos) | El objetivo es que **cada líder** acepte **sus** cifras. Una firma global no atribuye nada. |
| Firma a continuación del contenido, sin anclar | Quedaría a distinta altura en cada hoja; imprime feo y dificulta encontrarla. |
| Embeber una fuente con glifos `█░` para la barra | +300 KB al bundle por una barra; los rectángulos resuelven lo mismo con 4 líneas. |

## Verificación

Se ejecutó la **función real** desde Node interceptando `jsPDF.save()`, con datos de prueba de dos
líderes, y se rasterizó el PDF resultante con `pdfjs-dist` + `@napi-rs/canvas`:

- **4 páginas**: resumen global · Vanesa Arango · Samuel Parra · nota metodológica.
- Barra: pista gris + relleno azul en 100 % / 66 % / 33 % / 15 % — sin ningún caracter de texto.
- Página 2 y 3 arrancan con el nombre del líder (salto de página confirmado).
- Bloque de firma al pie con las dos líneas (firma larga a la izquierda, fecha corta a la derecha).
- Por extracción de texto: "ACEPTACIÓN DE RESULTADOS", el texto legal con el período interpolado,
  "Firma del líder", "Fecha" y el nombre, todos presentes en la página de su líder.

> Nota para quien repita esto: pdfjs en Node **no rasteriza el texto** de las fuentes estándar sin
> configurar `standardFontDataUrl`, y aun así es quisquilloso. Para verificar texto, usar
> `getTextContent()` (extracción), no la imagen. Para verificar geometría (barras, líneas, cajas),
> la imagen sí sirve.

Smoke en producción: generar un informe IVR, abrir el PDF. Las barras deben verse como barras
sólidas; cada líder debe tener su hoja con el bloque de firma abajo.
