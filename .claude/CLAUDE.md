# Zentinela — CLAUDE.md Maestro

## Descripción del proyecto
Sistema On-Premise para líderes de cobranza que permite la creación, 
medición y corrección de estrategias de cobranza. Conectado a SQL Server 
corporativo. Desplegado en servidor dedicado de la oficina.

## Stack técnico
- Lenguaje: C# / .NET
- Framework: ASP.NET Core MVC + Razor Pages
- Base de datos: SQL Server (10.0.0.89,1433 / DBCallCenter)
- ORM: Entity Framework Core
- Generación Excel: ClosedXML
- Autenticación: ASP.NET Identity
- Control de versiones: GitHub
- Metodología: SCRUM + Clean Architecture

## Arquitectura del entorno agéntico
Este proyecto opera con múltiples agentes de Claude Code en paralelo:
- **Orquestador**: coordina tareas, distribuye trabajo a agentes especializados
- **Agente Backend**: desarrollo C# / ASP.NET Core / SQL Server
- **Agente QA**: pruebas, validaciones, revisión de código
- **Agente Docs**: documentación automática de código complejo

Regla: El orquestador nunca escribe código directamente. 
Solo delega, revisa y consolida.

## Modelos por tipo de tarea
- Sonnet 4.6: CRUD, scaffolding, queries rutinarias, documentación
- Opus 4.6: decisiones de arquitectura, resolución de conflictos complejos, 
  diseño de módulos nuevos

## Estructura del proyecto
```
Zentinela/
├── src/          → código fuente ASP.NET Core MVC
├── docs/         → documentación, schema.md, decisiones de arquitectura
├── tests/        → pruebas unitarias e integración
└── .claude/
    ├── CLAUDE.md         → este archivo (contexto maestro)
    └── commands/         → slash commands personalizados
```

## Base de datos
- Servidor: 10.0.0.89,1433
- Base de datos: DBCallCenter
- Schema completo: ver docs/schema.md (autogenerado por Claude Code)
- Tablas clave: Titular, Cartera, Titular_Cartera, Líder
- Relación principal: Líder → Cartera → Titular_Cartera → Titular

## Roles del sistema
- **Administrador**: gestiona usuarios, carteras y asignaciones
- **Supervisor/Gerente**: visibilidad total, KPIs de todos los líderes
- **Líder de cobranza**: genera y descarga bases Excel de sus carteras
- **Analista de datos**: reportes, KPIs, seguimiento de estrategias
- **Asesor**: (módulo futuro) gestión diaria y titulares asignados

## Módulos planificados
1. **Generador de Bases Excel** ← módulo actual en desarrollo
2. Dashboard de KPIs por líder
3. Seguimiento de estrategias (qué funcionó)
4. Gestión de carteras y titulares
5. Reportes de SLA y tiempos
6. Administración de usuarios y roles

## Módulo actual: Generador de Bases Excel
- El líder inicia sesión y ve solo sus carteras asignadas
- Aplica filtros dinámicos según campos disponibles en las vistas SQL
- Previsualiza la base antes de descargar
- Descarga el Excel con estructura fija de columnas
- Las columnas exactas están definidas en docs/schema.md

## Reglas globales para todos los agentes
1. Respetar Clean Architecture en todo momento
2. Comentar código complejo en español
3. Nunca hardcodear cadenas de conexión — usar appsettings.json
4. Nunca exponer credenciales en el código fuente
5. Generar tests para toda lógica de negocio crítica
6. Ante decisiones de arquitectura: consultar al orquestador
7. Cambios en schema de BD: documentar en docs/schema.md
8. Commits en español, descriptivos y por módulo