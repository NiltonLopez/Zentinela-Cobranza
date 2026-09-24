# ADR-039 — Cargue de Fuentes de Datos: motor genérico con reglas por fuente e historial acumulativo

**Estado:** Aceptado
**Fecha:** 2026-09-24
**Módulo afectado:** Administración → Cargue de Fuentes de Datos (nuevo). Escribe las 22 tablas de
fuentes de localización — Fase 1: `Sura`, `Cajacopi`, `MutualSer`, `NuevaEps`, `SIMIT`, `DatosClaro`;
Fase 2: `Positiva`, `SaludTotal`, `SOS`, `Coosalud`, `SaviaSalud`, `MiVacuna`, `Fosyga`, `RGM`, `TigoUne`,
`Tigo`, `RUES`, `Camara`, `CamaraEmpresa`, `PositivaEmpresas`, `ARLSura`, `Compensados` — (+ `Titular`,
`Titular_Cartera`); tablas nuevas `CargueFuenteEjecucion` y `CargueFuenteReversion`. Impacta a todo lo que
lee esas fuentes (`vw_BaseMadre`, los tres `fn_BaseMadre*`, Calificador, Creador, Consulta Integrada,
Rastreo, Uso de Base de Datos).
**Reemplaza:** el flujo manual `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar` de las 21 fuentes que lo
tenían (6 en la Fase 1, 15 en la Fase 2) y la carga a mano de `Tigo`, que no tenía SP. `Runt` queda fuera
(ver "Fase 2").

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
  - Los 8 SP que cruzan la lista negra de correos (Sura, MutualSer, DatosClaro, Positiva, Coosalud,
    SaviaSalud, MiVacuna, TigoUne) lo hacen con `LIKE '%' + CorreoErroneo + '%'`: entradas como
    `a@gmail.com` bloquean a cualquiera que las contenga. Medido sobre los correos de `dbo.Camara`
    (1,86 M filas, 683.874 correos distintos): ese `LIKE` anularía **67.826** correos distintos (89.937
    filas) cuando solo **18** (126 filas) coinciden de verdad. *Corrección del 2026-09-24: la primera
    versión de este ADR decía que "en Cámara eso anuló" esos correos. No fue así: `sp_ProcesarCamaraAuxiliar`
    no cruza `ListaNegraCorreos` (ni valida el correo), así que la cifra es una medición del efecto que
    tendría el `LIKE` sobre los datos de Cámara, no un daño ocurrido (hallazgo H6 de
    `docs/sql/legacy/PARIDAD-empresas.md`; los correos siguen en la tabla).*
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
- **Reglas en Infrastructure** — `Services/CargueFuentes/Reglas/`: `IReglaFuente` con ganchos de
  implementación vacía (`Transformar` mueve o prefija datos antes de validar; `Expandir` convierte una
  fila en N; `Validar` agrega exclusiones propias; desde la Fase 2, `NormalizarClave`, identidad por
  defecto, limpia la clave antes de validarla) y dos banderas (`NecesitaPrefijos`,
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
Con destino `datetime` (solo RUES, Fase 2) el sello lleva hora, **truncada al segundo** (ver "Fase 2").

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
defecto; lista blanca de las 22 fuentes con su clave, tipo de sello y flag). Borra las `I` por clave
(titular; `NitEmpresa` en CamaraEmpresa) + hash recalculado + sello, devuelve la fecha a las `U` y reinserta
las `D` como fila vacía (clave + flag 0 + fecha): **las columnas de una vacía que no entran al flag
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

## Fase 2 (2026-09-24): 22 fuentes

La Fase 2 suma **16 fuentes** a las 6 de la Fase 1, en tres grupos que se trabajaron en paralelo (ramas
`feature/cargue-f2-salud`, `-otras`, `-empresas`, integradas en `feature/cargue-fuentes`):

| Grupo | Fuentes | Regla propia |
|---|---|---|
| salud | Positiva, SaludTotal, SOS, Coosalud, SaviaSalud, MiVacuna | `ReglaPositiva` (fijo que llega en el celular pasa al fijo, sin concatenar; columnas intercambiadas), `ReglaSaviaSalud` (celdas con varios números: primer celular en `NumeroCelular`, teléfono propio o el siguiente en `NumeroTelefono`, sin concatenar). El resto `ReglaGenerica` |
| otras | Fosyga, RGM, TigoUne, Tigo | `ReglaFosyga` (Estado 'AFILIADO FALLECIDO' → TipoAfiliado 'FALLECIDO'), `ReglaRgm` (modelo = año plausible, porque el destino es `int`). TigoUne y Tigo `ReglaGenerica` |
| empresas | RUES, Camara, CamaraEmpresa, PositivaEmpresas, ARLSura, Compensados | `ReglaCamara` (indicativo al fijo de 7 dígitos, textos de relleno del formulario), `ReglaCamaraEmpresa` (quita el DV de 'NIT-DV' + indicativo), `ReglaCompensados` ('NO SE ENCUENTRA' no es un dato). El resto `ReglaGenerica` |

Todas las definiciones se verificaron contra `sys.columns` del destino el 2026-09-24 (tipos, largos, FK,
triggers, índices). El motor no cambió de forma; hicieron falta cuatro extensiones **genéricas**, ninguna
con nombre de fuente:

1. **`ColumnaClaveTitular`** (`DefinicionFuenteCargue`, con `AliasClaveTitular`, `DescripcionClaveTitular`,
   `ClaveEsTitular` y `AliasDeClave`): la columna del destino que identifica la fila, `TitularID` por
   defecto. `dbo.CamaraEmpresa` no tiene `TitularID`: la fila es de una **empresa** y se identifica por
   `NitEmpresa` (bigint NULL; 71.424 filas, solo 2.999 NIT coinciden con un `Titular`). `GeneradorSqlCargue.ColumnaTitular`
   la usa en el cruce con el destino, el `INSERT`, el refresco, la purga de vacías (con su `OUTPUT` a
   `CargueFuenteReversion`, donde el NIT va como texto en `TitularID`) y la muestra. `ValidarDefinicion`
   exige que una clave que no es `TitularID` **no** cree `Titular` ni `Titular_Cartera` ni sea
   `TitularIdComoTexto`; el motor no la cruza con `dbo.Titular`; el orquestador publica la clave real en
   `columnasEsperadas` (el front no cambia).
2. **`IReglaFuente.NormalizarClave`** (identidad por defecto), llamado antes de validar la clave como
   documento. `ReglaCamaraEmpresa` quita el dígito de verificación de 'NIT-DV' ('900123456-7',
   '900.123.456-7'): sin eso el motor pegaría el DV al NIT y la fila no cruzaría con `ARLSura.NIT` en
   `vw_DatosCamaraEmpresa`.
3. **`GeneradorSqlCargue.ConsultarPrefijos`**: `PrefijoMunicipio` + `PrefijosDepartamento` (la tabla que
   usaban los SP de Cámara) como filas solo-departamento. Los 33 departamentos comunes tienen el mismo
   prefijo en las dos; `PrefijosDepartamento` aporta los nombres que `PrefijoMunicipio` escribe distinto
   ('NORTE DE SANTANDER' vs 'N. DE SANTANDER', San Andrés). Si algún día difirieran, `PrefijosFijo` ve dos
   prefijos y no resuelve (no adivina). Afecta también a Cajacopi: solo resuelve más.
4. **Sello `datetime` truncado al segundo** (`GeneradorSqlCargue.LeerSello`,
   `CONVERT(datetime, CONVERT(char(19), GETDATE(), 120), 120)`): `RUES.FechaConsulta` es `datetime` (el SP
   guardaba `GETDATE()`; 799.156 de sus 873.188 filas tienen hora), la única de las 22. `SelloFechaConsulta`
   y `FechaAnterior` son `datetime2(0)`: un sello con milisegundos se guardaría redondeado en la auditoría
   (09:43:49.920 → 09:43:50) y la reversión no volvería a encontrar las filas por igualdad. Truncar (no
   redondear) tampoco cambia de día a las 23:59:59.9. El script de reversión compara ese sello como
   `datetime`, nunca convertido a `date`.

**`FlagDesdeArchivo` no hizo falta.** El plan suponía que Compensados traía el flag en el archivo. El SP no
lo toma: recalcula `Estado = CASE WHEN Estado IS NULL AND EPS IS NULL AND UltimoPeriodoCompensado IS NULL
THEN 0 ELSE 1 END` y lo inserta por posición en `EstadoEnCompensados` (`int NOT NULL`, no `bit`). La columna
Estado del archivo solo servía para dar flag 1 a una fila **sin ningún dato** (2.315 de 366.072 filas
hoy). El motor calcula el flag con lo que guarda (EPS o período) y lista Estado como "columna que no se
usa". Si el usuario confirma que Estado significa "encontrado en Compensados" aunque no haya EPS ni período,
entonces sí hará falta la extensión (decisión pendiente, H2 de `PARIDAD-empresas.md`).

**Runt queda fuera.** `dbo.Runt` y `dbo.RuntAuxiliar` tienen 0 filas desde que se crearon (2026-02-18), no
hay `sp_ProcesarRuntAuxiliar` ni ningún objeto, trabajo o paquete que las escriba, y **nadie la lee**: no
está en la BaseMadre ni en ningún servicio, y el front es un `PaginaEnConstruccion`. Agregarla sería
mantener una definición sin datos ni consumidores; `CatalogoCoherenciaTests.Runt_no_esta_en_el_catalogo` lo
fija. Si se activa: su `FechaConsulta` también es `datetime` (ya soportado) y el flag se llama `EstadoRunt`.
El índice `IX_Runt_TitularID_Fecha` que citaban los documentos **no existe** (la tabla es un heap sin índices).

**Tigo no tiene SP.** `dbo.Tigo` se llenó a mano en febrero-marzo de 2025 desde `TigoAuxiliar` y nada la
escribe desde el 2025-03-14, pero **sigue en uso** (las cuatro BaseMadre, Calificador, Rastreo, Uso de Base
de Datos, Consulta Integrada, `sp_Actualizar_AuditoriaMensajeria`): el cargue es la única forma de volver a
actualizarla. Definición por analogía con TigoUne (misma forma, `ReglaGenerica`), sin paridad posible. Tiene
FK a `Titular`, así que crea titulares y **el archivo debe traer Nombre**: el formato de `TigoAuxiliar`
(sin nombre) se rechaza con "Faltan columnas obligatorias: Nombre". Todo celular que deja pasar el motor
cumple su CHECK `chk_Telefono`.

Otras decisiones de definición que no vienen del SP:

- **Teléfono que la BaseMadre expone como celular = `Celular`**, con el criterio de NuevaEps (§4): SOS
  (`Telefono`), SaludTotal, Coosalud, TigoUne y Tigo (`NumeroTelefono`) y MiVacuna (`Celular`). Un fijo o un
  número de 7 dígitos se anula con advertencia; los fijos legacy (625 de 7 dígitos y 34 60x en SOS, 1.101
  fijos en Tigo…) no se tocan. Camara y CamaraEmpresa (mezclan celulares y fijos) y
  `SaviaSalud.NumeroTelefono` son `Telefono`.
- **Coosalud, SaviaSalud, MiVacuna y TigoUne crean `Titular` y `Titular_Cartera`** aunque el destino no
  tenga FK a `Titular`, porque sus SP lo hacían.
- **ARLSura ≠ Sura.** Clave de ruta `ARLSura`, etiqueta "ARL Sura (empleador)", tabla `dbo.ARLSura`; su flag se
  llama `EstadoEnSura` como el de la EPS, pero es otra columna de otra tabla. El catálogo busca por igualdad
  (test que lo fija) y el harness verifica que cargar ARLSura no toca `dbo.Sura`.
- **Placas de RGM** con la misma validación que SIMIT (`PlacaVehiculo`): el 99,7 % de las 8.765 placas de
  `dbo.RGM` la cumple; las 25 que no son `'[objec'` (`'[object Object]'` truncado por el staging), `'AVAL'` y `'0'`.

**Paridad con los SP** (mismo método que la Fase 1: cuerpo del SP como batch sobre `#temp`, ningún SP
ejecutado; detalle en `docs/sql/legacy/PARIDAD-salud.md`, `PARIDAD-otras.md`, `PARIDAD-empresas.md` y el
consolidado en su `README.md`): **264 casos, 65 idénticos, 199 diferencias explicadas, 0 sin explicar**.
Tigo no tiene SP con qué comparar. Ninguna pérdida ni corrupción de datos atribuible al motor.

**Harness por grupos** (`tools/CargueHarness/Grupos.cs`, commit cad81e7): cada grupo tiene su rango
reservado de TitularIDs (fase1 `999000000001..0999`, salud `…1000..1999`, empresas `…2000..2999`, otras
`…3000..3999`, desglose `…4000..4999`), su usuario de auditoría y bloques de 100 por fuente, para correr
varios harness en paralelo sin pisarse. Última corrida de cada grupo: salud 321 OK, empresas 330 OK, otras
TODO OK; 0 fallas.

## Desglose de chips (2026-09-24)

Cada chip de conteo del preview y de la tarjeta de resultado se despliega y muestra **de qué se compone su
número**: grupos con cantidad exacta, y cada grupo con hasta 10 filas de ejemplo.

- **Contrato** (`CargueFuentesDtos.cs`): `PreviewCargueDto.Desglose` y `ResultadoCargueDto.Desglose`, lista de
  `DesgloseChipDto { Clave: ChipCargue, Grupos: GrupoDesgloseDto[] }`; `GrupoDesgloseDto { Titulo, Cantidad,
  Tono, Ejemplos }`. `ChipCargue` y `TonoDesglose` (`Error` · `Aviso` · `Info`) viajan como texto. El
  preview trae los 12 chips (`Leidas`, `Excluidas`, `Duplicadas`, `Insertadas`, `Refrescadas`,
  `VaciasDescartadas`, `VaciasPurgadas`, `TitularesNuevos`, `NombresReparados`, `RelacionesNuevas`,
  `SinCarteraLider`, `Advertencias`); el resultado, los 8 de la escritura. `ResultadoCargueDto` suma
  `NombresIrrecuperables`. El título de un grupo no lleva número de fila; los ejemplos sí
  ("Fila 12 · 1234567 · valor", valor recortado a 80).
- **Composición.** Leídas = Excluidas + Duplicadas en el archivo + "Pasaron a la carga", siempre en filas
  del **archivo** (commit 561c176): con expansión (SIMIT) las filas destino de más se descuentan del grupo
  donde terminaron y el título dice cuántas filas destino generan. Excluidas, Advertencias, Duplicadas y
  Sin cartera/líder: un grupo por motivo, de mayor a menor. Insertadas: "Titular sin datos previos en
  <fuente>" + "Contenido distinto al vigente". Refrescadas, Vacías descartadas y Vacías purgadas: un grupo
  cada una. Nombres = reparados + irrecuperables. Relaciones nuevas: un grupo por "Cartera — Líder".
- **De dónde sale.** `DesgloseCargue.cs` concentra la lógica; el motor solo lo alimenta con llamadas puntuales
  mientras recorre las filas (C#), y al final de los cruces una sola consulta saca los ejemplos que dependen
  de la BD: `TOP (10)` por grupo sobre `#Carga` / `#Relaciones` con las marcas ya calculadas y **una** pasada
  de `EXISTS` por titular contra el destino para separar "sin datos previos" de "contenido distinto"
  (materializada en `#DesgloseInsertar`).
- **Coherencia de sumas.** Invariante: la suma de los grupos de un chip es el número del chip
  (`DesgloseCargue.NumeroDelChip`), sin grupos negativos. En el **preview** se verifica y, si no cuadra,
  **lanza** (como las invariantes del motor: un conteo que no cuadra es una fila perdida en silencio);
  Insertadas y Relaciones nuevas, que salen de otra consulta, se concilian con un grupo de ajuste. En el
  **resultado** se concilia con los conteos finales de la escritura (re-marcados dentro de la transacción)
  y nunca lanza, porque los datos ya están escritos: si la base cambió entre preparar y escribir aparece el
  grupo "Diferencia entre la preparación y la escritura".
- **Topes de memoria.** 10 ejemplos por grupo (`DesgloseCargue.MaxEjemplos`) y 200 grupos por chip
  (`CatalogoCargueFuentes.MaxMensajesAgrupados`); lo que pase de 200 va a "Otros motivos" con su conteo exacto
  y el motivo original en el ejemplo. Nunca guarda filas: con un millón pesa lo mismo que con cien.
- **Front**: componente `DesgloseChips` reutilizable; las listas de errores y advertencias pasaron a ser los
  chips Excluidas / Advertencias (abiertos al inicio).

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

Común a los 21 SP: sin transacción ni conteos; staging nunca vaciado. En los 19 que crean titulares y
relaciones (todos menos DatosClaro y CamaraEmpresa): nombres sin limpiar; `Titular_Cartera` sin validar el
par en `Lider_Cartera` ni su `Activo`; `INSERT Titular SELECT DISTINCT TitularID, Nombre` sin validar (un
documento con dos grafías de nombre viola la PK; una fila sin nombre tumba el `INSERT` entero, error 515). Casi todos, además: dedup y limpiezas sobre **toda** la tabla final en cada carga, clave de
dedup recortada, dedup del staging con un `ORDER BY` sobre una fecha todavía NULL (fila arbitraria) y
ninguna validación de teléfono o correo más allá del largo. Los 8 que cruzan `ListaNegraCorreos` lo hacen
con `LIKE` (ver Contexto); los otros 13 no la cruzan (el motor la aplica exacta en toda columna Email).
Paridad caso por caso en `docs/sql/legacy/PARIDAD*.md`.

| Fuente | Defectos del SP que el motor no reproduce |
|---|---|
| Sura | Dedup del staging `ORDER BY FechaConsulta DESC` cuando esa fecha aún no estaba puesta (ganador arbitrario); flag que trataba `''` como vacío mientras el INSERT lo guardaba; dedup en destino sin `UltimaFechaConsulta` en la clave |
| Cajacopi | `INSERT INTO Cajacopi SELECT …` posicional; flag calculado **antes** de limpiar (un titular con solo un fijo `1111111` quedaba en 1 sin datos); prefijo por dos `LEFT JOIN` con texto exacto y tildes (`BOGOTÁ` ≠ `BOGOTA`; un municipio repetido multiplicaba filas); fijo de 7 dígitos sin prefijo anulado sin aviso; **no** cruzaba `ListaNegraCorreos` (el único email que anulaba era el que contenía `NOTIEN`): el cargue nuevo sí la aplica, como en el resto de fuentes |
| MutualSer | Concatenaba `fijo + ', ' + celular` en un `varchar(10)` (truncaba el fijo); orden de reubicación que perdía el 60x cuando la columna fijo traía un celular; correo en lista negra a `''` |
| NuevaEps | JOIN varchar/bigint con conversión implícita (no sargable, frágil con ceros a la izquierda; ahora la conversión va del lado de `#Carga` y usa el índice); clave recortada a teléfono/estado/tipo; teléfonos de 7 dígitos guardados como celular |
| SIMIT | Dedup por titular **antes** de despivotar (se perdían las placas de la segunda fila del titular); `''` y el relleno del `nchar(10)` de Placa15 contaban como placa; `ABC123`, `abc 123` y `ABC-123` eran placas distintas; placa > 10 rompía el INSERT; marcas de texto guardadas como placa |
| DatosClaro | Cruce contra `ListaNegraTelefonosAuxiliar`; teléfono sin validar; `ISNULL(x, '')` igualaba NULL con `''`; dedup del paso 7 sobre toda la tabla (3,5M filas) en cada carga; titular inexistente entraba en silencio (sigue entrando, ahora con advertencia) |
| **Fase 2** | |
| Positiva | Concatenaba `fijo + ', ' + celular` en `NumeroTelefonoFijo varchar(30)` (522 filas legacy con dos números); correo vetado por `LIKE` y anulado a `''`, que después contaba como dato en el flag; teléfonos sin validar (`'0'` guardado como celular: 30.056 filas); `INSERT` posicional; dedup global con clave recortada; purga de vacías con `AND` donde iba `OR` (exigía celular **y** fijo **y** email) |
| SaludTotal | Sin validación de teléfono ni correo y sin `ListaNegraCorreos`; `INSERT` posicional; dedup global con clave teléfono/email/NIT (un cambio de Cargo o Empresa borraba la fila anterior); vacías nunca purgadas ni descartadas; solo anulaba `IngresoBase = ''`, no `'0'` |
| SOS | `Telefono` sin validar en una columna que la BaseMadre expone como celular (625 fijos de 7 dígitos y 34 60x legacy); `INSERT` posicional; `Direccion` de la Auxiliar `varchar(150)` → destino `varchar(70)`: una dirección larga tumbaba el `INSERT` de todo el archivo; dedup sobre toda la tabla; vacías nunca purgadas |
| Coosalud | Correo por `LIKE`; teléfono validado solo por largo (letras y fijos 60x pasaban); clave recortada con `ISNULL(x, '')` (teléfono, email, tipo: un municipio nuevo con el mismo contacto se descartaba); no refrescaba la fecha del contenido existente; dedup del staging `ORDER BY TitularID`; vacías nunca descartadas ni purgadas |
| SaviaSalud | Partía y volvía a unir con coma: listas `'3147630194,3148795401'` en `NumeroCelular` (161 filas legacy); flag que contaba el correo, que el destino no guarda (2.051 filas con `Estado = 1` y sin teléfonos); `LEN = 10` dejaba pasar texto (`'COPACABANA'` como teléfono); correo por `LIKE`; clave recortada sin refresco de fecha; `EstadoAfiliacion`/`TipoAfiliado` de la Auxiliar (70) más largos que el destino (50): un valor largo tumbaba el `INSERT` de todo el archivo |
| MiVacuna | Correo por `LIKE` y anulado a `''` (1.118 filas legacy; contaba como dato en el flag); `Titular_Cartera` con `INNER JOIN Lider_Cartera` solo por `LiderID` (bastaba que el líder tuviera alguna cartera); dedup global con clave recortada (un cambio de `FechaExpedicion` borraba la fila anterior); purga de vacías sobre toda la tabla |
| Fosyga | Fila sin nombre → falla el `INSERT` a `Titular` entero y, por FK, el de `Fosyga` (es la carga del 2026-09-23 que quedó en `FosygaAuxiliar`); dedup arbitrario del staging; `''` contaba para el flag; `INSERT` posicional; `EPS varchar(200)` → `varchar(120)` tumbaba el `INSERT`; dedup global con clave recortada (un cambio de Municipio o de fechas borraba la fila vieja); la regla de fallecido con `=` no casaba el Estado con salto de línea (69 filas legacy) |
| RGM | `Titular_Cartera` con `LiderID = CarteraID` (viola la FK, o asigna el titular al líder equivocado en las carteras 4 y 22); `Titular` sin validar; dedup arbitrario; `INSERT` posicional con `Modelo varchar → int` (un `'ABC'` aborta el batch, error 245); `Placa` truncada a 6 en el staging (`'[object Object]'` → `'[objec'`); dedup global |
| TigoUne | Correo por `LIKE`; teléfono validado solo por largo (pasaban fijos y `'555…'`); email `''` guardado como dato (27.166 filas legacy con `''`); anti-join que no refrescaba la fecha del contenido idéntico; `Titular` sin validar (una fila sin nombre tumbaba el `INSERT` y dejaba filas huérfanas en `TigoUne`, que no tiene FK); dedup arbitrario del staging; sin purga de vacías |
| Tigo | Sin SP: nada que copiar. Se llenó a mano desde `TigoAuxiliar`, que no traía Nombre; 2.579 filas legacy con flag 1 y sin datos (no se tocan) |
| RUES | Sin purga ni descarte de vacías (809.306 de 873.188 filas tienen flag 0); matrícula `0` guardada como dato con flag 1; dedup del staging `ORDER BY FechaConsulta` sobre NULL; sello `GETDATE()` con milisegundos (el motor lo trunca al segundo); dedup global |
| Camara | No validaba teléfono ni correo ni cruzaba `ListaNegraCorreos`; indicativo por `UPPER(Departamento)` exacto con tildes (`'BOGOTA D.C.'` sin tilde o un departamento del exterior dejaban 7 dígitos: 3.189 filas hoy); textos del formulario del proveedor como dato (`'Seleccione un departamento'`, `'Cargando...'`); flag calculado antes de limpiar; `INSERT` posicional; dedup global por titular + teléfono + email (una mudanza borraba la fila vieja) |
| CamaraEmpresa | `'NIT-DV'` no cabía en `NitEmpresa bigint` (el import fallaba y el SP nunca veía la fila); indicativo por departamento exacto con tildes, y 7 dígitos guardados si no casaba; no validaba teléfono ni correo ni cruzaba la lista negra; dedup global |
| PositivaEmpresas | `INSERT` posicional; dedup global por titular + NIT + empresa (un cambio de estado de afiliación borraba la fila vieja); razón social con saltos de línea del PDF (94.096 filas legacy; el motor los colapsa); NIT `0` guardado como dato con flag 1 |
| ARLSura | `INSERT` posicional; dedup global por titular + NIT (un cambio de razón social borraba la fila vieja); NIT `0` como dato; el SP nombra la misma tabla con tres grafías (`ARLSura`, `ArlSura`, `ArlSuraAuxiliar`) |
| Compensados | `INSERT` posicional; `UltimoPeriodoCompensado varchar(50)` convertido a `date` en el `INSERT` (un período que no es fecha, `'AGOSTO'`, tumbaba el archivo entero, error 241); marca del proveedor `'NO SE ENCUENTRA'` guardada como `TipoAfiliado` (594 filas, que el Calificador cuenta como "encontrado"); flag que daba 1 a filas sin datos por el Estado del archivo; dedup global |

## Consecuencias

- El auxiliar sube un CSV/XLSX, ve el avance por fase, puede irse y volver, revisa un preview con errores,
  advertencias e info agrupados y una muestra, y confirma. Límite de subida 120 MB; XLSX hasta 250.000
  filas (ClosedXML materializa el libro; por encima se pide CSV, que se lee por streaming sin tope).
- Agregar una fuente es una definición + (a veces) una regla + tests: el front no cambia. Las 16 de la
  Fase 2 lo confirmaron: solo hicieron falta las cuatro extensiones genéricas de "Fase 2"
  (`ColumnaClaveTitular`, `NormalizarClave`, `ConsultarPrefijos` con `PrefijosDepartamento`, sello
  `datetime`); `FlagDesdeArchivo` no. Procedimiento en `/agregar-fuente-datos`, paso 5f.
- Todas las fuentes de localización se cargan por el módulo. Las 21 `<Fuente>Auxiliar` con SP y
  `TigoAuxiliar` quedan sin uso. Respaldo íntegro en `docs/sql/legacy/` y DROP con guardas en
  `docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql` (6) y `2026-09-26_drop_auxiliares_fase2.sql`
  (15 + `TigoAuxiliar`), **a ejecutar solo después de validar el cargue en producción**. El de la Fase 2
  hoy se bloquearía: `FosygaAuxiliar`, `RGMAuxiliar` y `TigoAuxiliar` tienen filas (ver su cabecera).
- Los datos que ya estaban en las tablas no se re-limpian (excepto SIMIT, por su script aparte): el
  motor solo decide sobre lo que carga. Consecuencia en la Fase 2: donde el legacy guardó `''` o `'0'`
  (Positiva, Coosalud, MiVacuna, TigoUne…) la primera recarga de un titular inserta una fila nueva en vez
  de refrescar (S1 de `PARIDAD-salud.md`); sin pérdida, las vistas toman la más reciente.

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
- **Columna `Entero` contra un destino `int`** (Fase 2, H1 de `PARIDAD-otras.md`): `#Carga` la lleva como
  `bigint`; un número enorme haría fallar el `INSERT` de todo el cargue (dentro de la transacción: se
  revierte, pero sin decir qué fila). Hoy solo `RGM.Modelo` es `int`, y lo cubre `ReglaRgm`.
- **NIT con dígito de verificación** en columnas `NIT` enteras (Sura, SaludTotal, PositivaEmpresas,
  ARLSura, SOS `NitEmpresa`): 'NIT-DV' se anula con la advertencia genérica "valor no numérico" (H4 de
  `PARIDAD-empresas.md`). Solo CamaraEmpresa, cuya clave es el NIT, le quita el DV.
- **Flujo viejo en paralelo:** alguien carga Fosyga a mano con el SP esta semana (2026-09-24). Avisarle
  antes de desplegar.

## Verificación

- Fase 1: 237 tests unitarios (tras la corrección de placas de SIMIT, commit d4e0a17) en `tests/Zentinela.Infrastructure.Tests` (helpers de Shared, `GeneradorSqlCargue`,
  una batería por regla y coherencia del catálogo: toda fuente habilitada tiene regla, alias ya
  normalizados, largos > 0).
- Harness `tools/CargueHarness/` contra la BD real, con TitularIDs de un rango reservado
  (`999000000001..999000000999`, 100 IDs por fuente): por fuente, validar → simular (rollback, tabla
  intacta) → cargar → **segunda carga del mismo archivo = 0 insertadas, N refrescadas y `COUNT(*)` igual**,
  invariante de conteos, casos de cada regla, y limpieza total del rango. Última corrida de la Fase 1
  (antes de los grupos): 361 OK, 0 fallas.
- Los joins de NuevaEps (conversión del lado de `#Carga`) verificados con `SHOWPLAN` (Index Seek sobre
  `IX_NuevaEps_TitularID_FechaConsulta`).
- Paridad con los SP viejos, hecha el 2026-09-24 para las 21 fuentes con SP (cuerpo del SP como batch
  sobre `#temp`, ningún SP ejecutado; ver `docs/sql/legacy/README.md`): Fase 1, 119 casos y 1 diferencia
  "sin explicar" que era solo de documentación (C10); Fase 2, 264 casos y 0 sin explicar. Ninguna
  pérdida ni corrupción de datos atribuible al motor.
- Fase 2: harness por grupos (salud 321 OK, empresas 330 OK, otras TODO OK, 0 fallas) con doble carga = 0
  insertadas en cada fuente; `CatalogoCoherencia*Tests` comparan cada definición con el destino real.
- Script de reversión: lista blanca de las 22 fuentes comprobada contra `ColumnasDato` del catálogo
  (mismo orden), compilado con `SET NOEXEC ON` junto con las sentencias dinámicas de las 22, y la
  expresión de hash ejecutada (solo lectura) sobre cada destino.
