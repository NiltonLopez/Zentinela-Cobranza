# Referencia de API — Zentinela

**Base URL:** `http://localhost:5000/api`
**Formato:** JSON
**Autenticación:** Bearer Token (JWT)

Los endpoints marcados como protegidos requieren el header:
```
Authorization: Bearer <token>
```

---

## POST /api/auth/login

Autentica un usuario con email y contraseña. Devuelve un token JWT y los datos del usuario.

**Autenticación requerida:** No

### Request body

```json
{
  "email": "string (email válido, requerido)",
  "password": "string (requerido)"
}
```

| Campo | Tipo | Requerido | Validación |
|---|---|---|---|
| `email` | `string` | Sí | Formato email válido (`[EmailAddress]`) |
| `password` | `string` | Sí | Sin restricciones de formato en el request |

### Response body — 200 OK

```json
{
  "token": "string",
  "nombreCompleto": "string",
  "rol": "string",
  "liderID": "int | null",
  "expiracion": "datetime (UTC, ISO 8601)"
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `token` | `string` | JWT firmado con HMAC-SHA256 |
| `nombreCompleto` | `string` | Nombre para mostrar en la UI |
| `rol` | `string` | Rol principal del usuario: `Admin`, `Supervisor`, `Líder`, `Analista` o `Asesor` |
| `liderID` | `int \| null` | ID del líder vinculado. `null` para usuarios Admin |
| `expiracion` | `datetime` | Fecha/hora UTC de expiración del token |

### Claims incluidos en el JWT

| Claim | Valor |
|---|---|
| `sub` | ID numérico del usuario |
| `email` | Email del usuario |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | Rol del usuario |
| `liderID` | ID del líder (string vacío si no aplica) |
| `nombreCompleto` | Nombre completo del usuario |
| `jti` | UUID único del token |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Credenciales correctas |
| `401 Unauthorized` | Email no encontrado o contraseña incorrecta. Body: `{ "mensaje": "Credenciales incorrectas" }` |
| `400 Bad Request` | Validación de modelo fallida (email inválido o campos vacíos) |

### Ejemplo curl

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@zentinela.com",
    "password": "Admin1234!"
  }'
```

### Respuesta de ejemplo

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "nombreCompleto": "Administrador",
  "rol": "Admin",
  "liderID": null,
  "expiracion": "2026-03-31T08:00:00Z"
}
```

---

## GET /api/auth/me

Devuelve los datos del usuario autenticado extraídos directamente de los claims del JWT. No realiza consulta a la base de datos.

**Autenticación requerida:** Sí (cualquier rol)

### Request body

Ninguno.

### Response body — 200 OK

```json
{
  "email": "string | null",
  "rol": "string | null",
  "liderID": "string | null",
  "nombreCompleto": "string | null"
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `email` | `string \| null` | Email del usuario (de `User.Identity.Name`) |
| `rol` | `string \| null` | Rol del claim `ClaimTypes.Role` |
| `liderID` | `string \| null` | Valor del claim `liderID` (string numérico o vacío) |
| `nombreCompleto` | `string \| null` | Nombre completo del claim `nombreCompleto` |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Token válido |
| `401 Unauthorized` | Token ausente, expirado o con firma inválida |

### Ejemplo curl

```bash
curl -X GET http://localhost:5000/api/auth/me \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

### Respuesta de ejemplo

```json
{
  "email": "lider1@zentinela.com",
  "rol": "Líder",
  "liderID": "42",
  "nombreCompleto": "Juan Pérez"
}
```

---

## GET /api/carteras

Devuelve las carteras del usuario autenticado. El comportamiento varía según el rol:

- **Admin**: devuelve todas las carteras de la base de datos.
- **Líder** (u otro rol con `liderID` válido en el token): devuelve solo las carteras asignadas a ese líder.
- **Rol sin `liderID`**: retorna `403 Forbidden`.

Los resultados se devuelven ordenados alfabéticamente por `NombreCartera`.

**Autenticación requerida:** Sí
**Rol mínimo:** Cualquiera con token válido; el filtrado es automático por rol.

### Request body

Ninguno.

### Response body — 200 OK

Array de objetos `CarteraDto`:

```json
[
  {
    "id": "int",
    "nombreCartera": "string"
  }
]
```

| Campo | Tipo | Descripción |
|---|---|---|
| `id` | `int` | Identificador de la cartera |
| `nombreCartera` | `string` | Nombre de la cartera |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Carteras devueltas (puede ser array vacío `[]`) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | El token no contiene un `liderID` parseable y el rol no es `Admin` |

### Ejemplo curl — como Admin

```bash
curl -X GET http://localhost:5000/api/carteras \
  -H "Authorization: Bearer <token-admin>"
```

### Ejemplo curl — como Líder

```bash
curl -X GET http://localhost:5000/api/carteras \
  -H "Authorization: Bearer <token-lider>"
```

### Respuesta de ejemplo

```json
[
  { "id": 3, "nombreCartera": "Cartera Bogotá Norte" },
  { "id": 7, "nombreCartera": "Cartera Medellín Sur" }
]
```

---

## Notas generales

### Política de contraseñas (configurada en Program.cs)

| Regla | Valor |
|---|---|
| Requiere dígito | Sí |
| Longitud mínima | 8 caracteres |
| Requiere mayúscula | No |
| Requiere carácter especial | No |
| Email único | Sí |

### Exploración interactiva (solo Development)

En entorno de desarrollo, la documentación OpenAPI y la UI de Scalar están disponibles:

- OpenAPI JSON: `http://localhost:5000/openapi/v1.json`
- Scalar UI: `http://localhost:5000/scalar/v1`
