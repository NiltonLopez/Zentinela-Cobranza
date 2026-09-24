# /agregar-fuente-datos

## Descripción

Implementa de forma **end-to-end** una nueva fuente de datos proveniente de la BD `DBCallCenter` y la propaga por **todas las capas** del sistema y por **todos los sub-módulos** que consumen la base madre. Está pensada para tablas tipo `DatosClaro`, `MutualSer`, contactabilidad, calificación socioeconómica, fechas (nacimiento, vinculación), datos demográficos, etc.

> Modelo recomendado: **Opus 4.7** (decisión arquitectónica + propagación cross-layer).

## Uso

```
/agregar-fuente-datos [NombreFuente] [TablaOrigen] [DescripcionCorta]
```

Ejemplos reales (ya integrados — sirven como referencia de patrón):
- `/agregar-fuente-datos DatosClaro DatosClaro "Contactabilidad Claro: celular, email, FechaInsercion (N:1 — varias filas por titular)"`
- `/agregar-fuente-datos MutualSer MutualSer "Contactabilidad + calificación socioeconómica: 2 celulares, fijo, email, régimen, tipoAfiliado, departamento, municipio, estado (1:1 por titular)"`

Si el usuario no provee los 3 argumentos, **preguntar antes de ejecutar** (preflight). Las preguntas mínimas:
1. Nombre canónico de la fuente (PascalCase, sin espacios — se usará en código).
2. Nombre exacto de la tabla u objeto en SQL Server.
3. Descripción funcional (1 línea) y campos clave que aporta.
4. **Cardinalidad** respecto a `TitularID`: ¿1:1 (una fila por titular) o N:1 (varias filas)? Esto cambia la entidad de ConsultaIntegrada y si el TVF necesita `ROW_NUMBER()` o no.
5. ¿Aplica como filtro en Calificador/Creador (selector de celular o email)? Si expone múltiples celulares (ej. MutualSer) confirmar si entran como entradas separadas o COALESCE.
6. ¿Es fuente de **localización**? **La respuesta por defecto es SÍ.** El panel "Porcentajes de Localización" del Calificador NO requiere Departamento/Municipio — mide encontrado/no-encontrado/falta-por-buscar para **cualquier fuente con al menos un campo identificable** (celular, email, NIT, empresa, placa, lo que sea). De las 16 fuentes activas hoy, solo hay que verificar con el usuario si hay una razón real para excluirla (p. ej. la fuente fue retirada del sistema, como `DatosExternos`). **No asumas "no" solo porque la fuente no trae geografía** — SIMIT y RGM son datos vehiculares y SÍ están en el panel; MutualSer/Positiva/Cajacopi tampoco traen depto/municipio y también están. Si el usuario responde "no", pedir la justificación explícita y registrarla en la memoria (paso 9) y en `docs/para-tener-en-cuenta.md` (paso 8) — no dejarlo como una omisión silenciosa. Si sí, ejecutar el paso 5b.
7. ¿Se alimenta **por archivo** (el proveedor entrega un Excel/CSV que hoy alguien importa a `<Fuente>Auxiliar` y procesa con `sp_Procesar<Fuente>Auxiliar`)? **Por defecto SÍ** para toda fuente de localización. Si sí → paso **5f** (Cargue de Fuentes de Datos). Solo se omite si la tabla la llena otro sistema (un ETL, una API) — registrar el motivo.
8. ¿Tiene página propia en el Sidebar? (raro — solo si hay vista dedicada útil al líder).

## Contexto del proyecto (snapshot)

Stack: **C# .NET + ASP.NET Core MVC API + EF Core + React/Vite + SQL Server (10.0.0.89,1433 / DBCallCenter)**.
Arquitectura: Clean Architecture monolítica modular bajo `src/`:

```
src/
├── Zentinela.Core/              → Entidades + Interfaces (sin dependencias)
│   ├── Entities/                → BaseMadreRow.cs, Titular.cs, etc.
│   └── Interfaces/              → IBaseMadreRepository.cs, etc.
├── Zentinela.Application/       → DTOs, Services interface, lógica de negocio
│   ├── DTOs/                    → Carpetas por feature (Carteras, Consultas, …)
│   ├── Interfaces/              → Contratos de servicios
│   └── Services/                → Implementaciones que orquestan repos
├── Infrastructure/
│   ├── Zentinela.Infrastructure.SQLServer/
│   │   ├── Persistence/         → CallCenterDbContext.cs, ZentinelaDbContext.cs
│   │   ├── Mappings/            → BaseMadreRowMapping.cs (Fluent API → vw_BaseMadre)
│   │   ├── Repositories/        → BaseMadreRepository.cs, CarteraRepository.cs
│   │   └── Services/            → CalificadorCarteraService, RastreoLineasService,
│   │                              ComparadorCarterasService, GestionCarterasService,
│   │                              VirtualidadEntrantesService, CargueReporteService, …
│   └── Zentinela.Infrastructure.FileExporter/  → ClosedXML
├── Zentinela.API/Controllers/   → Endpoints REST (1 por feature)
├── Modules/ConsultaIntegrada/   → Módulo autónomo (Core/Application/Infrastructure/Initialization)
└── Zentinela.Web/               → SPA React + Vite (TypeScript)
    └── src/
        ├── api/                 → *Api.ts (axios clients)
        ├── types/               → *.ts (tipos compartidos con backend) — agnósticos a fuentes
        ├── pages/
        │   ├── cartera/         → CalificadorCarteraPage, ComparadorCarterasPage, CreadorZentinelaPage
        │   ├── consultas/       → ConsultaIntegradaPage, RastreoLineasPage
        │   ├── localizacion/    → ArlSura, Cajacopi, Camara, Fosyga, Runt, Simit (stubs)
        │   ├── informes/        → InformesRecaudoPage, InformesVirtualidadPage
        │   ├── home/            → DashboardLider/Director/Asesor + PagosConfirmados*
        │   └── administracion/  → GestionAsignaciones, GestionCarteras, GestionPersonal,
        │                           CargueReportes, CarguePagos, AsignacionMetas
        └── config/navigation.ts → MODULOS y subModulos del Sidebar
```

### Destino SQL real: `dbo.fn_BaseMadrePorCartera` (iTVF)

> ⚠️ **CRÍTICO** — actualizado 2026-05-06: el código C# productivo NO consulta `vw_BaseMadre` directamente. Consulta el iTVF **`dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)`**, definido en `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`. El iTVF replica las columnas de la vista pero pre-restringe cada CTE `Ultimo*` al dominio de la cartera, ganando P90 ~3× / P99 ~17×.
>
> La `vw_BaseMadre` sigue existiendo y se usa exclusivamente para el **shape de fila de EF** (`BaseMadreRowMapping.ToView("vw_BaseMadre")`) — EF mapea por nombre de columna, así que mientras el TVF y la vista expongan los mismos alias, todo funciona. **Si modificas la vista pero olvidas el TVF, el sistema queda inconsistente y EF lanzará excepciones de columna faltante en runtime.**
>
> ⚠️ **Son CUATRO objetos, no dos** — corregido 2026-09-02 (integración Sura). Además de la vista y `fn_BaseMadrePorCartera` existen **`dbo.fn_BaseMadreParaTitulares(@CarteraID, @Titulares)`** y **`dbo.fn_BaseMadreParaTitulares_CarteraChica(@CarteraID, @Titulares)`** (`docs/sql/migrations/2026-07-11_fn_BaseMadreParaTitulares_CarteraChica.sql`), ambos con parámetro `dbo.TitularIDList READONLY`. Las cuatro definiciones deben quedar con los mismos alias, **en la misma migración**. Antes de escribir la migración, listar los objetos reales:
> ```sql
> SELECT name, type_desc FROM sys.objects WHERE name LIKE 'fn_BaseMadre%' OR name = 'vw_BaseMadre';
> ```
> Verificación después de ejecutar — pedirle los alias nuevos literalmente a los cuatro (si un alias difiere en una tilde, SQL responde *"Invalid column name"* aquí; en producción se vería recién a media descarga):
> ```sql
> DECLARE @t dbo.TitularIDList;
> SELECT TOP 0 <alias nuevos> FROM dbo.vw_BaseMadre;
> SELECT TOP 0 <alias nuevos> FROM dbo.fn_BaseMadrePorCartera(<cartera>, NULL);
> SELECT TOP 0 <alias nuevos> FROM dbo.fn_BaseMadreParaTitulares(<cartera>, @t);
> SELECT TOP 0 <alias nuevos> FROM dbo.fn_BaseMadreParaTitulares_CarteraChica(<cartera>, @t);
> ```

### Sub-módulos que consumen `fn_BaseMadrePorCartera` (revisar/actualizar)

| Sub-módulo                  | Service backend                     | Controller                     | Página React                 | Consume TVF |
|-----------------------------|-------------------------------------|--------------------------------|------------------------------|-------------|
| Creador Zentinela           | `CreadorZentinelaService`           | `CreadorZentinelaController`   | `CreadorZentinelaPage`       | sí (vía repo + EF) |
| Calificador de Cartera      | `CalificadorCarteraService`         | `CalificadorCarteraController` | `CalificadorCarteraPage`     | sí (SQL crudo: `SqlAgregados` + `ObtenerLocalizacionAsync`) |
| Comparador de Carteras      | `ComparadorCarterasService`         | `ComparadorCarterasController` | `ComparadorCarterasPage`     | NO — usa `UltimosEstadosTitular` |
| Consulta Integrada          | `Modules/ConsultaIntegrada/**`      | (módulo autónomo)              | `ConsultaIntegradaPage`      | NO — consulta tablas fuente directamente |
| Rastreo de Líneas           | `RastreoLineasService`              | `RastreoLineasController`      | `RastreoLineasPage`          | NO consume el TVF, pero **sí debe extenderse** si la nueva fuente expone columna(s) de teléfono — UNION ALL en `RastreoLineasService.SqlConsulta` |
| Uso de Base de Datos        | `UsoBaseDatosService`               | `InformesGeneralesController`  | `InformesGeneralesPage`      | NO consume el TVF, pero es el **espejo masivo de Rastreo** — mismo UNION ALL, contra `#Telefonos` en vez de `@numero`. Se actualiza **siempre junto con Rastreo** |
| Gestión de Carteras (admin) | `GestionCarterasService`            | `GestionCarterasController`    | `GestionCarterasPage`        | NO |
| Cargue de Reportes          | `CargueReporteService`              | `CargueReportesController`     | `CargueReportesPage`         | NO |
| Cargue de Fuentes de Datos  | `CargueFuentesMotor` + `Reglas/*` (orquestado por `CargueFuentesOrquestador`) | `CargueFuentesController` | `CargueFuentesDatosPage` | NO — **ESCRIBE la tabla fuente** (es su única entrada de datos, ADR-039) e invalida `ICalificadorCarteraCache` al terminar. Se extiende en el paso **5f** |
| Localización (6 stubs)      | (sin servicio actualmente)          | —                              | `Arl/Caja/Camara/...` stubs  | NO |

> **Nota crítica de arquitectura — vw_BaseMadre aplana NULLs a `'0'`**: por eso los módulos de localización con 3 estados (valor / vacío / sin consulta) NO la consultan: usan `LEFT JOIN (SELECT DISTINCT TitularID FROM TablaFuente)` directo + flags `EnFuente`. Si la nueva fuente expone localización, sigue ese patrón también en `ObtenerLocalizacionAsync` — no asumas que `vw_BaseMadre` te da la verdad sobre "no encontrado vs sin consultar".

### Fuentes ya integradas (referencia rápida)

> ⚠️ Corregido 2026-07-08: la columna Localización de esta tabla estaba desactualizada en varias filas (Tigo, PositivaEmpresas, Compensados, SIMIT, RGM decían "no" cuando el código actual **sí** las incluye en el panel — arrastraba el supuesto incorrecto de que localización = geografía). Verificar contra `LeerLocalizacionAsync`/`Fuentes` en `CalificadorCarteraService.cs` si hay dudas, no confiar ciegamente en esta tabla.

| Fuente | Cardinalidad | Selector cel/email | Localización | Patrón ConsultaIntegrada | Cargue (5f) |
|---|---|---|---|---|---|
| Cajacopi | 1:1 | sí / sí | sí | entidad 1:1 simple | **sí** — `ReglaCajacopi` (prefijo a fijos de 7, `11111`, "NO TIENE") |
| Camara | 1:1 | sí / sí | sí | entidad 1:1 simple | Fase 2 |
| SaludTotal | 1:1 | sí / sí | sí | entidad 1:1 + empresa | Fase 2 |
| Positiva | 1:1 | sí / sí | sí | entidad 1:1 simple | Fase 2 |
| Tigo | 1:1 | sí / sí | sí (aunque `ESTADO_FUENTE` la marca "Inactivo" — son cosas distintas) | entidad 1:1 simple | Fase 2 |
| SOS | 1:1 | sí / no | sí | entidad 1:1 + empresa | Fase 2 |
| DatosClaro | **N:1** | sí / sí | sí (Rastreo) | entidad N:1, aplana con sufijos `Email 1`, `Email 2`, … | **sí** — `ReglaDatosClaro` (N:1, no crea Titular, lista negra de teléfonos real) |
| MiVacuna | 1:1 | sí / sí | sí (agregado 2026-07-08 — se omitió por error en la integración original, ver `project_fuente_mivacuna.md`) | entidad 1:1 simple | Fase 2 |
| MutualSer | 1:1 | sí (×2 cels) / sí | sí (Rastreo + Localización) | entidad 1:1 simple | **sí** — `ReglaMutualSer` (reubica cel/cel2/fijo) |
| ARLSura | 1:1 | no | sí (solo empresa) | entidad simple | Fase 2 |
| Sura | 1:1 | sí / sí | sí | entidad 1:1 simple. **Al integrarla (2026-09-02) hubo que renombrar los alias de ARLSura** a `[NIT ARL Sura]`/`[Empresa ARL Sura]` — ver anti-patrón de colisión de nombres | **sí** — `ReglaGenerica` (plantilla) |
| PositivaEmpresas | 1:1 | no | sí | entidad simple | Fase 2 |
| RUES | 1:1 | no | sí (solo empresa) | entidad simple | Fase 2 |
| Fosyga | 1:1 | no | sí (regimen/depto/municipio) | entidad 1:1 | Fase 2 |
| Compensados | 1:1 | no | sí | entidad 1:1 | Fase 2 (necesita `FlagDesdeArchivo`) |
| NuevaEps | 1:1 | sí / no | sí | entidad 1:1 | **sí** — `ReglaGenerica` + `TitularIdComoTexto` |
| SIMIT | 1:1 | no | sí (vehicular — celular/email no aplica, pero placa sí es el campo identificable) | entidad 1:1 | **sí** — `ReglaSimit` (`Expandir` Placa1..15 → una fila por placa) |
| RGM | 1:1 | no | sí (vehicular — marca/modelo/placa como campo identificable) | entidad 1:1 | Fase 2 |
| DatosExternos | 1:1 | **ignorada** — no se mapea (ver `project_datos_externos_removido.md`) | — | — | no aplica |

> Coosalud, SaviaSalud, TigoUne y CamaraEmpresa (no listadas arriba) también tienen `sp_Procesar*Auxiliar` vivo: **Fase 2** (CamaraEmpresa necesita `ColumnaClaveTitular`, clave `NitEmpresa`).

## Pasos automáticos

> Ejecuta cada paso secuencialmente. Si el usuario interrumpe o un paso falla, **detente y reporta** — no avances con suposiciones.

### 0. Reconocimiento e inventario (siempre)

1. Leer `.claude/CLAUDE.md` y los archivos `MEMORY.md` relevantes para confirmar reglas vigentes.
2. Vía **MCP SQL Server** (o `sqlcmd` si el MCP no está cargado en la sesión — credenciales en `.mcp.json`), inspeccionar:
   - `SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='<TablaOrigen>' ORDER BY ORDINAL_POSITION`
   - `SELECT TOP 5 * FROM dbo.<TablaOrigen> WHERE <columna_clave> IS NOT NULL` (sample con datos reales).
   - `SELECT COUNT(*) AS Filas, COUNT(DISTINCT TitularID) AS TitularesUnicos FROM dbo.<TablaOrigen>` (determina cardinalidad real 1:1 vs N:1).
   - `SELECT i.name, i.is_primary_key, i.is_unique FROM sys.indexes i WHERE i.object_id = OBJECT_ID('dbo.<TablaOrigen>')` (confirma si la tabla es heap o tiene índice sobre `TitularID`).
   - `SELECT name FROM sys.tables WHERE name LIKE '<TablaOrigen>%'` (busca tabla `<X>Auxiliar` de staging).
   - `SELECT OBJECT_DEFINITION(OBJECT_ID(n)) FROM (VALUES('dbo.vw_BaseMadre'),('dbo.fn_BaseMadrePorCartera'),('dbo.fn_BaseMadreParaTitulares'),('dbo.fn_BaseMadreParaTitulares_CarteraChica')) v(n)` — **los CUATRO son el destino del paso 1**. Leer la definición **viva** de cada uno, no el archivo de migración más reciente: las migraciones son incrementales y ninguna contiene el estado actual completo. (Ojo: `OBJECT_DEFINITION` devuelve el texto tal cual se ejecutó, con sus comentarios de cabecera viejos — no fiarse de la fecha que digan.)
3. Mapear cada columna nueva a un nombre **PascalCase** consistente con `BaseMadreRow.cs` (ej. `CelularMutualSer`, `RegimenMutualSer`, `Celular2MutualSer` cuando hay múltiples). Sufijar con el nombre de la fuente para evitar colisión con columnas Fosyga (`Regimen`, `TipoAfiliado`, `Departamento`, `Municipio` ya existen).
4. Decidir, junto al usuario si hay ambigüedad, qué columnas:
   - se exponen en `vw_BaseMadre` + `fn_BaseMadrePorCartera` (información generalista, alta cardinalidad de uso).
   - quedan en tabla fuente y se exponen vía repositorio dedicado (datos especializados / localización con 3 estados).

### 1. Base de datos — actualizar **vista + TVF** + `schema.md` (MCP)

Crear migración nueva en `docs/sql/migrations/<YYYY-MM-DD>_<nombre>_in_basemadre.sql` con tres bloques idempotentes:

1. **Índice de soporte** (si la tabla origen es heap o no tiene índice sobre `(TitularID, FechaConsulta DESC)`):
   ```sql
   IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_<Tabla>_TitularID_FechaConsulta')
       CREATE INDEX IX_<Tabla>_TitularID_FechaConsulta
           ON dbo.<Tabla> (TitularID, FechaConsulta DESC);
   ```
   Sin INCLUDE por defecto: el INCLUDE de muchas columnas duplica storage sin ROI claro. Solo agregar INCLUDE si después se mide impacto P90 alto.
2. **`ALTER VIEW dbo.vw_BaseMadre`** — añadir CTE `Ultimo<Fuente>` y las columnas nuevas con `ISNULL(...)` y alias entre corchetes (`[Celular MutualSer]`, etc.). Mantener convención `ROW_NUMBER() PARTITION BY TitularID ORDER BY FechaConsulta DESC` aunque sea 1:1 (simetría con las otras CTEs).
3. **`CREATE OR ALTER FUNCTION`** para los **tres** iTVF — `fn_BaseMadrePorCartera`, `fn_BaseMadreParaTitulares` y `fn_BaseMadreParaTitulares_CarteraChica` — replicando en cada uno exactamente las mismas columnas, CTE y JOIN, **con la pre-restricción a `TitularesCartera`** (ver patrón en `2026-05-06_fn_BaseMadrePorCartera.sql:47-61`). El orden de columnas debe coincidir 1:1 con `BaseMadreRowMapping`.

   > **Cómo generar los cuatro bloques sin transcribirlos a mano** (lo que se hizo para Sura): bajar las definiciones vivas a archivos, y con un script corto insertar la línea nueva clonando la forma de la última fuente integrada (p. ej. SaviaSalud) más el CTE correspondiente. El script debe **abortar si detecta un alias duplicado** — es la única red que atrapa una colisión de nombres antes de tocar producción. Detalles que rompen un regex ingenuo: los TVF usan `LEFT  JOIN` con dos espacios donde la vista usa uno (usar `/LEFT\s+JOIN\s+/`), y el texto de `OBJECT_DEFINITION` empieza con comentarios, no con `CREATE` (localizar el `CREATE` y recortar lo anterior).

Mostrar el SQL al usuario y **pedir confirmación antes de ejecutarlo**. Tras ejecutar:
- `SELECT TOP 5 <ColsNuevas> FROM dbo.fn_BaseMadrePorCartera(<cartera_chica>, NULL)` para validar.
- Conteo de filas igual al previo (no debe explotar el join).

Regenerar la sección de la fuente nueva en **`docs/schema.md`** preservando el formato actual (encabezado con fecha, tablas en formato `| Columna | Tipo | Nullable | PK | FK |`). Incrementar la fecha "Actualizado" a la de hoy. Añadir el mapeo en la sección "Columnas expuestas por `vw_BaseMadre`" y registrar el índice nuevo en la sección de índices.

### 2. Backend — Core

Archivo: `src/Zentinela.Core/Entities/BaseMadreRow.cs`
- Añadir las propiedades nuevas con los tipos correctos (`string?`, `DateOnly?`, `int?`, `decimal?`, `long?`). **Siempre nullable** porque `vw_BaseMadre` aplana a `'0'`/NULL y EF necesita aceptar ambos.

Si la fuente debe consultarse fuera de la vista (3 estados o consulta dedicada), crear:
- `src/Zentinela.Core/Entities/<NombreFuente>.cs` (entidad de la tabla original).
- `src/Zentinela.Core/Interfaces/I<NombreFuente>Repository.cs`.

### 3. Backend — Infrastructure (SQL Server)

1. `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Mappings/BaseMadreRowMapping.cs`
   - Añadir `b.Property(x => x.<Nuevo>).HasColumnName("<NombreColumnaVista>")` para cada columna nueva. El `HasColumnName` debe coincidir EXACTAMENTE con el alias en el `SELECT` del TVF/vista.
2. `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Repositories/BaseMadreRepository.cs`
   - **⚠️ OBLIGATORIO — diccionario `SqlAlias` (cuarta lista espejo)**: por **cada** propiedad nueva de `BaseMadreRow` expuesta en la vista/TVF, añadir una entrada `["<NombrePropC#>"] = "[<Alias de columna en TVF>]"`. `SqlAlias` lo usan **solo** los caminos de export (CSV/Excel en `CreadorZentinelaService` → `StreamFilasAsync`) para proyectar el `SELECT` solo de las columnas pedidas. **No es opcional ni se limita a celular/email — aplica a TODA columna del catálogo.** Mantener el mismo orden que `ColumnasMadre`/`BaseMadreRowMapping`. Si se omite, el preview seguirá funcionando (usa reflexión sobre `BaseMadreRow`) pero **el export reventará a media transmisión**: el encabezado escribe N columnas, el `SELECT` proyecta N−(faltantes), el reader trae menos campos y `procesarFila` lanza `IndexOutOfRange` con el `200` ya enviado → el cliente ve `ERR_INCOMPLETE_CHUNKED_ENCODING` o un archivo con 1 fila. (Regresión real corregida en commit `1cbd33f`; `StreamFilasAsync` ahora lanza temprano si detecta una propiedad sin alias, en vez de descartarla en silencio — no desactivar ese guard.)
   - Si la fuente entra al selector de **celular** y/o **email**, extender los `switch` `ColumnaCelular(string fuente)` y `ColumnaEmail(string fuente)` (líneas ~154-175). Cada entrada devuelve la columna entre corchetes (`"[Celular MutualSer]"`).
   - **Excepción legítima — múltiples celulares por fuente**: si la fuente expone 2+ celulares (caso MutualSer: `NumeroTelefonoCelular` + `NumeroTelefonoCelular2`), añadir 2 entradas separadas al switch (`"MutualSer"` y `"MutualSer2"`). Es excepción consciente a la convención "1 fuente = 1 entrada"; documentar en `para-tener-en-cuenta.md`.
   - **NO** añadir teléfono fijo al selector — el selector es para marcado masivo a celulares.
   - La firma de `ObtenerAsync` no cambia salvo que añadas filtros nuevos; las columnas viajan automáticamente al exponerse en la entidad.
3. Si se creó entidad fuente:
   - `Mappings/<NombreFuente>Mapping.cs` (Fluent API → tabla real).
   - `Persistence/CallCenterDbContext.cs`: registrar el `DbSet`.
   - `Repositories/<NombreFuente>Repository.cs`: implementar `LEFT JOIN DISTINCT` con `Titular_Cartera` para localización (patrón Cajacopi/Camara).
   - `DependencyInjection.cs`: registrar el repo (`services.AddScoped<I…Repository, …Repository>()`).

### 4. Backend — Application

1. `src/Zentinela.Application/DTOs/CreadorZentinela/ColumnasMadre.cs`
   - Añadir una entrada al catálogo `Todas` por cada propiedad nueva: `new("<NombrePropC#>", "<EtiquetaUI>", TipoColumna.<Texto|Fecha|Numero>)`. Este catálogo es lo que el Creador usa para el selector de columnas del Excel.
2. `src/Zentinela.Application/DTOs/CreadorZentinela/FiltroBaseMadreRequest.cs`
   - Actualizar XML doc en `fuentesCelular` y `fuentesEmail` añadiendo las claves nuevas válidas.
3. Otros DTOs (Calificador, Comparador, Rastreo, GestionCarteras): la mayoría no requieren cambios porque heredan de `BaseMadreRow` o son agnósticos. Verificar caso por caso.

### 5. Backend — Services Infrastructure (consumidores)

> No todos los services consumen `fn_BaseMadrePorCartera`. La tabla "Sub-módulos…" arriba lista cuáles. **Solo tocar los que sí lo consumen.**

**5a. `Services/CalificadorCarteraService.cs` — `SqlAgregados` (líneas ~85-240)**:
- En el `SELECT INTO #t` (líneas ~85-110): proyectar las columnas nuevas con alias cortos (`AS CelMutualSer`, `AS EmMutualSer`, …).
- En las cuatro condicionales `OR ... NOT IN ('','0')` de `ConCelular`, `ConEmail`, `ConCelularYEmail`, `SinContacto` (líneas ~132-156): añadir las columnas nuevas si entran al selector.
- En el UNION ALL `desgloseFuentesCelular` (líneas ~225-237): añadir un `SELECT '<Fuente>', SUM(...)` por cada columna celular nueva.

**5b. `Services/CalificadorCarteraService.cs` — panel de localización (result set 9 del batch)** — **por defecto SÍ aplica** (ver pregunta 6 del preflight; solo se omite con justificación explícita del usuario):
> ⚠️ Actualizado 2026-07-08: `ObtenerLocalizacionAsync` y `LocalizacionResumenRaw` **ya no existen** — se fusionaron al batch único `SqlAgregados` en la optimización del 2026-05-21 (ver `project_optimizacion_cartera_estado.md`). Si encuentras esos nombres en código viejo o en tu propia memoria de sesión, están obsoletos. Esta es la causa raíz de que MiVacuna se integrara sin localización: la fuente se agregó días después de esa fusión y el paso quedó mal referenciado.
- Las columnas fuente (celular/email/NIT/lo que aplique) ya deben estar proyectadas en el `SELECT INTO #t` del paso 5a — si no, agrégalas ahí primero.
- En el bloque `SUM(CASE WHEN ...)` dentro del `SELECT` final de result set 9 (busca el comentario `-- Result set 9: localización`): añadir dos `SUM(CASE WHEN ...)` — uno `<Fuente>Enc` (el campo identificable no está vacío) y uno `<Fuente>NoEnc` (el campo está vacío PERO `<alias>.TitularID IS NOT NULL`, es decir, el titular existe en la tabla fuente pero sin dato útil). Sigue el patrón de la fuente más parecida ya integrada (p. ej. `SOS`/`Positiva` para 1-2 campos simples, `MutualSer` para múltiples campos).
- En los `LEFT JOIN` al final de la misma query: añadir `LEFT JOIN (SELECT DISTINCT TitularID FROM <Tabla>) <alias> ON <alias>.TitularID = t.TitularID`.
- En `LeerLocalizacionAsync` (método privado, mismo archivo): añadir una línea `Fuente("<EtiquetaUI>", Sum("<Fuente>Enc"), Sum("<Fuente>NoEnc"), total),` al array de retorno.
- En el diccionario `Fuentes` (mismo archivo, usado por la descarga CSV de cada celda de la tabla): añadir una entrada `["<slug>"] = new("<slug>", "<Etiqueta>", "<TablaFuente>", "<ColumnaFecha>", "<SelectDataCols>", "<PredicadoEncontrado>")` — replica exactamente el predicado usado en el `SUM` de arriba.
- **Frontend** — `CalificadorCarteraPage.tsx`: añadir la fuente a `FUENTE_SLUG` (mapea etiqueta → slug del CSV) y a `ESTADO_FUENTE` (color/etiqueta visual — ver paso 7 más abajo). Sin esto el panel muestra la fila pero el botón de descarga no funciona y no sale el badge de estado.

**5c. `Modules/ConsultaIntegrada/`**:
- `ConsultaIntegrada.Core/Entities/Contactabilidad/<NombreFuente>.cs`: crear entidad.
  - **Si cardinalidad 1:1**: modelar como `Cajacopi.cs` (entidad simple con `[Key] TitularID` + propiedades).
  - **Si cardinalidad N:1** (caso DatosClaro): modelar como `DatosClaro.cs` y en `ConsultaRepository.CargarContactabilidadAsync` traer todas las filas, deduplicar por número/email y aplanar con sufijos numerados (`Email`, `Email 1`, `Email 2` …).
- Registrar `DbSet<<Fuente>>` en el DbContext del módulo (`HasNoKey().ToTable("<Tabla>")` si aplica).
- En `CargarContactabilidadAsync`: añadir bloque de carga. Si es 1:1, simple `dict["Número Teléfono"] = entidad.NumeroTelefono`. Va dentro del grupo existente `"Contactabilidad"` con clave `"<NombreFuente>"`. ConsultaIntegradaPage es agnóstica y la renderizará automáticamente como sub-acordeón.

**5d. `Services/RastreoLineasService.cs`** — solo si la fuente expone una o más columnas de teléfono (celular, fijo, móvil, NumeroTelefono*, etc.):
- En el comentario superior y en el comentario sobre `SqlConsulta`: añadir el nombre de la fuente a la lista.
- Añadir un bloque `UNION ALL` por cada columna de teléfono dentro del CTE `Resultados` (ver patrón de `Cajacopi (celular)`/`Cajacopi (fijo)` o de `MutualSer (celular 1/2/fijo)` en líneas ~167-195).
- Patrón por bloque:
  ```sql
  UNION ALL
  -- <Fuente> (<descripción columna>)
  SELECT '<Fuente>', '<Etiqueta UI>',
         x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
    FROM dbo.<Tabla> x
    LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
   WHERE x.<ColumnaTelefono> = @numero
   GROUP BY x.TitularID, t.Nombre
  ```
- Nota: ADR-015 originalmente excluyó algunas tablas con teléfono (DatosClaro, MutualSer) bajo la idea de que "no son fuentes de localización". La práctica desde 2026-04 ha sido **incluir cualquier fuente con teléfono usable** — el ADR-015 quedó parcialmente desactualizado y se anota en el header del archivo.

**5e. `Services/UsoBaseDatosService.cs`** — **espejo obligatorio de 5d**, agregado 2026-08 y detectado como paso faltante en la integración Sura (2026-09-02). Es el cruce masivo del sub-módulo *Informes Generales → Uso de Base de Datos*: la misma lista de fuentes que Rastreo, pero por **hash join contra `#Telefonos`** en vez de un `@numero` escalar. Si se extiende Rastreo y no este, el informe de uso subestima en silencio (la fuente simplemente no aparece; no hay error).
- Patrón por bloque — nótese que **no lleva `LEFT JOIN Titular`** (el servicio resuelve el líder después) y que agrupa también por `t.Numero`:
  ```sql
  UNION ALL
  -- <Fuente> — <nota sobre qué columnas de teléfono tiene>
  SELECT t.Numero, '<Fuente>', '<Etiqueta UI>', x.TitularID, COUNT(*)
    FROM #Telefonos t JOIN dbo.<Tabla> x ON x.<ColumnaTelefono> = t.Numero
   GROUP BY t.Numero, x.TitularID
  ```
- **Verificación de paridad** (hacerla siempre, es una línea): contar las fuentes y columnas de ambos servicios y exigir el mismo número.
  ```bash
  grep -c "UNION ALL" src/Infrastructure/*/Services/RastreoLineasService.cs src/Infrastructure/*/Services/UsoBaseDatosService.cs
  ```

**5f. Cargue de Fuentes de Datos** (`Services/CargueFuentes/`, ADR-039) — **por defecto SÍ** (pregunta 7 del preflight): es la **única entrada de datos** de la tabla fuente y reemplaza al par `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar`. El motor genérico ya hace todo lo común; por fuente son cuatro toques y el front **no cambia** (el selector y el panel de columnas se alimentan de `GET /api/cargue-fuentes/fuentes`).

1. **Definición** — `src/Zentinela.Application/Constants/CatalogoCargueFuentes.cs`: una `DefinicionFuenteCargue` nueva, sumada a `Todas`.
   - `TablaDestino`, `ColumnaFlag` (`EstadoEn<Fuente>`, o `null` si no tiene), `ColumnaFecha` (`FechaConsulta`/`FechaInsercion`) y **`TipoFecha` según el tipo REAL de la columna destino** (`date` → `Date`; `datetime` → `DateTime`, primera fuente que lo use: revisar `GeneradorSqlCargue.LeerSello` y el script de reversión).
   - `ColumnasDato`: nombre EXACTO de la columna destino, alias ya normalizados (`ArchivoTabular.Normalizar`: minúsculas, sin tildes ni espacios), `Tipo` (`Texto`, `Celular`, `Fijo`, `Telefono`, `Email`, `Entero`, `Fecha`, `Placa`) y **`Largo` = `max_length` real verificado con `sys.columns`** (no con `schema.md`, que ha tenido errores; con `nvarchar` es la mitad). Un largo mal puesto hace que SQL lance *"String or binary data would be truncated"* a mitad de la transacción; bien puesto, el motor trunca con advertencia.
     ```sql
     SELECT c.name, t.name AS tipo, c.max_length, c.is_nullable
       FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
      WHERE c.object_id = OBJECT_ID('dbo.<Tabla>') ORDER BY c.column_id;
     -- y además: FK a Titular, triggers (sys.triggers) e índices (sys.indexes) del destino
     ```
   - **Clave de contenido = TODAS las columnas de dato** (`EntraAClave = true`, el default). No copiar la clave recortada del SP viejo: perdía cambios (p. ej. de Municipio) en silencio. `EntraAlFlag = false` para lo que no es dato de contacto (localización, régimen, fechas del proveedor), replicando el flag del SP.
   - `TitularIdComoTexto` si el `TitularID` del destino es varchar (NuevaEps); `CreaTitular`/`CreaTitularCartera = false` si la tabla no tiene FK a `Titular` (DatosClaro); `Cardinalidad = VariasPorTitular` para N:1; `AplicaPoliticaVacias = false` sin flag; `ColumnasArchivoExtra` para columnas del archivo que no van tal cual al destino (SIMIT `Placa1..15`) y `DesdeArchivo: false` para la columna que produce la regla.
   - Si la fuente no cabe en el modelo (clave de titular distinta de `TitularID`, flag que viene del archivo), **parar y consultar**: las extensiones previstas son `ColumnaClaveTitular` (CamaraEmpresa) y `FlagDesdeArchivo` (Compensados), y son cambios del motor, no de una regla.
2. **Regla** — `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/CargueFuentes/Reglas/Regla<Fuente>.cs` (o `ReglaGenerica` si el SP no hace nada propio) y registro en `CatalogoReglasFuente`. **Traducir** el SP viejo leyendo su respaldo (`OBJECT_DEFINITION` → `docs/sql/legacy/`): mover/prefijar datos en `Transformar` (corre antes de validar), 1→N filas en `Expandir`, exclusiones propias en `Validar`; `NecesitaPrefijos` / `NecesitaListaNegraTelefonos` si hacen falta. Lo genérico (vacíos → NULL, celular 3xx/10, fijo 6xx/10, email + lista negra exacta, truncado, flag, dedup, Titular, nombres, Titular_Cartera, refrescar/insertar, vacías) **no se reescribe**. Documentar en el `<summary>` de la regla qué se tradujo y **qué defectos del SP NO se copian**. Defectos típicos a no copiar (todos vistos en la Fase 1):
   - lista negra de correos con `LIKE '%x%'` (el motor usa igualdad exacta de correo o dominio) y correo anulado a `''` en vez de NULL;
   - flag calculado **antes** de limpiar, o contando `''` como dato;
   - concatenar dos teléfonos en un `varchar(10)`; `INSERT` posicional;
   - `JOIN` con conversión implícita varchar/bigint; `ISNULL(x, '')` que iguala NULL con `''`;
   - dedup por titular **antes** de despivotar; dedup, limpieza o purga de vacías sobre **toda** la tabla final;
   - cruzar contra una tabla staging (`ListaNegraTelefonosAuxiliar`) en vez de la real;
   - `Titular_Cartera` sin validar el par activo en `Lider_Cartera` (o con `LiderID = CarteraID`, RGM);
   - texto de relleno del proveedor guardado como dato (`PAZ Y SALVO` como placa, `1111111` como fijo): validar la forma del dato y anularlo con advertencia agrupada (mensaje fijo, sin el valor).
3. **Pruebas**:
   - `tests/Zentinela.Infrastructure.Tests/CargueFuentes/Regla<Fuente>Tests.cs` (unitarios de la regla, con `ReglaTestUtil`) y el caso de la fuente en `CatalogoCoherenciaTests` (tiene regla, largos, alias normalizados).
   - Caso en `tools/CargueHarness/Casos/Caso<Fuente>.cs` con **su propio sub-rango de 100 TitularIDs** del rango reservado `999000000001..999000000999` (ocupados: Sura 001-099, Cajacopi 100-199, MutualSer 200-299, NuevaEps 300-399, SIMIT 400-499, DatosClaro 500-599; la siguiente usa 600-699). Registrar la tabla en `Harness.TablasDelRango` y `Harness.TablasDestino` y el caso en `Program.cs`. El caso usa `Harness.FlujoEstandar`: validar → simular (rollback, tabla intacta) → cargar → **segunda carga del mismo archivo = 0 insertadas, N refrescadas y `COUNT(*)` igual** → limpieza total del rango. Un XLSX sintético con cada exclusión y cada advertencia de la regla.
   - Antes de borrar el SP viejo: comparar contra él con el mismo archivo (`EXEC` en transacción con ROLLBACK vs `simular: true`). Toda diferencia que no sea un defecto documentado en el paso 2 es un bug.
   - No levantar la API (lo hace el usuario). `dotnet test tests/Zentinela.Infrastructure.Tests` y `dotnet run --project tools/CargueHarness` (cadena en `ZENTINELA_CARGUE_CS`, nunca en un archivo) sí se corren; con la API arrancada el build puede fallar con MSB3027. No correr el harness mientras corre el ETL de Estado Titular (se queda quieto en `WRITELOG`).
4. **Front**: nada. **`docs/sql/revertir_cargue_fuente.sql`**: agregar la fuente a su lista blanca (`@Fuentes` + `@Columnas` en el MISMO orden que `ColumnasDato`: el orden entra al hash).

**Al cerrar la fuente** (después de validarla en producción, no antes): respaldar `sp_Procesar<Fuente>Auxiliar` (texto íntegro de `OBJECT_DEFINITION`, UTF-8 con BOM) y el CREATE de `<Fuente>Auxiliar` en `docs/sql/legacy/` (+ fila en su README); script de DROP en `docs/sql/migrations/` con el patrón de `2026-09-25_drop_auxiliares_fase1.sql` (guarda de 0 filas, hash de la definición contra el respaldo, dependencias, transacción, idempotente); y `docs/schema.md`: sección de la Auxiliar marcada, tabla de SP, sección "Flujo de carga masiva".

**Servicios que NO se tocan** (verificado: no consultan `vw_BaseMadre` ni `fn_BaseMadrePorCartera`): `ComparadorCarterasService`, `GestionCarterasService`, `CargueReporteService`.

### 6. Backend — API

- Endpoints existentes: añadir las propiedades nuevas en los modelos de respuesta (suelen heredar de DTO Application — verificar).
- Si hay endpoints **nuevos** para exponer la tabla fuente directamente, crearlos siguiendo el patrón de `ConsultaController` o `CalificadorCarteraController` (rutas en kebab/camelCase consistentes con las existentes).
- Compilar `dotnet build` y resolver advertencias relevantes.

### 7. Frontend — React (Zentinela.Web)

> Recordatorio: el **Sidebar siempre se ordena alfabético** (`feedback_sidebar_alfabetico.md`). No introducir orden manual.
> Recordatorio: las vistas previas de cargue muestran como máximo **10 registros**.

1. **Tipos** — `src/Zentinela.Web/src/types/`
   - **Generalmente NO requieren cambios.** Los tipos críticos (`calificador.ts`, `creador.ts`, `consulta.ts`, `rastreo.ts`) son agnósticos a fuentes específicas (usan `string[]`, `Record<string, ...>`, etc.). Solo extender si el endpoint añade campos en su shape de top-level.
2. **API clients** — `src/Zentinela.Web/src/api/`
   - Solo si los endpoints añadieron parámetros nuevos.
3. **Páginas afectadas** (cambios concretos):
   - `pages/cartera/CreadorZentinelaPage.tsx`: añadir entradas a las constantes `FUENTES_CELULAR` y `FUENTES_EMAIL` (líneas ~30-47). Si la fuente expone 2 celulares, agregar 2 entradas separadas (`{ key: 'X', label: 'X (1)' }`, `{ key: 'X2', label: 'X (2)' }`).
   - `pages/cartera/CalificadorCarteraPage.tsx`: añadir entrada en el mapa `ESTADO_FUENTE` (líneas ~222-237) con `etiqueta` (Funcional/Intermitente/Inactivo) y `color` (`#16A34A`/`#CA8A04`/`#DC2626`). Este mapa es metadato visual editorial, frontend-only — ver `project_estado_visual_fuentes.md`.
   - `pages/consultas/ConsultaIntegradaPage.tsx`: **agnóstica** — renderiza dinámicamente cualquier fuente devuelta por el backend. No se toca.
   - `pages/cartera/ComparadorCarterasPage.tsx`, `pages/consultas/RastreoLineasPage.tsx`, `pages/administracion/*`: solo si el backend correspondiente cambió.
   - Si la fuente aporta localización con vista dedicada útil: crear página `pages/localizacion/<NombreFuente>Page.tsx` (raro). Hoy las páginas de `pages/localizacion/` son stubs con `<PaginaEnConstruccion />`.
4. **Sidebar / Navigation** — `src/Zentinela.Web/src/config/navigation.ts`
   - Solo si la fuente requiere su propia página. El Sidebar lo ordenará alfabético automáticamente. **No** crear un grupo nuevo sin acordarlo antes.
5. **Routing** — `src/Zentinela.Web/src/App.tsx`
   - Registrar la ruta nueva si se añadió página.

### 8. Documentación

- `docs/schema.md` — actualizado en paso 1.
- `docs/api-referencia.md` — añadir/actualizar endpoints afectados.
- `docs/arquitectura-sistema.md` — si la fuente introduce nuevo flujo (ej. nuevo repositorio fuera de `vw_BaseMadre`/TVF), agregar diagrama/sección.
- `docs/para-tener-en-cuenta.md` — registrar advertencias específicas y excepciones (ej. *"MutualSer es la única fuente con 2 entradas en switch ColumnaCelular — `MutualSer` y `MutualSer2`. Excepción consciente porque expone 2 celulares como columnas independientes."*).
- Comentarios XML `///` en español sobre lógica compleja únicamente.

### 9. Memoria persistente

Guardar memoria `project_fuente_<nombre>.md` con:
- Estructura de la tabla origen (columnas + tipos + cardinalidad real).
- Si está dentro o fuera de `vw_BaseMadre`/TVF y por qué.
- Si entra al selector de celular/email y con cuántas entradas.
- Si está en el panel de localización del Calificador.
- Patrón usado en ConsultaIntegrada (1:1 simple vs N:1 con sufijos).
- Sub-módulos que ya la consumen.
- Excepciones de convención introducidas.

Indexar en `MEMORY.md` con una línea de hook bajo el orden alfabético.

### 10. Verificación, build y commit

1. **Smoke SQL post-migración**:
   ```sql
   SELECT TOP 5 <ColsNuevas> FROM dbo.fn_BaseMadrePorCartera(<cartera_chica>, NULL);
   ```
   Confirmar que las columnas existen y devuelven datos no triviales.
2. **No regresión P90 Calificador**: comparar contra baseline en `docs/optim/_artifacts/04_calificador_p90.sql`. Degradación esperada <5% por columna extra; si >15%, revisar plan del TVF (`SET STATISTICS XML ON`).
3. **Compilar solución completa sin errores**: `dotnet build` desde la raíz.
4. **Frontend**: `cd src/Zentinela.Web && npm run build` (o `npm run lint && npm run typecheck` si existen).
5. **Tests** existentes en `tests/`: ejecutar y dejar verdes. Si la lógica nueva es crítica, añadir test unitario (regla global #5).
6. **Verificar runtime end-to-end**: levantar API + Web localmente. Abrir Calificador con cartera real → MutualSer aparece en panel "Localización por fuente" si aplica. Abrir Creador → chips nuevos visibles, marcar uno y descargar Excel → columna trae datos. Abrir Consulta Integrada con un titular conocido en la fuente → sub-bloque renderiza.
7. **Commit en español** (regla global #8) con mensaje:
   ```
   feat(fuentes): agregar fuente <NombreFuente> - BD + API + UI
   ```
   Incluir Co-Authored-By estándar de Claude Code.
8. **Reportar al orquestador** un resumen estructurado:
   - Columnas añadidas a `vw_BaseMadre` + `fn_BaseMadrePorCartera`.
   - Archivos backend modificados (lista corta).
   - Archivos frontend modificados (lista corta).
   - Sub-módulos que ahora exponen la fuente.
   - Cualquier excepción de convención introducida.
   - Cualquier riesgo o follow-up pendiente.

## Criterios de éxito

- **Vista + TVF actualizados** en una misma migración SQL idempotente y reversible.
- `BaseMadreRow.cs` + `BaseMadreRowMapping.cs` reflejan las columnas nuevas y EF las hidrata sin error.
- `ColumnasMadre.Todas` incluye las nuevas entradas.
- **`SqlAlias` (en `BaseMadreRepository`) incluye una entrada por cada columna nueva** — las cuatro listas espejo (`BaseMadreRow`, `BaseMadreRowMapping`, `ColumnasMadre`, `SqlAlias`) quedan con exactamente las mismas columnas. El export CSV/Excel con **todas las columnas encendidas** baja completo, no se corta.
- Si la fuente expone celular/email: switches `ColumnaCelular`/`ColumnaEmail` y arrays `FUENTES_CELULAR`/`FUENTES_EMAIL` actualizados, en sincronía exacta de claves.
- **Salvo justificación explícita registrada**, la fuente está en el panel de localización: `SUM(CASE...)` Enc/NoEnc + `LEFT JOIN` en result set 9 + entrada en `LeerLocalizacionAsync` + entrada en el diccionario `Fuentes` + `FUENTE_SLUG`/`ESTADO_FUENTE` en el frontend.
- Si la fuente expone columnas de teléfono: `RastreoLineasService.SqlConsulta` **y `UsoBaseDatosService`** extendidos con `UNION ALL` por cada columna, con el mismo conteo de fuentes en ambos.
- ConsultaIntegrada renderiza la nueva fuente como sub-bloque dentro del grupo "Contactabilidad" (sin tocar el frontend).
- `ESTADO_FUENTE` en CalificadorCarteraPage incluye la nueva fuente con etiqueta + color.
- **Si la fuente se alimenta por archivo (5f):** aparece habilitada en `GET /api/cargue-fuentes/fuentes`; su caso del harness cumple la **invariante de conteos** (`leídas + agregadas por expansión = excluidas + duplicadas + candidatas` y `candidatas = insertadas + refrescadas + vacías descartadas`) y la **doble carga del mismo archivo da 0 insertadas** con `COUNT(*)` del destino igual; el rango reservado queda limpio. `CatalogoCoherenciaTests` y los tests de la regla en verde.
- Sidebar sigue alfabético; no se duplican rutas.
- Localización con 3 estados (si aplica) usa `LEFT JOIN DISTINCT` directo, **no** `vw_BaseMadre`.
- `dotnet build` y `npm run build` sin errores.
- Smoke runtime confirma datos en cartera real.
- Commit creado con el mensaje canónico y reporte final al orquestador.

## Anti-patrones (no hacer)

- ❌ **Modificar solo `vw_BaseMadre` y olvidar los TRES iTVF** — el código productivo consume los TVF; la vista solo se usa para EF mapping. Una migración que toca solo la vista, o que toca `fn_BaseMadrePorCartera` pero olvida `fn_BaseMadreParaTitulares` / `..._CarteraChica`, deja el sistema inconsistente. Son **cuatro** objetos; enumerarlos con `sys.objects` antes de escribir la migración, no fiarse de la memoria.
- ❌ **Extender `RastreoLineasService` y olvidar `UsoBaseDatosService`** — son espejos. El olvido no produce error: el informe de Uso de Base de Datos simplemente no cuenta esa fuente, y el faltante solo se nota comparando totales contra Rastreo.
- ❌ **Elegir un nombre de fuente que sea prefijo o sufijo de otra sin grepearlo antes** — al integrar `Sura` (2026-09-02) colisionó cuatro veces con `ARLSura`: los alias `[NIT Sura]`/`[Empresa Sura]`, la clave `["sura"]` del diccionario `Fuentes`, los alias SQL `SuraEnc`/`SuraNoEnc` del result set 9, y un `AS EmpresaSura` duplicado en el `SELECT INTO #t`. **Ninguna de las cuatro la atrapa el compilador**: la clave repetida revienta en runtime al inicializar el diccionario y las columnas duplicadas al ejecutar el SQL. Antes de escribir nada, `grep -rn "<NombreFuente>" src/` y revisar cada hit.
- ❌ **Verificar solo el export y dar por buena la integración** — el export (`StreamFilasAsync`) proyecta columnas explícitamente y el llamador lee del `DbDataReader` sin convertir al tipo C#, así que **no detecta un tipo cruzado**. El **preview** (`ObtenerAsync`) hace `SELECT *` y materializa `BaseMadreRow` por EF: es el único camino que exige que el tipo SQL de cada columna case con el tipo de su propiedad. Regresión real (Sura, 2026-09-02): `[NIT Sura]` se generó como `ISNULL(CAST(SU.NIT AS VARCHAR(20)), 0)` contra una propiedad `long?`, y el Creador quedó **caído en todas las carteras** con `InvalidCastException: Unable to cast object of type 'System.String' to type 'System.Int64'`, mientras el export pasaba limpio. **Ejercitar SIEMPRE los dos caminos.** Regla de tipos: una columna numérica se expone con `ISNULL(<col>, 0)` (bigint, como `[NIT ARL Sura]` y `[NIT Positiva]`) y su propiedad es `long?`; si se envuelve en `CAST(... AS VARCHAR)`, la propiedad tiene que ser `string?`. No mezclar.
- ❌ **Agregar la columna a `ColumnasMadre`/`BaseMadreRow`/mapping pero olvidar `SqlAlias`** — el preview engaña (funciona porque usa reflexión), pero el export CSV/Excel se rompe a media transmisión (`ERR_INCOMPLETE_CHUNKED_ENCODING` / archivo con 1 fila) por desfase entre el conteo de encabezados y los campos del reader. Las cuatro listas espejo se actualizan **juntas, siempre**. (Regresión histórica: integración MiVacuna, corregida en `1cbd33f`.)
- ❌ **Componer LINQ encima de `FromSql(TVF)`** — EF Core envuelve la TVF en subquery sin pushdown del TOP/filtros, causando timeouts (lección de `project_optimizacion_cartera_estado.md`). Mover TOP y filtros al SQL del TVF directamente.
- ❌ **Asumir que la fuente "no aplica" a localización solo porque no trae Departamento/Municipio** — el panel no es geográfico, es de trazabilidad (encontrado/no-encontrado/falta-por-buscar) por cualquier campo identificable. Esta suposición incorrecta fue exactamente lo que dejó a **MiVacuna sin localización** en su integración original (2026-05-29) — corregido el 2026-07-08. Por defecto la respuesta a la pregunta 6 del preflight es SÍ; si de verdad no aplica, decirlo explícitamente y registrar el motivo (memoria + `para-tener-en-cuenta.md`), no dejarlo pasar en silencio.
- ❌ **Olvidar alguna de las 4 piezas del panel de localización** — `SUM(CASE...)` en result set 9, el `LEFT JOIN`, la entrada en `LeerLocalizacionAsync`, y la entrada en el diccionario `Fuentes` (CSV) son 4 lugares distintos en el mismo archivo (`CalificadorCarteraService.cs`) que deben quedar sincronizados. Faltar cualquiera de los 4 deja la fuente a medias (aparece en la tabla pero sin datos, o con datos pero sin poder descargar el CSV).
- ❌ **Confiar en referencias a `ObtenerLocalizacionAsync`/`LocalizacionResumenRaw`** — esos nombres son de una arquitectura anterior a la fusión del 2026-05-21; el código actual vive en el batch único `SqlAgregados` (result set 9) + `LeerLocalizacionAsync` + el diccionario `Fuentes`. Si una memoria de sesión vieja o un comentario menciona esos nombres, están obsoletos — verificar contra el archivo real.
- ❌ **Asumir que `RastreoLineasService` no se toca solo porque ADR-015 lo dice** — el ADR-015 quedó parcialmente desactualizado. La regla práctica vigente: si la fuente expone teléfono, **se añade al `UNION ALL` de `RastreoLineasService.SqlConsulta`**.
- ❌ **Copiar el SP viejo dentro de la regla de cargue (5f)** — la regla traduce solo lo propio de la fuente; lo genérico ya lo hace el motor y los defectos del SP (lista de 5f.2) no se reproducen. Tampoco borrar `<Fuente>Auxiliar` ni su SP antes de validar el cargue en producción y respaldarlos en `docs/sql/legacy/`.
- ❌ **Hardcodear cadenas de conexión** (regla global #3).
- ❌ **Consumir la tabla fuente vía `vw_BaseMadre` cuando se necesitan 3 estados** — rompe localización (la vista aplana NULLs a `'0'`).
- ❌ **Añadir la fuente al `switch` de celular/email si la BD no la expone como tal** — generaría columnas fantasma y excepciones EF.
- ❌ **Crear sub-módulo nuevo sin reflejarlo en `navigation.ts`**, o agregarlo con orden manual rompiendo el orden alfabético del Sidebar.
- ❌ **Saltarse `docs/schema.md`** — los demás agentes pierden contexto inmediatamente.
- ❌ **Mezclar este cambio con refactors no relacionados** en el mismo commit.
- ❌ **Usar el patrón N:1 de DatosClaro para una fuente 1:1 en ConsultaIntegrada** — innecesariamente complejo. Modelar como `Cajacopi.cs` cuando una sola fila por titular.
- ❌ **Añadir teléfono fijo al selector de celular** — el selector existe para marcado masivo a móviles. Exponerlo como columna está bien; meterlo al switch no.
