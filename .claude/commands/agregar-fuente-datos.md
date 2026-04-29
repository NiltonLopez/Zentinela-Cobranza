# /agregar-fuente-datos

## Descripción

Implementa de forma **end-to-end** una nueva fuente de datos proveniente de la BD `DBCallCenter` y la propaga por **todas las capas** del sistema y por **todos los sub-módulos** que consumen la base madre. Está pensada para tablas tipo `DatosClaro`, `MutualSer`, contactabilidad, calificación socioeconómica, fechas (nacimiento, vinculación), datos demográficos, etc.

> Modelo recomendado: **Opus 4.7** (decisión arquitectónica + propagación cross-layer).

## Uso

```
/agregar-fuente-datos [NombreFuente] [TablaOrigen] [DescripcionCorta]
```

Ejemplos:
- `/agregar-fuente-datos DatosClaro DatosClaro "Contactabilidad Claro: celular, email, fecha último contacto"`
- `/agregar-fuente-datos MutualSer MutualSer "Calificación socioeconómica: cotiza, fecha nacimiento, estrato"`

Si el usuario no provee los 3 argumentos, **preguntar antes de ejecutar**:
1. Nombre canónico de la fuente (PascalCase, sin espacios — se usará en código).
2. Nombre exacto de la tabla u objeto en SQL Server.
3. Descripción funcional (1 línea) y campos clave que aporta.
4. ¿Aplica como filtro en Calificador / Creador (selector de fuentes)? (sí/no)
5. ¿Debe exponer celular o email (entra en lógica de coincidencias)? (sí/no)

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
        ├── types/               → *.ts (tipos compartidos con backend)
        ├── pages/
        │   ├── cartera/         → CalificadorCarteraPage, ComparadorCarterasPage, CreadorZentinelaPage
        │   ├── consultas/       → ConsultaIntegradaPage, RastreoLineasPage
        │   ├── localizacion/    → ArlSura, Cajacopi, Camara, Fosyga, Runt, Simit
        │   ├── informes/        → InformesRecaudoPage, InformesVirtualidadPage
        │   ├── home/            → DashboardLider/Director/Asesor + PagosConfirmados*
        │   └── administracion/  → GestionAsignaciones, GestionCarteras, GestionPersonal,
        │                           CargueReportes, CarguePagos, AsignacionMetas
        └── config/navigation.ts → MODULOS y subModulos del Sidebar
```

**Sub-módulos que consumen `vw_BaseMadre` y deben revisarse / actualizarse:**

| Sub-módulo                  | Service backend                     | Controller            | Página React                  | API client            |
|-----------------------------|-------------------------------------|-----------------------|-------------------------------|-----------------------|
| Creador Zentinela           | `CreadorZentinelaService`           | `CreadorZentinelaController` | `CreadorZentinelaPage`        | `creadorApi.ts`       |
| Calificador de Cartera      | `CalificadorCarteraService`         | `CalificadorCarteraController` | `CalificadorCarteraPage`      | `calificadorApi.ts`   |
| Comparador de Carteras      | `ComparadorCarterasService`         | `ComparadorCarterasController` | `ComparadorCarterasPage`      | `comparadorApi.ts`    |
| Consulta Integrada          | `Modules/ConsultaIntegrada/**`      | (módulo autónomo)     | `ConsultaIntegradaPage`       | `consultaApi.ts`      |
| Rastreo de Líneas           | `RastreoLineasService`              | `RastreoLineasController` | `RastreoLineasPage`           | `rastreoApi.ts`       |
| Gestión de Carteras (admin) | `GestionCarterasService`            | `GestionCarterasController` | `GestionCarterasPage`         | `gestionCarterasApi.ts` |
| Cargue de Reportes          | `CargueReporteService`              | `CargueReportesController` | `CargueReportesPage`          | `reportesApi.ts`      |
| Localización (6 vistas)     | (lectura directa por vista)         | `ConsultaController`  | `Arl/Caja/Camara/Fosyga/Runt/Simit` | `consultaApi.ts` |

> **Nota crítica de arquitectura (memoria del proyecto):** la vista `vw_BaseMadre` **aplana NULLs a "0"**. Los módulos de localización (Cajacopi, Cámara, Fósyga, etc.) NO la consultan: usan `LEFT JOIN DISTINCT` directo sobre las tablas fuente para preservar 3 estados (valor / vacío / sin consulta). Si la nueva fuente expone localización, sigue ese patrón — **no** la consumas vía `vw_BaseMadre` para localización.

## Pasos automáticos

> Ejecuta cada paso secuencialmente. Si el usuario interrumpe o un paso falla, **detente y reporta** — no avances con suposiciones.

### 0. Reconocimiento e inventario (siempre)

1. Leer `.claude/CLAUDE.md` y los archivos `MEMORY.md` relevantes para confirmar reglas vigentes.
2. Vía **MCP SQL Server**, inspeccionar la tabla origen y `vw_BaseMadre`:
   - `EXEC sp_columns @table_name = '<TablaOrigen>'`
   - `SELECT TOP 5 * FROM <TablaOrigen>`
   - `EXEC sp_helptext 'vw_BaseMadre'` para ver el SQL actual de la vista.
   - `SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='<TablaOrigen>'`
3. Mapear cada columna nueva a un nombre **PascalCase** consistente con `BaseMadreRow.cs` (p. ej. `CelularClaro`, `FechaContactoClaro`, `CotizaMutual`, `FechaNacimientoMutual`, `EstratoMutual`).
4. Decidir, junto al usuario si hay ambigüedad, qué columnas:
   - se exponen en `vw_BaseMadre` (información generalista, alta cardinalidad de uso).
   - quedan en tabla fuente y se exponen vía repositorio dedicado (datos especializados / localización).

### 1. Base de datos — actualizar `vw_BaseMadre` y `schema.md` (MCP)

1. Generar el `ALTER VIEW vw_BaseMadre AS …` aplicando el patrón existente (TOP 1 más reciente por fuente cuando aplique). Mostrar el SQL al usuario y **pedir confirmación antes de ejecutarlo**.
2. Ejecutar el `ALTER VIEW` vía MCP SQL Server.
3. Validar:
   - `SELECT TOP 5 <ColsNuevas> FROM vw_BaseMadre`
   - Conteo de filas igual al previo (no debe explotar el join).
4. Regenerar la sección de `vw_BaseMadre` y agregar la sección de la nueva tabla en **`docs/schema.md`** preservando el formato actual (encabezado con fecha, contadores, tablas en formato `| Columna | Tipo | Nullable | PK | FK |`). Incrementar la fecha “Actualizado” a la de hoy.

### 2. Backend — Core

Archivo: `src/Zentinela.Core/Entities/BaseMadreRow.cs`
- Añadir las propiedades nuevas con los tipos correctos (`string?`, `DateOnly?`, `int?`, `decimal?`, `long?`). Mantener nullables porque `vw_BaseMadre` aplana a "0"/NULL.

Si la fuente debe consultarse fuera de la vista (3 estados), crear:
- `src/Zentinela.Core/Entities/<NombreFuente>.cs` (entidad de la tabla original).
- `src/Zentinela.Core/Interfaces/I<NombreFuente>Repository.cs`.

### 3. Backend — Infrastructure (SQL Server)

1. `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Mappings/BaseMadreRowMapping.cs`
   - Añadir `b.Property(x => x.<Nuevo>).HasColumnName("<NombreColumnaVista>")` para cada columna nueva.
2. `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Repositories/BaseMadreRepository.cs`
   - Si la fuente entra en el selector de **celular** y/o **email** (Calificador / Creador):
     - Extender los `switch` `CelularDeFuente` y `EmailDeFuente` con la nueva entrada.
     - Extender la firma de `ObtenerAsync` solo si hay nuevos filtros que aplicar (de lo contrario, los nuevos campos viajan automáticamente al exponerse en la entidad).
3. Si se creó entidad fuente:
   - `Mappings/<NombreFuente>Mapping.cs` (Fluent API → tabla real).
   - `Persistence/CallCenterDbContext.cs`: registrar el `DbSet`.
   - `Repositories/<NombreFuente>Repository.cs`: implementar `LEFT JOIN DISTINCT` con `Titular_Cartera` para localización (patrón Cajacopi/Camara).
   - `DependencyInjection.cs`: registrar el repo (`services.AddScoped<I…Repository, …Repository>()`).

### 4. Backend — Application

1. `src/Zentinela.Application/DTOs/<Feature>/`
   - Extender los DTOs de respuesta de los servicios afectados (Calificador, Comparador, Creador, Rastreo, GestionCarteras) con las propiedades nuevas. Si la fuente solo agrega columnas a la base madre, la mayoría se propagan a través del DTO base de fila.
2. `src/Zentinela.Application/Services/CarteraService.cs` y otros servicios consumidores: revisar mapeos, no romper contratos existentes.

### 5. Backend — Services Infrastructure (consumidores)

Para **cada uno** de los siguientes services, abrir, identificar dónde se proyectan filas de `BaseMadreRow` hacia el DTO y agregar las propiedades nuevas en la proyección/mapeo:

- `Services/CalificadorCarteraService.cs`
- `Services/ComparadorCarterasService.cs`
- `Services/CreadorZentinelaService.cs` (vive en `Application/Services/`)
- `Services/GestionCarterasService.cs`
- `Services/RastreoLineasService.cs`
- `Services/CargueReporteService.cs` (si la fuente afecta cargue/validación)
- `Modules/ConsultaIntegrada/ConsultaIntegrada.Application/**` y `…Infrastructure/**`

**Regla:** si una fuente expone selector de celular/email, propagar el nuevo valor a las opciones de filtro/UI del Calificador y Creador (`fuentesCelular`, `fuentesEmail`).

### 6. Backend — API

- Endpoints existentes: añadir las propiedades nuevas en los modelos de respuesta (suelen heredar de DTO Application — verificar).
- Si hay endpoints **nuevos** para exponer la tabla fuente directamente, crearlos siguiendo el patrón de `ConsultaController` o `CalificadorCarteraController` (rutas en kebab/camelCase consistentes con las existentes).
- Compilar `dotnet build src/Zentinela.API` y resolver advertencias relevantes.

### 7. Frontend — React (Zentinela.Web)

> Recordatorio: el **Sidebar siempre se ordena alfabético** (`feedback_sidebar_alfabetico.md`). No introducir orden manual.
> Recordatorio: las vistas previas de cargue muestran como máximo **10 registros**.

1. **Tipos** — `src/Zentinela.Web/src/types/`
   - Extender los tipos afectados (`calificador.ts`, `comparador.ts`, `creador.ts`, `consulta.ts`, `rastreo.ts`, `gestionCarteras.ts`, `reportes.ts`) con las nuevas propiedades opcionales.
2. **API clients** — `src/Zentinela.Web/src/api/`
   - Si los endpoints añadieron parámetros nuevos (filtros de fuente), actualizar los `*Api.ts` correspondientes.
3. **Páginas afectadas** — agregar columnas en tablas, opciones de selector de fuente en filtros, tooltips o tarjetas de detalle. Revisar **todas** las que consumen base madre:
   - `pages/cartera/CreadorZentinelaPage.tsx`
   - `pages/cartera/CalificadorCarteraPage.tsx`
   - `pages/cartera/ComparadorCarterasPage.tsx`
   - `pages/consultas/ConsultaIntegradaPage.tsx`
   - `pages/consultas/RastreoLineasPage.tsx`
   - `pages/administracion/GestionCarterasPage.tsx`
   - `pages/administracion/CargueReportesPage.tsx`
   - Si la fuente aporta localización: añadir nueva página `pages/localizacion/<NombreFuente>Page.tsx` siguiendo el patrón existente (tabla simple + LeyendaUltimaActualizacion + 3 estados).
4. **Sidebar / Navigation** — `src/Zentinela.Web/src/config/navigation.ts`
   - Solo si la fuente requiere su **propia página** (caso localización), agregar el sub-módulo en el grupo correcto. El Sidebar lo ordenará alfabético automáticamente. **No** crear un grupo nuevo sin acordarlo antes.
5. **Routing** — `src/Zentinela.Web/src/App.tsx`
   - Registrar la ruta nueva si se añadió página.

### 8. Documentación

- `docs/schema.md` — actualizado en paso 1.
- `docs/api-referencia.md` — añadir/actualizar endpoints afectados.
- `docs/arquitectura-sistema.md` — si la fuente introduce nuevo flujo (p. ej. nuevo repositorio fuera de `vw_BaseMadre`), agregar diagrama/sección.
- `docs/para-tener-en-cuenta.md` — registrar advertencias específicas (p. ej. “MutualSer aplana NULLs a `0`, no usar para 3 estados”).
- Comentarios XML `///` en español sobre lógica compleja únicamente.

### 9. Memoria persistente

Guardar memoria `project_fuente_<nombre>.md` con:
- Tablas fuente y campos expuestos.
- Si está dentro o fuera de `vw_BaseMadre` y por qué.
- Si entra al selector de celular/email.
- Sub-módulos que ya la consumen.

Indexar en `MEMORY.md` con una línea de hook.

### 10. Verificación, build y commit

1. **Compilar solución completa sin errores**: `dotnet build` desde la raíz.
2. **Frontend**: `cd src/Zentinela.Web && npm run build` (o `npm run lint && npm run typecheck` si existen).
3. **Tests** existentes en `tests/`: ejecutar y dejar verdes. Si la lógica nueva es crítica, añadir test unitario (regla global #5 del proyecto).
4. **Verificar endpoint** afectado o nuevo (curl / cliente HTTP) responde correctamente con las propiedades nuevas.
5. **Verificar render** del componente React sin errores de tipos en consola.
6. **Commit en español** (regla global #8) con mensaje:
   ```
   feat(fuentes): agregar fuente <NombreFuente> - BD + API + UI
   ```
   Incluir Co-Authored-By estándar de Claude Code.
7. **Reportar al orquestador** un resumen estructurado:
   - Columnas añadidas a `vw_BaseMadre`.
   - Archivos backend modificados (lista corta).
   - Archivos frontend modificados (lista corta).
   - Sub-módulos que ahora exponen la fuente.
   - Cualquier riesgo o follow-up pendiente.

## Criterios de éxito

- `vw_BaseMadre` modificada y `docs/schema.md` regenerado con la fecha de hoy.
- `BaseMadreRow.cs` + `BaseMadreRowMapping.cs` reflejan las columnas nuevas.
- Todos los services consumidores siguen compilando y devuelven los nuevos campos.
- Selectores de fuente (Calificador/Creador) incluyen la nueva fuente **solo si aplica**.
- React compila sin errores de tipos; las páginas afectadas muestran (o no rompen) los nuevos campos.
- Sidebar sigue alfabético; no se duplican rutas.
- Localización (si aplica) usa `LEFT JOIN DISTINCT` directo, **no** `vw_BaseMadre`.
- `dotnet build` y `npm run build` sin errores.
- Commit creado con el mensaje canónico y reporte final al orquestador.

## Anti-patrones (no hacer)

- ❌ Hardcodear cadenas de conexión (regla global #3).
- ❌ Consumir la tabla fuente vía `vw_BaseMadre` cuando se necesitan 3 estados (rompe localización).
- ❌ Añadir la fuente al `switch` de celular/email si la BD no la expone como tal — generaría columnas fantasma.
- ❌ Crear sub-módulo nuevo sin reflejarlo en `navigation.ts`, o agregarlo con orden manual rompiendo el orden alfabético del Sidebar.
- ❌ Saltarse `docs/schema.md` — los demás agentes pierden contexto inmediatamente.
- ❌ Mezclar este cambio con refactors no relacionados en el mismo commit.
