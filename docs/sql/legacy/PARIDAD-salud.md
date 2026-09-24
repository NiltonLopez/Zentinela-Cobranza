# Paridad: SP legacy de la Fase 2 (grupo salud) vs Cargue de Fuentes de Datos (ADR-039)

Comparación, con los mismos datos de entrada, de lo que dejaban los seis
`sp_Procesar<Fuente>Auxiliar` del grupo salud (respaldados en esta carpeta) y lo que deja el motor
nuevo (`CargueFuentesMotor` + `Regla<Fuente>`). Corrida del **2026-09-24** contra DBCallCenter
(10.0.0.89), rama `feature/cargue-f2-salud`. Mismo método y misma herramienta que
[`PARIDAD.md`](PARIDAD.md) (Fase 1); aquí solo se resume lo que cambia.

## Resumen

| Fuente | Casos | Iguales | Diferencias | Sin explicar | Conclusión |
|---|---:|---:|---:|---:|---|
| Positiva | 23 | 4 | 19 | 0 | Paridad OK (E1, E2, E9–E11, P1–P6, R) |

"Sin explicar" = diferencia que no está en la leyenda (decisiones de ADR-039 o del plan) ni en el
comentario de la regla. Una diferencia sin explicar es un bug: se corrige y se vuelve a correr.

## Método

Igual que en la Fase 1 (ver [`PARIDAD.md`, "Método"](PARIDAD.md#método)). En resumen:

1. **Definición verificada.** Los seis `sp_Procesar*Auxiliar.sql` de esta carpeta se exportaron
   con `OBJECT_DEFINITION` y se verificó que `HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp))` es
   igual al SHA-256 del texto del archivo (sin BOM, en UTF-16LE): idénticos carácter por carácter.
   Los seis tienen `ANSI_NULLS ON` y `QUOTED_IDENTIFIER ON`.
2. **Lado viejo = cuerpo del SP como batch ad-hoc con sustitución por `#temp`.** Se quita la
   cabecera `CREATE PROCEDURE … AS` y se reemplaza, por palabra completa, cada tabla que el SP
   escribe: `<Fuente>Auxiliar → #Aux` (estructura exacta de `<Fuente>Auxiliar_CREATE.sql`),
   `<Fuente> → #Dest`, `Titular → #Titular`, `Titular_Cartera → #Titular_Cartera` (con
   `SELECT TOP 0 * INTO` y las PK reales). Las tablas que el SP solo lee (`ListaNegraCorreos`,
   `Lider_Cartera`) se leen directo. Antes de ejecutar, la herramienta quita comentarios y
   literales y **aborta** si el batch nombra una tabla real escribible (las 12 del grupo, las de la
   Fase 1, `Titular`, `Titular_Cartera`, auditoría) o si un `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`INTO`
   apunta a algo que no sea `#temp`, un alias o una CTE. **Ningún SP se ejecutó**, ni con ROLLBACK.
   Se corre con `ANSI_WARNINGS ON` y `XACT_ABORT OFF`, como en SSMS.
3. **Semillas.** El estado previo de cada caso (titular existente, filas previas en destino con
   `FechaConsulta = 2026-01-15`) se siembra igual en los dos lados: en las `#temp` del lado viejo
   y, del lado nuevo, en las tablas reales **dentro del rango del grupo salud**, sub-rangos
   999000001601..1899 (no se cruzan con los del harness, 1000..1599).
4. **Lado nuevo = el motor real**, con el `Harness` de `tools/CargueHarness` en el grupo `salud`
   (`ValidarAsync` + `ConfirmarAsync(simular: false)`, `Usuario = 'harness-cargue-salud'`), lectura
   del destino, `Titular` y `Titular_Cartera` del rango, y `Harness.Limpiar()`. La verificación de
   limpieza cuenta solo la auditoría del usuario del grupo: otros grupos corren su harness en
   paralelo y mueven los totales de `CargueFuenteEjecucion`.
5. **Comparación fila a fila por titular** (columnas de dato + flag; de la fecha, `HOY` o
   `viejo 2026-01-15`), más el nombre en `Titular` y las relaciones. Cada caso lleva una
   predicción escrita **antes** de correr ("=", "≠ categoría" o "? no determinista"); la
   herramienta marca las que no se cumplen.

Supuestos del import a la Auxiliar (los mismos de la Fase 1): celda vacía → NULL; `''` solo donde
el caso lo dice (import CSV); un valor que no cabe en el tipo de la Auxiliar hace fallar el import
de esa fila (el SP no la recibe; la herramienta lo anota); `FechaConsulta` de la Auxiliar llega
NULL, así que el `ROW_NUMBER() … ORDER BY FechaConsulta` (u `ORDER BY TitularID` en Coosalud y
SaviaSalud) conserva una fila arbitraria entre repetidos. Las `#temp` no tienen FK: se verifican
después.

**Par líder/cartera**: activo (1, 29); inactivo (5, 16), que existe en `Lider_Cartera` con
`Activo = 0`. El documento inválido `1234` solo existe en las `#temp` del lado viejo.

**Herramienta**: copia de la de la Fase 1 en el scratchpad de la sesión (`paridad-salud/tool`),
**fuera del repo**, enlazando `tools/CargueHarness/{Harness,Grupos,Casos/*}.cs` sin modificarlos.
Cambios respecto de la copia original: grupo `salud` y su usuario, lista de tablas prohibidas con
las 12 del grupo, alias/CTE de estos SP (`Duplicados`, `mv`) y conteo de auditoría por usuario.

## Leyenda de clasificación

Las mismas claves de [`PARIDAD.md`](PARIDAD.md#leyenda-de-clasificación):

| | |
|---|---|
| E1 | Nombres reparados/limpios (el SP no limpiaba) |
| E2 | Lista negra de correos exacta/dominio vs `LIKE`; correo vetado a NULL, no a `''` |
| E8 | Historial acumulativo: el nuevo refresca la fecha en vez de duplicar, borrar o ignorar |
| E9 | Sin limpiezas globales de toda la tabla |
| E10 | Sin relaciones con pares inactivos (o inexistentes) de `Lider_Cartera` |
| E11 | Dedup por titular conservando la fila con más datos |
| P1 | Validación genérica de contacto (celular 3xx de 10, fijo 6xx de 10, sin 57; email con forma mínima, en minúsculas, ≤ largo, contra `ListaNegraCorreos` **en toda columna Email**, también si el SP no la cruzaba: ADR-039 §4) |
| P2 | Vacíos convencionales: `''`, `'0'`, `'NULL'`, `'N/A'` → NULL (y el entero 0 → NULL) |
| P3 | Documento válido = 5 a 15 dígitos; si no, la fila se excluye |
| P4 | Clave de contenido = todas las columnas de dato |
| P5 | Política de vacías genérica (no entra una vacía de un titular con datos; se purgan sus vacías viejas), también donde el SP no purgaba o purgaba con otra condición |
| P6 | El flag solo cuenta columnas que se guardan en el destino, ya limpias |
| R | Documentada en el comentario de `Regla<Fuente>.cs` |
| F | Fragilidad del SP: un valor tumba la instrucción entera para todo el archivo |
| = | Resultado idéntico en destino, Titular y Titular_Cartera |

## Positiva

**Conclusión: paridad OK con 19 diferencias explicadas, 0 sin explicar.** Hay 4 casos idénticos:
el fijo 60x que llega en la columna de celular pasa al fijo (PO03, el paso 4 del SP), el titular
existente con contenido idéntico (PO11: el SP inserta y su dedup global deja la fila de hoy; el
motor refresca la fecha), la vacía vieja purgada cuando llegan los tres datos (PO14) y la fila
sin contacto con flag 0 (PO22).

Detalles:

- **PO04–PO06, paso 4 del SP.** El SP concatena `fijo + ', ' + celular` en
  `NumeroTelefonoFijo varchar(30)`, sin mirar qué había en el fijo: dos fijos (`6049876543,
  6012345678`), un celular y un fijo (`3001234560, 6012345670`) o basura y un fijo
  (`SIN DATO, 6012345671`). Ninguno es marcable. `ReglaPositiva` conserva el fijo de su columna,
  pasa el celular a la columna de celular cuando el proveedor las intercambió y reemplaza la
  basura, con advertencia en el primero y el último caso.
- **PO08, flag con `''`.** El paso 5 anula el correo vetado a `''` y el paso 6 mira `IS NULL`: el
  titular queda con `EstadoEnPositiva = 1` sin ningún dato. El motor deja NULL y flag 0.
- **PO13 y PO15, paso 10.** La purga de vacías exige una fila con celular **y** fijo **y** email a
  la vez (`AND` donde debía ir `OR`): un titular con solo celular conserva sus vacías (PO13) y
  una vacía nueva de un titular con datos entra y se queda (PO15). El motor aplica la política
  genérica (P5). Con los tres datos (PO14) coinciden.
- **PO23.** `573001110623` no cabe en `PositivaAuxiliar.NumeroTelefonoCelular varchar(10)`: el
  import habría fallado (supuesto del método). El motor quita el 57.


#### Escenario Positiva

Columnas del destino: `NumeroTelefonoCelular · NumeroTelefonoFijo · Email · FechaNacimiento · Departamento · Municipio · EstadoEnPositiva · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| PO01 | Titular nuevo; nombre con tilde; todos los campos | 3001234567 · 6051234567 · rocio@gmail.com · 1985-05-05 · ATLANTICO · BARRANQUILLA · 1 · HOY | 3001234567 · 6051234567 · rocio@gmail.com · 1985-05-05 · ATLANTICO · BARRANQUILLA · 1 · HOY | Rocío Muñoz / ROCIO MUÑOZ | L1/C29 / L1/C29 | difiere: titular | E1 solo el nombre |
| PO02 | Documento inválido (4 dígitos) | 3001234568 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| PO03 | Fijo 60x en la columna de celular, fijo vacío → pasa al fijo | ∅ · 6012345678 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · 6012345678 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO EN CELULAR / FIJO EN CELULAR | L1/C29 / L1/C29 | igual | = |
| PO04 | Fijo 60x en el celular y OTRO fijo en su columna | ∅ · 6049876543, 6012345678 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · 6049876543 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DOS FIJOS / DOS FIJOS | L1/C29 / L1/C29 | difiere: destino | R sin concatenar (el SP deja dos fijos en un varchar(30)) |
| PO05 | Columnas intercambiadas (fijo en celular, celular en fijo) | ∅ · 3001234560, 6012345670 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001234560 · 6012345670 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | INTERCAMBIADOS / INTERCAMBIADOS | L1/C29 / L1/C29 | difiere: destino | R cada número a su columna |
| PO06 | Fijo 60x en el celular y texto basura en el fijo | ∅ · SIN DATO, 6012345671 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · 6012345671 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO BASURA / FIJO BASURA | L1/C29 / L1/C29 | difiere: destino | R el fijo reemplaza la basura |
| PO07 | Celular + email exacto en ListaNegraCorreos ('no@gmail.com') | 3001110607 · ∅ · '' · ∅ · ∅ · ∅ · 1 · HOY | 3001110607 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CORREO VETADO / CORREO VETADO | L1/C29 / L1/C29 | difiere: destino | E2 el SP deja '' (no NULL) |
| PO08 | Solo email vetado ('no@gmail.com'), sin teléfonos | ∅ · ∅ · '' · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | SOLO CORREO VETADO / SOLO CORREO VETADO | L1/C29 / L1/C29 | difiere: destino | E2 + P6 el correo vetado queda NULL; el '' del SP contaba como dato (flag 1 sin datos) |
| PO09 | Email que solo cae por LIKE ('juana@gmail.com' contiene 'a@gmail.com') | 3001110609 · ∅ · '' · ∅ · ∅ · ∅ · 1 · HOY | 3001110609 · ∅ · juana@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | JUANA LIKE / JUANA LIKE | L1/C29 / L1/C29 | difiere: destino | E2 LIKE |
| PO10 | Celular '0' como único dato | 0 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | CELULAR CERO / CELULAR CERO | L1/C29 / L1/C29 | difiere: destino | P2 celular '0' → NULL (el SP lo guardaba con flag 1) |
| PO11 | Titular existente, contenido idéntico en destino | 3001110611 · 6041110611 · p11@gmail.com · 1980-01-01 · ANTIOQUIA · MEDELLIN · 1 · HOY | 3001110611 · 6041110611 · p11@gmail.com · 1980-01-01 · ANTIOQUIA · MEDELLIN · 1 · HOY | POSITIVA EXISTENTE / POSITIVA EXISTENTE | L1/C29 / L1/C29 | igual | = |
| PO12 | Existente; solo cambia Municipio | 3001110612 · ∅ · ∅ · ∅ · ANTIOQUIA · ENVIGADO · 1 · HOY | 3001110612 · ∅ · ∅ · ∅ · ANTIOQUIA · ENVIGADO · 1 · HOY<br>3001110612 · ∅ · ∅ · ∅ · ANTIOQUIA · MEDELLIN · 1 · viejo 2026-01-15 | CAMBIA MUNICIPIO / CAMBIA MUNICIPIO | L1/C29 / L1/C29 | difiere: destino | P4 clave completa (el SP borraba la fila vieja) |
| PO13 | Vacía vieja en destino; ahora trae solo celular | 3001110613 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | 3001110613 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | VACIA SOLO CELULAR / VACIA SOLO CELULAR | L1/C29 / L1/C29 | difiere: destino | P5 el paso 10 del SP exige celular Y fijo Y email para purgar la vacía |
| PO14 | Vacía vieja en destino; ahora trae celular, fijo y email | 3001110614 · 6041110614 · p14@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | 3001110614 · 6041110614 · p14@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | VACIA COMPLETO / VACIA COMPLETO | L1/C29 / L1/C29 | igual | = |
| PO15 | Datos (solo celular) en destino; el archivo trae fila vacía | 3001110615 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | 3001110615 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | difiere: destino | P5 el SP inserta la vacía y no la purga (su paso 10 exige los tres datos) |
| PO16 | Titular AJENO al archivo con duplicado exacto en destino | 3001110616 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110616 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3001110616 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | AJENO AL ARCHIVO / AJENO AL ARCHIVO | — / — | difiere: destino | E9 sin limpiezas globales (el SP deduplica toda la tabla) |
| PO17 | Titular repetido; la 2ª fila tiene más datos | 3001110617 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110617 · ∅ · rep17@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | E11 el SP conserva una fila arbitraria; el nuevo, la más completa |
| PO18 | Par Líder/Cartera inactivo | 3001110618 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110618 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 sin relación con par inactivo |
| PO19 | Celular de 7 dígitos ('3001234') + email | 3001234 · ∅ · p19@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · p19@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | CEL CORTO / CEL CORTO | L1/C29 / L1/C29 | difiere: destino | P1 celular de 10 dígitos |
| PO20 | Celular en la columna de fijo, celular vacío | ∅ · 3001110620 · p20@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · p20@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | CEL EN FIJO / CEL EN FIJO | L1/C29 / L1/C29 | difiere: destino | P1 celular en la columna de fijo se anula (el SP lo guardaba como fijo) |
| PO21 | Fijo de 7 dígitos ('2345678') + email | ∅ · 2345678 · p21@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · p21@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | FIJO CORTO / FIJO CORTO | L1/C29 / L1/C29 | difiere: destino | P1 fijo de 10 dígitos |
| PO22 | Solo FechaNacimiento y Departamento (no entran al flag) | ∅ · ∅ · ∅ · 1990-03-03 · CESAR · ∅ · 0 · HOY | ∅ · ∅ · ∅ · 1990-03-03 · CESAR · ∅ · 0 · HOY | SIN CONTACTO / SIN CONTACTO | L1/C29 / L1/C29 | igual | = |
| PO23 | Celular con indicativo 57 ('573001110623') | — | 3001110623 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — / CON INDICATIVO | — / L1/C29 | difiere: destino, titular, relación | P1 el 57 se quita (en el SP la fila no cabe en PositivaAuxiliar: varchar(10)) |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 23, excluidas 1, duplicadas 1, expansión +0, candidatas 21, insertadas 19, refrescadas 1, vacías descartadas 1, vacías a purgar 2, titulares nuevos 16, nombres reparados 0, relaciones nuevas 20.
Carga (ejecución 240): insertadas 19, refrescadas 1, vacías descartadas 1, purgadas 2, titulares nuevos 16, nombres reparados 0, relaciones 20.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Email: email en lista negra anulado
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: NumeroTelefonoCelular: celular inválido anulado
- aviso ×1: NumeroTelefonoCelular: fijo en la columna de celular descartado (la columna de fijo ya tenía otro fijo)
- aviso ×1: NumeroTelefonoFijo: celular en la columna de fijo, anulado
- aviso ×1: NumeroTelefonoFijo: fijo inválido anulado
- aviso ×1: NumeroTelefonoFijo: valor inválido reemplazado por el fijo que venía en la columna de celular
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×20: Relaciones titular-cartera nuevas
- info ×16: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×2: Filas vacías viejas que se eliminarán de dbo.Positiva
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Positiva 2213891→2213891; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
PO23: fila no importable a PositivaAuxiliar (NumeroTelefonoCelular='573001110623' excede varchar(10)); el SP no la recibe
── ejecución del cuerpo del SP ──
PRINT: 16 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 21 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS
PRINT: 21 PERSONAS FUERON INGRESADAS A POSITIVA
PRINT: 3 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```
</details>

## Hallazgos

Se completan al cerrar el grupo (ver el reporte de cada fuente arriba).
