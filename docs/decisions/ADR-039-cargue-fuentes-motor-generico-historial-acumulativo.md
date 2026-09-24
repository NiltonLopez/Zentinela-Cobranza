# ADR-039 — Cargue de Fuentes de Datos: motor genérico con reglas por fuente e historial acumulativo

**Estado:** Aceptado
**Fecha:** 2026-09-24
**Módulo afectado:** Administración → Cargue de Fuentes de Datos (nuevo). Escribe `Sura`, `Cajacopi`,
`MutualSer`, `NuevaEps`, `SIMIT`, `DatosClaro` (+ `Titular`, `Titular_Cartera`); tablas nuevas
`CargueFuenteEjecucion` y `CargueFuenteReversion`. Impacta a todo lo que lee esas fuentes (`vw_BaseMadre`,
los tres `fn_BaseMadre*`, Calificador, Creador, Consulta Integrada, Rastreo, Uso de Base de Datos).
**Reemplaza:** el flujo manual `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar` para las 6 fuentes de la
Fase 1 (las otras 15 lo conservan hasta la Fase 2).

---

## Contexto

Cargar una fuente de localización era manual: el auxiliar unía los Excel del proveedor, los importaba
con el asistente de SSMS a `<Fuente>Auxiliar` y ejecutaba `sp_Procesar<Fuente>Auxiliar`. Sin preview,
sin conteos, sin forma de saber qué había cambiado ni de deshacerlo.

Antes de diseñar se leyeron completos los 21 `sp_Procesar*Auxiliar` de fuentes y las 30 tablas
Auxiliar/staging de la BD real. Lo que apareció fijó el diseño:

- **Los 21 SP son el mismo esqueleto** con 0 a 5 reglas propias cada uno: dedup por titular → `INSERT
  Titular` faltantes → `INSERT Titular_Cartera` → vacíos a NULL → calcular `EstadoEn<Fuente>` →
  `FechaConsulta = GETDATE()` → `INSERT` → dedup y purga de vacías en la tabla final. Lo propio de cada
  fuente (prefijar fijos, mover celulares a fijo, despivotar placas, N:1) es poco y localizado.
- **El modelo de datos es un historial acumulativo**, no una foto: nunca se hace UPDATE de contenido;
  si el contenido llega igual se "refresca" y si cambió conviven las dos filas. Las vistas toman la
  última por `FechaConsulta`.
- **Defectos reales, repetidos en casi todos:**
  - Ningún SP limpia nombres: `Titular.Nombre` tiene ~11.485 nombres con mojibake (`PIÃ‘A`, incluso
    doble: `PIÃƒÂ‘A`) y ~2.356 con `?` irrecuperable.
  - La lista negra de correos se cruza con `LIKE '%' + CorreoErroneo + '%'`: en Cámara eso anuló
    **67.826** correos cuando solo **18** coincidían de verdad (entradas como `a@gmail.com` bloquean a
    cualquiera que las contenga).
  - Sin transacción, sin conteos, sin rechazo explícito de filas; las limpiezas y el dedup corren sobre
    **toda** la tabla final en cada carga; nadie vacía las Auxiliar.
  - `Titular_Cartera` se inserta sin mirar si el par líder-cartera existe o está activo (RGM, Fase 2,
    llega a insertar `LiderID = CarteraID`).
  - `INSERT` posicionales en varios SP, que se rompen al agregar una columna.
- **No existía en Zentinela ningún trabajo en segundo plano con avance.** Un archivo de un millón de
  filas no cabe en una petición HTTP síncrona.

## Decisión

### 1. Motor genérico + reglas por fuente

Un solo motor (`CargueFuentesMotor`) hace todo lo común; lo que una fuente tiene de propio va en una
regla chica que traduce su SP viejo.

- **Definición declarativa en Application** — `Constants/CatalogoCargueFuentes.cs`: por fuente, la tabla
  destino, las columnas de dato (nombre exacto, alias del encabezado, tipo, largo, si entra al flag, si
  entra a la clave, si la produce la regla), el flag, la columna de sello y su tipo, si el TitularID es
  texto, si crea Titular / Titular_Cartera, la cardinalidad y si aplica la política de vacías. Vive en
  Application porque el front la consume por `GET /api/cargue-fuentes/fuentes` (precedente
  `CatalogoCreadorVirtualidad`): una sola fuente de verdad para columnas y alias.
- **Reglas en Infrastructure** — `Services/CargueFuentes/Reglas/`: `IReglaFuente` con tres ganchos con
  implementación vacía (`Transformar` mueve o prefija datos antes de validar; `Expandir` convierte una
  fila en N; `Validar` agrega exclusiones propias) y dos banderas (`NecesitaPrefijos`,
  `NecesitaListaNegraTelefonos`). Sura y NuevaEps usan `ReglaGenerica`: su SP no hacía nada que el
  motor no haga. `CatalogoReglasFuente` registra una regla por fuente habilitada (test de coherencia).
- **Todo el SQL sale de la definición** — `GeneradorSqlCargue`: identificadores solo desde el catálogo,
  validados con regex y entre corchetes (nada del archivo llega a un identificador; los valores van por
  parámetro o por `#temp`); `INSERT` siempre con lista de columnas; igualdad de contenido NULL-safe con
  `EXISTS (SELECT … INTERSECT SELECT …)`; hash de reversión con centinela por columna.

### 2. Historial acumulativo, hecho bien

- **Contenido idéntico → solo se refresca la fecha** (`UPDATE … SET FechaConsulta = @Sello`).
  **Contenido nuevo → INSERT.** Nunca se modifica el contenido de una fila existente.
- **Clave de contenido = TODAS las columnas de dato** (`EntraAClave = true` por defecto). Los SP usaban
  una clave recortada (Cajacopi 3 columnas, MutualSer 4, NuevaEps 3): una fila con los mismos teléfonos
  y un Municipio nuevo se "deduplicaba" y el municipio nuevo se perdía en silencio. Con la clave
  completa ese caso es una fila nueva de historial, que es justo lo que el modelo quiere.
- **Política de vacías** (fuentes con flag): una fila con `EstadoEn<Fuente> = 0` no entra si el titular
  ya tiene datos (en el destino o en el mismo archivo), y las vacías viejas de un titular que ahora trae
  datos se purgan. Es la versión acotada del "DELETE Estado = 0" de los SP, que recorría toda la tabla.
- **`Refrescadas` se cuenta del lado de `#Carga`**, no con `@@ROWCOUNT` del UPDATE: el destino tiene
  duplicados legacy y una fila del archivo puede refrescar varias filas idénticas.
- La igualdad la decide SQL Server con la collation de la BD (`Modern_Spanish_CI_AS`): un cambio **solo
  de mayúsculas** cuenta como contenido idéntico (se refresca y se conserva el texto viejo). Los
  correos se guardan en minúsculas y los teléfonos/placas normalizados, así que en la práctica solo
  afecta a textos libres (Empresa, TipoAfiliado…). Aceptado.

### 3. Nombres de titular

`NormalizadorTitular.LimpiarNombre`: repara el mojibake **por tramos** (cada par lead+continuación
UTF-8 leído como cp1252, con tabla cp1252 propia y hasta dos pasadas para el doble), conserva la **Ñ**,
quita tildes y diéresis, mayúsculas, solo letras y espacios, máximo 70. Una `Ã`/`Â` que sobrevive a la
reparación (byte perdido: `MUÃOZ` puede ser MUÑOZ; `RAMÃREZ`, RAMÍREZ) **no se adivina**: pasa a `?`
y el nombre queda marcado irrecuperable, igual que los que ya traen `?`.

`Titular.Nombre` existente se actualiza **solo si está roto** (vacío, con `Ã`, `Â` o `?`) y el del archivo
está limpio y no es irrecuperable. Un nombre sano nunca se pisa. La decisión se toma en C#
(`NormalizadorTitular.EstaRoto`), no con `LIKE '%Ã%'` en SQL, para no depender de la collation.

### 4. Contacto: listas negras exactas y validación

- **Correos: igualdad exacta**, no subcadena. `ListaNegraCorreos` clasifica cada `CorreoErroneo` (trim +
  minúsculas): con `@` bloquea ese correo exacto; con forma de dominio bloquea el dominio exacto; lo que
  no es ni una cosa ni la otra (`NOTEINE`) se ignora y se informa en el preview.
- **Teléfonos: celular = 10 dígitos que empiezan en 3; fijo = 10 dígitos que empiezan en 6**; el 57 se
  quita. Lo inválido se anula con advertencia, la fila sigue.
- **DatosClaro cruza contra `ListaNegraTelefonos` real** (~700k filas), no contra
  `ListaNegraTelefonosAuxiliar` (staging de 1,2M que usaba el SP). Los números distintos del archivo
  suben a `#TelefonosArchivo` y se cruzan con `OPTION (HASH JOIN)` (la lista es un heap sin índice);
  el teléfono vetado se anula y la fila se revalida (puede pasar a excluida).
- **SIMIT: una placa válida** es, sin espacios ni guiones y en mayúsculas, de **5 a 7 caracteres A-Z/0-9
  con al menos una letra y un dígito** (`PlacaVehiculo.EsValida`; cubre AAA999, AAA99A, AAA99, 999AAA,
  A99999, AA9999). Las marcas que el proveedor pone en la columna de placa (`PAZ Y SALVO`, `NO CARGO`,
  `SIN`, `N/A`, `XXXXXX`…) se tratan como **"sin placa"**: se descartan con una advertencia agrupada y,
  si el titular se queda sin placas, entra como una fila `Placa NULL, EstadoEnSIMIT = 0`. El SP las
  guardaba como placas y en parte con `EstadoEnSIMIT = 1` (titular "encontrado en SIMIT" sin vehículo).
  Los datos legacy los corrige aparte `docs/sql/migrations/2026-09-24_simit_placas_legacy.sql`
  (según su cabecera: 5.718 `PAZ Y SALV`, ~150 marcas más con estado 1 y 7 placas reales escritas con
  espacio o guion); sin ese script la próxima carga de `QUR25` no reconocería la fila `QUR 25` y la
  duplicaría. Errores de digitación con forma de placa (`IGBZ78D`) pasan: no se distinguen de un
  formato raro.
- **NuevaEps: `NumeroTelefono` es celular** (la BaseMadre lo expone como `[Celular NuevaEps]`). El SP no
  lo validaba y la tabla tiene ~3.000 números de 7 dígitos guardados como celular. Con el motor, un
  número así se **descarta con advertencia**; las filas existentes **no se tocan**.

### 5. Sello del servidor SQL

`FechaConsulta` / `FechaInsercion` se toman con `SELECT CAST(GETDATE() AS date)` **dentro de la
transacción**, del servidor SQL y no del servidor de la API: es la misma fuente de hora que usaban los
SP y que usan las vistas. DatosClaro lo escribe en `FechaInsercion`, como su SP; una columna de fecha que traiga el archivo se ignora (el preview la lista como "no se usa").

### 6. Transacción e invariantes de conteo

La confirmación **re-procesa el archivo guardado en disco** (no reusa lo validado: el job no retiene
un millón de filas en memoria, y lo que se escribe es lo re-parseado contra la BD de ese momento; la
huella SHA-256 debe coincidir con la validada) y escribe todo en **una transacción**: titulares nuevos
→ nombres rotos → relaciones (solo pares activos de `Lider_Cartera`) → marcar impacto otra vez →
descartar vacías → refrescar → insertar → purgar vacías viejas. Dos invariantes, verificadas en C#;
si una no cuadra se hace rollback (el "éxito falso" de ADR-038):

- `Leídas + AgregadasPorExpansión = Excluidas + DuplicadasEnArchivo + Candidatas`
- `Candidatas = Insertadas + Refrescadas + DescartadasVacías`

Cada ejecución queda en `CargueFuenteEjecucion` (fila `EN CURSO` insertada **antes** de lanzar la carga,
cerrada `OK` / `ERROR` / `CANCELADA` con los conteos). Tras un OK se invalida la caché del Calificador
(`ICalificadorCarteraCache.InvalidarTodo()`): cachea hasta 24 h cifras que salen de estas tablas y
solo se limpia sola en el corte de la 1 p. m.

### 7. Reversión por tabla, no por fecha

`DELETE WHERE FechaConsulta = @sello` no sirve: el sello es `date` (dos cargues del mismo día de la
misma fuente lo comparten) y las filas refrescadas no se distinguen de las insertadas. Por eso el motor
escribe, con `OUTPUT … INTO` en la misma transacción, **`CargueFuenteReversion`**: `I` insertada (hash
del contenido), `U` refrescada (hash + fecha anterior), `D` vacía purgada (fecha; hash `0x00`). La tabla
no tiene FK, CHECK ni triggers a propósito: SQL Server los prohíbe en el destino de `OUTPUT INTO`.

Revertir es manual: `docs/sql/revertir_cargue_fuente.sql` (`@EjecucionID`, ensayo con ROLLBACK por
defecto). Borra las `I` por titular + hash recalculado + sello, devuelve la fecha a las `U` y reinserta
las `D` como fila vacía (titular + flag 0 + fecha): **las columnas de una vacía que no entran al flag
(Departamento, Municipio…) no se guardaron y vuelven en NULL**. No revierte `Titular`, nombres ni
`Titular_Cartera` (reparaciones aditivas que otros pueden estar usando). La ejecución revertida conserva
`Estado = 'OK'` (el CHECK no admite otro valor y no se cambió el esquema) y queda marcada al principio
de `Error` con `REVERTIDA <fecha> por <login>`. Revertir fuera de orden (con ejecuciones posteriores
de la misma fuente) está bloqueado salvo que se fuerce.

### 8. Trabajo en segundo plano en memoria, con sus límites

- `CargueFuentesJobStore` (Singleton): jobs en un `ConcurrentDictionary`, retención 48 h (job + carpeta
  temporal), purga cada hora y de carpetas huérfanas al arrancar.
- `CargueFuentesOrquestador` (Singleton, `ICargueFuentesService`): guarda el archivo en
  `CargueFuentes:CarpetaTemporal/<jobId>/archivo.<ext>`, controla cupos (**un job activo por fuente** —
  dos cargues de la misma tabla compartirían sello y se pisarían la política de vacías — y **2 en total**)
  y lanza `Task.Run` con un **scope propio** (`IServiceScopeFactory`) donde resuelve el motor (Scoped: usa
  `CallCenterDbContext`). Captura toda excepción; la cancelación llega hasta los `SqlCommand`.
- El front hace **polling** cada 1,5 s mientras el job valida o carga y guarda el `jobId` en
  `sessionStorage` para retomarlo al volver.
- **Límite aceptado — IIS in-process:** un reciclaje del AppPool o su *idle timeout* matan el job. Si
  estaba en `Cargando`, la transacción se revierte sola (no hay escritura parcial) y la fila queda
  `EN CURSO`; la lista de cargues la muestra como **"Interrumpido"** y la cierra en BD como `ERROR`
  con `Interrumpida (reinicio del servidor)`. Mitigación de despliegue: AppPool con `Idle Time-out = 0`
  y `Start Mode = AlwaysRunning` (`docs/despliegue.md`).

## Alternativas descartadas

- **Una pantalla que ejecute los SP existentes.** Heredaba todos los defectos, 21 variantes que mantener
  y ningún preview ni conteo confiable.
- **Un SP genérico con SQL dinámico en la BD.** Identificadores armados en T-SQL, sin tests unitarios, y
  la limpieza de nombres (mojibake) y la validación de correos no se expresan bien en T-SQL.
- **MERGE / una fila por titular.** Destruye el historial que las vistas ya consumen y no sirve para
  fuentes N:1 (DatosClaro, SIMIT).
- **Clave de contenido recortada como en los SP.** Pierde cambios de localización en silencio.
- **Revertir por fecha de sello.** Ver punto 7.
- **Cola persistente / Hangfire / servicio Windows.** Robusto frente a reciclajes, pero es una dependencia
  e infraestructura nueva para una operación de pocas veces por semana. Se acepta el límite de IIS y se
  documenta la configuración del AppPool.
- **SignalR para el avance.** El polling alcanza para un avance que cambia cada segundo y no agrega
  infraestructura.
- **Fecha del servidor de la API como sello.** Dos relojes distintos para el mismo dato.

## Qué NO se copió de los SP (por fuente)

Común a las seis: nombres sin limpiar; lista negra de correos con `LIKE`; sin transacción ni conteos; dedup y limpiezas sobre toda la tabla final en cada carga;
`Titular_Cartera` sin validar el par en `Lider_Cartera` ni su `Activo`; `INSERT Titular SELECT DISTINCT
TitularID, Nombre` (un documento con dos grafías de nombre en el archivo viola la PK); staging nunca
vaciado.

| Fuente | Defectos del SP que el motor no reproduce |
|---|---|
| Sura | Dedup del staging `ORDER BY FechaConsulta DESC` cuando esa fecha aún no estaba puesta (ganador arbitrario); flag que trataba `''` como vacío mientras el INSERT lo guardaba; dedup en destino sin `UltimaFechaConsulta` en la clave |
| Cajacopi | `INSERT INTO Cajacopi SELECT …` posicional; flag calculado **antes** de limpiar (un titular con solo un fijo `1111111` quedaba en 1 sin datos); prefijo por dos `LEFT JOIN` con texto exacto y tildes (`BOGOTÁ` ≠ `BOGOTA`; un municipio repetido multiplicaba filas); fijo de 7 dígitos sin prefijo anulado sin aviso |
| MutualSer | Concatenaba `fijo + ', ' + celular` en un `varchar(10)` (truncaba el fijo); orden de reubicación que perdía el 60x cuando la columna fijo traía un celular; correo en lista negra a `''` |
| NuevaEps | JOIN varchar/bigint con conversión implícita (no sargable, frágil con ceros a la izquierda; ahora la conversión va del lado de `#Carga` y usa el índice); clave recortada a teléfono/estado/tipo; teléfonos de 7 dígitos guardados como celular |
| SIMIT | Dedup por titular **antes** de despivotar (se perdían las placas de la segunda fila del titular); `''` y el relleno del `nchar(10)` de Placa15 contaban como placa; `ABC123`, `abc 123` y `ABC-123` eran placas distintas; placa > 10 rompía el INSERT; marcas de texto guardadas como placa |
| DatosClaro | Cruce contra `ListaNegraTelefonosAuxiliar`; teléfono sin validar; `ISNULL(x, '')` igualaba NULL con `''`; dedup del paso 7 sobre toda la tabla (3,5M filas) en cada carga; titular inexistente entraba en silencio (sigue entrando, ahora con advertencia) |

## Consecuencias

- El auxiliar sube un CSV/XLSX, ve el avance por fase, puede irse y volver, revisa un preview con errores,
  advertencias e info agrupados y una muestra, y confirma. Límite de subida 120 MB; XLSX hasta 250.000
  filas (ClosedXML materializa el libro; por encima se pide CSV, que se lee por streaming sin tope).
- Agregar una fuente de la Fase 2 es una definición + (a veces) una regla + tests: el front no cambia.
  Única extensión del modelo prevista: `ColumnaClaveTitular` (CamaraEmpresa, clave `NitEmpresa`) y
  `FlagDesdeArchivo` (Compensados). Procedimiento en `/agregar-fuente-datos`, paso 5f.
- Las 6 `<Fuente>Auxiliar` y sus SP quedan sin uso. Respaldo íntegro en `docs/sql/legacy/` y DROP con
  guardas en `docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql`, **a ejecutar solo después de
  validar el cargue en producción**. `ARLSuraAuxiliar` / `sp_ProcesarArlSuraAuxiliar` no son de la Fase 1.
- Los datos que ya estaban en las tablas no se re-limpian (excepto SIMIT, por su script aparte): el
  motor solo decide sobre lo que carga.

## Riesgos

- **Reciclaje del AppPool** a mitad de un cargue: sin escritura parcial, pero hay que volver a subir el
  archivo. Mitigado con la configuración del AppPool.
- **Cargar mientras corre el ETL de Estado Titular** (`sp_Insertar_UltimoEstadoTitular`, ADR-038): va muy
  lento. Medido el 2026-09-24: el servidor quedó saturado de escritura de log (esperas `WRITELOG`) y el
  harness se detuvo hasta que el ETL terminó. No hay bloqueo entre ambos, es contención de disco de log.
  No cargar en la ventana del ETL.
- **Memoria:** un XLSX se abre dos veces por pasada (conteo de filas y lectura) y la confirmación repite
  las dos; con el tope de 250.000 filas y 2 jobs simultáneos es manejable. Un CSV de un millón de filas
  mantiene en memoria las filas limpias hasta subirlas a `#Carga` (cientos de MB).
- **Vacías purgadas:** su reversión es parcial (punto 7).
- **Duplicados legacy en destino:** un refresco los actualiza a todos; `Refrescadas` no lo refleja (se
  cuenta desde el archivo) pero `CargueFuenteReversion` sí tiene una `U` por fila tocada.
- **Collation CI:** un cambio solo de mayúsculas en un texto libre no genera historial.
- **Caché del Calificador:** se invalida al terminar un cargue, pero no al revertir con el script manual.

## Verificación

- 237 tests unitarios (tras la corrección de placas de SIMIT, commit d4e0a17) en `tests/Zentinela.Infrastructure.Tests` (helpers de Shared, `GeneradorSqlCargue`,
  una batería por regla y coherencia del catálogo: toda fuente habilitada tiene regla, alias ya
  normalizados, largos > 0).
- Harness `tools/CargueHarness/` contra la BD real, con TitularIDs de un rango reservado
  (`999000000001..999000000999`, 100 IDs por fuente): por fuente, validar → simular (rollback, tabla
  intacta) → cargar → **segunda carga del mismo archivo = 0 insertadas, N refrescadas y `COUNT(*)` igual**,
  invariante de conteos, casos de cada regla, y limpieza total del rango. Última corrida: 361 OK, 0 fallas.
- Los joins de NuevaEps (conversión del lado de `#Carga`) verificados con `SHOWPLAN` (Index Seek sobre
  `IX_NuevaEps_TitularID_FechaConsulta`).
- Pendiente antes del DROP: comparar cada fuente contra su SP viejo con el mismo archivo (`EXEC` en
  transacción con ROLLBACK vs `simular: true`). Diferencias esperadas: nombres reparados, lista negra
  exacta, flag post-limpieza en Cajacopi, sin truncar en MutualSer, lista negra real en DatosClaro,
  placas en SIMIT, teléfonos de 7 dígitos en NuevaEps. Cualquier otra es un bug.
