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
6. ¿Es fuente de **localización** (aporta Departamento/Municipio o Ciudad)? Si sí, además del SqlAgregados hay que añadirla al panel de localización del Calificador (paso 5b).
7. ¿Tiene página propia en el Sidebar? (raro — solo si hay vista dedicada útil al líder).

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
> La `vw_BaseMadre` sigue existiendo y se usa exclusivamente para el **shape de fila de EF** (`BaseMadreRowMapping.ToView("vw_BaseMadre")`) — EF mapea por nombre de columna, así que mientras el TVF y la vista expongan los mismos alias, todo funciona. **Si modificas la vista pero olvidas el TVF, el sistema queda inconsistente y EF lanzará excepciones de columna faltante en runtime.** Siempre actualizar **ambos** objetos en la misma migración.

### Sub-módulos que consumen `fn_BaseMadrePorCartera` (revisar/actualizar)

| Sub-módulo                  | Service backend                     | Controller                     | Página React                 | Consume TVF |
|-----------------------------|-------------------------------------|--------------------------------|------------------------------|-------------|
| Creador Zentinela           | `CreadorZentinelaService`           | `CreadorZentinelaController`   | `CreadorZentinelaPage`       | sí (vía repo + EF) |
| Calificador de Cartera      | `CalificadorCarteraService`         | `CalificadorCarteraController` | `CalificadorCarteraPage`     | sí (SQL crudo: `SqlAgregados` + `ObtenerLocalizacionAsync`) |
| Comparador de Carteras      | `ComparadorCarterasService`         | `ComparadorCarterasController` | `ComparadorCarterasPage`     | NO — usa `UltimosEstadosTitular` |
| Consulta Integrada          | `Modules/ConsultaIntegrada/**`      | (módulo autónomo)              | `ConsultaIntegradaPage`      | NO — consulta tablas fuente directamente |
| Rastreo de Líneas           | `RastreoLineasService`              | `RastreoLineasController`      | `RastreoLineasPage`          | NO consume el TVF, pero **sí debe extenderse** si la nueva fuente expone columna(s) de teléfono — UNION ALL en `RastreoLineasService.SqlConsulta` |
| Gestión de Carteras (admin) | `GestionCarterasService`            | `GestionCarterasController`    | `GestionCarterasPage`        | NO |
| Cargue de Reportes          | `CargueReporteService`              | `CargueReportesController`     | `CargueReportesPage`         | NO |
| Localización (6 stubs)      | (sin servicio actualmente)          | —                              | `Arl/Caja/Camara/...` stubs  | NO |

> **Nota crítica de arquitectura — vw_BaseMadre aplana NULLs a `'0'`**: por eso los módulos de localización con 3 estados (valor / vacío / sin consulta) NO la consultan: usan `LEFT JOIN (SELECT DISTINCT TitularID FROM TablaFuente)` directo + flags `EnFuente`. Si la nueva fuente expone localización, sigue ese patrón también en `ObtenerLocalizacionAsync` — no asumas que `vw_BaseMadre` te da la verdad sobre "no encontrado vs sin consultar".

### Fuentes ya integradas (referencia rápida)

| Fuente | Cardinalidad | Selector cel/email | Localización | Patrón ConsultaIntegrada |
|---|---|---|---|---|
| Cajacopi | 1:1 | sí / sí | sí | entidad 1:1 simple |
| Camara | 1:1 | sí / sí | sí | entidad 1:1 simple |
| SaludTotal | 1:1 | sí / sí | sí | entidad 1:1 + empresa |
| Positiva | 1:1 | sí / sí | sí | entidad 1:1 simple |
| Tigo | 1:1 | sí / sí | no (inactiva) | entidad 1:1 simple |
| SOS | 1:1 | sí / no | sí | entidad 1:1 + empresa |
| DatosClaro | **N:1** | sí / sí | sí (Rastreo) | entidad N:1, aplana con sufijos `Email 1`, `Email 2`, … |
| MutualSer | 1:1 | sí (×2 cels) / sí | sí (Rastreo + Localización) | entidad 1:1 simple |
| ARLSura | 1:1 | no | sí (solo empresa) | entidad simple |
| PositivaEmpresas | 1:1 | no | no | entidad simple |
| RUES | 1:1 | no | sí (solo empresa) | entidad simple |
| Fosyga | 1:1 | no | sí (regimen/depto/municipio) | entidad 1:1 |
| Compensados | 1:1 | no | no | entidad 1:1 |
| SIMIT | 1:1 | no | no (vehicular) | entidad 1:1 |
| RGM | 1:1 | no | no (vehicular) | entidad 1:1 |
| DatosExternos | 1:1 | **ignorada** — no se mapea (ver `project_datos_externos_removido.md`) | — | — |

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
   - `EXEC sp_helptext 'dbo.vw_BaseMadre'` y leer `docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql` (TVF actual). **Ambos son el destino del paso 1**.
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
3. **`DROP/CREATE FUNCTION dbo.fn_BaseMadrePorCartera`** — replicar exactamente las mismas columnas, CTE y JOIN dentro del iTVF, **con la pre-restricción a `TitularesCartera`** (ver patrón en `2026-05-06_fn_BaseMadrePorCartera.sql:47-61`). El orden de columnas debe coincidir 1:1 con `BaseMadreRowMapping`.

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

**5b. `Services/CalificadorCarteraService.cs` — `ObtenerLocalizacionAsync` (líneas ~440-555)** — solo si la fuente aporta localización (Departamento/Municipio o equivalente):
- En el `SELECT` del subquery `b` (líneas ~459-495): proyectar las columnas de localización (`v.[Departamento <Fuente>] AS Departamento<Fuente>`, …) y añadir flag `CASE WHEN <alias>.TitularID IS NOT NULL THEN 1 ELSE 0 END AS En<Fuente>`.
- En los `LEFT JOIN` (líneas ~511-524): añadir `LEFT JOIN (SELECT DISTINCT TitularID FROM <Tabla>) <alias> ON <alias>.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)`.
- En los SUMs (líneas ~390-458): añadir `<Fuente>Enc` y `<Fuente>NoEnc` con la misma lógica que las fuentes existentes.
- En `LocalizacionResumenRaw` (líneas ~558-574): añadir `<Fuente>Enc` y `<Fuente>NoEnc` properties.
- En el array de retorno (líneas ~530-546): añadir `Fuente("<EtiquetaUI>", r.<Fuente>Enc, r.<Fuente>NoEnc, total)`.

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
- Si la fuente expone celular/email: switches `ColumnaCelular`/`ColumnaEmail` y arrays `FUENTES_CELULAR`/`FUENTES_EMAIL` actualizados, en sincronía exacta de claves.
- Si la fuente aporta localización: `ObtenerLocalizacionAsync` actualizada (subquery + JOIN + SUMs + `LocalizacionResumenRaw` + array `Fuente(...)`).
- Si la fuente expone columnas de teléfono: `RastreoLineasService.SqlConsulta` extendido con `UNION ALL` por cada columna.
- ConsultaIntegrada renderiza la nueva fuente como sub-bloque dentro del grupo "Contactabilidad" (sin tocar el frontend).
- `ESTADO_FUENTE` en CalificadorCarteraPage incluye la nueva fuente con etiqueta + color.
- Sidebar sigue alfabético; no se duplican rutas.
- Localización con 3 estados (si aplica) usa `LEFT JOIN DISTINCT` directo, **no** `vw_BaseMadre`.
- `dotnet build` y `npm run build` sin errores.
- Smoke runtime confirma datos en cartera real.
- Commit creado con el mensaje canónico y reporte final al orquestador.

## Anti-patrones (no hacer)

- ❌ **Modificar solo `vw_BaseMadre` y olvidar `fn_BaseMadrePorCartera`** — el código productivo consume el TVF; la vista solo se usa para EF mapping. Una migración que toca solo la vista deja el sistema inconsistente.
- ❌ **Componer LINQ encima de `FromSql(TVF)`** — EF Core envuelve la TVF en subquery sin pushdown del TOP/filtros, causando timeouts (lección de `project_optimizacion_cartera_estado.md`). Mover TOP y filtros al SQL del TVF directamente.
- ❌ **Olvidar la lista enumerada en `LocalizacionResumenRaw` y en el array `Fuente(...)`** — el método `ObtenerLocalizacionAsync` enumera las fuentes a mano (no es agnóstico). Si añades MutualSer al SQL pero no al DTO ni al retorno, la fuente nueva no aparece en el panel.
- ❌ **Asumir que `RastreoLineasService` no se toca solo porque ADR-015 lo dice** — el ADR-015 quedó parcialmente desactualizado. La regla práctica vigente: si la fuente expone teléfono, **se añade al `UNION ALL` de `RastreoLineasService.SqlConsulta`**.
- ❌ **Hardcodear cadenas de conexión** (regla global #3).
- ❌ **Consumir la tabla fuente vía `vw_BaseMadre` cuando se necesitan 3 estados** — rompe localización (la vista aplana NULLs a `'0'`).
- ❌ **Añadir la fuente al `switch` de celular/email si la BD no la expone como tal** — generaría columnas fantasma y excepciones EF.
- ❌ **Crear sub-módulo nuevo sin reflejarlo en `navigation.ts`**, o agregarlo con orden manual rompiendo el orden alfabético del Sidebar.
- ❌ **Saltarse `docs/schema.md`** — los demás agentes pierden contexto inmediatamente.
- ❌ **Mezclar este cambio con refactors no relacionados** en el mismo commit.
- ❌ **Usar el patrón N:1 de DatosClaro para una fuente 1:1 en ConsultaIntegrada** — innecesariamente complejo. Modelar como `Cajacopi.cs` cuando una sola fila por titular.
- ❌ **Añadir teléfono fijo al selector de celular** — el selector existe para marcado masivo a móviles. Exponerlo como columna está bien; meterlo al switch no.
