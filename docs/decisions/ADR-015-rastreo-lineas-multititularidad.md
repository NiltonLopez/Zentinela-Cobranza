# ADR-015 — Rastreo de Líneas (multititularidad)

**Estado:** Aceptada — parcialmente actualizada (ver "Nota 2026-05-07")
**Fecha:** 2026-04-29

> **Nota 2026-05-07** — El criterio "solo tablas de Localización" del bloque
> original quedó superado en la práctica. La regla vigente es: **si una tabla
> tiene columna de teléfono usable, se incluye en el rastreo**. Tablas ya
> agregadas posteriormente: `DatosClaro` (commit `ca35f7c`), `MutualSer`
> (commit en curso). El criterio de exclusión actual queda restringido a:
> auditorías (`AuditoriaMensajeria`, `AuditoriaIVR`), logs salientes
> (`MensajesEnviados`, `IVREnviados`), listas negras (`ListaNegraTelefonos`),
> directorios (`NumeroTelefonoGeneral`), staging (`*Auxiliar`), respaldos
> (`*_bak`, `Staging_*`) y vistas (`vw_*`). Ver `RastreoLineasService.cs`
> para el set vigente.

## Contexto

Las líneas celulares de los titulares viven dispersas en ~14 tablas de `DBCallCenter` (fuentes externas como Cajacopi, Camara, DatosClaro, MutualSer, Positiva, SaludTotal, SOS, Tigo; tablas de auditoría AuditoriaMensajeria/AuditoriaIVR; lista negra; directorio general; y los logs salientes de MensajesEnviados/IVREnviados). Hoy no existe un punto de consulta unificado: identificar **multititularidad** — un mismo número asociado a varios titulares — requiere queries manuales tabla por tabla.

El módulo "Consulta Integrada" cubre el caso opuesto (cédula → fuentes), pero no el inverso (teléfono → titulares).

## Decisión

1. **Renombrar el módulo "Consulta Integrada" a "Consultas"** y convertir el actual buscador en sub-módulo. La etiqueta del módulo en el navbar pasa a `Consultas`; el `ModuloKey` en `navigation.ts` cambia de `'consulta'` a `'consultas'`. Las rutas:
   - `/consultas/integrada` (antes `/consulta`)
   - `/consultas/rastreo` (nueva)
   - `/consulta` queda como `Navigate` permanente a `/consultas/integrada` para no romper enlaces previos.

2. **Sub-módulo nuevo "Rastreo de Líneas"** que recibe un celular (10 dígitos, debe iniciar en 3) y devuelve, por cada tabla de contactabilidad donde aparezca, una **tarjeta** con el conteo y los titulares asociados, además de un resumen global de titulares distintos.

3. **Esta iteración solo visualiza** — no persiste "líneas confirmadas como multititulares". Eso queda pendiente para una iteración posterior si el negocio lo pide.

4. **Backend monolítico** — `IRastreoLineasService` en `src/Zentinela.Application`, implementación en `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/RastreoLineasService.cs`, controller en `src/Zentinela.API/Controllers/RastreoLineasController.cs`. NO se sigue el patrón modular de `src/Modules/ConsultaIntegrada/` para evitar duplicar boilerplate.

5. **Estrategia de query: un único `SqlQueryRaw` con `UNION ALL`** sobre las 14 tablas relevantes. Decisión tomada porque las tablas externas (Cajacopi, Camara, etc.) **no están mapeadas como entidades EF en `CallCenterDbContext`** y mapearlas solo para esta búsqueda sería un peso innecesario. El SQL devuelve filas `(Tabla, Etiqueta, NumeroDocumento, Nombre, Apariciones)` y el agrupado por tabla / titular se hace en C# en memoria (los volúmenes son pequeños porque siempre filtramos por número exacto).

6. **Match exacto, 10 dígitos comenzando en 3**. Validación duplicada cliente y servidor; un input inválido devuelve `400`.

7. **Deeplink al titular**: cada nombre/listado del rastreo enlaza a `/consultas/integrada?cedula={NumeroDocumento}` para abrir el sub-módulo hermano con ese titular cargado. La aceptación del query string `?cedula=` por parte de `ConsultaIntegradaPage` queda pendiente como mejora menor.

## Tablas consultadas

Solo las **tablas de Localización** (las fuentes externas usadas por el bloque Localización del Calificador de Cartera, ver `CalificadorCarteraService.ObtenerLocalizacionAsync`) que tienen columna de teléfono:

| Tabla | Columna(s) de teléfono | Identificador del titular |
|---|---|---|
| Cajacopi | NumeroTelefonoCelular, NumeroTelefonoFijo | TitularID (bigint) |
| Camara | NumeroTelefono | TitularID |
| Positiva | NumeroTelefonoCelular, NumeroTelefonoFijo | TitularID |
| SaludTotal | NumeroTelefono | TitularID |
| SOS | Telefono | TitularID |
| Tigo | NumeroTelefono | TitularID |

Tablas de Localización SIN columna de teléfono (no aplican): ARLSura, Fosyga, Compensados, PositivaEmpresas, RUES, SIMIT, RGM.

Tablas EXCLUIDAS deliberadamente porque NO son fuentes de localización (aunque tengan teléfono): `MensajesEnviados`, `IVREnviados`, `AuditoriaMensajeria`, `AuditoriaIVR`, `ListaNegraTelefonos`, `NumeroTelefonoGeneral`, `DatosClaro`, `MutualSer`, `CamaraEmpresa`. También quedan fuera todas las `*Auxiliar` (staging), `*_bak` (respaldos), `Staging_*`, `DatosExternos` (memoria del proyecto: ignorada) y vistas `vw_*`.

## Consecuencias

**Positivas**
- Detección inmediata de multititularidad sin buscar tabla por tabla.
- Una sola consulta SQL parametrizada — eficiente, fácil de explicar y de extender.
- Cero entidades nuevas en el monolito; las tablas externas siguen siendo propiedad del sistema de cobranza.
- UX consistente con el resto del producto (estilo de tarjetas + KPIs igual que Comparador / Mensajería).

**Negativas**
- Si una tabla nueva con teléfono se agrega en `DBCallCenter`, no aparece automáticamente en el rastreo — hay que extender el SQL del service.
- El SQL es un string largo (~150 líneas). Aceptable por ser plano y declarativo; si crece más, evaluar generar dinámicamente la lista de fuentes.
- El nombre del titular se denormaliza con `LEFT JOIN dbo.Titular`; si un `TitularID` huérfano existe en una tabla externa, el resultado muestra `(sin titular)`. Esto es el comportamiento deseado (transparencia sobre datos sucios).

## Verificación

Endpoint: `GET /api/consultas/rastreo/{numero}`
- Probar con un celular real que aparezca en `dbo.Cajacopi.NumeroTelefonoCelular` o `dbo.Camara.NumeroTelefono`. Resultado esperado: `totalApariciones >= 1` y al menos una `RastreoTablaCard` con `etiqueta` correspondiente a la fuente de localización.
- Si el mismo número aparece en `Cajacopi` y `Tigo` para distintos `TitularID`, `titularesDistintos` debe ser `>= 2` y la UI dispara el aviso de "posible multititularidad".
- Probar con número inexistente → `200` con `totalApariciones = 0`.
- Nota: el seed `3009999999` (de `MensajesEnviados`) ya **no** dispara resultados — `MensajesEnviados` no es fuente de localización.

Front:
- `/consultas/rastreo` accesible para Admin/Líder/Director/Asesor.
- Buscar `3009999999` muestra KPIs > 0, una tarjeta con el conteo y permite navegar a Consulta Integrada.
- Buscar un número inexistente muestra "No se encontraron apariciones".
- Input inválido (no 10 dígitos / no empieza en 3) deshabilita el botón.

## Pendientes

- `ConsultaIntegradaPage` debería leer `?cedula=` del query string para auto-completar el buscador cuando se navega desde Rastreo.
- Tabla `LineaMultititular(NumeroTelefono, FechaConfirmacion, UsuarioID)` + endpoint POST si el negocio quiere persistir confirmaciones manuales.
- Búsqueda por documento (no solo por teléfono) en el mismo sub-módulo.
- Exportar Excel con el detalle del rastreo.
- Indexar `(NumeroTelefono)` en las tablas que lo usen frecuentemente para acelerar el `WHERE = @numero` en volúmenes grandes.
