# ADR-010 — Gestión de Personal: campo Supervisor y filtro por supervisor

**Estado:** Aceptado  
**Fecha:** 2026-04-23  
**Módulos afectados:** Administración → Gestión de Personal

---

## Contexto

El módulo de Gestión de Personal mostraba una tabla de usuarios con columnas de nombre, usuario, rol y estado de contraseña. No había forma de saber qué supervisor tiene cada usuario ni de filtrar por supervisor. El administrador tenía que abrir el modal de reasignación para consultar el supervisor actual de cada persona.

---

## Decisiones

### 1. Campo `NombreSupervisor` en `UsuarioAdminDto`

Se agrega `NombreSupervisor: string?` al DTO. El valor viene de `DBRecursosHumanos.Empleado.IDSupervisor` resuelto como nombre completo.

La columna **no se muestra** en la tabla (decisión del producto). El campo existe en el DTO como soporte del filtro.

### 2. Fetch batch de supervisores en `ListarUsuariosAsync`

Se evita un N+1. En lugar de consultar el supervisor de cada usuario individualmente, se hace **una sola query** aprovechando la relación recursiva `Empleado.Supervisor` ya mapeada en EF Core:

```sql
SELECT e.NumeroDocumento,
       s.Nombre + ' ' + s.Apellidos AS NombreSupervisor
FROM Empleado e
LEFT JOIN Empleado s ON s.ID = e.IDSupervisor
WHERE e.NumeroDocumento IN (...)
```

Resultado: `Dictionary<long, string?>` (NumeroDocumento → nombre supervisor), que se aplica al construir cada `UsuarioAdminDto`.

### 3. Endpoint `GET /api/admin/supervisores-activos`

Fuente de datos exclusiva para el dropdown del filtro. Devuelve todos los empleados cuyo `ID` aparece en el campo `IDSupervisor` de al menos un empleado — es decir, los que **realmente son supervisores** en la BD, sin filtrar por cargo.

```sql
SELECT * FROM Empleado
WHERE ID IN (SELECT DISTINCT IDSupervisor FROM Empleado WHERE IDSupervisor IS NOT NULL)
ORDER BY Nombre
```

Esto es distinto del endpoint existente `GET /api/admin/supervisores` (filtra por `CargoID ∈ {2,7,8,13,18}`), que sigue siendo el usado en el modal de reasignación.

### 4. Filtro por supervisor en la barra de búsqueda

Se agrega un `<select>` a la derecha del input de búsqueda. Las opciones vienen de `supervisores-activos` cargado con `useQuery` al montar la página. El filtro actúa como `AND` con el filtro de texto.

- Opción por defecto: "Todos los supervisores"
- La búsqueda de texto **no** incluye el nombre del supervisor — el único mecanismo para filtrar por supervisor es el dropdown.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Derivar opciones del array de usuarios cargados | Solo muestra supervisores de usuarios Zentinela registrados; no refleja todos los supervisores reales de RRHH |
| Mostrar columna Supervisor en la tabla | El admin ya ve el supervisor en el modal de reasignación; agregar columna sobrecarga la tabla visualmente |
| Filtrar por texto también sobre supervisor | Dos mecanismos haciendo lo mismo para el mismo campo; el dropdown es suficiente y más preciso |
