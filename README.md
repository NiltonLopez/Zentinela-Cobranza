# Zentinela

Plataforma on-premise para los líderes de cobranza de un call center: arman, miden y corrigen
estrategias (bases en Excel, campañas SMS, localización de titulares, recaudo) sobre millones de
registros en SQL Server, con roles y alcance por líder resueltos en el servidor.

**Stack:** .NET 9 · Clean Architecture · EF Core + ADO.NET · SQL Server · React 19 + Vite + TypeScript ·
TanStack Query · JWT por roles · ETL en Python/PowerShell/T-SQL · despliegue con GitHub Actions a IIS.

**Ejecutar** (.NET 9 y Node 22). Las pruebas no necesitan base de datos; la app sí:

```bash
dotnet test                                        # más de 1.000 pruebas
cd src/Zentinela.Web && npm install && npm test
dotnet run --project src/Zentinela.API             # con appsettings.Development.json apuntando a SQL Server
npm run dev --prefix src/Zentinela.Web
```

**Hecho con IA:** cerca de 400 commits desde marzo de 2026, la mayoría en pareja con Claude Code.
Cómo trabajo con los agentes y qué límites les puse: [`.claude/CLAUDE.md`](.claude/CLAUDE.md).
Las decisiones de diseño están en [`docs/decisions/`](docs/decisions/) (44 ADRs).
