# ADR-009 — Gestión de Asignaciones: SqlBulkCopy + validaciones + titulares nuevos

**Estado:** Aceptado  
**Fecha:** 2026-04-23  
**Módulos afectados:** Administración → Gestión de Asignaciones

---

## Contexto

El módulo de Gestión de Asignaciones permite cargar masivamente titulares a una combinación Cartera+Líder mediante un archivo Excel. La implementación original usaba un SP (`sp_ProcesarAsignacionCartera`) con una tabla de staging compartida (`Titular_CarteraAuxiliar`). En esta iteración se identificaron varios problemas:

1. **SP inexistente en producción** — el SP no había sido creado en BD, haciendo inoperable el módulo.
2. **Race condition en staging** — `TRUNCATE → INSERT → EXEC` no es atómico. Cargas concurrentes mezclan datos entre sí.
3. **Sin validación de datos de entrada** — cédulas inválidas, nombres sin apellido y duplicados pasaban directamente a BD.
4. **No creaba titulares nuevos** — si un NumeroDocumento no existía en `Titular`, la fila se descartaba silenciosamente.

---

## Decisiones

### 1. SqlBulkCopy + transacción explícita (reemplaza SP + staging)

Se elimina la dependencia del SP y la tabla de staging. El flujo de confirmación ahora es:

```
BeginTransaction
  ├── INSERT INTO Titular (titulares nuevos que no existían)
  ├── DELETE FROM Titular_Cartera WHERE CarteraID=X AND LiderID=Y
  └── SqlBulkCopy → Titular_Cartera (BatchSize=10.000)
CommitTransaction / RollbackTransaction
```

**Por qué SqlBulkCopy y no EF Core `AddRange`:**  
`AddRange` + `SaveChangesAsync` genera batches de INSERTs individuales — inviable para el volumen máximo estimado (~1 millón de filas). `SqlBulkCopy` transmite los datos directamente al motor con el protocolo TDS, manejando 1M filas en segundos. La transacción garantiza atomicidad: si el bulk insert falla, el DELETE previo se revierte.

### 2. Validaciones en la etapa de preview (antes de confirmar)

Cada fila del Excel pasa por las siguientes reglas **en orden**:

| Regla | Acción si falla |
|---|---|
| `NumeroDocumento` duplicado en el mismo archivo | `Error — "Duplicado en el archivo"` |
| Cédula < 6 dígitos | `Error — "Cédula inválida (mínimo 6 dígitos)"` |
| No existe en BD y nombre tiene < 2 palabras | `Error — "Nombre no cumple el formato (Nombre + Apellido)"` |
| Ya asignado a esta Cartera+Líder | `YaAsignado` (no se asigna, no es error) |
| Existe en BD, no asignado | `Ok` |
| No existe en BD, nombre válido | `Nuevo` — se creará en `Titular` al confirmar |

El nombre de titulares nuevos se **limpia antes de validar**: se eliminan caracteres especiales (conservando letras, tildes, ñ) y se colapsan espacios múltiples.

### 3. Creación automática de titulares nuevos

Si una fila tiene `NumeroDocumento` que no existe en `Titular` pero trae un nombre válido (≥ 2 palabras), se marca como `Nuevo` y se incluye en los válidos. Al confirmar, se inserta primero en `Titular` (dentro de la misma transacción) y luego se asigna normalmente.

### 4. Deduplicación en confirmación

Aunque la validación ya marca duplicados como `Error`, se aplica `Distinct()` sobre `titularIDs` como segunda línea de defensa antes del bulk insert. La respuesta incluye el campo `Duplicados` para informar al usuario cuántos se descartaron.

### 5. UX de la vista previa

La vista previa muestra tres tarjetas:
- **Verde** — listos para asignar (existen en BD)
- **Amarilla** — titulares nuevos (se crearán en BD)
- **Roja** — errores de validación (no se cargarán)

La pantalla de resultado final muestra la etiqueta de la Cartera+Líder afectada junto con el conteo de insertados y duplicados descartados.

---

## Nombres de columnas en la plantilla

Los encabezados de la plantilla Excel generada son exactamente los nombres de columna de la tabla `Titular` en BD: `NumeroDocumento`, `Nombre`. Esto elimina ambigüedad y permite que el usuario reutilice exportaciones directas de BD.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| SP + tabla staging | Race condition; lógica de negocio repartida entre C# y BD; SP no existía |
| EF Core `AddRange` | Inviable a 1M filas; genera miles de batches de INSERTs individuales |
| TVP (Table-Valued Parameter) | Funciona hasta ~100K filas; para 1M es borderline y más complejo de mantener |
| Validar solo en frontend | Sin segunda línea de defensa; el backend siempre debe validar |
