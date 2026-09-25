# ADR-040 — Informes de Localización: precálculo por cortes, mismas cifras que el Calificador

**Estado:** Aceptado (etapas 0, 1 y 2 implementadas; 3 y 4 pendientes)
**Fecha:** 2026-09-25
**Módulo afectado:** Informes → Informes de Localización (nuevo, `/informes/localizacion`). Lee las 21
fuentes de localización del panel del Calificador, `Titular_Cartera`, `Lider_Cartera`, `Cartera`, `Lider`,
`UltimoEstadoTitular` y `vw_EtlEstadoTitularEstado`. Tablas nuevas en `DBCallCenter.dbo`:
`LocalizacionCorte`, `LocalizacionResumenFuente`, `LocalizacionResumenFuenteLider`,
`LocalizacionResumenPar`, `LocalizacionResumenLider`, `LocalizacionEnvio`, `LocalizacionEnvioDetalle`
(script `docs/sql/migrations/2026-09-26_informes_localizacion.sql`, lo aplica el usuario).
**Toca además:** el Calificador (el estado de las fuentes sale del catálogo nuevo) y el Cargue de Fuentes
(pide un corte nuevo tras cada cargue OK).

---

## Contexto

El gerente de cobranza necesita ver cómo está la localización de cada líder y de cada cartera,
compararlos en rankings con fotos, saber a quién conviene mandar a localizar y medir cómo usa cada líder
al departamento de localización. Hasta ahora solo existía el panel "Porcentajes de Localización" del
Calificador: una cartera a la vez, sin historial, y con la etiqueta de "fuente funcional" escrita a mano
en el front (y desactualizada: Fosyga ya se carga por el Cargue de Fuentes).

Lo que midió la exploración:

- 22 fuentes, todas heaps con índice `(TitularID, Fecha DESC)`. Recalcular por par y fuente cuesta
  0,2–0,8 s; toda la base en un solo batch, alrededor de un minuto.
- Las fotos están en `DBRecursosHumanos.dbo.FotoEmpleado` (varbinary, hasta 2 MB), por cédula. 14 de los
  29 líderes activos tienen foto.
- No hay registro de envíos a localización.

## Decisiones

### Etapa 0 — Catálogo de fuentes y fotos

- `CatalogoFuentesLocalizacion` (Application) es la **fuente única de verdad** del estado de cada fuente
  (Funcional / Intermitente / Inactivo, lo mantiene el equipo; **Fosyga pasa a Funcional**), su grupo,
  su tabla, su columna de fecha y su **predicado de "encontrado"**, traducido 1:1 del result set 9 del
  Calificador a las columnas origen. `GET /api/fuentes-localizacion` lo publica con el último cargue OK; el
  Calificador dejó de tener su constante propia.
- Fotos: `FotoPersonaService` (SkiaSharp, recorte cuadrado, 48/96/160/320 px, JPEG 82, caché de 64 MB con
  ETag), `GET /api/personas/{documento}/foto` (solo líderes y asesores activos) y `AvatarPersona` en el front.

### Etapa 1 — Resumen, Cobertura, Frescura y Rankings

#### Cifras: las del Calificador

- **Total** = filas de `Titular_Cartera` del par cuyo titular existe en `dbo.Titular` (la BaseMadre del
  Calificador hace `INNER JOIN Titular`).
- **Encontrado** en una fuente = la **última fila** del titular (`ROW_NUMBER() … ORDER BY Fecha DESC`)
  cumple el predicado del catálogo. **No encontrado** = tiene filas pero la última no cumple. **Falta por
  buscar** = no tiene ninguna fila. La bandera `EstadoEn<Fuente>` no se usa (ver discrepancias abajo).
- **ConContacto** = celular o email en la última fila de alguna fuente = Total − `SinContacto` del
  result set 1 del Calificador. Se replicó esa definición exacta, que es la que el usuario ya conoce: 15
  columnas de celular y 11 de email de `fn_BaseMadreParaTitulares` (`PredicadoContacto` en el catálogo;
  un test compara la lista columna por columna). No es el OR de los "encontrado" del grupo Contacto:
  edad, NIT, empresa o departamento no son contacto.
- **CamaraEmpresa** queda fuera de todas las métricas por titular: es la misma información de Cámara pero
  de **empresas** (clave `NitEmpresa`). El precálculo solo recorre las 21 fuentes con
  `EnPanelCalificador = true`. Uso futuro posible: datos de contacto del empleador vía `Titular.NitEmpresa`
  (no implementado).

#### Desempate determinista de la "última fila"

El Calificador ordena solo por la fecha. En 20 de las 21 fuentes la fecha es `date`, así que un titular
con dos filas del mismo día tiene empate y SQL Server elige cualquiera: el resultado puede cambiar entre
corridas. El precálculo ordena por **`Fecha DESC, Enc DESC, Con DESC`**: en empate de fecha gana la fila
con dato. Es determinista, y como "tener contacto" implica "encontrado" en todas las fuentes de contacto,
elige a la vez el máximo de las dos banderas.

Consecuencia: la paridad con el Calificador puede diferir en unos pocos titulares por par y fuente,
**siempre titulares con empate de fecha**, y siempre a favor de "encontrado" en los Informes. Se midió
(abajo): todas las diferencias son de ese tipo.

#### Precálculo por cortes

- Un **corte** = una corrida completa sobre todos los pares activos (`Lider_Cartera.Activo = 1`,
  `Cartera.Activa = 1`). Lo arma `GeneradorSqlLocalizacion` desde el catálogo, igual que
  `GeneradorSqlCargue`: `#Alc` (filas del alcance) → `#Tit` (titulares distintos) → `#F_<Clave>` por fuente
  (última fila con `Enc`, `Con`, `Fecha`) → agregados por par/fuente, por líder/fuente con titulares
  distintos (LiderID 0 = toda la base), métricas que cruzan fuentes por titular (`#X`) y saldo de
  `UltimoEstadoTitular` (por TitularID + CarteraID) → una **transacción corta** copia todo a las tablas.
  `OPTION (MAXDOP 4, HASH JOIN)` en todo (servidor compartido; nested loops O(n²) si se deja al optimizador).
- Los nombres de las tablas destino son parametrizables (`dbo.` / `#` / `##`): el mismo batch corre contra
  tablas temporales para medir paridad y rendimiento sin DDL permanente.
- **Cuándo se calcula** (`LocalizacionResumenCoordinador`, Singleton): tras cada cargue OK del Cargue de
  Fuentes (`CargueFuentesMotor`, junto a `_cache.InvalidarTodo()`, en segundo plano, no bloquea el job); al
  primer acceso después de las 13:00 (misma regla de corte de `CalificadorCarteraCache`); al arrancar la
  API si no hay corte OK del día (30 s después, sin bloquear el arranque); con el botón "Recalcular" de
  Admin. **Single-flight** con `SemaphoreSlim`: un cálculo a la vez; un cargue que termina mientras corre
  deja UNA corrida pendiente (el corte en curso pudo leer la fuente antes del commit); los demás pedidos
  los cubre el corte en curso. Tras un fallo, los disparos automáticos de lectura esperan 15 min. Entre
  procesos (API de desarrollo y de producción contra la misma BD) evita dos cortes simultáneos mirando si
  hay un corte `EN CURSO` de menos de 15 min; uno más viejo se marca `ERROR` (huérfano).
- **Retención:** 26 semanas (las filas de resumen se borran en cascada con su corte).
- **Lecturas:** el corte completo (~2.000 filas) se carga en memoria una vez y se cachea 10 min con clave
  `Version` del coordinador (un corte nuevo lo invalida al instante). Resumen, Cobertura, Frescura y
  Rankings se calculan en C# sobre ese snapshot: milisegundos.
- Si las tablas no existen (el usuario aún no aplicó el script), los endpoints responden **503** con el
  nombre del script y el arranque no falla; si todavía no hay corte, **409** ("calculando").

#### Universos

| Alcance | Qué cuenta |
|---|---|
| Par (líder + cartera) | Filas de `Titular_Cartera` del par (= Calificador del par) |
| Cartera (Admin/Director) | Suma de sus pares **activos** (la "Cartera Completo" del Calificador también cuenta relaciones inactivas) |
| Líder | Titulares **distintos** de todas sus carteras (un titular en dos carteras del líder cuenta una vez; su saldo, la suma de esas carteras) |
| Todos (sin filtro) | Titulares distintos de toda la base (LiderID 0) |

#### Métricas e índice

- **Cobertura funcional** = buscados en al menos una fuente Funcional. **Frescura** = su consulta más
  reciente (cualquier fuente) tiene 180 días o menos. **Sin contacto con saldo** = `SaldoTotal > 0` en
  `UltimoEstadoTitular` y sin contacto; se informa el conteo y la plata.
- **Índice de localización** (0–100) = 100 × (0,5 × %contacto + 0,25 × %cobertura funcional + 0,25 ×
  %frescura) / suma de pesos. Pesos en `appsettings.json` → `InformesLocalizacion`.
- **Frescura por fuente:** tramos de la última consulta: hasta 90, 91–180, 181–365 y más de 365 días. Un
  buscado sin fecha cuenta como viejo.
- **Alertas:** fuente Funcional con más del 30 % por buscar; fuente con último dato de más de 365 días;
  par con más del 40 % de sus titulares sin contacto y con saldo (umbrales en appsettings).

#### Rankings

- Criterios: Índice (predeterminado), %Contacto, %Cobertura, %Frescura y Sin contacto con saldo (% del
  total; gana el que menos tiene). Empates a un decimal (lo que se ve) comparten posición ("1, 2, 2, 4").
- Líderes: podio con el top 3 y fotos grandes, tabla con avatar de 32 px y variación contra el **último
  corte OK de un día anterior** (no contra el de hace una hora). Universo: titulares distintos. Por defecto
  sin relaciones externas ni "BASE GENERAL" (líder o cartera); el interruptor para incluirlas es solo de
  Admin/Director.
- Carteras: ranking sin foto (Admin/Director).
- **Líder:** recibe el top 5 (con los empates de la 5.ª posición) más su propia fila y su posición, nunca
  la lista completa. En "carteras" recibe **sus pares**, ordenados entre sí: un Líder nunca obtiene cifras
  de cartera completa.

#### Alcance y seguridad

`[Authorize(Roles = Admin, Líder, Director)]` en la clase. Todo endpoint pasa por
`User.TryResolverLiderID`: al Líder se le ignora el `liderID` de la URL y se usa el del token; si pide
una `carteraID`, tiene que estar en su `Lider_Cartera` activo (si no, 403). `GET resumen/estado` queda
abierto a los tres roles porque solo expone metadatos del corte (el front lo usa para el banner y los
estados "sin tablas"/"calculando"); `POST resumen/recalcular` es solo Admin.

### Etapa 2 — Sugerencias de localización

`GET sugerencias` y `GET sugerencias/excel` (`LocalizacionSugerenciasService`, `GeneradorSqlSugerencias`,
`DespliegueSugerencias`; reglas en `Application/Utils/SugerenciaLocalizacion.cs`). Pestaña "Sugerencias"
con consulta manual (botón "Generar sugerencias", como Virtualidad).

#### Universo y datos

- **En línea sobre el par**, sin las tablas del precálculo: la pestaña funciona aunque el usuario no haya
  aplicado el script. Titulares = filas de `Titular_Cartera` con `Lider_Cartera.Activo = 1` y titular en
  `dbo.Titular` (el Total del Calificador). Admin/Director pueden pedir la **cartera completa** (sin
  `liderID`): un titular con dos líderes cuenta una vez, con el menor LiderID. El Líder siempre ve su par.
- Saldo, perfiles, promesa y última gestión: `UltimoEstadoTitular` por (TitularID, CarteraID).
- **ETL de gestión:** antes de calcular se lee `vw_EtlEstadoTitularEstado`. Si el último intento está
  `EN CURSO` hace menos de 180 min (`MinutosEtlColgado`; la edad se calcula en SQL porque el reloj de la BD
  no es necesariamente el de la API) → **409** con mensaje ("los datos de gestión se están actualizando");
  más viejo → se trata como colgado y solo se avisa. Última carga OK de más de 72 h → aviso en la respuesta.
- "Hoy" es la fecha del servidor de BD.

#### Categorías (por titular y fuente Funcional pedida)

Con la **misma última fila que la etapa 1** (`GeneradorSqlLocalizacion.UltimaFilaConsulta`: mismo predicado
de "encontrado", desempate `Fecha DESC, Enc DESC, Con DESC`):

| Código (= CHECK de `LocalizacionEnvioDetalle`) | Regla |
|---|---|
| `NUNCA_BUSCADO` | Sin ninguna fila en la fuente ("Falta por buscar") |
| `NO_ENC_VIEJO` | La última fila no cumple el predicado y su fecha es anterior a hoy − `umbralDias` (180). Sin fecha = viejo |
| `ENC_VIEJO` | Solo si se pide `umbralRefresco`: cumple el predicado pero es anterior a hoy − umbral |

Solo fuentes **Funcionales** del panel (una Intermitente o Inactiva → 400). Se usaron los códigos del CHECK
de la tabla de envíos (y no NUNCA/NOENC_VIEJO/REFRESCO) para que la etapa 3 guarde la categoría tal cual.

#### Exclusiones (un motivo por candidato, en este orden)

1. `YA_PAGO`: **UltimoPerfil o MejorPerfil** en `PerfilClasificacion.YaPago` (paz y salvo, ya pagó, pago
   reflejado, consignación por aplicar, caso especial…). Se agregó MejorPerfil porque ADR-029 clasifica "ya
   pagó" por el MejorPerfil: medido, ~970 titulares con MejorPerfil paz y salvo y otro UltimoPerfil.
2. `FALLECIDO` (Fallecido, Cliente fallecido), `NO_GESTIONAR`, `RECLAMACION` (PQR, reclamación o derecho de
   petición), `ACUERDO` (`PerfilClasificacion.Acuerdos`): por UltimoPerfil.
3. `PROMESA`: `FechaPromesa >= hoy`.
4. `ENVIADO`: el titular va en un lote `ENVIADO` o `CERRADO` **de la misma fuente** (de cualquier par: la
   búsqueda es del titular en la fuente) con `FechaEnvio` dentro de `excluirEnviadosDias` (60). Si no existen
   `LocalizacionEnvio*` no se excluye a nadie y la respuesta lo avisa (no es un 503).
5. `SIN_SALDO`: con "solo con saldo" (default), sin `SaldoTotal > 0`.

**Perfiles: una sola regla, normalizada.** C# (`PerfilesSugerencia`) clasifica los perfiles de los conjuntos
de `PerfilClasificacion` con la clave normalizada (mayúsculas, sin tildes, espacios colapsados: "DATOS DE
LOCALIZACION", "Datos Localizacion", "Pago  reflejado"…) y se la pasa al SQL como JSON; el SQL normaliza el
perfil de la BD igual y compara con `Latin1_General_CI_AI`. Un perfil desconocido no excluye ni suma.

#### Puntaje explicable (pesos en `InformesLocalizacion:Sugerencias`)

| Componente | Puntos |
|---|---|
| UltimoPerfil ilocalizado (total/parcial), número equivocado, línea errada, fuera de servicio o datos de localización | +40 |
| Otro perfil del conjunto SinContacto, "SIN GESTION" o sin perfil | +25 |
| Saldo | hasta +30 = round(30 × min(1, saldo / P90)); P90 = `PERCENTILE_CONT(0.9)` del saldo de los titulares **con saldo** del par (con los ceros, el P90 de una cartera con poco saldo sería 0) |
| Última gestión nunca o de más de 90 días / de más de 30 | +15 / +5 |
| Nunca buscado en ninguna fuente Funcional | +15 |
| Nunca buscado en la fuente | +10 |
| No encontrado y viejo en la fuente | +5 |
| Contacto fresco: celular o email (`PredicadoContacto`) en la última fila de alguna Funcional de 180 días o menos | −100, salvo "incluir localizados" |

"Línea errada" y "fuera de servicio" se sumaron a los +40 (decisión propia): dicen lo mismo que "número
equivocado", que el dato de contacto está mal. El SQL calcula el puntaje para ordenar; el servidor lo
recalcula en C# (`PrioridadSugerencia.Calcular`) para cada fila devuelta y registra un warning si difiere
(en las mediciones: 0 diferencias). Cada fila trae el desglose; el front lo muestra en un popover.

**Orden:** prioridad, saldo (mayor primero), TitularID, fuente. **Top** 500 por defecto, máximo 5.000.

#### Cómo se calcula (un batch)

Una fila por titular (`#TE`) con **máscaras de bits** por fuente Funcional (buscado, encontrado, no
encontrado y viejo, encontrado pero viejo, enviado) y lo que es del titular (exclusión, puntos que no
dependen de la fuente). Los 2,7 M candidatos (titular × fuente) del par grande **nunca se escriben**: los
conteos salen de agrupar las combinaciones de máscaras (~8.000) y desplegarlas en C#; el top sale de los N
mejores titulares por su mejor fila, que contienen a las N mejores filas (cualquier fila de otro titular
queda detrás de la mejor fila de cada uno de esos N; un test lo compara contra fuerza bruta).

Lo que costó (servidor compartido, tempdb lento), medido el 2026-09-25:

- Materializar candidatos en tempdb: 7 s. Descartado.
- `UltimoEstadoTitular` pesa ~600 MB (`Gestion` nvarchar(1000)): con HASH JOIN, ~2 s; por su PK con LOOP JOIN
  desde el par, ~0,5 s. Con estadísticas viejas de `IX_UltimoEstado_CarteraID` (el ETL la recarga cada día)
  el optimizador buscó por CarteraID y recorrió un spool por titular: **63 s** en 36k titulares. Por eso
  `FORCE ORDER` + `INDEX(1), FORCESEEK`.
- Compilación: en un batch ad hoc, una sentencia que lee una # creada en el mismo batch se recompila en cada
  ejecución (1,3 s la grande) y crea estadísticas automáticas al filtrar o agrupar (8 estadísticas, ~1 s).
  Por eso `@G`/`@Tit`/`@PG`/`@EnvM` son variables de tabla, las consultas finales no filtran ni agrupan por
  varias columnas de `#TE` (agrupan por una "Firma" y ordenan por `OrdenTop`) y nada lleva RECOMPILE salvo el
  llenado de `@G`. El texto lleva el **tramo de tamaño** del par (chico/mediano/grande/enorme) como
  comentario para que un par chico y uno grande no compartan plan (medido: el grande con el plan del chico,
  10,7 s en vez de 4 s).
- Las 16 fuentes Funcionales (18 M de filas) se leen con HASH JOIN en una sola sentencia: es el piso, ~2,5 s a
  MAXDOP 4 (un LOOP JOIN por el índice `(TitularID, Fecha)` fue el doble de lento).

#### Excel con el formato del Cargue de Fuentes

`IExcelExporter.GenerarListaLocalizacion`: por fuente, hoja `BASE` (la que el lector del cargue busca
primero) con los encabezados **exactos** de `ColumnasEsperadasCargue.De(def)` — la lista que el orquestador
del cargue muestra en "Columnas esperadas", movida a Application para que la usen los dos: TitularID,
Nombre, CarteraID, Cartera, LiderID, Lider y las columnas de dato vacías. TitularID como texto y formato `@`
en la clave y en toda columna de dato que no es fecha (un celular o un NIT pegado ahí no pasa a científico).
Sin filas de ejemplo. El Cargue recibe **un archivo por fuente**: una fuente → `.xlsx`; varias → `.zip` con
un `.xlsx` por fuente. El Excel lleva exactamente el top que se ve en pantalla.

Verificado en `ExcelListaLocalizacionTests` sin BD, para las 16 fuentes Funcionales: el archivo generado y
llenado se lee con `ArchivoTabular.LeerXlsxPorFilas` y `CargueFuentesMotor.ResolverColumnas` (ahora
`internal`): cero columnas faltantes, cero sin uso, todas las columnas de dato resueltas, la clave
`1234567890123` leída tal cual y válida para `TryDocumento`. Falta la prueba a mano de punta a punta
(exportar Fosyga, llenarlo y subirlo en Cargue de Fuentes).

"Crear lote de envío" no aparece en la pestaña (no se muestra un botón deshabilitado): llega con la etapa 3.

### Etapas siguientes (plan aprobado)

2. ~~Sugerencias de localización y Excel con el formato del Cargue de Fuentes.~~ Hecha (arriba).
3. Envíos (`LocalizacionEnvio*`, tablas ya creadas por el script de la etapa 1), retorno, impacto y uso
   del departamento; criterios de ranking de uso y retorno.
4. Tendencias, servicio de las 13:05, asesores con foto y alertas semanales.

## Lo que no se puede medir hoy

Se dice en la UI ("Qué no se puede medir hoy") y aquí:

- Cuánto lleva cada titular asignado al líder: `Titular_Cartera` no tiene fecha.
- Envíos a localización anteriores a este módulo.
- La frescura real de los cargues legacy: los SP no refrescaban la fecha cuando el contenido no cambiaba
  (ADR-039). Desde el Cargue de Fuentes sí se refresca.
- Cámara Empresa (su clave es el NIT) y Runt.
- "No encontrado" en Claro: `DatosClaro` solo trae filas con dato (su No encontrado es siempre 0).
- El costo o la capacidad del departamento de localización.
- **El saldo de las carteras cuyo saldo no llega a `UltimoEstadoTitular`**: medido el 2026-09-25, 16
  carteras activas no tienen ningún titular con `SaldoTotal > 0` — con filas pero saldo NULL: las cinco
  Delmar con filas (Expertos, Elite, Contact, Vittoria, Consultorías), LATIN, Mil Ambientaria,
  Interenlace y Prima y Nuevagarantia; sin filas en UET: COBRANO, Claro, BASE GENERAL, Delmar Internacional y
  las externas DXS, Serviplata y Retornamos Legal. Ahí "sin contacto con saldo" sería 0 y ganaría el
  ranking; por eso ese porcentaje vale `null` ("—") cuando el universo no tiene ningún titular con saldo,
  y queda al final.

## Discrepancias Calificador vs Cargue de Fuentes (encontradas en la etapa 0)

"Encontrado" en el Calificador (y en estos informes) no es lo mismo que la bandera `EstadoEn<Fuente>` que
calcula el Cargue de Fuentes (heredada de los SP legacy). Los informes siguen al Calificador:

| Fuente | Calificador / Informes | Flag del cargue |
|---|---|---|
| Cajacopi | Celular, email o **fecha de nacimiento** (edad ≠ 0) | Incluye la fecha de nacimiento, como el SP |
| Positiva | Celular, email o **fecha de nacimiento** (edad ≠ 0) | La fecha de nacimiento **no** entra |
| SaviaSalud | Celular, régimen o estado de afiliación; **`NumeroTelefono` no cuenta** (la BaseMadre no lo proyecta) | Cuenta `NumeroTelefono` y `NumeroCelular`; régimen y afiliación no |
| Compensados | EPS o tipo de afiliado | EPS, último período compensado o el `Estado` del archivo; tipo de afiliado no |
| RUES | Solo el nombre de la empresa | Matrícula, estado o actividad; el nombre **no** entra |

## Verificación

### Paridad con el Calificador (2026-09-25)

El batch se corrió contra tablas `#temp` y se comparó con el SQL del Calificador (la materialización de
`#t` sobre `fn_BaseMadreParaTitulares` con el TVP, el result set 9 por titular y `SinContacto` del result
set 1, copiados del código). Para cada titular distinto se buscó si tenía un **empate ambiguo** (dos o más
filas en la fecha máxima con distinto Enc o Con).

- **Total y Falta por buscar: idénticos** en los 3 pares y las 21 fuentes.
- Encontrados / No encontrados / ConContacto: toda diferencia es un titular con empate ambiguo
  ("explicados" = "distintos" en las 63 filas).

| Par | Titulares | Fuentes con diferencia (titulares distintos = explicados por empate) | ConContacto Calificador → Informes |
|---|---:|---|---|
| Líder 15 / COBRANO | 1.550 | ninguna | 1.196 → 1.196 |
| Líder 14 / SG+ | 36.197 | ARL Sura 1, TigoUne 26 | 27.694 → 27.696 (2 titulares, ambos con empate) |
| Líder 6 / DELMAR EXPERTOS | 223.353 | ARL Sura 33, Cajacopi 1, Cámara 5, Compensados 29, Coosalud 137, Fosyga 2, Positiva 2, Positiva Emp. 10, SaviaSalud 1 | 165.916 → 165.970 (54 titulares, todos con empate) |

En los mismos pares hay más empates ambiguos que diferencias (p. ej. Coosalud: 305 empates, 137
diferencias): en el resto el Calificador eligió por azar la fila con dato. Correr el Calificador dos veces
puede dar cifras distintas en esas fuentes; los Informes no.

### Rendimiento

Batch completo (56 pares activos, 1.931.286 titulares distintos, 21 fuentes) contra `#temp`: **51 s**
(objetivo < 120 s). Aproximadamente 22 s en las últimas filas por fuente, 20 s en los 42 agregados por
fuente y 9 s en saldo, métricas cruzadas y líderes. Lecturas: en memoria sobre el snapshot del corte.

### Sugerencias: conciliación y rendimiento (2026-09-25)

**Conciliación con "Falta por buscar"** (Fosyga, calculado aparte con `NOT EXISTS` sobre `dbo.Fosyga` y el
mismo universo): los candidatos `NUNCA_BUSCADO` antes de exclusiones son **idénticos** en los tres pares.

| Par | Falta por buscar | Nunca (antes) | Exclusiones en Fosyga (filas, ambas categorías) | Nunca (después) |
|---|---:|---:|---|---:|
| Líder 15 / COBRANO (solo con saldo) | 288 | 288 | SIN_SALDO 923 (la cartera no tiene saldo en UET) | 0 |
| Líder 15 / COBRANO (sin filtro de saldo) | 288 | 288 | ninguna | 288 |
| Líder 14 / SG+ (solo con saldo) | 15.531 | 15.531 | SIN_SALDO 5.811, ACUERDO 117, YA_PAGO 67, PROMESA 1 (= 5.996) | 9.825 |
| Líder 6 / DELMAR EXPERTOS (sin filtro de saldo) | 48.186 | 48.186 | YA_PAGO 1.112, FALLECIDO 358, NO_GESTIONAR 87, ACUERDO 26, RECLAMACION 2 (= 1.585) | 46.613 |

(NuevaEps, con TitularID de texto, también cuadra: 36.123 en SG+.) DELMAR con "solo con saldo" queda en 0
candidatos: sus titulares no tienen saldo en `UltimoEstadoTitular` (ver "Lo que no se puede medir hoy"); la
respuesta lo avisa y sugiere desmarcar el filtro.

**Rendimiento** (16 fuentes Funcionales, top 500, plan en caché). Tiempo en el servidor (medido con el
batch real por `sp_executesql` y los resultados grandes a tablas temporales, sin red):

| Par | Titulares | Servidor |
|---|---:|---:|
| Líder 15 / COBRANO | 1.550 | 0,5–0,7 s |
| Líder 14 / SG+ | 36.197 | 1,2–1,8 s |
| Líder 6 / DELMAR EXPERTOS | 223.353 | **3,7–4,0 s** (5 corridas seguidas: 3,67 / 3,76 / 3,73 / 3,72 / 3,81) |
| DELMAR EXPERTOS cartera completa | 526.934 | 9–11 s |

Desde el equipo de desarrollo (remoto: la BD está en otra zona horaria y la red agrega ~2 s de transferencia
de resultados), el servicio completo midió 5,4–6,1 s para el par grande. En producción la API corre en el
mismo servidor. Primera ejecución de cada tramo: +2–8 s de compilación. Hubo picos aislados de 9–16 s por
esperas de tempdb (una inserción de 0,4 s de CPU tardó 13 s), sin relación con la consulta.

### Tests

`GeneradorSqlLocalizacionTests` (tabla, fecha, predicado, desempate, NuevaEps con `TRY_CAST`, contacto =
columnas de `SinContacto`, prefijo de tablas, sin CamaraEmpresa), `RankingCalculoTests` (índice, orden,
empates, top + posición del Líder, exclusión de externas, saldo sin datos) y `ReglaCorteLocalizacionTests`
(regla de las 13:00 y de arranque). Etapa 2: `PrioridadSugerenciaTests` (puntos, variantes de perfil, exclusiones
y su precedencia, −100 por contacto fresco, despliegue de máscaras, titulares por motivo, top exacto contra
fuerza bruta, tramos de tamaño, fuentes solo Funcionales) y `ExcelListaLocalizacionTests` (encabezados =
columnas esperadas del cargue para las 16 Funcionales, TitularID texto, el lector del cargue acepta el archivo
lleno).

## Consecuencias

- Las cifras de un corte son estables entre consultas y comparables en el tiempo; las del Calificador
  pueden moverse en los empates.
- Los informes pueden quedar hasta un corte atrás de los datos (el banner dice "Localización al … ·
  Gestión al …"); un cargue OK dispara un corte nuevo en segundo plano (~1 min).
- Agregar una fuente al catálogo la suma al precálculo, a la cobertura y a la frescura sin tocar SQL. Si
  aporta celular o email, hay que declarar su `PredicadoContacto` **y** revisar `SinContacto` del Calificador.
