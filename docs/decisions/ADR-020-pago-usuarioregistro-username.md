# ADR-020 — Pago: UsuarioControlID (Id) → UsuarioRegistro (UserName)

**Fecha:** 2026-05-27
**Estado:** Aceptado
**Módulos:** Cargue de Pagos (`CarguePagosService`, `PagosController`), Autenticación (`JwtTokenGenerator`, `AuthService`, `TokenPayload`), schema `dbo.Pago`

---

## Contexto

La tabla `dbo.Pago` registraba en `UsuarioControlID` (`int NOT NULL`) el **Id** del `ApplicationUser` que confirmaba el cargue, como referencia cross-DB a `DBZentinela.dbo.AspNetUsers` (sin FK por ser cross-database).

El problema: un Id entero no dice nada al leer la tabla directamente (consultas ad-hoc, reportes, auditoría). Para saber *quién* registró un pago había que hacer un JOIN cross-DB manual contra Identity. La operación quería ver el **UserName** (el login con el que la persona entra al sistema) directamente en la fila.

Además, el JWT no transportaba el UserName: el `PagosController` leía `ClaimTypes.NameIdentifier` (el Id) y lo persistía. No existía un claim con el nombre de usuario.

---

## Decisión

### 1. Renombrar y retipar la columna

`dbo.Pago.UsuarioControlID` (`int NOT NULL`) → `dbo.Pago.UsuarioRegistro` (`nvarchar(256) NOT NULL`). Guarda el `UserName` (no el Id). Sigue siendo una referencia conceptual a Identity en `DBZentinela`, sin FK de BD.

256 caracteres = el mismo largo que `AspNetUsers.UserName` en Identity.

### 2. Claim `userName` en el JWT

Se agrega un claim custom **`userName`** al token (`JwtTokenGenerator`), siguiendo el patrón ya existente de claims custom (`liderID`, `nombreCompleto`, `numeroDocumento`). `AuthService` lo puebla con `usuario.UserName`. `TokenPayload` gana el campo `UserName`.

El `PagosController.Confirmar` lee `User.FindFirstValue("userName")` en vez de `ClaimTypes.NameIdentifier` y lo pasa a `CarguePagosService.ConfirmarCargueAsync(request, string usuarioRegistro)`.

### 3. Migración con backfill cross-DB

`docs/migrations/2026-05-27_pago_usuarioregistro.sql`, idempotente:

1. Agrega `UsuarioRegistro NVARCHAR(256) NULL`.
2. Backfill: `UPDATE` con JOIN cross-DB a `DBZentinela.dbo.AspNetUsers` traduciendo cada `UsuarioControlID` (Id) a su `UserName`.
3. Fallback para Ids huérfanos (sin usuario en AspNetUsers): `'ID:<n>'` para no perder trazabilidad.
4. `ALTER COLUMN ... NOT NULL`.
5. `DROP COLUMN UsuarioControlID`.

---

## Consecuencias

### Inmediatas

- **Orden de despliegue acoplado:** la migración hace `DROP COLUMN UsuarioControlID`. Si el código viejo (que escribe ese campo) sigue activo cuando la migración corre, sus `INSERT` fallan. La migración y el deploy del código nuevo deben ir en la misma ventana.
- **Tokens vigentes sin el claim:** los JWT emitidos antes del deploy no traen `userName`. Esos usuarios reciben `401` al confirmar un cargue hasta volver a iniciar sesión. Transitorio (los tokens expiran según `JwtSettings.ExpirationHours`).
- La tabla `Pago` se truncó antes de migrar (datos de prueba), así que el backfill operó sobre 0 filas.

### A futuro

- Cualquier consumidor nuevo que necesite el usuario que registró un pago lee `UsuarioRegistro` directo, sin JOIN a Identity.
- Si en algún momento se requiere el Id además del UserName, habría que reintroducir el claim/columna; hoy no hay consumidor que lo pida.

### Reversión

```sql
-- Requiere re-traducir UserName → Id contra AspNetUsers.
ALTER TABLE dbo.Pago ADD UsuarioControlID INT NULL;
UPDATE p SET p.UsuarioControlID = u.Id
FROM dbo.Pago p
INNER JOIN DBZentinela.dbo.AspNetUsers u ON u.UserName = p.UsuarioRegistro;
-- Resolver filas sin match antes de:
ALTER TABLE dbo.Pago ALTER COLUMN UsuarioControlID INT NOT NULL;
ALTER TABLE dbo.Pago DROP COLUMN UsuarioRegistro;
```

Más `git revert` del commit del código.

---

## Referencias

- `src/Zentinela.Core/Entities/Pago.cs` — `UsuarioControlID` (int) → `UsuarioRegistro` (string).
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Mappings/PagoMapping.cs` — `nvarchar(256)` required.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CarguePagosService.cs` — firma `string usuarioRegistro`.
- `src/Zentinela.Application/Interfaces/ICarguePagosService.cs` — firma actualizada.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/JwtTokenGenerator.cs` — claim `userName`.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/AuthService.cs` + `src/Zentinela.Application/DTOs/Auth/TokenPayload.cs` — `UserName` en el payload.
- `src/Zentinela.API/Controllers/PagosController.cs` — lee el claim `userName`.
- `docs/migrations/2026-05-27_pago_usuarioregistro.sql` — migración con backfill.
- `docs/arquitectura-auth.md` — tabla de claims del JWT.
- ADR-018 (Pagos BASE GENERAL) y ADR-003 (Cargue de Pagos — lookup automático líder) — antecedentes del módulo.
