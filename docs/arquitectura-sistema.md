# Arquitectura del sistema — Zentinela

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Backend API | C# / .NET 9 — ASP.NET Core Web API |
| Autenticación | ASP.NET Core Identity + JWT (HMAC-SHA256) |
| ORM | Entity Framework Core (SQL Server provider) |
| Base de datos | SQL Server — 10.0.0.129,1433 / DBCallCenter |
| Documentación API | OpenAPI + Scalar (solo en desarrollo) |
| Frontend | React 18 + TypeScript (Vite) |
| Enrutamiento frontend | React Router v6 |
| Estado servidor | TanStack Query (React Query) |
| Testing backend | xUnit + Moq + EF Core InMemory |

---

## Estructura del repositorio

```
Zentinela/
├── .claude/
│   ├── CLAUDE.md                  — contexto maestro para agentes
│   └── commands/                  — slash commands personalizados
├── docs/
│   ├── arquitectura-sistema.md    — este archivo
│   ├── api-referencia.md
│   ├── design-system.md
│   ├── guia-tests.md
│   ├── arquitectura-auth.md
│   ├── schema.md
│   └── migrations/
├── src/
│   ├── Zentinela.API/             — proyecto backend ASP.NET Core
│   │   ├── Application/
│   │   │   ├── DTOs/
│   │   │   │   ├── Auth/          — LoginRequest, LoginResponse
│   │   │   │   └── Carteras/      — CarteraDto
│   │   │   └── Interfaces/        — IAuthService, ICarteraRepository
│   │   ├── Controllers/           — AuthController, CarterasController
│   │   ├── Domain/
│   │   │   └── Entities/          — ApplicationUser, Cartera, Lider
│   │   ├── Infrastructure/
│   │   │   ├── Data/              — ZentinelaDbContext, DbSeeder
│   │   │   ├── Repositories/      — CarteraRepository
│   │   │   └── Services/          — AuthService
│   │   └── Program.cs
│   └── Zentinela.Web/             — proyecto frontend React
│       └── src/
│           ├── components/
│           │   ├── layout/        — AppLayout, Navbar, Sidebar
│           │   ├── ZButton.tsx
│           │   ├── MetricCard.tsx
│           │   └── RutaProtegida.tsx
│           ├── context/           — AuthContext
│           ├── pages/             — LoginPage, GeneradorPage, CarterasPage, HistorialPage, KPIsPage, SinPermisoPage
│           └── types/             — auth.ts, ui.ts, carteras.ts
└── tests/
    └── Zentinela.API.Tests/       — AuthServiceTests, CarterasControllerTests, CarteraRepositoryTests
```

---

## Capas de la arquitectura

Zentinela sigue Clean Architecture. Las dependencias apuntan siempre hacia el centro (Domain), nunca hacia afuera.

```
┌────────────────────────────────────────────┐
│              Controllers                   │  ← Capa de presentación
│   AuthController / CarterasController      │    Recibe HTTP, devuelve IActionResult
└──────────────────┬─────────────────────────┘
                   │ usa interfaces de
┌──────────────────▼─────────────────────────┐
│              Application                   │  ← Capa de aplicación
│   Interfaces: IAuthService                 │    Contratos — no contiene lógica concreta
│               ICarteraRepository           │
│   DTOs: LoginRequest, LoginResponse        │
│          CarteraDto                        │
└──────────────────┬─────────────────────────┘
                   │ implementadas por
┌──────────────────▼─────────────────────────┐
│            Infrastructure                  │  ← Capa de infraestructura
│   Services:      AuthService               │    Acceso a Identity, JWT, BD
│   Repositories:  CarteraRepository         │
│   Data:          ZentinelaDbContext         │
│                  DbSeeder                  │
└──────────────────┬─────────────────────────┘
                   │ persiste y lee
┌──────────────────▼─────────────────────────┐
│               Domain                       │  ← Núcleo — sin dependencias externas
│   Entities: ApplicationUser, Cartera, Lider│
└────────────────────────────────────────────┘
```

### Registro de dependencias (Program.cs)

| Interfaz | Implementación | Lifetime |
|---|---|---|
| `IAuthService` | `AuthService` | Scoped |
| `ICarteraRepository` | `CarteraRepository` | Scoped |
| `ZentinelaDbContext` | EF Core + SQL Server | Scoped |
| `UserManager<ApplicationUser>` | ASP.NET Identity | Scoped |

---

## Diagrama de flujo de una petición

El siguiente diagrama muestra el ciclo completo de una solicitud HTTP autenticada, usando `GET /api/carteras` como ejemplo:

```
Cliente (React)
    │
    │  GET /api/carteras
    │  Authorization: Bearer <JWT>
    ▼
[Middleware CORS]
    │  Verifica origen permitido (http://localhost:5173)
    ▼
[Middleware Authentication]
    │  Valida firma JWT, issuer, audience, expiración
    │  Extrae claims → ClaimsPrincipal (User)
    ▼
[Middleware Authorization]
    │  Verifica [Authorize] en CarterasController
    ▼
CarterasController.ObtenerCarteras()
    │  Lee claim "rol" y "liderID" desde User.Claims
    │  Si Admin → llama ICarteraRepository.ObtenerTodasAsync()
    │  Si Líder → llama ICarteraRepository.ObtenerPorLiderAsync(liderID)
    │  Si otro  → retorna 403 Forbid
    ▼
CarteraRepository
    │  Consulta ZentinelaDbContext.Carteras
    │  Filtra por LiderID (o devuelve todas)
    │  OrderBy(NombreCartera).AsNoTracking()
    ▼
ZentinelaDbContext
    │  EF Core genera SQL y ejecuta contra SQL Server
    │  Servidor: 10.0.0.129,1433 / DBCallCenter
    ▼
Base de datos SQL Server (tabla Cartera)
    │
    │  Retorna filas
    ▼
CarteraRepository → IEnumerable<Cartera>
    ▼
CarterasController
    │  Proyecta entidades a CarteraDto (ID, NombreCartera)
    ▼
HTTP 200 OK — JSON: [{ "id": 1, "nombreCartera": "..." }, ...]
    ▼
Cliente (React)
```

---

## Modelo de datos

### Entidades del dominio

#### ApplicationUser
Extiende `IdentityUser<int>`. Se mapea a la tabla `AspNetUsers`.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Id` | `int` | PK (heredado de Identity) |
| `Email` | `string` | Email único, usado como username de login |
| `NombreCompleto` | `string` | Nombre para mostrar en la UI |
| `LiderID` | `int?` | FK nullable hacia `Lider.ID`. Null para usuarios Admin |

#### Lider
Se mapea a la tabla existente `Lider` en SQL Server (excluida de migraciones).

| Propiedad | Tipo | Descripción |
|---|---|---|
| `ID` | `int` | PK |
| `Nombre` | `string` | Nombre del líder (max 100 chars) |
| `Usuario` | `ApplicationUser?` | Navegación inversa hacia el usuario del sistema |

#### Cartera
Se mapea a la tabla existente `Cartera` en SQL Server (excluida de migraciones).

| Propiedad | Tipo | Descripción |
|---|---|---|
| `ID` | `int` | PK |
| `NombreCartera` | `string` | Nombre de la cartera (max 50 chars) |
| `LiderID` | `int?` | FK nullable hacia `Lider.ID` |
| `Lider` | `Lider?` | Propiedad de navegación |

### Relaciones

```
ApplicationUser (AspNetUsers)
    │ LiderID (FK, nullable, SetNull on delete)
    │
    ▼
Lider (tabla existente en producción)
    │ ID
    │
    ▼  (uno a muchos)
Cartera (tabla existente en producción)
    LiderID (FK, nullable, SetNull on delete)
```

### Roles del sistema

Los roles se crean automáticamente por `DbSeeder` al iniciar en modo desarrollo:

| Rol | Descripción |
|---|---|
| `Admin` | Acceso total, ve todas las carteras |
| `Supervisor` | Visibilidad total de líderes y KPIs |
| `Líder` | Ve solo sus carteras asignadas |
| `Analista` | Reportes y KPIs |
| `Asesor` | Módulo futuro — sin lógica implementada actualmente |

---

## Configuración por entorno

La cadena de conexión y los secretos JWT se leen desde `appsettings.json` / `appsettings.Development.json` mediante `IConfiguration`. **Nunca** se hardcodean en el código fuente.

### Claves esperadas en appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=DBCallCenter;..."
  },
  "JwtSettings": {
    "SecretKey": "<mínimo 32 caracteres>",
    "Issuer": "zentinela",
    "Audience": "zentinela-api",
    "ExpirationHours": 8
  }
}
```

### Comportamientos por entorno

| Comportamiento | Development | Production |
|---|---|---|
| OpenAPI / Scalar UI | Habilitado (`/openapi`, `/scalar/v1`) | Deshabilitado |
| DbSeeder (roles + admin) | Ejecutado al arrancar | No ejecutado |
| Mensajes de error detallados | Sí | No |

### CORS

En `Program.cs` se define la política `FrontendPolicy` que permite solicitudes únicamente desde `http://localhost:5173` (servidor de desarrollo de Vite).

---

## Cómo arrancar el proyecto localmente

### Requisitos previos

- .NET 9 SDK
- Node.js 20+ y npm
- Acceso a SQL Server en 10.0.0.129,1433

### Backend

```bash
# 1. Ir al directorio del API
cd src/Zentinela.API

# 2. Configurar appsettings.Development.json con cadena de conexión y JwtSettings

# 3. Aplicar migraciones de Identity (tablas AspNet*)
dotnet ef database update

# 4. Arrancar el servidor (puerto 5000 por defecto en HTTP)
dotnet run
```

Al iniciar en modo Development, `DbSeeder` crea automáticamente los roles y el usuario administrador inicial:
- Email: `admin@zentinela.com`
- Password: `Admin1234!`

### Frontend

```bash
# 1. Ir al directorio del frontend
cd src/Zentinela.Web

# 2. Instalar dependencias
npm install

# 3. Arrancar el servidor de desarrollo (Vite — puerto 5173)
npm run dev
```

El frontend queda disponible en `http://localhost:5173` y apunta al API en `http://localhost:5000`.
