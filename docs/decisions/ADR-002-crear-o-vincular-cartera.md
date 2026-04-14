# ADR-002 — Gestión de Carteras: crear-o-vincular en un solo endpoint

**Fecha:** 2026-04-14  
**Estado:** Aceptado  
**Módulo:** Gestión de Líderes y Carteras (GestionCarterasService)

---

## Contexto

Al crear una relación Líder_Cartera desde el sub-módulo de administración, el administrador puede:

1. **Seleccionar una cartera existente** (encontrada vía autocompletado).
2. **Escribir un nombre nuevo** que no existe en la tabla `Cartera`.

En el caso 2, el sistema debe crear el registro en `Cartera` antes de poder crear la fila en `Lider_Cartera`.

---

## Decisión

Se usa un **único endpoint `POST /api/gestion-carteras/relaciones`** que acepta opcionalmente `carteraID` (selección) o `nombreCartera` (creación). El servicio resuelve la rama internamente:

```
Si CarteraID tiene valor  → solo INSERT en Lider_Cartera
Si NombreCartera no vacío → INSERT en Cartera, luego INSERT en Lider_Cartera
```

La validación de nombre duplicado corre **antes** del INSERT para dar un error claro al usuario.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|-------------|-------------------|
| Dos endpoints separados (`POST /carteras` + `POST /relaciones`) | Mayor fricción en el frontend: dos llamadas, dos estados de error, UX más compleja |
| Crear siempre la cartera aunque ya exista | Riesgo de duplicados silenciosos en la tabla `Cartera` |

---

## Consecuencias

- **Positivo:** Un solo round-trip desde el frontend; la UI no necesita gestionar el ID generado de la cartera nueva.
- **Positivo:** La validación de duplicado de nombre está centralizada en el servicio.
- **Negativo:** El DTO `CrearRelacionDto` tiene dos campos opcionales mutuamente excluyentes; se documenta en el XML y en la API reference.
- **Nota:** `Cartera` en DBCallCenter usa `ExcludeFromMigrations` pero sí acepta escrituras. Las filas creadas por Zentinela tienen `Activa = true`.
