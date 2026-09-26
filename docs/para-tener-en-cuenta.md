# Para tener en cuenta

Bitácora de revisión general del proyecto. Se trata como **inbox**, no como
backlog: los items se cierran o se descartan con razón explícita.

Última actualización: **2026-09-26**.

---

## PENDIENTE: script de Informes de Localización (ADR-040) — re-ejecutar por la etapa 4

- **Aplicado en producción el 2026-09-26 (versión de la etapa 1); falta re-ejecutarlo por la etapa 4 (abajo).** Se ejecuta con `docs/sql/migrations/2026-09-26_informes_localizacion.sql` en DBCallCenter
  (el clasificador bloquea las escrituras de DDL desde los agentes):
  `sqlcmd -S 10.0.0.89,1433 -U <usuario> -P <clave> -d DBCallCenter -C -b -f 65001 -i docs/sql/migrations/2026-09-26_informes_localizacion.sql`.
  Idempotente; crea `LocalizacionCorte`, `LocalizacionResumenFuente`, `LocalizacionResumenFuenteLider`,
  `LocalizacionResumenPar`, `LocalizacionResumenLider` y, ya para la etapa 3, `LocalizacionEnvio` y
  `LocalizacionEnvioDetalle`. Validado con `SET NOEXEC ON` y creando copias `#temp` de las tablas.
- **Mientras no se aplique**, `/api/informes/localizacion/*` responde **503** con el nombre del script y la
  página muestra "El módulo todavía no está instalado". El arranque de la API no falla (solo deja un aviso
  en el log).
- **Después de aplicarlo**, el primer corte se calcula solo (~1 min): al arrancar la API (30 s después) o
  al primer acceso a la página. Luego se recalcula tras cada cargue OK del Cargue de Fuentes, al primer
  acceso después de las 13:00 y con "Recalcular" (Admin).
- Una API de desarrollo contra la misma BD también calcula cortes al arrancar; poner
  `InformesLocalizacion:CalcularAlArrancar = false` en su appsettings si no se quiere. Dos procesos no
  calculan a la vez (se miran los cortes `EN CURSO`).
- **Las cifras pueden diferir del Calificador en unos pocos titulares** por par y fuente: los que tienen
  dos filas con la misma fecha en la fuente. El Calificador elige una al azar; los Informes, la que tiene
  dato. Medido y explicado en ADR-040.
- **Carteras sin saldo:** 16 carteras activas no tienen ningún `UltimoEstadoTitular.SaldoTotal > 0` (las Delmar,
  LATIN, Mil Ambientaria, Interenlace, Prima; o sin filas en UET, como COBRANO y las externas; detalle en
  ADR-040). "Sin contacto con saldo" sale "—" ahí. Si ese saldo debería existir, revisar el
  ETL de gestión.
- **Sugerencias (etapa 2) no necesitan el script:** se calculan en línea. Sin las tablas `LocalizacionEnvio*`
  no excluyen a los ya enviados y lo avisan. En las carteras sin saldo (Delmar, COBRANO…) "Solo con saldo"
  deja 0 candidatos: la pantalla avisa y hay que desmarcarlo. Mientras corre el ETL de gestión responden
  409. **Pendiente la prueba a mano:** exportar el Excel de Fosyga, llenarlo y subirlo en Cargue de Fuentes
  (los tests ya pasan el archivo por el lector de columnas del cargue).
- **Envíos (etapa 3) sí necesitan el script** (`LocalizacionEnvio*`; sin él, la pestaña Envíos y los rankings de
  Uso y Retorno avisan que falta). El script no cambió con la etapa 3. Probado solo contra tablas `#temp` (ADR-040
  → "Envíos: pruebas contra tablas temporales"). **Pendiente la prueba a mano:** crear un lote desde Sugerencias,
  enviarlo, subir el retorno en Cargue de Fuentes y ver retorno e impacto; con un Líder, confirmar que no ve lotes
  de otros. El retorno es el **primer cargue OK de la fuente después del envío**: si se suben archivos de la
  misma fuente que no son la respuesta del lote, cuentan igual (el vínculo explícito llega en la etapa 4).
- **Etapa 4 (2026-09-26): volver a correr el script.** El coordinador aplicó en producción la versión de la etapa 1
  (las 7 tablas existen, vacías). La etapa 4 **agregó `LocalizacionResumenAsesor`** al mismo script (bloque
  `IF OBJECT_ID … IS NULL`, idempotente): re-ejecutarlo la crea sin tocar lo demás. Mientras falte, el corte se
  calcula igual y la pestaña Asesores avisa. Todo lo de la etapa 4 se probó solo contra tablas `#temp` (ADR-040 →
  "Etapa 4: pruebas contra tablas temporales").
- **Recálculo programado de las 13:05** (`LocalizacionRecalculoProgramado`, un `BackgroundService` que se registra
  solo): cada día a las 13:05 **hora de Colombia** (`InformesLocalizacion:ZonaHoraria`, no el reloj del equipo; en el
  equipo de desarrollo son las 20:05) pide un corte y espera; si ya hay uno posterior a las 13:00 no hace nada; con el
  ETL de gestión en curso o un fallo reintenta cada 10 min (12 veces). Necesita el proceso vivo a esa hora: AppPool
  con `AlwaysRunning` e `Idle Time-out = 0` (docs/despliegue.md). Una API de desarrollo contra la misma BD también lo
  corre: `InformesLocalizacion:RecalculoProgramado:Activado = false` en su appsettings si no se quiere (dos procesos
  no calculan a la vez: el segundo ve el `EN CURSO` y luego el corte hecho).
- **`FechaCorte` ahora es hora de Colombia** en cualquier equipo (antes, el reloj de la API): la regla de las 13:00 y
  el banner "Localización al …" coinciden con el reloj de la BD. En el servidor no cambia nada.
- **Vínculo lote ↔ cargue de retorno:** tras cada cargue OK, los lotes ENVIADOS de esa fuente cuyos titulares vienen
  en el archivo quedan con `CargueEjecucionID`/`FechaRetorno` (primer cargue que los trae; nunca uno del harness).
  No cierra lotes solo salvo que se configure `InformesLocalizacion:VinculoRetorno:CerrarAlCubrirPct`. Un titular que
  llega "vacío" y ya tenía datos no deja fila en `CargueFuenteReversion` (política de vacías): la cobertura es un
  piso. **Pendiente la prueba a mano:** enviar un lote, cargar el archivo de retorno y ver el vínculo en Envíos.
- **Alertas de la semana:** solo se muestran en el Resumen, no se envían. Con el módulo recién instalado habrá muchas
  "Cola sin enviar" (nadie ha enviado lotes todavía) y "Fuente sin datos nuevos" para las fuentes que siguen en SP
  legacy sin cargas recientes (medido: 5 Funcionales, p. ej. MiVacuna con último dato del 22/05/2026).
- **Sugerencias y `UltimoEstadoTitular`:** el batch busca por la PK con `INDEX(1), FORCESEEK`. Si algún día
  cambia la PK de la tabla, revisar `GeneradorSqlSugerencias` (sin la sugerencia, con estadísticas viejas, un
  par mediano tardó 63 s).

## Cargue de Fuentes de Datos — lo que hay que saber (2026-09-24, ADR-039)

- **Caché del Calificador.** El cargue llama `ICalificadorCarteraCache.InvalidarTodo()` al terminar OK
  (cachea hasta 24 h y solo se limpia sola al corte de la 1 p. m.). **No** se invalida si los datos
  cambian por otro camino: `revertir_cargue_fuente.sql`, un `sp_Procesar*Auxiliar` de la Fase 2 o un
  UPDATE a mano → reciclar el AppPool o esperar la 1 p. m. El harness corre en otro proceso: tampoco
  limpia la caché de la API.
- **NuevaEps guarda `TitularID` como varchar(30).** El motor valida el documento como número y escribe
  `CONVERT(varchar(30), bigint)` (sin ceros a la izquierda); los joins convierten del lado de `#Carga`
  para usar el índice. Una fila legacy con ceros a la izquierda o espacios no matchearía y se insertaría
  como nueva (hoy hay 0). Sus ~3.000 teléfonos de 7 dígitos ya no entran como celular; los existentes
  quedan.
- **SIMIT: placas.** Placa válida = sin espacios ni guiones, mayúsculas, 5–7 caracteres A-Z/0-9 con letra y
  dígito. Marcas (`PAZ Y SALVO`, `NO CARGO`, `SIN`…) = "sin placa". El legacy lo corrigió
  `docs/sql/migrations/2026-09-24_simit_placas_legacy.sql`, **ejecutado en producción el 2026-09-25**
  (sesión de las 03:41, hora del servidor): 7 placas pasadas a canónicas; 5.870 marcas a "sin placa"
  (5.718 `PAZ Y SALV` con estado 0 + 152 con estado 1); borradas 75 filas vacías de titulares con placas,
  5.727 "sin placa" repetidas y 1 placa repetida; 0 placas no canónicas restantes; `dbo.SIMIT` 301.840 → 296.037 filas.
  Coincidió exactamente con la prueba con `ROLLBACK`. Respaldo en `dbo.SIMIT_Respaldo_20260924` (11.804
  filas: todas las de los 5.873 titulares tocados, antes del cambio). **Cómo revertir:** script comentado
  al final de la migración, por titular (borrar sus filas actuales y reinsertar las del respaldo); descarta
  lo que un cargue SIMIT posterior les haya hecho.
- **Clave de contenido con collation `Modern_Spanish_CI_AS`.** El refresco compara con `INTERSECT`, que usa
  la collation: un cambio **solo de mayúsculas** (o de espacios finales) cuenta como contenido idéntico,
  se refresca la fecha y el texto viejo se conserva. Un cambio de tilde sí es contenido nuevo.
- **Duplicados legacy en destino.** Los SP deduplicaban tarde y las tablas son heaps sin PK: una fila del
  archivo puede refrescar varias filas idénticas. Por eso `Refrescadas` se cuenta del lado de `#Carga` (no
  con `@@ROWCOUNT`) y `CargueFuenteReversion` puede tener más `U` que `Refrescadas`. No es un error.
- **XLSX se abre dos veces por pasada** (contar filas y leerlas, ClosedXML carga el libro entero cada vez)
  y confirmar repite las dos: cuatro aperturas por cargue. De ahí el tope de 250.000 filas; para archivos
  grandes, **CSV**. Un CSV "ANSI" cuyos primeros 64 KB son ASCII puro se detecta como UTF-8 y una Ñ
  posterior llega rota (`PI�A`): guardarlo como "CSV UTF-8".
- **Estado `Interrumpido`.** No es un valor de `EstadoCargueJob`: solo aparece en `GET jobs` para una
  ejecución que quedó `EN CURSO` sin job en memoria (el proceso murió). En BD se cierra como `ERROR` con
  `Error = 'Interrumpida (reinicio del servidor)'`. No escribió nada (la transacción se revirtió). El front
  lo trata aparte (ámbar).
- **No cargar con el ETL de Estado Titular corriendo** (ADR-038): contención de log (`WRITELOG`), el cargue
  se queda quieto hasta que el ETL termina. Medido el 2026-09-24.
- **Un job activo por fuente y 2 en total** (409). Dos cargues de la misma tabla compartirían sello y se
  pisarían la política de vacías.

## Cargue de Fuentes de Datos, Fase 2 — lo que hay que saber (2026-09-24, ADR-039)

Las 22 fuentes de localización ya se cargan por el módulo. Lo nuevo de la Fase 2:

- **AVISAR ANTES DE DESPLEGAR: alguien carga Fosyga a mano esta semana** con `FosygaAuxiliar` +
  `sp_ProcesarFosygaAuxiliar` (el SP se modificó el 2026-09-22 y hubo cargas el 22 y el 23). Hay que
  decirle que pase al módulo. Además `FosygaAuxiliar` guarda una carga **fallida** del 2026-09-23 (18.598
  filas; ~18.053 no están en `Fosyga`): el SP no pudo con filas sin Nombre ni cartera/líder. Si esos datos
  se quieren, se recargan por el módulo con un archivo que traiga el nombre (H4 de `PARIDAD-otras.md`).
- **S1 — `''`/`'0'` legacy vs NULL: la primera recarga crea una fila nueva en vez de refrescar.** El motor
  guarda NULL donde los SP dejaban `''` o `'0'`, y la igualdad de contenido (`INTERSECT`) distingue `''`
  de NULL. Sin pérdida (las vistas toman la más reciente, que es la limpia), pero la tabla crece una fila
  por titular afectado. Volumen legacy medido el 2026-09-24: **Positiva 140.758 correos `''` y 30.056
  celulares `'0'`**, **Coosalud 32.698 correos `''`**, **MiVacuna 1.118 correos `''`**, SaludTotal 14 y
  SOS 6 teléfonos, TigoUne 27.166 filas con `''` en teléfono o email. **Resuelto:** el script (commit
  199d99b) se ejecutó en producción el 2026-09-25: ver "HECHO: normalizar vacíos legacy" abajo.
- **Ejecuciones de prueba ocultas** (commit cb20c62, `UsuariosPruebaCargue`). La lista "Cargues en curso y
  recientes" no muestra las ejecuciones de cuentas `harness-cargue*`: el harness escribe **temporalmente en
  la BD de producción** (`CargueFuenteEjecucion`, que borra al limpiar). El filtro va en el SQL de
  `ListarAsync`, y eso además evita que abrir la lista mientras el harness corre cierre su ejecución
  `EN CURSO` como `ERROR` "Interrumpida (reinicio del servidor)" (no tiene job en la memoria de la API).
  **Un grupo nuevo del harness debe usar ese prefijo** (`harness-cargue-<grupo>`) o sus corridas aparecerán
  en la lista y se cerrarán como interrumpidas. Para ver lo del harness: consultar la tabla directamente.
- **Selector de fuente con búsqueda.** Es el único `ZDropdown` con `buscable` (escribir filtra por nombre y
  clave, sin tildes ni mayúsculas); las demás pantallas no cambian. Las columnas esperadas ya no salen en
  cada opción, solo en su panel del paso 2.
- **Tigo exige la columna Nombre.** `dbo.Tigo` tiene FK a `Titular`, así que el cargue crea titulares y
  el archivo debe traer Nombre. El formato viejo de `TigoAuxiliar` (sin Nombre) **se rechaza** con
  "Faltan columnas obligatorias: Nombre". Tigo no tenía SP: se llenó a mano en 2025 y es la primera vez que
  se puede actualizar.
- **Teléfono como celular.** SOS (`Telefono`), SaludTotal, Coosalud, TigoUne y Tigo (`NumeroTelefono`) y
  MiVacuna (`Celular`) se validan como **celular** (10 dígitos, empieza en 3), porque la BaseMadre los
  expone como `[Celular <Fuente>]` y alimentan el selector de celulares: un fijo o un número de 7 dígitos
  se anula con advertencia. **Los fijos legacy no se tocan** (SOS tiene 625 de 7 dígitos y 34 60x; Tigo
  1.101 fijos). Camara, CamaraEmpresa y `SaviaSalud.NumeroTelefono` sí aceptan fijo (tipo `Telefono`).
- **CamaraEmpresa se identifica por NIT, no por titular.** Clave `NitEmpresa` (`ColumnaClaveTitular`): el
  archivo trae el NIT de la empresa, no crea `Titular` ni `Titular_Cartera`, y la reversión la ubica por NIT
  (guardado como texto en `CargueFuenteReversion.TitularID`). 'NIT-DV' se acepta y se le quita el DV.
- **H4 — NIT-DV en columnas NIT enteras se anula.** En las columnas de dato `NIT` de tipo `Entero` (Sura,
  SaludTotal, PositivaEmpresas, ARLSura; `NitEmpresa` de SOS) un '900123456-7' se anula con la advertencia
  genérica "valor no numérico anulado": no se pierde en silencio, pero se pierde. Solo la clave de
  CamaraEmpresa le quita el DV. Si los proveedores traen DV, conviene un tipo `Nit` genérico.
- **RUES tiene sello con hora.** `RUES.FechaConsulta` es `datetime` (la única): el sello va truncado al
  segundo. Quien compare esa columna con una fecha debe usar rangos o convertir.
- **ARLSura ≠ Sura.** Dos fuentes, dos tablas; el flag de las dos se llama `EstadoEnSura`.
- **Compensados: el Estado del archivo es "encontrado sin datos"** (confirmado por el usuario, H2 de
  `PARIDAD-empresas.md`). `EstadoEnCompensados = 1` con EPS o período, **o** con Estado `1`/`SI`/`ENCONTRADO`;
  `0`, `NO` o 'NO SE ENCUENTRA' no lo encienden (el SP viejo sí, con cualquier valor). Estado no se guarda.
  Por eso en Compensados puede haber filas con flag 1 y todo NULL: son legítimas, no vacías.
- **Columna `Entero` contra destino `int`** (H1 de `PARIDAD-otras.md`): `#Carga` la lleva como `bigint` y un
  número enorme haría fallar el cargue entero (con rollback, sin decir la fila). Hoy solo `RGM.Modelo`, que
  `ReglaRgm` valida como año.

## HECHO: normalizar vacíos legacy (ADR-039, commit 199d99b; ejecutado el 2026-09-25)

- `docs/sql/migrations/2026-09-25_normalizar_vacios_legacy.sql` **se ejecutó en producción el 2026-09-25**
  (sesión de las 03:41, hora del servidor, Colombia), para todas las fuentes (`@Solo = NULL`): 18 min,
  **19 tablas OK, 0 errores**, número de filas intacto en todas, 0 centinelas pendientes, 0 flags 0 → 1. Pasó a NULL los centinelas legacy (`''`, `'0'`, `'NULL'`, `'N/A'`, blancos,
  NIT `0`, fechas <= 1900) y recortó/colapsó blancos con el mismo criterio del motor, para que la primera
  recarga refresque en vez de insertar una fila por titular afectado (S1).
- **500.591 filas en 19 tablas, igual a lo medido el 2026-09-24:** Positiva 170.455, PositivaEmpresas
  102.661, Coosalud 101.539, SaludTotal 37.297, SaviaSalud 31.804, TigoUne 27.300, ARLSura 18.043,
  MutualSer 8.078, Fosyga 1.681, MiVacuna 1.118, Cajacopi 204, SOS 129, Camara 103, Tigo 63,
  CamaraEmpresa 40, DatosClaro 27, RUES 24, NuevaEps 18, RGM 7. Sura y Compensados: nada. SIMIT fue por su
  propio script (ver arriba).
- **Flag 1 → 0** (filas / titulares que se quedaron sin ninguna fila con flag 1): Positiva 29.882 / 28.683,
  MiVacuna 269 / 269, Camara 69 / 57, CamaraEmpresa 13 / 6, ARLSura 1 / 1, MutualSer 1 / 1. Cambian
  `[Estado <Fuente>]` de la BaseMadre y las vistas `vw_Datos*` / `vw_SinDatos*` (pasan a "sin datos"). El
  **panel de localización del Calificador casi no cambia** (ya trataba `''` y `'0'` como vacíos): solo 16
  titulares de Positiva con fecha de nacimiento <= 1900. **No recalculó el flag de Compensados**.
- **Caché del Calificador:** el script no la invalida (cachea hasta 24 h); los cambios de ambos scripts se
  ven tras el corte de la 1 p. m. o un reciclaje del AppPool.
- **Fosyga ya tenía su primer cargue real con el módulo** (2026-09-24, usuario `usuario.cargue`, OK; la
  tabla tenía 2.716.121 filas al normalizar). Revertir ese cargue por `CargueFuenteReversion` **no
  restaurará las filas que refrescó y que luego se normalizaron**: su contenido cambió y el hash guardado
  ya no coincide.
- **Respaldos:** las filas tocadas, tal como estaban y con su RID, en las 19 tablas
  `dbo.<Tabla>_RespaldoVacios_20260925` (ver `docs/schema.md`, "Respaldos temporales del Cargue de
  Fuentes"). **Cómo revertir:** por tabla, con el script comentado al final de la migración (devuelve cada
  fila a su RID desde el respaldo). Ojo: si ya corrió un cargue de esa fuente (Fosyga, por lo menos),
  revertir devuelve los centinelas a filas que ese cargue refrescó. Los respaldos se pueden borrar tras
  validar, p. ej. junto con el DROP de las Auxiliar (~2026-10-25).

## DOS documentos ADR-039 (2026-09-24)

- `docs/decisions/ADR-039-cargue-fuentes-motor-generico-historial-acumulativo.md` (Cargue de Fuentes, en
  `main` desde el 2026-09-24) y `docs/decisions/ADR-039-cargador-estado-titular-sin-ssis.md` (cargador de
  `UltimoEstadoTitular` sin SSIS, de otra sesión, posterior) comparten número. **No renombrar ninguno**: los
  commits, el código y los demás documentos citan "ADR-039" para los dos. Al citarlos, dar el nombre del
  archivo o el tema; el próximo ADR nuevo es el 040.

## PENDIENTE: DROP de las 6 Auxiliar de la Fase 1 (ADR-039)

- `docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql`, **a mano y solo después de validar el
  cargue en producción** (al menos un cargue real por fuente). Guardas: 0 filas, definición idéntica al
  respaldo de `docs/sql/legacy/` (hash), sin dependencias. `ARLSuraAuxiliar` no entra (va en la Fase 2).
- La comparación de cada fuente contra su SP viejo ya se hizo (`docs/sql/legacy/PARIDAD.md`, 2026-09-24).

## PENDIENTE: DROP de las 16 Auxiliar de la Fase 2 (ADR-039)

- `docs/sql/migrations/2026-09-26_drop_auxiliares_fase2.sql`: 15 `<Fuente>Auxiliar` + sus SP y
  `TigoAuxiliar` (sin SP). **A mano y solo después de validar el cargue en producción.** Mismas guardas
  que la Fase 1; la de dependencias excluye todos los SP que se borran juntos (`sp_ProcesarSaludTotalAuxiliar`
  nombra `RUESAuxiliar` en un comentario).
- **Hoy la guarda de filas lo bloquea**: `FosygaAuxiliar` (18.598, carga fallida: hablar con quien carga
  Fosyga), `RGMAuxiliar` (30.144, residuo ya contenido en RGM) y `TigoAuxiliar` (661.072, residuo ya en
  Tigo; 67 variantes con otro teléfono/correo se perderían). Vaciarlas es decisión del usuario, con
  confirmación explícita; la cabecera del script trae las consultas para comprobarlo de nuevo.
- No entran (y por qué, en la cabecera): `TitularAuxiliar`, `Titular_CarteraAuxiliar`,
  `ListaNegraTelefonosAuxiliar`, `DatosExternosAuxiliar`, `RuntAuxiliar` y los `Staging_*`.
- Paridad hecha: 264 casos en 15 fuentes, 0 sin explicar (`docs/sql/legacy/README.md`).

---

## Los enums de la API viajan como TEXTO, no como número (2026-09-18)

- `Program.cs` registra `JsonStringEnumConverter`: **todo enum llega al front como string**
  (`"Disponible"`, no `0`). Un tipo TS `0 | 1 | 2` compila pero falla en runtime.
- Costó una pantalla en gris en el paso 3 del Creador de Virtualidad: el lookup del estilo daba
  `undefined` y React tumbaba el render. Con el catálogo vacío no había tarjetas y parecía funcionar.
- **Regla:** al tipar en TS un enum del backend, usar la unión de literales de texto y dejar un
  fallback para valores desconocidos.

## Agregar columnas a tablas del sistema de cobranza: se puede, con verificación (2026-09-18)

- `dbo.Agente` recibió `NumeroCorporativo`, `LinkWhatsapp`, `ContactoActualizadoEn/Por` en vez de
  crear una tabla 1:1 aparte. Precedente: `Lider_Cartera.EsExterna/Activo`.
- **Lo que hay que comprobar antes**: que ningún sp/fn/vista/trigger/job inserte en la tabla, que
  ninguna otra BD la referencie, y que los `INSERT` propios nombren columnas. Para `Agente` el único
  escritor resultó ser `RegistroService` por EF.
- Si algo de eso no se puede confirmar, la tabla aparte sigue siendo la opción segura.

## PENDIENTE: DROP de FechaResultado en HistoricoVirtualidad

- El código no la usa desde 2026-08-14 (era write-only) pero la columna **sigue en la BD**.
  Script listo: `docs/sql/migrations/2026-08-14_drop_fecharesultado_historicovirtualidad.sql`.
- Sin riesgo a estas alturas: el código desplegado hace semanas ya no la referencia.

---

## Tablas LlamadaPerdida / LlamadaEntrante no existen — 3 tabs de Virtualidad rotas (2026-09-17)

- **`LlamadaPerdida` y `LlamadaEntrante` no existen en ninguna BD del servidor** (verificado con
  `sp_MSforeachdb`; tampoco hay DDL en `docs/sql/`). Sus mappings apuntan a esos nombres.
- Consecuencia: **Llamadas Perdidas, Llamadas Entrantes y Pagos vs Impacto fallan para todos
  los roles** con `El nombre de objeto 'LlamadaPerdida' no es válido`, desde el commit `6a063a7`
  que creó el módulo. Nadie lo había reportado — probablemente nadie las usa.
- **Decisión pendiente del negocio:** crear las tablas (habría que escribir el DDL desde las
  entidades) o desacoplar Pagos vs Impacto de las llamadas para que al menos esa tab funcione.
- Detectado al integrar el rol Líder en Informes de Virtualidad (ADR-036).

## Corte de alcance por líder: hacerlo en el servidor, no en el dropdown (2026-09-17)

- `AlcanceLiderExtensions.TryResolverLiderID` (Zentinela.API) es el patrón: para rol `Líder`
  el `liderID` de la petición se ignora y se usa el del claim; sin claim → 403.
- **El Calificador todavía tiene el agujero**: `GET /calificador/{cartera}/{lider}` acepta
  cualquier par; solo el dropdown de opciones está acotado. Aplicar el mismo patrón.
- `navigation.ts` muestra Informes de Virtualidad a `Director` pero los controllers no lo
  admiten → 403. Decidir cuál lado está bien.

## Fuente Sura — colisión de alias que obligó a renombrar ARLSura (2026-09-02)

- **`dbo.Sura` es una fuente 1:1** (EPS/aseguradora) que expone **6 columnas**: celular
  (`NumeroCelular`), email, NIT, empresa, tipo afiliado y estado afiliación.
- **⚠ Excepción a la convención: se renombraron los alias de OTRA fuente.** `ARLSura`
  ya ocupaba `[NIT Sura]` y `[Empresa Sura]` — nombres que le corresponden a esta
  fuente, no a ella. Sin el renombre la vista falla con *"the column ... was specified
  multiple times"*. Se decidió (usuario, 2026-09-02) **liberar el nombre "Sura"** y
  pasar los de ARLSura a `[NIT ARL Sura]` / `[Empresa ARL Sura]`, propiedades C#
  `NITArlSura` / `EmpresaArlSura`. Es la primera vez que integrar una fuente **renombra
  columnas existentes**; el criterio fue que el nombre correcto pese más que la
  estabilidad del alias viejo, porque `[NIT Sura]` era engañoso desde el principio.
- **Impacto de ese renombre, asumido conscientemente:** (a) la etiqueta que ve el líder
  en el Creador pasa de `NIT Sura` a `NIT ARL Sura`; (b) **una selección de columnas
  guardada con el nombre viejo deja de resolver**; (c) en el panel de localización la
  fuente pasó de `Sura (ARL)` a `ARL Sura`, y su slug de `sura` a `arlsura`.
- **Tres colisiones internas más**, todas por el mismo motivo, corregidas al integrar:
  la clave `["sura"]` del diccionario `Fuentes` (era de ARLSura → `["arlsura"]`), los
  alias SQL `SuraEnc`/`SuraNoEnc` del result set 9 (→ `ArlSuraEnc`/`ArlSuraNoEnc`) y un
  `[Empresa Sura] AS EmpresaSura` duplicado en el `SELECT INTO #t`. **Al agregar una
  fuente cuyo nombre sea prefijo o sufijo de otra, grepear el nombre completo antes de
  escribir nada** — el compilador no atrapa una clave de diccionario repetida.
- **La tabla estaba VACÍA (0 filas)** al integrar; los datos los carga el equipo aparte.
  Marcada `Funcional` en `ESTADO_FUENTE` por decisión del usuario.
- **Localización**: predicado celular OR email OR NIT OR empresa, con
  `LEFT JOIN (SELECT DISTINCT TitularID FROM Sura)` para distinguir *no encontrado* de
  *falta por buscar* (`vw_BaseMadre` aplana NULL a `'0'` y no sirve para eso).
- **Único teléfono es `NumeroCelular`** — la tabla no tiene columna de fijo. Rastreo de
  Líneas y Uso de Base de Datos cruzan solo esa columna.
- **Migración:** `docs/sql/migrations/2026-09-02_sura_in_basemadre.sql`. Aplicada a
  producción (exit=0, 8.192 ms) y verificada contra los CUATRO objetos SQL.
- **Decisión completa en `docs/decisions/ADR-034-fuente-sura-renombra-alias-de-arlsura.md`.**

## Fuente Savia Salud — EPS con datos; NumeroTelefono vacío, TitularID nullable (2026-08-05)

- **`dbo.SaviaSalud` es la primera fuente nueva de esta tanda que llega CON datos**
  (193.752 filas, 1:1). Se expusieron **7 columnas**: celular (`NumeroCelular`),
  régimen, tipo afiliado, estado afiliación, fecha nacimiento, estado (bit), última
  consulta. Marcada `Funcional` en `ESTADO_FUENTE` (se actualiza a diario).
- **`NumeroTelefono` (varchar 10) está 100% vacío** en las 193k filas → NO se mapea
  (columna muerta). El celular real vive en **`NumeroCelular`** (varchar 100, pero
  contiene un solo móvil de 10 dígitos). El selector de celular y el Rastreo usan
  `NumeroCelular`.
- **Sin email** → NO entra al switch/selector de email ni a `FUENTES_EMAIL`.
- **`TitularID` es bigint NULLABLE** (a diferencia de las demás fuentes, NOT NULL).
  El JOIN por igualdad (`TC.TitularID = SV.TitularID`) descarta las filas con
  TitularID NULL de forma natural — no requiere manejo especial. La entidad de
  ConsultaIntegrada usa `long? TitularId`.
- **Localización**: predicado celular OR régimen OR estado afiliación (no hay
  geografía). Requiere 3 columnas en `#t`: CelSaviaSalud, RegimenSaviaSalud,
  EstadoAfiliacionSaviaSalud.
- **Etiqueta con espacio**: en el panel/CSV la fuente se llama **"Savia Salud"**
  (con espacio); slug `saviasalud`; nombre en código/tabla `SaviaSalud` (sin espacio).
- **Migración:** `docs/sql/migrations/2026-08-05_saviasalud_in_basemadre.sql`. Aplicada
  y smoke con datos reales OK (cartera 22).

## Fuente Coosalud — EPS rica tipo NuevaEps, tabla vacía al integrarse (2026-08-04)

- **`dbo.Coosalud` es una fuente 1:1 tipo EPS**, estructuralmente cercana a NuevaEps
  pero **ampliada**: además de celular, estado afiliación, tipo afiliado, fecha
  nacimiento y geografía, trae **email, régimen y nivel Sisbén**. Se exponen **11
  columnas** en `vw_BaseMadre` + los 3 iTVF (decisión del usuario: exponer todas).
- **TitularID es bigint** (como Tigo/TigoUne), NO varchar como NuevaEps — por eso en
  ConsultaIntegrada se compara directo contra `titular.NumeroDocumento` sin `.ToString()`.
- **La tabla estaba VACÍA (0 filas)** al integrar (staging `CoosaludAuxiliar` también).
  `ESTADO_FUENTE['Coosalud']` pasó a `Funcional` el 2026-08-05 al llegar datos
  (369.049 titulares).
- **Localización**: entra con predicado celular OR email OR depto OR municipio (patrón
  NuevaEps). Requiere 4 columnas en `#t` de `SqlAgregados`: CelCoosalud, EmCoosalud,
  DepartamentoCoosalud, MunicipioCoosalud.
- **Migración:** `docs/sql/migrations/2026-08-04_coosalud_in_basemadre.sql`. Aplicada
  a producción tras validación con rollback.

## Fuente TigoUne — gemela de Tigo, tabla vacía al integrarse (2026-08-04)

- **`dbo.TigoUne` es estructuralmente idéntica a `dbo.Tigo`** (TitularID bigint,
  NumeroTelefono varchar(10), Email varchar(255), EstadoEnTigoUne bit,
  FechaConsulta date). Se integró replicando el patrón de Tigo en las 4 capas:
  columnas `[Celular TigoUne]` / `[Email TigoUne]` en `vw_BaseMadre` **y en los
  tres iTVF** (`fn_BaseMadrePorCartera`, `fn_BaseMadreParaTitulares`,
  `fn_BaseMadreParaTitulares_CarteraChica`), selector celular+email, panel de
  localización, Rastreo de Líneas y ConsultaIntegrada. Cardinalidad 1:1.
- **La tabla estaba VACÍA (0 filas) al momento de integrar**, igual que su staging
  `TigoUneAuxiliar`. El cableado está completo y validado (migración ejecutada en
  transacción con rollback contra la BD real), pero el smoke con datos reales y el
  estado visual `Funcional` quedan pendientes del primer cargue. Por eso
  `ESTADO_FUENTE['TigoUne']` pasó a `Funcional` el 2026-08-05 al llegar datos
  (5.192 titulares).
- **Migración:** `docs/sql/migrations/2026-08-04_tigoune_in_basemadre.sql`.
  **Aún no aplicada a producción** al cierre de esta integración — requiere
  confirmación porque toca objetos SQL compartidos que toda consulta del
  Calificador/Creador usa.

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
