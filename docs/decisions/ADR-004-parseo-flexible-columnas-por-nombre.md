# ADR-004 — Parseo flexible de columnas por nombre en lugar de por posición

**Fecha:** 2026-04-15  
**Estado:** Aceptado  
**Módulos afectados:** Cargue de Pagos (CSV) · Cargue de Reportes (Excel)

---

## Contexto

Los archivos que carga Valentina provienen de sistemas externos (ControlNext, exportaciones manuales). El orden de las columnas puede variar entre versiones del archivo, pueden incluir columnas extra, y los nombres de encabezado pueden tener variaciones menores (tildes, espacios, mayúsculas, alias).

El parseo original de Cargue de Reportes usaba posiciones fijas (columna 1 = TitularID, columna 2 = AgenteID, etc.), lo que rompía silenciosamente si el archivo venía en distinto orden.

---

## Decisión

Ambos módulos usan el mismo patrón de **resolución de columnas por nombre**:

1. **Leer el encabezado** (primera fila del Excel / primera línea del CSV).
2. **Normalizar** cada nombre de columna: quitar tildes, espacios, guiones, asteriscos → minúsculas.
3. **Buscar en un diccionario de alias** que mapea variantes al nombre canónico del campo.
4. **Registrar el índice** de cada columna encontrada.
5. **Parsear cada fila de datos** usando los índices resueltos en lugar de posiciones hardcodeadas.

### Normalización

```
"Número Documento" → "numerodocumento"
"TitularID *"      → "titularid"
"Fecha de pago"    → "fechadepago"
```

### Columnas no encontradas

- **Campos opcionales** (la mayoría): se les asigna índice `0`; el acceso devuelve `string.Empty` → el campo queda `null` en el DTO.
- **Campos obligatorios** en Cargue de Pagos (todos los 6): si falta alguno, `ValidarCsvAsync` devuelve un `ValidarPagosResponse` con una fila de error y mensaje descriptivo de cuáles columnas no se encontraron.

### Extensibilidad

Para agregar un nuevo alias basta con añadir una entrada al diccionario estático (`AliasColumnas` en `CarguePagosService` / `AliasColumnasExcel` en `ExcelExporter`). No se requiere cambio en la lógica de parseo.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|-------------|-------------------|
| Mantener posiciones fijas y exigir plantilla exacta | Frágil ante cambios de fuente; genera errores silenciosos difíciles de diagnosticar |
| Detectar columnas con ML / similitud de cadenas | Exceso de complejidad para el caso de uso real |

---

## Consecuencias

- **Positivo:** El sistema acepta archivos con columnas en cualquier orden y con columnas extra.
- **Positivo:** Variantes comunes de nombres de columna funcionan sin intervención del usuario.
- **Positivo:** Nuevos alias se agregan en un solo lugar sin tocar la lógica de parseo.
- **Negativo:** El diccionario de alias hay que mantenerlo cuando aparece una nueva variante no contemplada (costo bajo — una línea).
