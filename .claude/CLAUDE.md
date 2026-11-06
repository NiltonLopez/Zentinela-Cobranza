# Zentinela — contexto para Claude Code

Plataforma on-premise para líderes de cobranza de un call center. Los líderes arman, miden y corrigen
estrategias (bases en Excel, SMS, localización de titulares, recaudo) sobre la base de cobranza en
SQL Server. Una persona (el desarrollador) decide; los agentes proponen, implementan y documentan.

## Arquitectura

```
src/
├── Zentinela.Core/            entidades e interfaces, sin dependencias
├── Zentinela.Application/     DTOs, contratos de servicios, reglas de negocio puras
├── Infrastructure/
│   ├── ...SQLServer/          EF Core (3 DbContext: Zentinela, CallCenter, RecursosHumanos) + ADO.NET
│   └── ...FileExporter/       Excel con ClosedXML
├── Modules/ConsultaIntegrada/ módulo autónomo
├── Zentinela.API/             controladores REST, JWT, roles
└── Zentinela.Web/             SPA React 19 + Vite + TypeScript + TanStack Query
etl/                           carga diaria de estado de titulares (Python + PowerShell + T-SQL)
tools/CargueHarness/           casos reales de punta a punta del Cargue de Fuentes
```

- **Base de datos:** las tablas del negocio (`DBCallCenter`) son de otro sistema. Zentinela lee, y escribe
  solo en tablas propias o en las que un ADR lo autoriza. EF para el CRUD; ADO.NET con SQL a mano para
  las consultas masivas, que es donde está el rendimiento (ver `docs/optim/`).
- **Roles:** Admin, Director, Líder, Líder Externo, Asesor, Auxiliar de Sistemas. **El alcance por líder
  se resuelve en el servidor** (`AlcanceLiderExtensions`), nunca confiando en lo que manda el cliente.
- **Conexiones y secretos:** `appsettings.json` solo trae marcadores (`__DB_PASSWORD__`, `__JWT_SECRET__`);
  los valores reales viven en `appsettings.Development.json` (ignorado) y en el servidor.

## Cómo se trabaja

1. **Planear antes de tocar código** en todo lo que no sea trivial: explorar, medir contra la base real
   (solo lectura), proponer el diseño y esperar la aprobación. Las preguntas de negocio se le hacen a la
   persona; no se inventan.
2. **Implementar por etapas desplegables**, cada una en su propio worktree y su rama. Las tareas grandes
   se reparten en subagentes por etapa; la sesión principal revisa e integra.
3. **Documentar en la misma entrega:** ADR en `docs/decisions/` para cada decisión de diseño,
   `docs/api-referencia.md`, `docs/schema.md` si cambia la base y `docs/para-tener-en-cuenta.md`
   (bandeja de pendientes, riesgos y deudas, que se cierran con razón explícita).
4. **Integrar a `main` y a `production` solo cuando la persona lo pide.** Un push a `production`
   despliega (`.github/workflows/deploy.yml`).

## Verificación

```bash
dotnet build
dotnet test
cd src/Zentinela.Web && npx tsc --noEmit -p tsconfig.app.json && npm test && npm run build
npx eslint <archivos tocados>
```

ESLint se corre sobre lo que se toca: el proyecto arrastra unos 20 errores de lint viejos, sobre todo de las
reglas nuevas de React (refs y `setState` dentro de efectos) que se corrigen al pasar por cada archivo, con
prueba manual, no en una pasada masiva.

La prueba manual la hace la persona: **no levantar la API ni abrir el navegador** para "probar".
La API corriendo bloquea el build (los binarios quedan en uso), así que se pide que la detenga.

## Límites (y por qué existen)

Cada regla salió de algo que pasó. Se cumplen aunque parezca que ahorrarlas sería más rápido.

- **La IA no escribe en producción.** Migraciones, DDL, trabajos del Agente SQL y publicaciones se
  entregan como script idempotente, con rollback comentado, y **los ejecuta una persona**.
- **Nada de comandos de administración del servidor** (`DBCC`, `KILL`, `sp_configure`, `ALTER SERVER…`),
  ni siquiera "para diagnosticar". Un subagente limpió una vez la caché de planes de producción
  queriendo medir una consulta. Todo prompt a un subagente con acceso a `sqlcmd` lo prohíbe por escrito.
- **Las credenciales nunca se escriben ni se imprimen.** Con `sqlcmd` se usa `SQLCMDPASSWORD`, nunca
  `-P`, porque la línea de comandos queda registrada.
- **No se confía en el estado de un proceso, se mira el dato.** El ETL de estado de titulares vació una
  vez la tabla principal y reportó éxito; nadie lo notó en días. Desde entonces tiene compuertas que se
  niegan a publicar datos incompletos, y su skill (`.claude/skills/etl-estado-titular/`) verifica primero
  los datos y después el trabajo.
- **Nada de borrados sin respaldo ni de `TRUNCATE` sobre tablas del negocio.** Primero se respalda y se
  documenta la fecha de limpieza.
- **Git:** nunca `git stash` a secas (la pila se comparte entre worktrees y sesiones paralelas); se usa
  un commit WIP. No se usa `--no-verify`.

## Herramientas propias

- **Comandos** (`.claude/commands/`): `/agregar-fuente-datos` integra una fuente nueva en todas las capas
  (es la lista de verificación que salió de varias integraciones con errores), `/nuevo-modulo`,
  `/documentar-modulo`, `/revisar-codigo`, `/diagnostico-servidor`, `/generar-base`.
- **Skill** `etl-estado-titular`: diagnóstico del ETL en un orden fijo, causas conocidas y aviso por
  WhatsApp sin repetir alertas.

## Convenciones

- Código, comentarios, commits y documentación **en español**; comentarios solo donde la lógica no se
  explica sola.
- Commits por módulo: `feat(modulo): …`, `fix(modulo): …`, `docs(modulo): …`.
- Pruebas para toda regla de negocio crítica y, cuando se puede, para cada error que se corrige.
- TypeScript estricto, evitando `any`; el estado del servidor vive en TanStack Query.

## Evolución

El proyecto empezó con cuatro agentes con rol fijo (orquestador, backend, QA, docs), cada uno en su
worktree. Con el tiempo pasó a una sesión principal que planea con la persona y reparte etapas a
subagentes, más comandos y skills que guardan lo aprendido. El historial de commits muestra ese cambio.
