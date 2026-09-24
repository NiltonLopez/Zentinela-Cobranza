# Paridad y alcance del grupo "otras" (ADR-039, Fase 2): Fosyga, RGM, TigoUne, Tigo y Runt

Comparación, con los mismos datos de entrada, de lo que dejaban `sp_ProcesarFosygaAuxiliar`,
`sp_ProcesarRGMAuxiliar` y `sp_ProcesarTigoUneAuxiliar` (respaldados en esta carpeta) y lo que deja el
motor nuevo (`CargueFuentesMotor` + `Regla<Fuente>`). Incluye además la investigación de **Tigo** y
**Runt** (tienen tabla Auxiliar pero no SP) y el estado de las Auxiliar que hoy tienen filas. Corrida del
**2026-09-24** contra DBCallCenter (10.0.0.89), rama `feature/cargue-f2-otras`.

Método, leyenda y categorías (E1–E11, P1–P5, R, F) son los de [`PARIDAD.md`](PARIDAD.md) (Fase 1); aquí
solo se anota lo que cambia.

## Resumen

| Fuente | SP | Casos | Iguales | Diferencias | Sin explicar | Conclusión |
|---|---|---:|---:|---:|---:|---|
| Fosyga (+ B, C) | `sp_ProcesarFosygaAuxiliar` (2026-09-22) | 21 | 5 | 16 | 0 | Paridad OK: E1, E9, E10, E11, P2–P5, R, F |
| RGM (+ B, C) | `sp_ProcesarRGMAuxiliar` (2026-04-13) | 22 | 5 | 17 | 0 | Paridad OK: E1, E9, E10, E11, P2, P3, P5, R, F y el defecto `LiderID = CarteraID` |

## Método (diferencias con la Fase 1)

Igual que en [`PARIDAD.md`](PARIDAD.md#método): **ningún SP se ejecutó** (ni con ROLLBACK) y **ninguna
tabla `*Auxiliar` real se tocó**. El lado viejo es el cuerpo del SP como batch
ad-hoc con toda tabla que escribe sustituida por `#temp` (`<Fuente>Auxiliar → #Aux` con la estructura
exacta de `<Fuente>Auxiliar_CREATE.sql`, destino `→ #Dest`, `Titular → #Titular`,
`Titular_Cartera → #Titular_Cartera`); `ListaNegraCorreos` y `Lider_Cartera` se leen directo. La
herramienta aborta si el batch, sin comentarios ni literales, todavía nombra una tabla real escribible
o si un verbo de escritura apunta a algo que no sea `#temp`, un alias sobre `#temp` o una CTE. El lado
nuevo es el motor real con carga real (`simular: false`) en el rango del grupo y limpieza con
`Harness.Limpiar()`.

Lo que cambia respecto de la Fase 1:

- **Rango**: el del grupo `otras` (999000003000..3999). Los casos de paridad usan 3601–3699 (Fosyga),
  3701–3799 (RGM) y 3801–3899 (TigoUne), fuera de los bloques del harness (3000–3499).
- **Auditoría**: solo la del usuario `harness-cargue-otras` (otros grupos corren sus harness a la vez;
  contar toda `CargueFuenteEjecucion` daría falsos positivos).
- **Lista de tablas prohibidas** ampliada con `Fosyga`, `RGM`, `TigoUne`, `Tigo`, `Runt` y sus Auxiliar;
  destinos de escritura permitidos ampliados con el alias `ta` y la CTE `Duplicados` (los usa el SP de
  TigoUne).
- **FK al destino**: además de Cajacopi y SIMIT, se verifica después de correr el batch si las filas de
  `#Dest` de **Fosyga** y **RGM** (que tienen FK a `Titular`) habrían violado la FK. TigoUne no tiene FK.
- **Par líder/cartera**: activo (1, 29) e inactivo (5, 16), como en la Fase 1. En RGM el escenario
  principal usa el par activo **(4, 4)**, con LiderID = CarteraID, para que el defecto conocido del SP
  (`LiderID = CarteraID`) no tape las demás diferencias; ese defecto se aísla en RGM-B con (1, 29).
- **Herramienta**: copia de la de la Fase 1 en el scratchpad de la sesión (`paridad-otras/`), fuera del
  repo: enlaza `tools/CargueHarness/{Harness,Grupos}.cs` y `Casos/*.cs` de este worktree sin modificarlos.

**Limpieza verificada** al final de cada escenario: el rango 999000003000..3999 queda con 0 filas en
todas las tablas de `Harness.TablasDelRango`, la auditoría del usuario del grupo en 0 y el `COUNT(*)` de
cada destino igual al inicial (Fosyga 2.140.272; RGM 1.663.543).

## Fosyga

**Definición** (`CatalogoCargueFuentes.Fosyga`, `sys.columns` de `dbo.Fosyga` el 2026-09-24): Estado
varchar(30), EPS varchar(120), Regimen varchar(30), FechaAfiliacionEfectiva y FechaFinalizacionAfiliacion
`date`, TipoAfiliado varchar(50), Departamento varchar(70), Municipio varchar(50), EstadoEnFosyga bit,
FechaConsulta `date` NOT NULL. FK a `Titular`, sin triggers ni CHECK, heap con
`IX_Fosyga_TitularID_Fecha`. 2.140.272 filas, 1.736.471 titulares: una fila vigente por titular
(`UnaPorTitular`, como el dedup por titular del SP). Clave de contenido = las 8 columnas. Flag = Estado,
EPS o Régimen (paso 5 del SP); fechas, tipo de afiliado y localización no entran.

**Regla** (`ReglaFosyga`): el paso 4 del SP, Estado 'AFILIADO FALLECIDO' → TipoAfiliado 'FALLECIDO',
sobre el texto ya limpio y sin distinguir mayúsculas; se informa (info, no advertencia). Presente en los
datos desde la primera carga (2025-03-10): en `dbo.Fosyga` todas las filas con ese estado tienen
TipoAfiliado 'FALLECIDO' salvo 126 de marzo/abril de 2025.

**Conclusión: paridad OK.** 17 casos + 4 de fragilidad (21): 5 idénticos y 16 diferencias, todas explicadas;
0 predicciones fallidas. Idénticos: la regla de fallecido (también en minúsculas), contenido idéntico
(el SP inserta y deduplica; el motor refresca: mismo resultado), solo localización (flag 0) y la fecha
2999-12-31 (vigente).

Detalles:

- **F04 (R).** Hay 69 filas en `dbo.Fosyga` con Estado `'AFILIADO' + salto de línea + 'FALLECIDO'` (y 1
  `'PROTECCIÓN' + salto + 'LABORAL C'`): la celda del Excel traía el salto. El `=` del paso 4 no las casa
  y quedan con el tipo de afiliado original (en la BD: 44 CABEZA DE FAMILIA, 18 COTIZANTE, 6 BENEFICIARIO, 1
  ADICIONAL, ninguna FALLECIDO). El motor colapsa espacios y saltos en el tipado genérico,
  antes de la regla, así que la regla aplica y el Estado se guarda en una línea.
- **F06 (P4).** El paso 8 deduplica **toda** la tabla con la clave (TitularID, Estado, EPS, Régimen,
  TipoAfiliado): la fila vieja que solo difería en Municipio (o en las fechas) se borraba. El motor la
  conserva como historial.
- **F08, F09 (P5).** Este SP no purgaba vacías; el motor aplica la política genérica, como en SIMIT.
- **F10 (E9).** El paso 8 borró un duplicado de un titular que no venía en el archivo.
- **F15.** Estado `''` (import CSV) contaba como dato para el flag del SP (`IS NULL`).
- **Fosyga-B (F).** Una fila sin nombre hace fallar el `INSERT` a `Titular` **entero** (error 515) y,
  en la BD real, también el de `Fosyga` por `FK_Titular_Fosyga` (la herramienta lo detecta: "2 filas de
  Fosyga violarían FK a Titular"). Es exactamente lo que le pasó a la carga que quedó en
  `FosygaAuxiliar` (ver [Auxiliar con datos](#auxiliar-con-datos)). El motor excluye esa fila con error
  y carga el resto.
- **Fosyga-C (F).** `FosygaAuxiliar.EPS` es varchar(200) y `Fosyga.EPS` varchar(120): una EPS larga
  hace fallar el `INSERT` a `Fosyga` de todo el archivo (error 2628). El motor la trunca a 120 con aviso.
  Hoy la EPS más larga en `dbo.Fosyga` tiene 100 caracteres: sin impacto actual.
- **Dato legacy, no del SP:** 901 filas de la carga del 2025-10-23 tienen fechas en la columna
  `Regimen` ('31/12/2999', '25/08/2013', números de serie de Excel) con las columnas de fecha en NULL:
  el import de SSMS de ese día mapeó mal las columnas. El motor resuelve cada columna por su
  **encabezado**, así que ese error de mapeo no se puede repetir; las 901 filas no se tocan.

#### Escenario Fosyga

Columnas del destino: `Estado · EPS · Regimen · FechaAfiliacionEfectiva · FechaFinalizacionAfiliacion · TipoAfiliado · Departamento · Municipio · EstadoEnFosyga · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| F01 | Titular nuevo; nombre con tilde; todos los campos | ACTIVO · NUEVA EPS S.A. · SUBSIDIADO · 2016-01-01 · ∅ · CABEZA DE FAMILIA · BOLIVAR · SAN PABLO · 1 · HOY | ACTIVO · NUEVA EPS S.A. · SUBSIDIADO · 2016-01-01 · ∅ · CABEZA DE FAMILIA · BOLIVAR · SAN PABLO · 1 · HOY | José Núñez / JOSE NUÑEZ | L1/C29 / L1/C29 | difiere: titular | ≠ E1 nombre limpio |
| F02 | Documento inválido (4 dígitos) | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | ≠ P3 documento inválido |
| F03 | Estado 'AFILIADO FALLECIDO' con TipoAfiliado 'COTIZANTE' | AFILIADO FALLECIDO · COOSALUD EPS S.A. · SUBSIDIADO · ∅ · ∅ · FALLECIDO · ∅ · ∅ · 1 · HOY | AFILIADO FALLECIDO · COOSALUD EPS S.A. · SUBSIDIADO · ∅ · ∅ · FALLECIDO · ∅ · ∅ · 1 · HOY | FALLECIDO COTIZANTE / FALLECIDO COTIZANTE | L1/C29 / L1/C29 | igual | = |
| F04 | Estado con salto de línea ('AFILIADO⏎FALLECIDO', 69 filas así en dbo.Fosyga) | 'AFILIADO⏎FALLECIDO' · ∅ · ∅ · ∅ · ∅ · COTIZANTE · ∅ · ∅ · 1 · HOY | AFILIADO FALLECIDO · ∅ · ∅ · ∅ · ∅ · FALLECIDO · ∅ · ∅ · 1 · HOY | FALLECIDO SALTO / FALLECIDO SALTO | L1/C29 / L1/C29 | difiere: destino | ≠ R el motor colapsa el salto y aplica la regla; el SP no casaba el '=' |
| F05 | Titular existente, contenido idéntico en destino | ACTIVO · SANITAS · CONTRIBUTIVO · ∅ · ∅ · COTIZANTE · ∅ · ∅ · 1 · HOY | ACTIVO · SANITAS · CONTRIBUTIVO · ∅ · ∅ · COTIZANTE · ∅ · ∅ · 1 · HOY | FOSYGA EXISTENTE / FOSYGA EXISTENTE | L1/C29 / L1/C29 | igual | = |
| F06 | Existente; solo cambia Municipio (fuera de la clave del SP) | ACTIVO · SANITAS · CONTRIBUTIVO · ∅ · ∅ · ∅ · ∅ · MALAMBO · 1 · HOY | ACTIVO · SANITAS · CONTRIBUTIVO · ∅ · ∅ · ∅ · ∅ · MALAMBO · 1 · HOY<br>ACTIVO · SANITAS · CONTRIBUTIVO · ∅ · ∅ · ∅ · ∅ · SOLEDAD · 1 · viejo 2026-01-15 | CAMBIA MUNICIPIO / CAMBIA MUNICIPIO | L1/C29 / L1/C29 | difiere: destino | ≠ P4 clave completa (el SP borraba la fila vieja) |
| F07 | Solo TipoAfiliado, localización y fecha (no entran al flag) | ∅ · ∅ · ∅ · 2020-05-01 · ∅ · BENEFICIARIO · CESAR · VALLEDUPAR · 0 · HOY | ∅ · ∅ · ∅ · 2020-05-01 · ∅ · BENEFICIARIO · CESAR · VALLEDUPAR · 0 · HOY | SOLO LOCALIZACION / SOLO LOCALIZACION | L1/C29 / L1/C29 | igual | = |
| F08 | Vacía vieja en destino; ahora trae datos | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | difiere: destino | ≠ P5 política de vacías (el SP no purgaba) |
| F09 | Datos en destino; el archivo trae fila vacía | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | difiere: destino | ≠ P5 la vacía no entra (el SP la insertaba) |
| F10 | Titular AJENO al archivo con duplicado exacto en destino | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | AJENO AL ARCHIVO / AJENO AL ARCHIVO | — / — | difiere: destino | ≠ E9 sin limpiezas globales (el paso 8 del SP deduplica toda la tabla) |
| F12 | Par Líder/Cartera inactivo | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | ≠ E10 sin relación con par inactivo |
| F13 | Titular repetido; la 2ª fila tiene más datos | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ACTIVO · SALUD TOTAL · CONTRIBUTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | ? SP conserva una fila arbitraria (FechaConsulta NULL en la Auxiliar); nuevo la más completa |
| F14 | 'N/A' en Estado y '0' en EPS | N/A · 0 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | NA TEXTO / NA TEXTO | L1/C29 / L1/C29 | difiere: destino | ≠ P2 vacíos convencionales ('N/A','0' → NULL; flag 0) |
| F15 | Estado '' (import CSV), sin más datos | '' · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | ESTADO VACIO / ESTADO VACIO | L1/C29 / L1/C29 | difiere: destino | ≠ flag post-limpieza: el SP contaba '' como dato (flag 1) |
| F16 | FechaFinalizacionAfiliacion 2999-12-31 (vigente) | ACTIVO · NUEVA EPS S.A. · ∅ · ∅ · 2999-12-31 · ∅ · ∅ · ∅ · 1 · HOY | ACTIVO · NUEVA EPS S.A. · ∅ · ∅ · 2999-12-31 · ∅ · ∅ · ∅ · 1 · HOY | VIGENTE / VIGENTE | L1/C29 / L1/C29 | igual | = |
| F17 | Estado en minúsculas 'afiliado fallecido' | afiliado fallecido · ∅ · ∅ · ∅ · ∅ · FALLECIDO · ∅ · ∅ · 1 · HOY | afiliado fallecido · ∅ · ∅ · ∅ · ∅ · FALLECIDO · ∅ · ∅ · 1 · HOY | MINUSCULAS / MINUSCULAS | L1/C29 / L1/C29 | igual | = |
| F18 | Titular existente con nombre mojibake en BD; archivo 'Piña Ruiz' | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ACTIVO · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PIÃ‘A RUIZ / PIÑA RUIZ | L1/C29 / L1/C29 | difiere: titular | ≠ E1 nombre reparado |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 17, excluidas 1, duplicadas 1, expansión +0, candidatas 15, insertadas 13, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 10, nombres reparados 1, relaciones nuevas 14.
Carga (ejecución 241): insertadas 13, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 10, nombres reparados 1, relaciones 14.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×14: Relaciones titular-cartera nuevas
- info ×10: Titulares nuevos que se crearán
- info ×3: TipoAfiliado puesto en 'FALLECIDO' porque el Estado es 'AFILIADO FALLECIDO'
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.Fosyga
- info ×1: Nombres de titular rotos en BD que se repararán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Fosyga 2140272→2140272; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 11 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 16 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
PRINT: 16 INSERTADOS EN LA TABLA FOSYGA
PRINT: 3 DUPLICADOS FUERON ELIMINADOS DE FOSYGA
```

</details>

#### Escenario Fosyga-B

Columnas del destino: `Estado · EPS · Regimen · FechaAfiliacionEfectiva · FechaFinalizacionAfiliacion · TipoAfiliado · Departamento · Municipio · EstadoEnFosyga · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| FB1 | Titular nuevo SIN nombre (como las 18.598 filas de FosygaAuxiliar) | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | — / — | L1/C29 / — | difiere: destino, relación | ≠ el nuevo lo excluye; en el SP el INSERT a Titular falla entero (F) |
| FB2 | Titular nuevo normal en el mismo archivo | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — / TITULAR NORMAL | L1/C29 / L1/C29 | difiere: titular | ≠ F: víctima del fallo de FB1 (sin Titular en el SP) |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 1, duplicadas 0, expansión +0, candidatas 1, insertadas 1, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 1, nombres reparados 0, relaciones nuevas 1.
Carga (ejecución 243): insertadas 1, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 1, nombres reparados 0, relaciones 1.
- error ×1: Titular nuevo sin nombre en el archivo
- info ×1: Relaciones titular-cartera nuevas
- info ×1: Titulares nuevos que se crearán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Fosyga 2140272→2140272; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
ERROR 515 (línea 8): No se puede insertar el valor NULL en la columna 'Nombre', tabla 'tempdb.dbo.#Titular'. La columna no admite valores NULL. Error de INSERT.
PRINT: Se terminó la instrucción.
PRINT: 0 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
PRINT: 2 INSERTADOS EN LA TABLA FOSYGA
PRINT: 0 DUPLICADOS FUERON ELIMINADOS DE FOSYGA
AVISO: 2 relaciones violarían FK_TitularCartera_Titular (en la BD real el INSERT de Titular_Cartera habría fallado entero)
AVISO: 2 filas de Fosyga violarían FK a Titular (en la BD real el INSERT habría fallado entero)
```

</details>

#### Escenario Fosyga-C

Columnas del destino: `Estado · EPS · Regimen · FechaAfiliacionEfectiva · FechaFinalizacionAfiliacion · TipoAfiliado · Departamento · Municipio · EstadoEnFosyga · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| FC1 | EPS de 137 caracteres | — | ACTIVO · ENTIDAD PROMOTORA DE SALUD XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | EPS LARGA / EPS LARGA | L1/C29 / L1/C29 | difiere: destino | ≠ el nuevo trunca a 120 con aviso; en el SP el INSERT a Fosyga falla entero (F) |
| FC2 | Fila normal en el mismo archivo | — | ACTIVO · SANITAS · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FILA NORMAL / FILA NORMAL | L1/C29 / L1/C29 | difiere: destino | ≠ F: víctima del fallo de FC1 (sin fila en Fosyga en el SP) |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 0, duplicadas 0, expansión +0, candidatas 2, insertadas 2, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 2, nombres reparados 0, relaciones nuevas 2.
Carga (ejecución 246): insertadas 2, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 2, nombres reparados 0, relaciones 2.
- aviso ×1: EPS: valor truncado a 120 caracteres
- info ×2: Relaciones titular-cartera nuevas
- info ×2: Titulares nuevos que se crearán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Fosyga 2140272→2140272; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 2 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
ERROR 2628 (línea 68): Los datos binarios o de la cadena se truncan en la columna "EPS" de la tabla "tempdb.dbo.#Dest". Valor truncado: "ENTIDAD PROMOTORA DE SALUD XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX".
PRINT: Se terminó la instrucción.
PRINT: 0 INSERTADOS EN LA TABLA FOSYGA
PRINT: 0 DUPLICADOS FUERON ELIMINADOS DE FOSYGA
```

</details>

## RGM

**Definición** (`CatalogoCargueFuentes.Rgm`, `sys.columns` de `dbo.RGM` el 2026-09-24): Marca
varchar(50), **Modelo int**, Placa varchar(50), EstadoEnRGM bit, FechaConsulta `date` NOT NULL. FK a
`Titular` (`FK_RGM`), sin triggers ni CHECK, heap con `IX_RGM_TitularID_Fecha`. 1.663.543 filas,
1.662.514 titulares: una fila vigente por titular (`UnaPorTitular`, como el dedup del SP; solo 15
titulares tienen dos vehículos distintos, en cargas de fechas distintas). 8.774 filas con datos; el
resto (1.654.769) son vacías. Clave = las 3 columnas; flag = cualquiera de las 3 (paso 4 del SP).
`RGMAuxiliar` tenía `Modelo varchar(50)` y `Placa varchar(6)`.

**Placa: sí aplica la validación de SIMIT** (`PlacaVehiculo.EsValida`, genérica para columnas tipo
`Placa`). Sobre las 8.765 placas no nulas de `dbo.RGM`: 8.740 (99,7 %) la cumplen (AAA999 4.821, AAA99A
3.871, AAA99 26, 999AAA 21, con espacio inicial ' HGK96'…); las 25 que no son `[objec` ×21
(`'[object Object]'` truncado por el varchar(6) del staging), `AVAL` ×3 y `0` ×1. Ninguna placa real se
pierde.

**Regla** (`ReglaRgm`): el SP no tenía reglas propias. La regla agrega una validación del **Modelo**:
año entre 1900 y el actual + 2, lo demás se anula con aviso. Motivo: la columna destino es `int` y
`#Carga` lo lleva como `bigint` (el motor no conoce el rango de la columna); un número enorme haría
fallar el `INSERT` de todo el cargue por desbordamiento. Rango real en `dbo.RGM`: 1991..2026 y un 2323.

**Conclusión: paridad OK.** 18 casos + 4 de fragilidad y del defecto de relaciones (22): 5 idénticos y
17 diferencias, todas explicadas; 0 predicciones fallidas.

Detalles:

- **RGM-B: `LiderID = CarteraID`.** El paso 2 inserta `SELECT DISTINCT fa.TitularID, fa.CarteraID,
  fa.CarteraID`: con el par (1, 29) el SP crea (29, 29), que viola `FK_TitularCartera_LiderCartera`
  (en la BD real el `INSERT` de relaciones falla entero). Con las carteras 4 y 22, que tienen varios
  líderes y entre ellos uno con el mismo número (pares activos (4, 4) y (22, 22)), el SP asignaba el
  titular a ese líder aunque el archivo dijera otro. El motor usa el par del archivo validado contra
  `Lider_Cartera` activo. El escenario principal usa el par (4, 4) para que este defecto no tape el resto.
- **R03, R04, R05, R06 (R).** Placas: `[objec` y `AVAL` anuladas (R03 queda sin datos: flag 0);
  `' HGK96'` → `HGK96`, `kya-12` → `KYA12`.
- **R08 (R).** Modelo 2323 anulado, el resto de la fila entra.
- **RGM-C (F).** Un Modelo no numérico (`'ABC'`, o `'2021.0'` de Excel) cabe en `RGMAuxiliar.Modelo`
  varchar(50) y hace fallar la conversión a `int` del `INSERT` a `RGM` con **error 245, que aborta el
  batch**: no entra ninguna fila del archivo y el dedup del paso 7 tampoco corre. El motor anula ese
  modelo con aviso ("valor no numérico") y lee `'2021.0'` como 2021 (caso T6 del harness).
- **R11, R12 (P5), R13 (E9), R14 (E11), R17 (P2).** Como en Fosyga: vacías, dedup global, repetido, vacíos
  convencionales.

#### Escenario RGM

Columnas del destino: `Marca · Modelo · Placa · EstadoEnRGM · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| R01 | Titular nuevo; nombre con tilde; marca, modelo y placa | CHEVROLET · 2022 · KNR827 · 1 · HOY | CHEVROLET · 2022 · KNR827 · 1 · HOY | Óscar Peña / OSCAR PEÑA | L4/C4 / L4/C4 | difiere: titular | ≠ E1 nombre limpio |
| R02 | Documento inválido (4 dígitos) | KIA · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L4/C4 / — | difiere: destino, titular, relación | ≠ P3 documento inválido |
| R03 | Placa '[objec' (21 filas así en dbo.RGM: '[object Object]' truncado a 6) | ∅ · ∅ · [objec · 1 · HOY | ∅ · ∅ · ∅ · 0 · HOY | PLACA BASURA / PLACA BASURA | L4/C4 / L4/C4 | difiere: destino | ≠ R placa validada (PlacaVehiculo.EsValida): anulada, flag 0 |
| R04 | Placa 'AVAL' con marca | RENAULT · ∅ · AVAL · 1 · HOY | RENAULT · ∅ · ∅ · 1 · HOY | PLACA AVAL / PLACA AVAL | L4/C4 / L4/C4 | difiere: destino | ≠ R placa validada: anulada, el resto entra |
| R05 | Placa ' HGK96' (espacio inicial, como en dbo.RGM) | AKT · 2019 · ' HGK96' · 1 · HOY | AKT · 2019 · HGK96 · 1 · HOY | PLACA ESPACIO / PLACA ESPACIO | L4/C4 / L4/C4 | difiere: destino | ≠ R placa normalizada ('HGK96') |
| R06 | Placa en minúsculas con guion 'kya-12' | ∅ · ∅ · kya-12 · 1 · HOY | ∅ · ∅ · KYA12 · 1 · HOY | PLACA MINUSCULA / PLACA MINUSCULA | L4/C4 / L4/C4 | difiere: destino | ≠ R placa normalizada ('KYA12') |
| R08 | Modelo 2323 (1 fila así en dbo.RGM) | MAZDA · 2323 · ABC123 · 1 · HOY | MAZDA · ∅ · ABC123 · 1 · HOY | MODELO RARO / MODELO RARO | L4/C4 / L4/C4 | difiere: destino | ≠ R modelo fuera de rango anulado |
| R09 | Existente, contenido idéntico | NISSAN · 2017 · IPR432 · 1 · HOY | NISSAN · 2017 · IPR432 · 1 · HOY | RGM EXISTENTE / RGM EXISTENTE | L4/C4 / L4/C4 | igual | = |
| R10 | Existente; otro vehículo (placa distinta) | HYUNDAI · 2014 · TZL873 · 1 · viejo 2026-01-15<br>MINI · 2011 · RHV471 · 1 · HOY | HYUNDAI · 2014 · TZL873 · 1 · viejo 2026-01-15<br>MINI · 2011 · RHV471 · 1 · HOY | OTRO VEHICULO / OTRO VEHICULO | L4/C4 / L4/C4 | igual | = |
| R11 | Vacía vieja en destino; ahora trae datos | BAJAJ · 2021 · OGT78F · 1 · HOY<br>∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | BAJAJ · 2021 · OGT78F · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L4/C4 / L4/C4 | difiere: destino | ≠ P5 política de vacías (el SP no purgaba) |
| R12 | Datos en destino; el archivo trae fila vacía | TVS · 2025 · VTJ69G · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · 0 · HOY | TVS · 2025 · VTJ69G · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L4/C4 / L4/C4 | difiere: destino | ≠ P5 la vacía no entra (el SP la insertaba) |
| R13 | Titular AJENO al archivo con duplicado exacto en destino | KIA · 2020 · GXR046 · 1 · viejo 2026-01-15 | KIA · 2020 · GXR046 · 1 · viejo 2026-01-15<br>KIA · 2020 · GXR046 · 1 · viejo 2026-01-15 | AJENO RGM / AJENO RGM | — / — | difiere: destino | ≠ E9 sin limpiezas globales (el paso 7 del SP deduplica toda la tabla) |
| R14 | Titular repetido con dos vehículos igual de completos | NISSAN · 2017 · IPR367 · 1 · HOY | NISSAN · 2018 · IPR432 · 1 · HOY | DOS VEHICULOS / DOS VEHICULOS | L4/C4 / L4/C4 | difiere: destino | ? SP conserva una fila arbitraria; nuevo la última (misma completitud) con aviso |
| R15 | Solo Marca | CREDIEMPLEADO · ∅ · ∅ · 1 · HOY | CREDIEMPLEADO · ∅ · ∅ · 1 · HOY | SOLO MARCA / SOLO MARCA | L4/C4 / L4/C4 | igual | = |
| R16 | Solo Modelo | ∅ · 2020 · ∅ · 1 · HOY | ∅ · 2020 · ∅ · 1 · HOY | SOLO MODELO / SOLO MODELO | L4/C4 / L4/C4 | igual | = |
| R17 | 'N/A' en Marca y '0' en Modelo | N/A · 0 · ∅ · 1 · HOY | ∅ · ∅ · ∅ · 0 · HOY | NA MARCA / NA MARCA | L4/C4 / L4/C4 | difiere: destino | ≠ P2 vacíos convencionales ('N/A','0' → NULL; flag 0) |
| R18 | Moto: marca, modelo y placa AAA99A válidos | YAMAHA · 2021 · LZB10H · 1 · HOY | YAMAHA · 2021 · LZB10H · 1 · HOY | MOTO VALIDA / MOTO VALIDA | L4/C4 / L4/C4 | igual | = |
| R19 | Titular existente con nombre mojibake en BD; archivo 'Piña Ruiz' | HONDA · ∅ · ∅ · 1 · HOY | HONDA · ∅ · ∅ · 1 · HOY | PIÃ‘A RUIZ / PIÑA RUIZ | L4/C4 / L4/C4 | difiere: titular | ≠ E1 nombre reparado |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 18, excluidas 1, duplicadas 1, expansión +0, candidatas 16, insertadas 14, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 11, nombres reparados 1, relaciones nuevas 16.
Carga (ejecución 270): insertadas 14, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 11, nombres reparados 1, relaciones 16.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Placa: valor que no es una placa anulado
- aviso ×1: Modelo: no es un año de modelo plausible, anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×16: Relaciones titular-cartera nuevas
- info ×11: Titulares nuevos que se crearán
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.RGM
- info ×1: Nombres de titular rotos en BD que se repararán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; RGM 1663543→1663543; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 12 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 17 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
PRINT: 2 DUPLICADOS FUERON ELIMINADOS DE #Dest
```

</details>

#### Escenario RGM-B

Columnas del destino: `Marca · Modelo · Placa · EstadoEnRGM · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| RB1 | Par activo con LiderID ≠ CarteraID (1, 29) | KIA · 2026 · PQS894 · 1 · HOY | KIA · 2026 · PQS894 · 1 · HOY | PAR NORMAL / PAR NORMAL | L29/C29 / L1/C29 | difiere: relación | ≠ relación: el SP inserta LiderID = CarteraID (viola FK_TitularCartera_LiderCartera en la BD real) |
| RB2 | Par inactivo (5, 16) | RENAULT · ∅ · ∅ · 1 · HOY | RENAULT · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L16/C16 / — | difiere: relación | ≠ E10 + LiderID = CarteraID en el SP |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 0, duplicadas 0, expansión +0, candidatas 2, insertadas 2, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 2, nombres reparados 0, relaciones nuevas 1.
Carga (ejecución 274): insertadas 2, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 2, nombres reparados 0, relaciones 1.
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- info ×2: Titulares nuevos que se crearán
- info ×1: Relaciones titular-cartera nuevas
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; RGM 1663543→1663543; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 2 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
PRINT: 0 DUPLICADOS FUERON ELIMINADOS DE #Dest
AVISO: 2 relaciones violarían FK_TitularCartera_LiderCartera
```

</details>

#### Escenario RGM-C

Columnas del destino: `Marca · Modelo · Placa · EstadoEnRGM · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| RC1 | Modelo 'ABC' (texto) | — | SUZUKI · ∅ · IVQ978 · 1 · HOY | MODELO TEXTO / MODELO TEXTO | L4/C4 / L4/C4 | difiere: destino | ≠ el nuevo lo anula con aviso; en el SP el INSERT a RGM falla entero (F) |
| RC2 | Fila normal en el mismo archivo | — | JAC · 2023 · ETL038 · 1 · HOY | FILA NORMAL / FILA NORMAL | L4/C4 / L4/C4 | difiere: destino | ≠ F: víctima del fallo de RC1 (sin fila en RGM en el SP) |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 0, duplicadas 0, expansión +0, candidatas 2, insertadas 2, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 2, nombres reparados 0, relaciones nuevas 2.
Carga (ejecución 276): insertadas 2, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 2, nombres reparados 0, relaciones 2.
- aviso ×1: Modelo: valor no numérico anulado
- info ×2: Relaciones titular-cartera nuevas
- info ×2: Titulares nuevos que se crearán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; RGM 1663543→1663543; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 2 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS
ERROR 245 (línea 61): Error de conversión al convertir el valor varchar 'ABC' al tipo de datos int.
```

</details>

## Auxiliar con datos

Tres Auxiliar del grupo tienen filas. **No se modificaron, no se vaciaron y no se ejecutó ningún SP**:
solo se leyeron (conteos, fechas y cruces contra el destino) el 2026-09-24. `TigoUneAuxiliar` y
`RuntAuxiliar` están vacías. Los `_CREATE.sql` de esta carpeta respaldan **solo la estructura**.

| Tabla | Filas | Qué es | Evidencia |
|---|---:|---|---|
| `FosygaAuxiliar` | 18.598 | **Carga fallida del 2026-09-23 (a medias)**: el SP corrió, pero ni `Titular` ni `Fosyga` recibieron las filas | ver abajo |
| `RGMAuxiliar` | 30.144 | **Residuo** de la carga del 2026-08-31, ya procesada | ver abajo |
| `TigoAuxiliar` | 661.072 | **Residuo** de las cargas de febrero-marzo de 2025, ya en `Tigo` | ver abajo |

**`FosygaAuxiliar` — carga fallida.** `FechaInsercion` 2026-09-23 11:33:21–11:33:24 (import) y
`FechaConsulta` 2026-09-23 11:33:33 en las 18.598 filas: el paso 6 del SP corrió 9 s después del import.
Pero `Nombre`, `CarteraID` y `LiderID` están en NULL en todas; 4.124 de los 18.598 titulares no existen
en `Titular`, y solo 545 filas tienen su contenido en `Fosyga` (455 con una fila de `Fosyga` del
2026-09-23, que vino de otras tandas de ese día: `Fosyga` recibió 85.666 filas el 09-22 y 174.764 el
09-23). La explicación que cuadra con todo es la de Fosyga-B: el `INSERT` a `Titular` falló entero por
`Nombre` NULL (error 515), el de `Titular_Cartera` por `CarteraID` NULL, y el de `Fosyga` por
`FK_Titular_Fosyga` (4.124 titulares inexistentes). El SP se modificó el 2026-09-22 14:04, un día antes;
la versión respaldada es la actual. **Es decir: hay 18.053 filas de afiliación que alguien intentó cargar
y no entraron.** Si se quieren, se pueden cargar por el módulo con un archivo que traiga el nombre (sin
nombre, los 4.124 titulares nuevos quedarían excluidos con error y el resto entraría). Alguien está
cargando Fosyga a mano **esta semana** con el flujo viejo.

**`RGMAuxiliar` — residuo.** Las 30.144 filas tienen `FechaInsercion` 2026-08-31 10:41 y
`FechaConsulta` 2026-08-31 10:41:47 (el SP las procesó). El 100 % (30.144) tiene su contenido
(Marca, Modelo, Placa) en `RGM` con `FechaConsulta = 2026-08-31`, que es la carga de `RGM` de ese día
(30.144 filas, 782 con datos: los mismos números). Todas con CarteraID 28 y LiderID 14; las 30.144 ya
tienen la relación (14, 28) en `Titular_Cartera`. El SP habría intentado insertar (28, 28) (defecto
`LiderID = CarteraID`), pero el `LEFT JOIN` por el par (14, 28) ya existente dejó 0 filas que insertar,
así que no llegó a chocar con la FK.

**`TigoAuxiliar` — residuo.** Sin columna `Nombre` y con `CarteraID`/`LiderID` en NULL en todas.
`FechaConsulta` 2025-02-20 (415.877), 2025-02-21 (245.027) y 2025-03-07 (168); `Tigo` tiene para esas
fechas 415.838, 244.989 y 168. Los 661.072 titulares están en `Tigo`; 661.005 (99,99 %) con el mismo
contenido y 660.932 además con la misma fecha. `Tigo` tiene una tanda más (2025-02-26, 365.337 filas)
que no pasó por esta Auxiliar, y nada posterior al 2025-03-14.

