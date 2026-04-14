# ADR-001 — Gestión de Personal: operaciones multi-base de datos sin transacción distribuida

**Fecha:** 2026-04-14  
**Estado:** Aceptado  
**Módulo:** Gestión de Personal (AdminService)

---

## Contexto

Las acciones de Gestión de Personal (reasignar supervisor, inactivar usuario) afectan
simultáneamente tres bases de datos distintas:

| Base de datos | Operación |
|---------------|-----------|
| `DBZentinela` (Identity) | Leer/eliminar `ApplicationUser` |
| `DBRecursosHumanos` | Actualizar `Empleado.IDSupervisor` / `EstadoEmpleado` |
| `DBCallCenter` | Eliminar registros de `Cartera_Agente` |

Estas BDs no comparten servidor de transacciones, por lo que una transacción ACID
distribuida (MS DTC) implicaría configuración de infraestructura fuera del alcance del proyecto.

---

## Decisión

Se ejecutan las operaciones en **secuencia con orden de criticidad**:

1. **RRHH primero** (`SaveChangesAsync`) — es la fuente de verdad del estado del empleado.
2. **CallCenter segundo** (`SaveChangesAsync`) — limpieza de asignaciones derivadas.
3. **Identity al final** (`DeleteAsync`) — solo si los pasos anteriores tuvieron éxito.

Si un paso intermedio falla se lanza `InvalidOperationException` y los pasos siguientes no se ejecutan. El estado puede quedar parcialmente actualizado, pero en la dirección "segura": el empleado quedará inactivo en RRHH aunque su usuario de Zentinela persista (puede reintentarse).

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|-------------|-------------------|
| MS DTC (transacción distribuida) | Requiere configuración de infraestructura; complejidad desproporcionada para el volumen actual |
| Saga / Outbox pattern | Sobredimensionado; el número de usuarios a gestionar es acotado y los fallos parciales son recuperables manualmente |
| Base de datos única | Las BDs son sistemas externos (RRHH, CallCenter) que no pueden migrarse a DBZentinela |

---

## Consecuencias

- **Positivo:** Sin dependencias de infraestructura adicional; código simple y auditable.
- **Positivo:** Orden de operaciones garantiza que el empleado queda inactivo en RRHH aunque Identity falle.
- **Negativo:** Posibilidad de estado parcialmente actualizado en caso de fallo de red entre pasos.  
  Mitigación: el administrador puede reintentar la acción; la operación es idempotente en RRHH y Cartera_Agente.
