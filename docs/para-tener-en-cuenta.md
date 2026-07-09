# Para tener en cuenta

Bitácora de revisión general del proyecto. Se trata como **inbox**, no como
backlog: los items se cierran o se descartan con razón explícita.

Última actualización: **2026-07-09**.

---

## Notas de arquitectura — rendimiento en servidor compartido (2026-07-09)

- **El SQL Server corporativo tiene solo 8 núcleos y es compartido con toda la oficina.**
  Cualquier query que consulte `dbo.fn_BaseMadrePorCartera` / `dbo.fn_BaseMadreParaTitulares`
  sin `OPTION (MAXDOP N)` puede pedir los 8 núcleos completos y monopolizar el
  servidor durante toda su ejecución — no solo se hace lenta a sí misma, ahoga
  a **cualquier otra query concurrente** (Calificador, Creador, lo que sea).
  Ya se agregó `MAXDOP 4` a: `CalificadorCarteraService.SqlAgregados` y
  `EscribirLocalizacionCsvAsync`, y las 3 queries de `BaseMadreRepository`
  (Creador). Medido: cartera de 628k filas, 83.7s sin tope → 12.7s con
  `MAXDOP 4` (6.5×). Si se agrega una consulta nueva sobre estas TVFs,
  replicar el mismo tope.
  **`OPTION (RECOMPILE)` NO es automático** — ayuda cuando el parámetro es un
  TVP con cardinalidad variable (Calificador), pero mide peor cuando los
  parámetros son escalares simples como `CarteraID`/`LiderID` (Creador: medido
  37-59s con RECOMPILE vs 12.7s sin él). Medir antes de asumir cuál ayuda.

- **⚠️ Incidente real (2026-07-09): `OPTION (...)` en SQL crudo ejecutado vía
  EF Core rompió el Creador en producción (500 en el 100% de las llamadas).**
  `BaseMadreRepository.ObtenerAsync` corría el conteo así:
  `_context.Database.SqlQueryRaw<int>(countSql, ...).FirstOrDefaultAsync()`.
  EF Core traduce `.FirstOrDefaultAsync()` sobre `SqlQueryRaw` **envolviendo
  el SQL crudo en un subquery** para aplicar `TOP(1)`
  (`SELECT TOP(1) [v] FROM (<countSql>) AS t`). Como `countSql` terminaba en
  `OPTION (MAXDOP 4)`, ese `OPTION` quedaba dentro del subquery — sintaxis
  inválida en SQL Server. El benchmark con `sqlcmd` (SQL suelto, top-level)
  no lo detectó porque no reproduce cómo EF Core compone la query real.
  **Fix:** cambiar `.FirstOrDefaultAsync()` por `.ToListAsync()` +
  `.FirstOrDefault()` en C# — no dispara wrapping.
  **Regla práctica:** antes de agregar `OPTION(...)` a SQL crudo vía
  `SqlQueryRaw`/`FromSqlRaw`, revisar qué operador LINQ se encadena después.
  `FirstOrDefaultAsync`/`SingleAsync`/`Take`/`Where`/`OrderBy` compuestos
  probablemente disparan wrapping en subquery — `OPTION` ahí rompe.
  `ToListAsync()` solo, o ejecución vía ADO.NET puro (`SqlCommand` directo,
  como el batch del Calificador), son seguros. Ante la duda, probar contra la
  app real corriendo (login + curl a un endpoint), no solo `sqlcmd` aislado.

- **`.OrderByDescending(x => Peso(x)).First()` para sacar el máximo de un grupo es un anti-patrón.**
  Ordena el grupo completo (O(n log n)) solo para quedarse con el primer
  elemento. Usar `.MaxBy(x => Peso(x))` (.NET 6+) — un solo pase O(n), sin el
  overhead de ordenar. Corregido en `ComparadorCarterasService.cs` (6
  ocurrencias) — mejoró la cartera más grande del sistema de 14.7s a 10.7s en
  caliente. Si aparece este patrón en código nuevo, cambiarlo directamente.

---

## Notas de arquitectura — fuentes de datos

- **MutualSer expone 2 celulares como entradas independientes en el selector.**
  Es la única fuente con 2 entradas en `BaseMadreRepository.ColumnaCelular`
  (`MutualSer` → `[Celular MutualSer]`, `MutualSer2` → `[Celular 2 MutualSer]`).
  Excepción consciente a la convención "1 fuente = 1 entrada": la tabla expone
  `NumeroTelefonoCelular` y `NumeroTelefonoCelular2` como columnas con valor
  propio (no por prioridad), y queremos que el líder pueda filtrar por cualquiera
  de las dos. El frontend (`CreadorZentinelaPage.FUENTES_CELULAR`) muestra dos
  chips: `MutualSer (1)` y `MutualSer (2)`. El `NumeroTelefonoFijo` se expone
  como columna pero NO entra al selector — el switch existe para marcado masivo
  a móviles, no a fijos.

---

## Resueltos en esta sesión (2026-05-06, Ola 1)

- [x] **Inputs de meta no se resincronizan tras `invalidateQueries`**
  `AsignacionMetasPage.tsx` ahora pasa `key` ligada al valor servido en
  `<FilaLider/>` y `<FilaAgente/>`. Si otra sesión actualiza una meta y
  el query se refresca, los componentes remontan y los inputs reflejan el
  valor real, no el local en memoria.

- [x] **Agentes retirados aparecen en Asignación de Metas**
  `AsignacionMetasService.ObtenerFilasAgenteAsync` filtra por
  `EstadoEmpleado` al consultar `_rrhh.Empleados` y descarta del resultado
  toda asignación cuyo agente no esté activo.

- [x] **Join en memoria sin documentar en `ObtenerFilasAgenteAsync`**
  Comentario de bloque arriba del método explicando por qué el join vive en
  LINQ-to-Objects (cross-DbContext) y por qué no se debe mover al query.

- [x] **Try/catch ciegos en cargues**
  `CargueReporteService.ConfirmarCargueAsync` y
  `AsignacionMasivaService.ConfirmarCargueAsync` ahora loguean con
  `ILogger<T>.LogError` antes del rollback, con contexto (cantidad de filas,
  cartera/líder cuando aplica) para diagnóstico en producción.

- [x] **Abreviaciones en firmas públicas**
  `req` → `request` en `GuardarMetaLiderAsync` y `GuardarMetaAgenteAsync`.
  `lc` → `liderCartera` en el `Select(...)` largo de
  `InformesRecaudoService.GetInformeLideresAsync`. **No** se renombró `lc`
  en lambdas one-liner de `Include` ni `idx` en loops del `ExcelExporter`.

- [x] **Timeout extendido del Calificador documentado**
  `CalificadorCarteraService.CalificarAsync` tiene comentario que explica
  por qué se amplía el timeout y por qué es seguro (DbContext Scoped, no
  reutilizado fuera de este flujo).

---

## Resueltos antes de esta sesión (verificados en 2026-05-06)

- [x] **Bug `FilaAgente` — precedencia de operadores**
  `BadgeMeta tieneMeta={(fila.tieneMeta && !modificado) || guardado}` con
  paréntesis correctos en `AsignacionMetasPage.tsx:217`.

- [x] **CORS en `Program.cs`**
  Política `FrontendPolicy` con orígenes explícitos + fallback IP on-premise
  (`10.0.0.89:5050`). Ya no es hardcode de un solo origen.

- [x] **`AsNoTracking()` consistente en `AsignacionMetasService`**
  Todas las queries de solo lectura lo aplican.

- [x] **Validación de negocio fuera del controlador**
  `AsignacionMetasController` valida `Valor <= 0` en endpoint, pero el
  servicio puede asumir el invariante. La regla se respeta.

- [x] **Manejo global de errores en frontend**
  Interceptor Axios en `authApi.ts` redirige en 401 y captura otros errores
  con mensaje al usuario.

- [x] **`AsignacionMetasService` recibe dos `DbContext`**
  Es legítimo cross-DB (`CallCenterDbContext` y `RecursosHumanosDbContext`)
  — ver ADR-001. No es violación de DIP en el contexto del proyecto.

---

## Ola 2 — Para cuando aterrice el softphone

Refactors que pagan doble: limpian deuda actual **y** desbloquean la
integración de un marcador propio + creación de gestiones desde Zentinela.
Requieren ADR antes de tocar código.

- [ ] **ADR-016: ¿BD destino para gestiones propias?**
  Decisión a tomar antes de cualquier código de softphone: `DBZentinela` vs
  `DBCallCenter`. Recomendación: `DBZentinela` (preserva el contrato de
  `CallCenterDbContext` como `ExcludeFromMigrations` / read-only).

- [ ] **Abstracción `IGestionesRepository`**
  Hoy `DashboardDirectorService` y `DashboardAsesorService` consultan
  `_callCenter.UltimosEstadosTitular` directamente. Una interfaz en
  `Zentinela.Application/Interfaces/Repositories/` con dos
  implementaciones (Legacy + Zentinela) permite intercambiar fuente sin
  tocar dashboards.

- [ ] **Entidad `Gestion` en Core + `ExtensionSIP` en `Agente`**
  Ahora solo existen `UltimoEstadoTitular` (snapshot) y `HistoricoEstadoTitular`
  (histórico de snapshots). No hay entidad de evento "asesor X gestionó a
  titular Y a las HH:mm:ss". Necesario para softphone. Agregar `ExtensionSIP`
  (string, nullable) a `Agente` para mapear a la extensión telefónica.

- [ ] **SignalR cableado básico en `Program.cs`**
  Registrar `AddSignalR()` y un `NotificacionesHub` vacío, sin migrar nada
  existente. El softphone necesita push bidireccional para estado de llamada.

- [ ] **Partir `InformesRecaudoPage.tsx` (1.304 líneas) e `InformesVirtualidadPage.tsx` (1.367 líneas)**
  Estructura propuesta original sigue válida:
  ```
  pages/informes/_shared/   ← extraer patrón compartido entre Recaudo y Virtualidad
  pages/informes/recaudo/
    InformesRecaudoPage.tsx     ← solo el shell con tabs
    tabs/...
    components/...
    hooks/...
    utils/formatters.ts
    constants.ts
  pages/informes/virtualidad/
    ...
  ```
  Hacer una sola vez bien y reutilizar.

- [ ] **`CancellationToken` end-to-end en operaciones largas**
  Selectivo: solo en (a) export de Excel, (b) cargues con BulkCopy,
  (c) cualquier query >2s en producción. No propagar a todo el codebase.

---

## WONTFIX (over-engineering al stage actual)

Items descartados con razón explícita. Reevaluar solo si cambia un supuesto
del proyecto (más clientes, más equipo, más ambientes, mucho más volumen).

- **Partir `ExcelExporter` en `IBaseMadreExporter` / `IReporteExporter` / `IAsignacionesExporter`**
  597 líneas cohesivas y poco modificadas. Triplicar interfaces no reduce
  complejidad real. Si un export se vuelve dramáticamente distinto en el
  futuro, ahí se extrae.

- **Modularizar `DependencyInjection.cs` con `AddXModule()`**
  22 registros organizados es archivo que se toca una vez por feature. Costo
  de leer 22 líneas < costo de saltar entre 8 archivos. Reevaluar si pasa
  de 50.

- **`IAgenteLookupService` para encapsular acceso a RRHH**
  Hay un solo lookup (en `AsignacionMetasService`). Crear abstracción para
  una llamada es ceremonia. Cuando aparezca el segundo o tercer servicio
  que mire `_rrhh.Empleados`, ahí se extrae.

- **Mover `RegistroService` de Infrastructure a Application**
  ADR-001 ya documentó que el patrón actual permite servicios multi-DB en
  Infrastructure. Coherencia > pureza académica. Si en algún momento se
  migra el patrón, se hace en bloque, no archivo por archivo.

- **Value object `PeriodoMesActual`**
  La verificación de 2026-05-06 mostró que la lógica está en **un solo
  lugar** (`InformesRecaudoService`), no duplicada con
  `AsignacionMetasService` como suponía la nota original. No hay duplicación
  real que eliminar.

- **`.nvmrc` / `engines` en `package.json`**
  Riesgo bajo: 1 cliente, máquinas controladas. Agregar cuando un drift
  cause un bug, no preventivamente.

- **CORS a `appsettings.json`**
  Ya está en `Program.cs` con fallback IP funcionando. Mover a config solo
  cuando haya >1 ambiente con valores distintos.

- **Renombrar todas las abreviaciones**
  Cubierto en sección de resueltos solo para firmas públicas y bloques de
  código que se leen a diario. Renombrar `idx` y similares en loops cortos
  genera churn que ensucia git blame sin aportar legibilidad real.

- **NuevaEps: `TitularID` es `varchar(30)`, no `bigint`** (2026-06-12)
  Única fuente de base madre cuyo `TitularID` es texto. Se usa
  `TRY_CAST(TitularID AS BIGINT)` en el CTE de `vw_BaseMadre`/
  `fn_BaseMadrePorCartera`, en los JOIN de localización de
  `CalificadorCarteraService` y en el UNION de `RastreoLineasService`. Todos
  los valores son numéricos (verificado), así que la conversión es segura.
  En ConsultaIntegrada la entidad usa `string? TitularID` y compara contra
  `titular.NumeroDocumento.ToString()`. Si se añade otra fuente con TitularID
  varchar, replicar este patrón. Sin email (solo 1 celular).
