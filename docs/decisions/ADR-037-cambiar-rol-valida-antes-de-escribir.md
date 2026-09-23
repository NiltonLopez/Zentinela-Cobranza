# ADR-037 — `CambiarRolAsync` valida todo antes de escribir (regresión de ADR-036)

**Estado:** Aceptado
**Fecha:** 2026-09-23
**Módulo afectado:** Administración → Gestión de Personal (cambio de rol)
**Corrige una regresión introducida por:** [ADR-036](ADR-036-ascenso-a-lider-vincula-liderid-en-cualquier-orden.md)

> Cuentas identificadas solo por `AspNetUsers.Id`. Cédulas y nombres no van al historial de git.

---

## Qué pasó

Un admin intentó ascender a un asesor a líder y el sistema se lo rechazó — correctamente, porque
ese empleado todavía figura con cargo `1 = Asesor de Cobranza` en RRHH y no puede darse de alta
como líder (solo cargos 2, 7 y 8, ver `CargoRoles.CargosLider`).

Pero el rechazo **dejó la cuenta sin ningún rol**. `AspNetUsers.Id 13` quedó con 0 filas en
`AspNetUserRoles`: no podía usar nada del sistema, porque todos los controllers exigen
`[Authorize(Roles = …)]`.

## Causa: la validación quedó después de la primera escritura

`CambiarRolAsync` hace *remover rol actual* → *agregar rol nuevo*. Identity **no expone
transacción**, así que son dos escrituras independientes.

ADR-036 agregó la validación de líder **entre las dos**:

```csharp
await _userManager.RemoveFromRolesAsync(usuario, rolesActuales);   // 1. ya escribió

if (lider is null)
    throw new InvalidOperationException(...);                       // 2. aborta acá

await _userManager.AddToRoleAsync(usuario, nuevoRol);               // 3. nunca llega
```

Resultado: un ascenso rechazado **borra el rol que el usuario ya tenía**. El fix de ADR-036 era
correcto en intención (no crear estados inconsistentes en silencio) pero se ubicó en el lugar
equivocado, y creó un estado inconsistente distinto y peor.

## Decisión

**Toda validación va antes de cualquier escritura**, y el `AddToRole` final se compensa si falla:

1. Resolver y validar el líder **primero**, guardando el `LiderID` en una variable local.
2. Recién entonces: remover roles, actualizar `LiderID`, agregar el rol nuevo.
3. Si `AddToRoleAsync` devolviera `!Succeeded`, **restaurar los roles anteriores** y propagar el
   error. Sin esto, cualquier fallo de Identity en el último paso repite el mismo agujero.

**Regla general:** cuando una operación son varias escrituras sin transacción, el orden no es
cosmético — todo lo que pueda lanzar se ejecuta primero, y el último paso debe tener compensación.
Agregar una validación en medio de una secuencia de escrituras es introducir un modo de fallo
nuevo, aunque la validación en sí sea correcta.

## Mejora adicional en el mensaje de error

El mensaje ahora menciona la causa raíz que el admin no puede adivinar:

> …Si no aparece en la lista de empleados elegibles, **su cargo en Recursos Humanos todavía no es
> de líder**.

Sin eso, el admin va a Gestión de Carteras → Líderes, no encuentra a la persona en el dropdown, y
queda sin pista. El orden correcto del ascenso empieza **fuera de Zentinela**: RRHH cambia el cargo
→ alta del líder → cambio de rol → asignar carteras.

## Reparación de datos (ejecutada 2026-09-23)

`AspNetUsers.Id 13` — rol `Asesor` restaurado, en transacción con guardas por documento y por
"sigue sin ningún rol". Verificado después: **ninguna** cuenta sin rol, **ninguna** con rol
duplicado. Era el único afectado.

Debe cerrar sesión y volver a entrar para que el rol viaje en un token nuevo.

## Verificación

1. Ascenso a Líder de alguien sin líder dado de alta: se rechaza **y conserva su rol actual**
   (antes lo perdía).
2. Ascenso a Líder de alguien con líder activo: funciona.
3. Cambio a cualquier rol no-líder: funciona y limpia `LiderID`.
