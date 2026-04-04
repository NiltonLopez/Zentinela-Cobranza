# Arquitectura del sistema — Zentinela

## Stack tecnológico

| Capa | Tecnología |
|---|---|
| Backend API | C# / .NET 9 — ASP.NET Core Web API |
| Autenticación | ASP.NET Core Identity + JWT (HMAC-SHA256) |
| ORM | Entity Framework Core 9 (SQL Server provider) |
| Bases de datos | SQL Server — 10.0.0.89,1433 (3 BDs) |
| Documentación API | OpenAPI + Scalar (solo desarrollo) |
| Frontend | React 18 + TypeScript (Vite) |
| Enrutamiento frontend | React Router v6 |
| Estado servidor | TanStack Query (React Query) |
| Testing backend | xUnit |
| Testing frontend | Vitest + Testing Library |

---

## Estructura del repositorio

```
Zentinela/
├── .claude/
│   ├── CLAUDE.md                  — contexto maestro para agentes
│   └── commands/                  — slash commands personalizados
├── docs/
│   ├── arquitectura-sistema.md    — este archivo
│   ├── api-referencia.md          — endpoints documentados
│   ├── design-system.md           — variables CSS y componentes UI
│   ├── guia-tests.md              — guía de pruebas
│   └── schema.md                  — schema de BDs
├── src/
│   ├── Zentinela.Core/            — entidades de dominio, interfaces de repositorio
│   ├── Zentinela.Application/     — servicios, DTOs, interfaces de servicios, constantes
│   ├── Infrastructure/
│   │   ├── Zentinela.Infrastructure.SQLServer/   — EF Core, Identity, JWT, servicios
│   │   └── Zentinela.Infrastructure.FileExporter/ — exportación Excel (pendiente)
│   ├── Zentinela.API/             — controllers, Program.cs
│   └── Zentinela.Web/             — React + TypeScript (Vite)
└── tests/
```

---

## Clean Architecture — capas y dependencias

```
API  →  Application  →  Core
 ↘         ↗
  Infrastructure
```

| Capa | Responsabilidad | Puede referenciar |
|---|---|---|
| `Core` | Entidades de dominio, interfaces de repositorio | Nadie |
| `Application` | DTOs, interfaces de servicios, servicios de app, constantes | Core |
| `Infrastructure` | EF Core, Identity, JWT, implementaciones de servicios | Application + Core |
| `API` | Controllers, middleware, DI bootstrap | Application + Infrastructure |

**Reglas estrictas:**
- Controllers nunca inyectan repositorios directamente — siempre a través de un servicio de Application
- La lógica de negocio (quién ve qué, qué reglas aplican) vive en Application, no en controllers
- `ClaimsPrincipal` se lee en el controller y se convierte a un DTO de contexto (`ContextoUsuario`) antes de pasar a Application

---

## Tres DbContexts

| Contexto | Base de datos | Propietario | Migraciones |
|---|---|---|---|
| `ZentinelaDbContext` | `DBZentinela` | Zentinela | ✅ Activas — EF Core gestiona el schema |
| `CallCenterDbContext` | `DBCallCenter` | Sistema de cobranza | ❌ `ExcludeFromMigrations` — solo lectura/escritura puntual |
| `RecursosHumanosDbContext` | `DBRecursosHumanos` | RRHH | ❌ `ExcludeFromMigrations` — solo lectura |

Misma instancia SQL Server → cross-DB queries con performance nativo.

---

## Autenticación y autorización

- **Esquema:** JWT Bearer (HMAC-SHA256)
- **Claims en el token:** `sub` (UsuarioID), `email`, `role`, `liderID`, `nombreCompleto`, `numeroDocumento`, `jti`
- **Roles:** `Admin`, `Líder`, `Director`, `Asesor` — definidos en `Application.Roles`
- **Mapeo de roles:** `CargoID` de RRHH → Rol Zentinela (ver `Application.CargoRoles`)
- **Primer login:** `RequiereCambioPassword = true` → frontend intercepta y redirige a `/cambiar-password`
- **Configuración:** `IOptions<JwtSettings>` (fuente: `appsettings.json` sección `JwtSettings`)

---

## Registro de usuario

Flujo sin autenticación previa (auto-registro):

1. Empleado ingresa `Cédula` + `UsuarioControlNext`
2. Sistema valida cédula en `DBRecursosHumanos.Empleado`
3. Deriva rol desde `Empleado.CargoID` (mapa en `CargoRoles`)
4. Crea `Agente` en `DBCallCenter.Agente` (AgenteID = cédula)
5. Crea `ApplicationUser` en `DBZentinela` con `RequiereCambioPassword = true`
6. Contraseña inicial = cédula (el usuario la cambia en el primer login)

---

## Módulos implementados

| Módulo | Estado | Roles |
|---|---|---|
| Autenticación (login / cambio de contraseña) | ✅ Completo | Todos |
| Registro de usuario | ✅ Completo | Público |
| Dashboard Asesor | ✅ Completo | Asesor |
| Generador de Bases Excel | 🚧 Esqueleto UI | Admin, Líder, Director |
| Carteras | 🚧 Listado básico | Admin, Líder, Director |
| KPIs / Reportes | 🚧 Placeholder | Admin, Director |
| Dashboard Líder | ⏳ Pendiente | Líder |
| Dashboard Director | ⏳ Pendiente | Director |

---

## Navegación

| Tab navbar | Ruta default | Roles que lo ven |
|---|---|---|
| Dashboard | `/dashboard` | Asesor |
| Bases | `/bases` | Admin, Director, Líder |
| Carteras | `/carteras` | Admin, Director, Líder |
| Reportes | `/kpis` | Admin, Director |

Redirect por defecto: Asesor → `/dashboard`, otros → `/bases`

---

## CORS

Política `FrontendPolicy`: origen `http://localhost:5173`, cualquier header y método, con credenciales.
En producción: actualizar `WithOrigins` con la URL del servidor dedicado.

> **Nota:** No llamar `app.UseRouting()` explícitamente en ASP.NET Core 9 — rompe CORS con endpoint routing. El routing es implícito.
