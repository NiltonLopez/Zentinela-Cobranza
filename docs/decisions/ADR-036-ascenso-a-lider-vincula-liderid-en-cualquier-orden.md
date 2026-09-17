# ADR-036 — El ascenso a Líder vincula `LiderID` en cualquier orden y nunca en silencio

**Estado:** Aceptado
**Fecha:** 2026-09-17
**Módulos afectados:** Administración → Gestión de Personal (cambio de rol) · Gestión de Carteras
(alta de líder) · selectores de cartera de Calificador, Comparador y Creador

> Este documento identifica cuentas y líderes solo por sus **Ids internos** (`AspNetUsers.Id`,
> `Lider.ID`). Cédulas, nombres y usuarios se omiten a propósito: el historial de git es permanente
> y se replica fuera del servidor. Quien necesite el detalle lo cruza contra la base.

---

## Contexto

Una asesora ascendida a líder reportó que "su cuenta no funciona". Al revisar, **todo estaba bien
salvo un campo**:

| Dónde | Estado |
|---|---|
| `AspNetUsers` (Id 31) | usuario existe, rol Líder, hash ok, sin bloqueo |
| `AspNetUsers.LiderID` | **NULL** ← el enlace entre el login y su perfil de líder |
| `DBCallCenter.Lider` (ID 33) | activo, mismo documento, mismo email |
| `Lider_Cartera` | una cartera activa asignada |
| RRHH `Empleado` | cargo de líder, activa |

Y no era la única: **5 cuentas** con rol Líder tenían `LiderID` null. Dos de ellas (Ids 31 y 105)
tenían su líder activo esperando en cobranza (Lider.ID 33 y 32) — mismo bug, la segunda sin
reportar.

### La trampa de orden

El ascenso son dos acciones que el admin hace por separado, en pantallas distintas:

1. **Cambiar el rol** a Líder — Gestión de Personal → `AdminService.CambiarRolAsync`
2. **Dar de alta al líder** — Gestión de Carteras → `GestionCarterasService.CrearLiderAsync`

`CambiarRolAsync` buscaba el líder por documento y hacía:

```csharp
usuario.LiderID = lider?.ID;   // si el líder aún no existe → NULL, sin avisar
```

Y `CrearLiderAsync` creaba el líder pero **nunca enlazaba** una cuenta existente. Resultado: el
flujo **solo funcionaba en el orden inverso al natural** (líder primero, rol después), y nada
en la interfaz lo indicaba. Si el admin hacía primero lo obvio — "cambiarle el rol" — la cuenta
quedaba rota para siempre.

### Cómo se manifestaba: inconsistente, y una fuga de privilegios

Un Líder con `LiderID` null no fallaba de una sola forma:

- `CarteraService.ObtenerCarterasAsync` → **lista vacía**. "No ve nada."
- `CalificadorCarteraService`, `ComparadorCarterasService`, `CreadorZentinelaService` →
  `ObtenerOpcionesAsync` tenía `if (LiderID.HasValue && Rol is Líder) {…} else {ramal Admin}`.
  Con `null` la condición es falsa y **cae al ramal de Admin/Director**: ve **todas** las
  carteras de la empresa.

Un bug funcional que además dejaba a una cuenta de líder mal configurada con visibilidad global.

## Decisión

Tres cambios que cierran el problema por todos los lados:

### 1. `CambiarRolAsync` falla fuerte, con instrucción

```csharp
if (lider is null)
    throw new InvalidOperationException(
        $"No existe un líder activo con la cédula {usuario.NumeroDocumento} en el sistema de " +
        $"cobranza. Primero dalo de alta en Gestión de Carteras → Líderes (o reactívalo si " +
        $"estaba inactivo) y después cambia el rol.");
usuario.LiderID = lider.ID;
```

Un estado inconsistente nunca debe crearse en silencio. El mensaje dice exactamente qué hacer.

### 2. `CrearLiderAsync` enlaza la cuenta existente

Nuevo `VincularUsuarioLiderAsync(numeroDocumento, liderID)`: al crear **o reactivar** un líder,
busca una cuenta con esa cédula, rol Líder/Líder Externo y `LiderID` null, y la enlaza.

Con esto el flujo **funciona en cualquier orden**: si el rol fue primero, el alta hace el enlace
que el cambio de rol no pudo hacer.

Solo toca cuentas con `LiderID` null — una cuenta ya enlazada a otro líder no se pisa.

### 3. Guardarraíl en los tres selectores

```csharp
if ((contexto.Rol is Roles.Lider or Roles.LiderExterno) && !contexto.LiderID.HasValue)
    return [];
```

Antes del `if` existente, en Calificador, Comparador y Creador. Un Líder sin `LiderID` devuelve
vacío. **Nunca** cae al ramal de Admin. Defensa en profundidad: con 1 y 2 el estado ya no debería
producirse, pero si aparece por otra vía (edición manual, importación), no escala privilegios.

## Reparación de datos (ejecutada 2026-09-17)

Sobre `DBZentinela` (base propia de Zentinela), en transacción con guardas por documento y
`LiderID IS NULL`, con `QUOTED_IDENTIFIER ON` (`sqlcmd -I`; `AspNetUsers` tiene índice filtrado):

```sql
UPDATE AspNetUsers SET LiderID = 33 WHERE Id = 31  AND NumeroDocumento = <doc> AND LiderID IS NULL;
UPDATE AspNetUsers SET LiderID = 32 WHERE Id = 105 AND NumeroDocumento = <doc> AND LiderID IS NULL;
```

Verificación cruzada posterior: el documento del usuario coincide con el del líder enlazado en
ambos casos. No queda ninguna cuenta Líder con `LiderID` null que tenga un líder activo
disponible.

**Los afectados deben cerrar sesión y volver a entrar**: el `LiderID` viaja en el token JWT, y el
que tenían emitido llevaba `null`.

## Pendientes (decisión del administrador)

Tres cuentas con rol Líder **sin** registro de líder en cobranza — no son reparables por datos,
requieren decidir:

| `AspNetUsers.Id` | Nota |
|---|---|
| 196 | Darlo de alta como líder, o el rol no le corresponde |
| 230 | Ídem |
| 136 | Parece cuenta de prueba (documento no real) |

Con el código nuevo, darlos de alta en Gestión de Carteras los enlaza automáticamente.

Residuo menor: la cuenta Id 31 **sigue registrada como asesora** en `DBCallCenter.Agente` con 3
asignaciones en `Cartera_Agente`. No le afecta como líder, pero seguirá apareciendo en listas de
asesores hasta que se limpie. El ascenso no tiene hoy un paso que retire el registro de agente —
candidato a mejora.

## Verificación

1. Cuentas Id 31 y 105: cerrar sesión, entrar, y ver **sus** carteras (no vacío, no todas).
2. Ascenso nuevo en orden "rol primero": debe rechazarse con el mensaje que indica dar de alta al
   líder antes.
3. Ascenso nuevo en orden "líder primero, rol después": debe funcionar como siempre.
4. Ascenso "rol primero" seguido de alta del líder: la cuenta queda enlazada al dar de alta.
