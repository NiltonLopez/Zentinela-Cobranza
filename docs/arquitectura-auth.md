# Módulo de Autenticación — Zentinela

> Documento generado por el Agente Docs · 2026-03-30
> Basado exclusivamente en el código fuente implementado.

---

## Decisión de arquitectura

El módulo implementa la **Opción C: ASP.NET Identity + JWT + ApplicationUser vinculado a Lider**.

La decisión combina tres componentes:

1. **ASP.NET Identity** gestiona el ciclo de vida de usuarios, contraseñas y roles. La clase `ApplicationUser` extiende `IdentityUser<int>` (clave primaria entera) para agregar el campo `NombreCompleto` y la referencia opcional `LiderID`.

2. **Vínculo con la entidad de negocio `Lider`**: `ApplicationUser.LiderID` es una FK nullable hacia la tabla `Lider` (que ya existe en producción y está excluida de migraciones). Cuando el usuario tiene rol `Líder`, `LiderID` apunta a su registro de negocio. Para usuarios `Admin` y otros roles sin cartera, `LiderID` es `null`.

3. **JWT Bearer** es el mecanismo de autenticación stateless. El backend emite un token firmado con HMAC-SHA256 que el frontend almacena en `localStorage` y adjunta a cada petición como `Authorization: Bearer <token>`.

Esta arquitectura evita mantener sesiones en el servidor y permite que el frontend React opere de forma desacoplada de la API.

---

## Diagrama de flujo de login

```
Usuario (navegador)
    │
    │  POST /api/auth/login  { email, password }
    ▼
AuthController.Login()
    │
    │  Llama a IAuthService.LoginAsync()
    ▼
AuthService.LoginAsync()
    │
    ├─ UserManager.FindByEmailAsync(email)       → busca usuario en AspNetUsers
    ├─ UserManager.CheckPasswordAsync(u, pwd)    → valida hash de contraseña
    ├─ UserManager.GetRolesAsync(u)              → obtiene roles de AspNetUserRoles
    │
    │  Si email o password son inválidos → retorna null → HTTP 401
    │
    ├─ GenerarToken(usuario, rol)
    │     └─ Crea JwtSecurityToken con claims + firma HMAC-SHA256
    │
    └─ Retorna LoginResponse { token, nombreCompleto, rol, liderID, expiracion }
    │
    ▼
AuthController
    │  HTTP 200 OK  → LoginResponse
    ▼
Frontend (authApi.ts → AuthContext.iniciarSesion)
    │
    ├─ Guarda token   → localStorage["zentinela_token"]
    ├─ Guarda usuario → localStorage["zentinela_usuario"]
    └─ Actualiza estado React (AuthContext)
         │
         └─ Cada petición posterior:
               api interceptor agrega  Authorization: Bearer <token>
```

---

## Endpoints

### POST /api/auth/login

Autentica al usuario y emite un token JWT.

**Request body** (`application/json`)

```json
{
  "email": "lider@empresa.com",
  "password": "MiPassword1"
}
```

| Campo      | Tipo   | Validaciones                        |
|------------|--------|-------------------------------------|
| `email`    | string | Requerido, formato email válido     |
| `password` | string | Requerido                           |

**Response body — HTTP 200 OK**

```json
{
  "token": "<JWT>",
  "nombreCompleto": "Ana García",
  "rol": "Líder",
  "liderID": 7,
  "expiracion": "2026-03-31T14:00:00Z"
}
```

| Campo           | Tipo         | Descripción                                              |
|-----------------|--------------|----------------------------------------------------------|
| `token`         | string       | JWT firmado con HMAC-SHA256                              |
| `nombreCompleto`| string       | Nombre del usuario para mostrar en la UI                 |
| `rol`           | string       | Primer rol asignado al usuario en Identity               |
| `liderID`       | int \| null  | ID de la entidad Lider vinculada; null si no aplica      |
| `expiracion`    | DateTime UTC | Momento exacto en que expira el token                    |

**Códigos de respuesta**

| Código | Condición                                      |
|--------|------------------------------------------------|
| 200    | Credenciales válidas; incluye token en body    |
| 401    | Email no existe o contraseña incorrecta        |
| 400    | Body inválido (campos faltantes o mal formados)|

**Cuerpo del 401**

```json
{ "mensaje": "Credenciales incorrectas" }
```

---

### GET /api/auth/me

Devuelve los datos del usuario autenticado leyendo directamente los claims del token. No consulta la base de datos.

**Headers requeridos**

```
Authorization: Bearer <token>
```

**Response body — HTTP 200 OK**

```json
{
  "email": "lider@empresa.com",
  "rol": "Líder",
  "liderID": "7",
  "nombreCompleto": "Ana García"
}
```

> Nota: `liderID` se devuelve como `string` porque proviene directamente del claim. El cliente debe parsearlo si necesita compararlo como número.

**Códigos de respuesta**

| Código | Condición                          |
|--------|------------------------------------|
| 200    | Token válido; retorna claims       |
| 401    | Token ausente, inválido o expirado |

---

## Estructura de capas (backend)

```
src/Zentinela.API/
│
├── Domain/
│   └── Entities/
│       ├── ApplicationUser.cs     → extiende IdentityUser<int>; agrega NombreCompleto y LiderID
│       └── Lider.cs               → entidad de negocio; relación inversa con ApplicationUser
│
├── Application/
│   ├── DTOs/Auth/
│   │   ├── LoginRequest.cs        → contrato de entrada con validaciones DataAnnotations
│   │   └── LoginResponse.cs       → contrato de salida con token y datos del usuario
│   └── Interfaces/
│       └── IAuthService.cs        → interfaz del servicio de autenticación (inyección de dependencias)
│
├── Infrastructure/
│   ├── Data/
│   │   └── ZentinelaDbContext.cs  → IdentityDbContext<ApplicationUser, IdentityRole<int>, int>;
│   │                                 configura tablas y relación ApplicationUser → Lider
│   └── Services/
│       └── AuthService.cs         → implementación de IAuthService; usa UserManager + genera JWT
│
└── Controllers/
    └── AuthController.cs          → expone POST /api/auth/login y GET /api/auth/me
```

---

## Claims del token JWT

El token generado por `AuthService.GenerarToken()` incluye los siguientes claims:

| Claim              | Nombre estándar / custom | Valor                                       |
|--------------------|--------------------------|---------------------------------------------|
| Subject            | `sub`                    | ID entero del usuario (`ApplicationUser.Id`)|
| Nombre de usuario  | `userName` (custom)      | `ApplicationUser.UserName` (login de Identity) |
| Email              | `email`                  | Dirección de correo del usuario             |
| Rol                | `ClaimTypes.Role`        | Primer rol asignado en Identity             |
| ID de líder        | `liderID` (custom)       | `LiderID` como string; vacío si es null     |
| Nombre completo    | `nombreCompleto` (custom)| `NombreCompleto` del usuario                |
| JWT ID             | `jti`                    | GUID único por token (previene replay)      |

El token valida issuer, audience, lifetime y firma (HMAC-SHA256). La clave secreta, issuer, audience y duración se configuran en `appsettings.json` bajo la sección `JwtSettings`.

**Parámetros de validación activos en Program.cs**

- `ValidateIssuer = true`
- `ValidateAudience = true`
- `ValidateLifetime = true`
- `ValidateIssuerSigningKey = true`

---

## Roles del sistema

Los roles son strings gestionados por ASP.NET Identity (`AspNetRoles`). El tipo `Rol` definido en el frontend es la fuente de verdad de los valores esperados:

| Rol          | Propósito                                                                 |
|--------------|---------------------------------------------------------------------------|
| `Admin`      | Gestiona usuarios, carteras y asignaciones. No tiene `LiderID` vinculado. |
| `Supervisor` | Visibilidad total; KPIs de todos los líderes.                             |
| `Líder`      | Genera y descarga bases Excel de sus carteras. Tiene `LiderID` vinculado. |
| `Analista`   | Reportes, KPIs y seguimiento de estrategias.                              |
| `Asesor`     | Módulo futuro — gestión diaria y titulares asignados.                     |

El `AuthService` toma el **primer rol** de la lista devuelta por `UserManager.GetRolesAsync()`. Si el usuario no tiene ningún rol asignado, el claim `rol` toma el valor `"Sin rol"`.

---

## Estructura frontend

```
src/Zentinela.Web/src/
│
├── types/
│   └── auth.ts              → interfaces TypeScript: LoginRequest, LoginResponse, Usuario, Rol
│
├── api/
│   └── authApi.ts           → instancia de axios con baseURL desde VITE_API_URL;
│                               interceptor que inyecta Authorization: Bearer en cada petición;
│                               función login() que llama a POST /api/auth/login
│
├── context/
│   └── AuthContext.tsx      → AuthProvider: mantiene estado de usuario y token en React;
│                               persiste en localStorage con claves "zentinela_token" y
│                               "zentinela_usuario"; expone iniciarSesion(), cerrarSesion()
│                               y estaAutenticado; recupera sesión al recargar la página
│
└── components/
    └── RutaProtegida.tsx    → HOC de React Router: redirige a /login si no autenticado;
                               redirige a /sin-permiso si el rol del usuario no está en
                               rolesPermitidos (prop opcional)
```

### localStorage: claves utilizadas

| Clave                | Contenido                                     |
|----------------------|-----------------------------------------------|
| `zentinela_token`    | String JWT raw                                |
| `zentinela_usuario`  | JSON serializado del objeto `Usuario`         |

---

## Cómo agregar un nuevo usuario (pasos para el administrador)

El módulo de autenticación actual no expone un endpoint HTTP para crear usuarios. La creación se realiza directamente en la base de datos o mediante un seed de desarrollo con `UserManager`. Los pasos recomendados son:

1. **Crear el usuario en la base de datos** usando `UserManager<ApplicationUser>` desde un seed script o herramienta de administración interna.

2. **Datos mínimos requeridos**:
   - `Email` (debe ser único — `RequireUniqueEmail = true`)
   - `Password` (mínimo 8 caracteres, al menos un dígito — según config en `Program.cs`)
   - `NombreCompleto`
   - `LiderID` (asignar el ID del `Lider` correspondiente si el rol es `Líder`; dejar `null` para `Admin`, `Supervisor`, `Analista`)

3. **Asignar rol** mediante `UserManager.AddToRoleAsync(usuario, "Líder")`. El rol debe existir previamente en `AspNetRoles`.

4. **Verificar** haciendo `POST /api/auth/login` con las credenciales del nuevo usuario y confirmando que la respuesta incluye `liderID` correcto y el rol esperado.

---

## Notas de seguridad

| Aspecto                       | Implementación                                                                 |
|-------------------------------|--------------------------------------------------------------------------------|
| Contraseñas                   | Nunca se almacenan en texto plano; gestionadas por ASP.NET Identity (hash PBKDF2) |
| Clave JWT                     | Leída desde `appsettings.json` (`JwtSettings:SecretKey`); nunca hardcodeada   |
| Cadena de conexión            | Leída desde `appsettings.json` (`ConnectionStrings:DefaultConnection`); nunca hardcodeada |
| Token en cliente              | Almacenado en `localStorage`; adjuntado solo vía header `Authorization`        |
| CORS                          | Restringido al origen `http://localhost:5173` (frontend React en desarrollo)   |
| Validación de token           | Issuer, Audience, Lifetime y firma verificados en cada petición                |
| Rutas protegidas (frontend)   | `RutaProtegida` bloquea acceso si no hay token; verifica rol si se especifica `rolesPermitidos` |
| Unicidad de email             | Enforced por Identity (`RequireUniqueEmail = true`)                            |
| JWT ID (`jti`)                | Cada token tiene un GUID único para identificación (base para futura revocación) |
| Endpoints públicos            | Solo `POST /api/auth/login`; el resto requiere `[Authorize]`                  |
