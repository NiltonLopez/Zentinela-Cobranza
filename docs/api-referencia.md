# Referencia de API — Zentinela

**Base URL:** `http://localhost:5153/api`
**Formato:** JSON
**Autenticación:** Bearer Token (JWT)

Los endpoints marcados como protegidos requieren el header:
```
Authorization: Bearer <token>
```

---

## POST /api/registro

Registra un nuevo usuario a partir de su cédula y usuario ControlNext. No requiere autenticación previa.

**Autenticación requerida:** No

### Flujo interno

1. Valida la cédula en `DBRecursosHumanos.Empleado`
2. Deriva el rol desde `Empleado.CargoID` (mapa `CargoRoles`)
3. Crea `Agente` en `DBCallCenter.Agente` (AgenteID = cédula)
4. Crea `ApplicationUser` en `DBZentinela` con `RequiereCambioPassword = true`
5. Contraseña inicial = cédula (el usuario la cambia en el primer login)

### Request body

```json
{
  "cedula": 1234567890,
  "usuarioControlNext": "string"
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `cedula` | `long` | Sí | Número de documento del empleado |
| `usuarioControlNext` | `string` | Sí | Usuario en el sistema ControlNext |

### Response body — 200 OK

```json
{
  "nombreCompleto": "string",
  "rol": "string",
  "mensaje": "string"
}
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Usuario registrado exitosamente |
| `400 Bad Request` | Cédula no encontrada en RRHH, usuario ya registrado, o error de validación. Body: `{ "mensaje": "..." }` |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/registro \
  -H "Content-Type: application/json" \
  -d '{"cedula": 1234567890, "usuarioControlNext": "jperez"}'
```

---

## POST /api/auth/login

Autentica un usuario con email/username y contraseña. Devuelve un token JWT y los datos del usuario.

**Autenticación requerida:** No

### Request body

```json
{
  "email": "string",
  "password": "string"
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `email` | `string` | Sí | Email o username del usuario |
| `password` | `string` | Sí | Contraseña |

### Response body — 200 OK

```json
{
  "token": "string",
  "email": "string",
  "nombreCompleto": "string",
  "rol": "string",
  "liderID": "int | null",
  "expiracion": "datetime (UTC, ISO 8601)",
  "requiereCambioPassword": "bool"
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `token` | `string` | JWT firmado con HMAC-SHA256 |
| `email` | `string` | Email del usuario |
| `nombreCompleto` | `string` | Nombre para mostrar en la UI |
| `rol` | `string` | `Admin`, `Líder`, `Director` o `Asesor` |
| `liderID` | `int \| null` | ID del líder vinculado; `null` para Admin/Director |
| `expiracion` | `datetime` | Fecha/hora UTC de expiración del token |
| `requiereCambioPassword` | `bool` | `true` en el primer login — frontend redirige a `/cambiar-password` |

### Claims incluidos en el JWT

| Claim | Valor |
|---|---|
| `sub` | UsuarioID (string numérico) |
| `email` | Email del usuario |
| `role` | Rol del usuario |
| `liderID` | ID del líder (string vacío si no aplica) |
| `nombreCompleto` | Nombre completo |
| `numeroDocumento` | Número de cédula |
| `jti` | UUID único del token |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Credenciales correctas |
| `401 Unauthorized` | Credenciales incorrectas. Body: `{ "mensaje": "Credenciales incorrectas" }` |
| `400 Bad Request` | Campos vacíos o modelo inválido |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@zentinela.com", "password": "Admin1234!"}'
```

---

## POST /api/auth/cambiar-password

Cambia la contraseña del usuario autenticado. En el primer login (`requiereCambioPassword = true`) la contraseña actual es la cédula del usuario. Al completar, desactiva el flag `RequiereCambioPassword`.

**Autenticación requerida:** Sí (cualquier rol)

### Request body

```json
{
  "passwordActual": "string",
  "nuevoPassword": "string"
}
```

### Response body — 200 OK

```json
{ "mensaje": "Contraseña cambiada exitosamente" }
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Contraseña cambiada |
| `400 Bad Request` | Contraseña actual incorrecta o no cumple política. Body: `{ "mensaje": "..." }` |
| `401 Unauthorized` | Token ausente o inválido |

### Política de contraseñas

| Regla | Valor |
|---|---|
| Longitud mínima | 8 caracteres |
| Requiere dígito | Sí |
| Requiere mayúscula | No |
| Requiere carácter especial | No |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/auth/cambiar-password \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"passwordActual": "1234567890", "nuevoPassword": "MiPassword1"}'
```

---

## GET /api/auth/me

Devuelve los datos del usuario autenticado desde los claims del JWT. No consulta la base de datos.

**Autenticación requerida:** Sí (cualquier rol)

### Response body — 200 OK

```json
{
  "email": "string | null",
  "rol": "string | null",
  "liderID": "string | null",
  "nombreCompleto": "string | null"
}
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Token válido |
| `401 Unauthorized` | Token ausente, expirado o firma inválida |

---

## GET /api/dashboard/asesor

Devuelve el dashboard del asesor autenticado: acuerdos vigentes y métricas de rendimiento del último ciclo de actualización.

**Autenticación requerida:** Sí
**Roles permitidos:** `Asesor`

### Response body — 200 OK

```json
{
  "nombreSupervisor": "string | null",
  "acuerdosVigentes": [
    {
      "titularID": "int",
      "nombreTitular": "string",
      "ultimoPerfil": "string",
      "fechaPromesa": "date (ISO 8601)",
      "valorPromesa": "decimal | null",
      "saldoTotal": "decimal | null",
      "semaforo": "rojo | amarillo | verde"
    }
  ],
  "rendimiento": {
    "fechaActualizacion": "datetime | null",
    "totalGestiones": "int",
    "tasaContactabilidad": "decimal",
    "tasaEfectividad": "decimal"
  }
}
```

#### Lógica del semáforo (acuerdos)

| Semáforo | Condición |
|---|---|
| `rojo` | `fechaPromesa <= hoy` — vencido |
| `amarillo` | `fechaPromesa <= hoy + 3 días` — próximo a vencer |
| `verde` | `fechaPromesa > hoy + 3 días` — vigente |

#### Fuentes de datos

| Campo | Fuente |
|---|---|
| `nombreSupervisor` | `DBRecursosHumanos.Empleado` (relación supervisor) |
| `acuerdosVigentes` | `DBCallCenter.UltimoEstadoTitular` (tipificaciones en `Tipificaciones.AcuerdosVigentes`) |
| `rendimiento` | `DBCallCenter.HistoricoEstadoTitular` (día del último ciclo) |

#### Métricas de rendimiento

| Métrica | Cálculo |
|---|---|
| `fechaActualizacion` | `MAX(FechaActualizacion)` en `UltimoEstadoTitular` para el asesor |
| `totalGestiones` | Registros en `HistoricoEstadoTitular` del día de actualización |
| `tasaContactabilidad` | `contactosPositivos / totalGestiones` |
| `tasaEfectividad` | `acuerdosAbonos / contactosPositivos` |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Dashboard devuelto (puede tener listas vacías si no hay datos) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | El usuario no tiene rol `Asesor` |

### Ejemplo curl

```bash
curl -X GET http://localhost:5153/api/dashboard/asesor \
  -H "Authorization: Bearer <token-asesor>"
```

---

## GET /api/carteras

Devuelve las carteras del usuario autenticado filtradas por rol.

- **Admin**: todas las carteras
- **Líder / Director**: solo las carteras asignadas al líder
- **Sin liderID válido**: `403 Forbidden`

**Autenticación requerida:** Sí

### Response body — 200 OK

```json
[
  { "id": 3, "nombreCartera": "Cartera Bogotá Norte" },
  { "id": 7, "nombreCartera": "Cartera Medellín Sur" }
]
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Lista de carteras (puede ser `[]`) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | Rol sin `liderID` parseable y no es Admin |

---

## Exploración interactiva (solo Development)

- OpenAPI JSON: `http://localhost:5153/openapi/v1.json`
- Scalar UI: `http://localhost:5153/scalar/v1`
