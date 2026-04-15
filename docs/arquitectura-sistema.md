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

| Módulo | Sub-módulo | Estado | Roles |
|---|---|---|---|
| Autenticación | Login / cambio de contraseña | ✅ Completo | Todos |
| Autenticación | Registro de usuario | ✅ Completo | Público |
| Home | Dashboard (Asesor / Líder / Director) | ✅ Completo | Todos |
| Cartera | Creador Zentinela (Generador de Bases Excel) | ✅ Completo | Admin, Líder, Director |
| Cartera | Calificador de Cartera | ✅ Completo | Admin, Líder, Director |
| Informes | Informes de Virtualidad | ✅ Completo | Admin, Líder, Director |
| Informes | Informes de Recaudo | 🚧 Placeholder | Admin, Líder, Director |
| Administración | Gestión de Asignaciones | ✅ Completo | Admin |
| Administración | Gestión de Líderes y Carteras | ✅ Completo | Admin |
| Administración | Gestión de Personal | ✅ Completo | Admin |
| Administración | Cargue de Reportes | ✅ Completo | Admin |
| Administración | Cargue de Pagos | ✅ Completo | Admin |
| Consulta Integrada | — | ✅ Completo | Todos |

---

## Navegación

| Tab navbar | Ruta default | Roles que lo ven |
|---|---|---|
| Home | `/home` | Todos |
| Cartera | `/cartera/creador` | Admin, Líder, Director |
| Informes | `/informes/virtualidad` | Admin, Líder, Director |
| Administración | `/administracion/gestion` | Admin |
| Consulta Integrada | `/consulta` | Todos |

---

## CORS

Política `FrontendPolicy`: origen `http://localhost:5173`, cualquier header y método, con credenciales.
En producción: actualizar `WithOrigins` con la URL del servidor dedicado.

> **Nota:** No llamar `app.UseRouting()` explícitamente en ASP.NET Core 9 — rompe CORS con endpoint routing. El routing es implícito.

---

## Dashboard Director — detalles de implementación

### Fuente de datos real
`HistoricoEstadoTitular` **está vacía** — el ETL nunca la ha poblado. La única fuente real de gestiones es `UltimoEstadoTitular` (2,262 filas a Abril 2026, `MAX(FechaUltimaGestion)` = 2026-04-09).

Ambos servicios (`DashboardDirectorService` y `DashboardAsesorService`) utilizan `UltimosEstadosTitular` hasta que el ETL comience a poblar `HistoricoEstadoTitular`.

### Selector de período (Director)
El director puede analizar cualquier rango de fechas via `fechaDesde` / `fechaHasta` en el query string. El frontend ofrece presets:

| Preset | Desde | Hasta | Valor API |
|--------|-------|-------|-----------|
| Último ciclo (default) | `MAX(FechaUltimaGestion)` | idem | sin params |
| Hoy | hoy | hoy | `fechaDesde=hoy&fechaHasta=hoy` |
| Este mes | 1° del mes | hoy | `fechaDesde=YYYY-MM-01&fechaHasta=hoy` |
| Mes pasado | 1° mes anterior | último día mes anterior | explicit |
| Últimos 7 días | hoy−6 | hoy | explicit |
| Últimos 30 días | hoy−29 | hoy | explicit |
| Personalizado | date picker | date picker | explicit |

Cuando el endpoint recibe `fechaDesde`/`fechaHasta` como `null`, el backend resuelve el "Último ciclo" usando `MAX(FechaUltimaGestion)` de `UltimoEstadoTitular`.

### Tipificaciones relevantes
Definidas como constantes en `Application.Tipificaciones`:
- `ContactoPositivo`: gestiones que cuentan como contacto efectivo
- `AcuerdosVigentes`: perfiles con promesa de pago activa
- `AcuerdoAbono`: gestiones con compromiso de abono (numerador de TasaEfectividad)

### Métricas calculadas
- **TasaContactabilidad** = ContactosPositivos / TotalGestiones
- **TasaEfectividad** = AcuerdosAbonos / ContactosPositivos

### Vista global vs. desglose
- Sin `liderID`: métricas globales + `FilasLider` (desglose por líder)
- Con `liderID`: métricas del líder + `FilasAsesor` (desglose por asesor, nombres de `DBRecursosHumanos`)
- Con `liderID + carteraID`: métricas de la cartera + `FilasAsesor`

---

## Optimizaciones de base de datos

### Índices — Dashboard (UltimoEstadoTitular)
Creados en 2026-04-13 para soportar los filtros del Dashboard Director y Asesor:

```sql
-- Consultas por agente (Dashboard Asesor)
CREATE INDEX IX_UltimoEstadoTitular_AgenteID
    ON UltimoEstadoTitular(AgenteID)
    INCLUDE (FechaUltimaGestion, UltimoPerfil, FechaActualizacion,
             FechaPromesa, ValorPromesa, SaldoTotal, TitularID);

-- Consultas por cartera + fecha (Dashboard Director)
CREATE INDEX IX_UltimoEstadoTitular_CarteraID_Fecha
    ON UltimoEstadoTitular(CarteraID, FechaUltimaGestion)
    INCLUDE (AgenteID, UltimoPerfil);

-- Consultas globales por fecha (Dashboard Director sin filtro)
CREATE INDEX IX_UltimoEstadoTitular_Fecha
    ON UltimoEstadoTitular(FechaUltimaGestion)
    INCLUDE (CarteraID, AgenteID, UltimoPerfil);
```

### Índices — Calificador de Cartera (tablas fuente de vw_BaseMadre)
La vista `vw_BaseMadre` usa `ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC)` en cada tabla de datos externos. Sin índices, cada consulta hacía full scans sobre ~10M+ filas.

Índices creados (patrón `TitularID, FechaConsulta DESC`):

| Tabla | Índice |
|-------|--------|
| ARLSura | `IX_ARLSura_TitularID_Fecha` |
| Cajacopi | `IX_Cajacopi_TitularID_Fecha` |
| Camara | `IX_Camara_TitularID_Fecha` |
| Compensados | `IX_Compensados_TitularID_Fecha` |
| Fosyga | `IX_Fosyga_TitularID_Fecha` |
| Positiva | `IX_Positiva_TitularID_Fecha` |
| PositivaEmpresas | `IX_PositivaEmpresas_TitularID_Fecha` |
| RGM | `IX_RGM_TitularID_Fecha` |
| RUES | `IX_RUES_TitularID_Fecha` |
| Runt | `IX_Runt_TitularID_Fecha` |
| SaludTotal | `IX_SaludTotal_TitularID_Fecha` |
| SIMIT | `IX_SIMIT_TitularID_Fecha` |
| SOS | `IX_SOS_TitularID_Fecha` |
| Tigo | `IX_Tigo_TitularID_Fecha` |

Índice adicional en `Titular_Cartera` para filtros por líder/cartera:
```sql
CREATE INDEX IX_TitularCartera_LiderID_CarteraID
    ON Titular_Cartera(LiderID, CarteraID);
```
