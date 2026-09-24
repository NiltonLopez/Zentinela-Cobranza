# Paridad: SP legacy de la Fase 1 vs Cargue de Fuentes de Datos (ADR-039, etapa E)

Comparación, con los mismos datos de entrada, de lo que dejaban los seis
`sp_Procesar<Fuente>Auxiliar` (respaldados en esta carpeta) y lo que deja el motor nuevo
(`CargueFuentesMotor` + `Regla<Fuente>`). Corrida del **2026-09-24** contra DBCallCenter
(10.0.0.89), rama `feature/cargue-fuentes` en `d4e0a17`.

## Resumen

| Fuente | Casos | Iguales | Diferencias | Sin explicar | Conclusión |
|---|---:|---:|---:|---:|---|
| Sura (+ Sura-B) | 25 | 9 | 16 | 0 | Paridad OK con 16 diferencias explicadas (E1, E2, E9, E10, E11, P1–P4, F) |
| Cajacopi | 20 | 7 | 13 | 1 (C10, solo de documentación) | Paridad OK con 12 diferencias explicadas; C10 aplica ListaNegraCorreos, que el SP de Cajacopi no tenía y ningún documento lo dice (D1) |
| MutualSer (A + B + C) | 26 | 11 | 15 | 0 | Paridad OK con 15 diferencias explicadas (E1, E2, E4, E9–E11, P1, P3, R, F) |
| NuevaEps | 14 | 5 | 9 | 0 | Paridad OK con 9 diferencias explicadas (E1, E6, E9–E11, P1–P4) |
| SIMIT | 15 | 2 | 13 | 0 | Paridad OK con 13 diferencias explicadas (E1, E7, E9–E11, P3, P5, R) |
| DatosClaro (+ DatosClaro-B) | 19 | 8 | 11 | 0 | Paridad OK con 11 diferencias explicadas; B1: el motor acepta teléfonos de 10 dígitos que no empiezan en 3 ni en 6 (igual que el SP, pero contra la definición) |
| **Total** | **119** | **42** | **77** | **1** | **No hay pérdida ni corrupción de datos atribuible al motor nuevo.** |

"Sin explicar" = diferencia que no está en la lista de esperadas de la tarea, ni en el plan, ni en
el comentario de la regla. Solo hay una (C10) y el comportamiento es el correcto según el plan: lo
que está mal es el comentario de `ReglaCajacopi`.

Hallazgos (detalle y reproducción mínima al final):

- **B1** (bajo): la columna tipo `Telefono` (DatosClaro) acepta cualquier número de 10 dígitos
  (`'5551234567'`), aunque la definición dice "celular 3xx o fijo 60x". El SP tampoco validaba,
  así que hay paridad, pero el motor no cumple su propia definición. Hoy el 100 % de
  `dbo.DatosClaro.NumeroTelefono` (2.576.595 filas) es 3xx de 10 dígitos: sin impacto actual.
- **B2** (bajo): un nombre de más de 70 caracteres se trunca sin advertencia (SB1).
- **B3** (cosmético): un email más largo que la columna destino se informa como "formato inválido" (DB1).
- **D1–D3**: tres comentarios de regla que describen mal al SP (Cajacopi ×2, MutualSer ×1).
- **O1**: en Sura `UltimaFechaConsulta` entra en la clave de contenido. Si una recarga trae el
  mismo dato con otra `UltimaFechaConsulta`, se inserta una fila nueva en vez de refrescar la
  fecha. El SP la dejaba fuera de su clave (S17). Hoy la columna es NULL en las 3.749 filas de
  `dbo.Sura`: sin impacto actual. Queda como decisión pendiente.
- **O5**: el nombre limpio quita dígitos y puntuación ("Inversiones 3A S.A.S." → "INVERSIONES A S A S").
  Es lo que decidió el plan, pero en una razón social se pierde información.
- **F**: la comparación encontró cuatro fragilidades del SP en las que un solo valor tumba un paso
  entero para todo el archivo. En esos casos el motor nuevo sigue fila a fila. Los números
  confirman las diferencias esperadas E1, E4 y P1.

## Método

**Sin tocar tablas reales con los SP.** Ningún SP se ejecutó, ni siquiera con ROLLBACK. Para
cada fuente:

1. **Definición verificada.** Se comparó `OBJECT_DEFINITION` de los seis SP con los archivos
   `sp_Procesar*.sql` de esta carpeta. El código es idéntico; solo difieren las tildes de
   comentarios, por la codificación de salida de `sqlcmd`.
2. **Lado viejo = cuerpo del SP como batch ad-hoc con sustitución por `#temp`.** Se quita la
   cabecera `CREATE PROCEDURE … AS` y se reemplaza, por palabra completa, cada tabla que el SP
   escribe:
   `<Fuente>Auxiliar → #Aux` (estructura exacta de `<Fuente>Auxiliar_CREATE.sql`),
   `<Fuente> → #Dest`, `Titular → #Titular` y `Titular_Cartera → #Titular_Cartera` (las tres con
   `SELECT TOP 0 * INTO`, más las PK reales). Las tablas que el SP solo lee (`ListaNegraCorreos`,
   `PrefijoMunicipio`, `ListaNegraTelefonosAuxiliar`) se leen directo. Antes de ejecutar, la
   herramienta quita comentarios y literales del batch y aborta en dos casos: si queda algún
   nombre de tabla real escribible, o si un `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`INTO` apunta a
   algo que no sea `#temp`, un alias sobre `#temp` o una CTE. Además se revisó a mano la lista de
   destinos de escritura de los 10 batches. Se ejecuta con `ANSI_WARNINGS ON` y
   `XACT_ABORT OFF`, como en SSMS: un error de truncamiento corta la instrucción, no el
   batch. Así el SP se comporta igual que en producción.
3. **Semillas.** El estado previo de cada caso (titular existente, filas previas en destino con
   `FechaConsulta = 2026-01-15`) se siembra igual en los dos lados: en las `#temp` del lado viejo
   y, del lado nuevo, en las tablas reales **dentro del rango reservado** 999000000601..899, que no
   se cruza con los sub-rangos del harness (001..599).
4. **Lado nuevo = el motor real.** Se usa el mismo `Harness` de `tools/CargueHarness`:
   `ValidarAsync` + `ConfirmarAsync(simular: false)` con ejecución registrada
   (`Usuario = 'harness-cargue'`). Después se lee lo que quedó en destino, `Titular` y
   `Titular_Cartera` del rango, y `Harness.Limpiar()` borra el rango y la auditoría. Se usa carga
   real y no `simular: true` porque el rollback no deja ver las filas escritas. Solo se tocan
   filas del rango y de la auditoría del harness, lo mismo que hace el harness en cada corrida.
5. **Comparación fila a fila por titular**: todas las columnas de dato más el flag; de la fecha
   solo si es la de hoy (`HOY`) o la sembrada (`viejo 2026-01-15`). También el nombre en `Titular`
   y las relaciones en `Titular_Cartera`. Cada caso llevaba una predicción escrita **antes** de
   correr ("=" o "≠ categoría"); la herramienta marca cuando una predicción no se cumple. En la
   corrida final no falló ninguna. Las que fallaron en corridas previas eran errores del caso,
   no del motor:
   - `'maria@hotmail.com'` también cae por LIKE. Se conservó como S03.
   - `'1112345'` prefijado no contiene `11111`. Se cambió a `'1111234'`.
   - `3001234567` está en `ListaNegraTelefonosAuxiliar`. Se cambió el número.
   - Nombres sintéticos con dígitos (`'CEL2 …'`): el limpiador quita el dígito (E1). Se cambió
     el nombre y se agregó S22 para cubrirlo a propósito.

**Supuestos sobre cómo llegaba el archivo a la Auxiliar** (el import de SSMS):

- Una celda vacía llega como NULL, como en un import de Excel. `''` llega solo donde el caso lo
  indica (import CSV: S21, M19, P06).
- Un valor que no cabe en el tipo de la Auxiliar hace fallar el import. Ningún caso lo necesitó.
  Los casos B se diseñaron para caber en la Auxiliar y **no** en el destino.
- `FechaConsulta` de la Auxiliar llega NULL. Por eso el `ROW_NUMBER() … ORDER BY FechaConsulta`
  de los SP elige una fila arbitraria entre repetidos.
- **Única desviación de estructura:** `#Aux.TitularID` de NuevaEps es `varchar(30)` y no
  `varchar(10)`, porque los TitularID del rango reservado tienen 12 dígitos. La lógica del SP
  (conversión implícita varchar↔bigint) no cambia.
- Las FK no existen en las `#temp`. Se verifican después: solo en Sura-B se habrían violado
  (ver F).
- Las fechas del lado viejo van como `'yyyymmdd'`. En la primera corrida, `'2026-08-01'` en una
  columna `datetime` con el login en español (dmy) se leyó como 8 de enero. Era un artefacto de
  la prueba, ya corregido.

**Par líder/cartera** usado: activo (1, 29); inactivo (5, 16), que existe en `Lider_Cartera` con
`Activo = 0` y por eso satisface la FK. El documento inválido `1234` solo existe en las `#temp` del
lado viejo; el nuevo lo excluye, y se verificó que `Titular` y el destino reales no ganaron filas
con `1234`.

**Limpieza verificada** al final de cada escenario y en conjunto: el rango
999000000001..999 tiene 0 filas en `Titular`, `Titular_Cartera`, `Sura`, `Cajacopi`, `MutualSer`,
`NuevaEps` (TitularID texto, con `TRY_CAST`), `SIMIT` y `DatosClaro`. `CargueFuenteEjecucion` y
`CargueFuenteReversion` terminan con 0 filas, igual que al empezar. El `COUNT(*)` de cada destino
es el inicial: Sura 3.749; Cajacopi 1.277.608; MutualSer 864.703; NuevaEps 62.834; SIMIT 301.840;
DatosClaro 3.528.387.

**Herramientas**: el proyecto `Paridad` (se llama `CargueHarness` para heredar el
`InternalsVisibleTo`; enlaza `tools/CargueHarness/Harness.cs` sin modificarlo), los batches
generados (`batches/*.sql`) y las salidas crudas quedaron en el scratchpad de la sesión, **fuera
del repo**. No se modificó código del repo.

## Leyenda de clasificación

**E**: diferencia esperada de la lista de la etapa E.

| | |
|---|---|
| E1 | Nombres reparados/limpios (el SP no limpiaba) |
| E2 | Lista negra de correos exacta/dominio vs LIKE |
| E3 | Flag de Cajacopi calculado después de limpiar |
| E4 | MutualSer sin concatenar/truncar |
| E5 | DatosClaro con `ListaNegraTelefonos` real y validación de teléfono |
| E6 | NuevaEps: TitularID sin ceros a la izquierda; números de 7 dígitos descartados |
| E7 | SIMIT con placas validadas (marcas → sin placa; guion y espacios fuera) |
| E8 | Historial acumulativo: el nuevo refresca la fecha en vez de duplicar/borrar |
| E9 | Sin limpiezas globales de toda la tabla |
| E10 | Sin relaciones con pares inactivos de `Lider_Cartera` |
| E11 | Dedup por titular conservando la fila con más datos |

**P**: por diseño del plan ("Decisiones acordadas" / pipeline), aunque no esté en la lista de la
tarea. Conviene que el ADR-039 las nombre explícitamente.

| | |
|---|---|
| P1 | Validación genérica de contacto en toda fuente: celular = 10 dígitos que empiezan en 3 (solo dígitos, sin 57); fijo = 10 dígitos que empiezan en 6; email con forma mínima, en minúsculas, sin espacios, ≤ largo de la columna y contra `ListaNegraCorreos`. Lo inválido se anula con advertencia |
| P2 | Vacíos convencionales: `''`, `'0'`, `'NULL'`, `'N/A'` → NULL (y el entero 0 → NULL) |
| P3 | Documento válido = 5 a 15 dígitos; si no, la fila se excluye |
| P4 | Clave de contenido = todas las columnas de dato (el SP usaba una clave recortada y borraba la fila vieja) |
| P5 | Política de vacías genérica también en SIMIT (su SP no purgaba vacías) |

**R**: documentada en el comentario de `Regla<Fuente>.cs`.
**N**: no documentada en ningún lado.
**F**: fragilidad del SP; un valor tumba la instrucción entera para todo el archivo.
**=**: resultado idéntico en destino, Titular y Titular_Cartera.

## Sura

**Conclusión: paridad OK con 16 diferencias explicadas.** Hay 9 casos idénticos: titular
existente con dato igual (refresco = insert + dedup del SP), dato distinto (conviven ambas filas),
vacía vieja purgada, fila vacía de un titular con datos, email vetado exacto y por dominio,
titular sin datos, email `''` y titular existente con nombre sano (ninguno de los dos lo cambia).

Detalles:

- **S13, SP no determinista.** El SP deduplica la Auxiliar con `ORDER BY FechaConsulta`, que
  llega NULL, así que conserva una fila cualquiera. En esta corrida se quedó con la **menos**
  completa.
- **S03, LIKE.** La lista negra con LIKE anula emails legítimos: `'maria@hotmail.com'` contiene
  la entrada `'a@hotmail.com'`.
- **S17 (ver O1).** El SP conserva solo la fila con la `UltimaFechaConsulta` nueva. El motor
  guarda las dos porque la fecha es parte de la clave.
- **Sura-B.** Un nombre de 75 caracteres (cabe en `SuraAuxiliar.Nombre varchar(80)`) hace fallar
  el `INSERT` a `Titular` para **todo** el archivo: SB2, un titular normal, queda sin crear. En la
  BD real, además, las relaciones de ese archivo fallarían por `FK_TitularCartera_Titular`. El
  motor trunca el nombre a 70, sin advertencia (B2).

#### Escenario Sura

Columnas del destino: `NumeroCelular · Email · NIT · Empresa · TipoAfiliado · UltimaFechaConsulta · EstadoAfiliacion · EstadoEnSura · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| S01 | Titular nuevo; nombre con tilde; email con mayúsculas; todos los campos | 3001234567 · Juan.Perez@Gmail.com · 900123456 · ACME SAS · COTIZANTE · 2026-08-01 · ACTIVO · 1 · HOY | 3001234567 · juan.perez@gmail.com · 900123456 · ACME SAS · COTIZANTE · 2026-08-01 · ACTIVO · 1 · HOY | Juan Pérez / JUAN PEREZ | L1/C29 / L1/C29 | difiere: destino, titular | E1 nombres + P1 email en minúsculas |
| S02 | Documento inválido (4 dígitos) | 3001234568 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| S03 | Fijo en la columna de celular; email 'maria@hotmail.com' | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | ∅ · maria@hotmail.com · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | MARIA GOMEZ / MARIA GOMEZ | L1/C29 / L1/C29 | difiere: destino | E2 LIKE: 'maria@hotmail.com' contiene la entrada 'a@hotmail.com' |
| S04 | Titular existente, contenido idéntico en destino | 3001110004 · carlos@gmail.com · ∅ · ACME · COTIZANTE · 2026-01-01 · ACTIVO · 1 · HOY | 3001110004 · carlos@gmail.com · ∅ · ACME · COTIZANTE · 2026-01-01 · ACTIVO · 1 · HOY | CARLOS EXISTENTE / CARLOS EXISTENTE | L1/C29 / L1/C29 | igual | = |
| S05 | Titular existente, dato distinto (celular nuevo) | 3001110005 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3002220005 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110005 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3002220005 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DIANA CAMBIA / DIANA CAMBIA | L1/C29 / L1/C29 | igual | = |
| S06 | Titular existente con nombre mojibake en BD; archivo 'Piña Ruiz' | 3001110006 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110006 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PIÃ‘A RUIZ / PIÑA RUIZ | L1/C29 / L1/C29 | difiere: titular | E1 nombre reparado |
| S07 | Vacía vieja en destino; ahora trae datos | 3001110007 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110007 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | igual | = |
| S08 | Datos en destino; el archivo trae fila vacía | 3001110008 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110008 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | igual | = |
| S09 | Titular AJENO al archivo con duplicado exacto + vacía en destino | 3001110009 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110009 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3001110009 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | AJENO AL ARCHIVO / AJENO AL ARCHIVO | — / — | difiere: destino | E9 sin limpiezas globales |
| S10 | Email exacto en ListaNegraCorreos ('no@gmail.com') | 3001110010 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110010 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ANA NEGRA / ANA NEGRA | L1/C29 / L1/C29 | igual | = |
| S11 | Email que solo cae por LIKE ('juana@gmail.com' contiene 'a@gmail.com') | 3001110011 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110011 · juana@gmail.com · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | JUANA LIKE / JUANA LIKE | L1/C29 / L1/C29 | difiere: destino | E2 LIKE |
| S12 | Email sin arroba | 3001110012 · correo-sin-arroba.com · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110012 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | LUIS FORMATO / LUIS FORMATO | L1/C29 / L1/C29 | difiere: destino | P1 formato de email |
| S13 | Titular repetido; la 2ª fila tiene más datos | 3001110013 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110013 · rep@gmail.com · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | E11 dedup (el SP se quedó con la fila MENOS completa) |
| S14 | Titular nuevo sin ningún dato | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | SIN DATOS / SIN DATOS | L1/C29 / L1/C29 | igual | = |
| S15 | Solo 'N/A' en TipoAfiliado y '0' en Empresa | ∅ · ∅ · ∅ · 0 · N/A · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | NA TEXTO / NA TEXTO | L1/C29 / L1/C29 | difiere: destino | P2 vacíos convencionales |
| S16 | Par Líder/Cartera inactivo | 3001110016 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110016 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 par inactivo |
| S17 | Mismo contenido, solo cambia UltimaFechaConsulta | 3001110017 · ∅ · ∅ · ∅ · COTIZANTE · 2026-02-01 · ∅ · 1 · HOY | 3001110017 · ∅ · ∅ · ∅ · COTIZANTE · 2026-01-01 · ∅ · 1 · viejo 2026-01-15<br>3001110017 · ∅ · ∅ · ∅ · COTIZANTE · 2026-02-01 · ∅ · 1 · HOY | UFC CAMBIA / UFC CAMBIA | L1/C29 / L1/C29 | difiere: destino | P4 clave completa (ver O1) |
| S18 | Celular de 10 caracteres con letra ('300123456X') | 300123456X · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | CEL LETRAS / CEL LETRAS | L1/C29 / L1/C29 | difiere: destino | P1 celular solo dígitos |
| S19 | Email de dominio vetado ('x@uniquindio.edu.co') | 3001110019 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110019 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DOMINIO NEGRO / DOMINIO NEGRO | L1/C29 / L1/C29 | igual | = |
| S20 | NIT '0' | 3001110020 · ∅ · 0 · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110020 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | NIT CERO / NIT CERO | L1/C29 / L1/C29 | difiere: destino | P2 vacíos convencionales |
| S21 | Email '' (import CSV) → rama 'correos vacíos' del SP | 3001110021 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110021 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | EMAIL VACIO / EMAIL VACIO | L1/C29 / L1/C29 | igual | = |
| S22 | Nombre con dígitos y puntos ('Inversiones 3A S.A.S.') | 3001110022 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110022 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | Inversiones 3A S.A.S. / INVERSIONES A S A S | L1/C29 / L1/C29 | difiere: titular | E1 nombres (dígitos y puntuación fuera, ver O5) |
| S23 | Titular existente con nombre sano; el archivo trae otro nombre | 3001110023 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110023 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | NOMBRE VIEJO / NOMBRE VIEJO | L1/C29 / L1/C29 | igual | = |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 23, excluidas 1, duplicadas 1, expansión +0, candidatas 21, insertadas 19, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 14, nombres reparados 1, relaciones nuevas 20.
Carga (ejecución 177): insertadas 19, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 14, nombres reparados 1, relaciones 20.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Email: email en lista negra anulado
- aviso ×1: Email: email con formato inválido anulado
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: NumeroCelular: celular inválido anulado
- aviso ×1: NumeroCelular: número fijo en la columna de celular, anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×20: Relaciones titular-cartera nuevas
- info ×14: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.Sura
- info ×1: Nombres de titular rotos en BD que se repararán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Sura 3749→3749; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 15 PERSONAS FUERON INGRESADAS A LA TABLA TITULAR
PRINT: 22 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA
PRINT: 1 DUPLICADOS FUERON ELIMINADOS DE SURA AUXILIAR
PRINT: 22 PERSONAS FUERON INGRESADAS A SURA
PRINT: 3 DUPLICADOS FUERON ELIMINADOS DE SURA
PRINT: 3 REGISTROS VACIOS FUERON ELIMINADOS DE SURA
```
</details>

#### Escenario Sura-B

Columnas del destino: `NumeroCelular · Email · NIT · Empresa · TipoAfiliado · UltimaFechaConsulta · EstadoAfiliacion · EstadoEnSura · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| SB1 | Titular nuevo con nombre de 75 caracteres | 3001110641 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110641 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — / NOMBRE MUY LARGO DE UNA RAZON SOCIAL QUE NO CABE EN SETENTA CARACTERES | L1/C29 / L1/C29 | difiere: titular | E1 nombre ≤ 70 · F: en el SP el INSERT a Titular falla entero |
| SB2 | Titular nuevo normal en el mismo archivo | 3001110642 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110642 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — / TITULAR NORMAL | L1/C29 / L1/C29 | difiere: titular | F: víctima del fallo de SB1 (sin Titular en el SP) |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 0, duplicadas 0, expansión +0, candidatas 2, insertadas 2, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 2, nombres reparados 0, relaciones nuevas 2.
Carga (ejecución 178): insertadas 2, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 2, nombres reparados 0, relaciones 2.
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×2: Relaciones titular-cartera nuevas
- info ×2: Titulares nuevos que se crearán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Sura 3749→3749; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
ERROR 2628 (línea 8): Los datos binarios o de la cadena se truncan en la columna "Nombre" de la tabla "#Titular". Valor truncado: "NOMBRE MUY LARGO DE UNA RAZON SOCIAL QUE NO CABE EN SETENTA CARACTERES".
PRINT: Se terminó la instrucción.
PRINT: 0 PERSONAS FUERON INGRESADAS A LA TABLA TITULAR
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA
PRINT: 0 DUPLICADOS FUERON ELIMINADOS DE SURA AUXILIAR
PRINT: 2 PERSONAS FUERON INGRESADAS A SURA
PRINT: 0 DUPLICADOS FUERON ELIMINADOS DE SURA
PRINT: 0 REGISTROS VACIOS FUERON ELIMINADOS DE SURA
AVISO: 2 relaciones violarían FK_TitularCartera_Titular (en la BD real el INSERT de Titular_Cartera habría fallado entero)
```
</details>

## Cajacopi

**Conclusión: paridad OK con 12 diferencias explicadas y 1 sin documentar (C10: correcta según
el plan, pero el comentario de la regla describe mal al SP, ver D1).** Hay 7 casos idénticos,
entre ellos:

- el prefijo por departamento (C01, `'3456789'` → `'6053456789'`);
- el prefijo por la rama municipio del SP (C14, `'Atlántico'` con tilde no casa en el SP, pero
  `'Barranquilla'` sí);
- el `'NOTIENE@…'` anulado;
- el celular en la columna de fijo y el fijo en la de celular;
- el titular con contenido idéntico.

Detalles:

- **C06.** `'1111234'` + BOGOTA → `'6011111234'`: el SP lo anula en el paso 9 (el número
  prefijado contiene `11111`); el motor lo conserva, porque mira los dígitos que mandó la fuente,
  como dice su comentario. La fila del SP queda además sin datos y con flag 1 (E3).
- **C13.** Un fijo de 7 dígitos que empieza en 6, con departamento desconocido, **se guardaba de
  7 dígitos** en el SP (`'6123456'`): el paso 9 solo anula lo que no empieza en 6. El comentario
  de la regla dice que "el paso 9 lo anulaba después"; eso vale solo para los que no empiezan
  en 6 (D2).
- **C20.** El paso 9 del SP corre sobre toda la tabla `Cajacopi` en cada carga: anuló fijo y
  email de un titular que no venía en el archivo.

#### Escenario Cajacopi

Columnas del destino: `NumeroTelefonoCelular · NumeroTelefonoFijo · Email · Departamento · Municipio · FechaNacimiento · EstadoEnCajacopi · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| C01 | Titular nuevo; fijo de 7 con ATLANTICO/BARRANQUILLA | 3001234567 · 6053456789 · jose@gmail.com · ATLANTICO · BARRANQUILLA · 1985-05-05 · 1 · HOY | 3001234567 · 6053456789 · jose@gmail.com · ATLANTICO · BARRANQUILLA · 1985-05-05 · 1 · HOY | José Núñez / JOSE NUÑEZ | L1/C29 / L1/C29 | difiere: titular | E1 nombres |
| C02 | Documento inválido (4 dígitos) | 3001234568 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| C03 | Fijo de 7 con Departamento 'BOGOTA D.C.' (la tabla dice 'BOGOTA') | 3001110653 · ∅ · ∅ · BOGOTA D.C. · ∅ · ∅ · 1 · HOY | 3001110653 · 6012345678 · ∅ · BOGOTA D.C. · ∅ · ∅ · 1 · HOY | BOGOTA DC / BOGOTA DC | L1/C29 / L1/C29 | difiere: destino | R prefijo sin tildes/puntos (Bogotá) |
| C04 | Titular existente, contenido idéntico | 3001110654 · 6051234567 · c4@gmail.com · ATLANTICO · BARRANQUILLA · 1980-01-01 · 1 · HOY | 3001110654 · 6051234567 · c4@gmail.com · ATLANTICO · BARRANQUILLA · 1980-01-01 · 1 · HOY | CAJA EXISTENTE / CAJA EXISTENTE | L1/C29 / L1/C29 | igual | = |
| C05 | Existente; solo cambia Municipio | 3001110655 · ∅ · ∅ · ATLANTICO · SOLEDAD · ∅ · 1 · HOY | 3001110655 · ∅ · ∅ · ATLANTICO · BARRANQUILLA · ∅ · 1 · viejo 2026-01-15<br>3001110655 · ∅ · ∅ · ATLANTICO · SOLEDAD · ∅ · 1 · HOY | CAMBIA MUNICIPIO / CAMBIA MUNICIPIO | L1/C29 / L1/C29 | difiere: destino | P4 clave completa |
| C06 | Fijo '1111234' + BOGOTA → '6011111234' (contiene 11111 solo tras prefijar) | ∅ · ∅ · ∅ · BOGOTA · ∅ · ∅ · 1 · HOY | ∅ · 6011111234 · ∅ · BOGOTA · ∅ · ∅ · 1 · HOY | ONCES PREFIJO / ONCES PREFIJO | L1/C29 / L1/C29 | difiere: destino | R '11111' sobre los dígitos de la fuente + E3 flag |
| C07 | Solo fijo de relleno '1111111' | ∅ · ∅ · ∅ · ATLANTICO · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ATLANTICO · ∅ · ∅ · 0 · HOY | SOLO RELLENO / SOLO RELLENO | L1/C29 / L1/C29 | difiere: destino | E3 flag post-limpieza |
| C08 | Email 'NO TIENE' (con espacio) | 3001110658 · ∅ · NO TIENE · ∅ · ∅ · ∅ · 1 · HOY | 3001110658 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | NO TIENE ESPACIO / NO TIENE ESPACIO | L1/C29 / L1/C29 | difiere: destino | R 'NOTIEN' sin espacios |
| C09 | Email 'NOTIENE@GMAIL.COM' | 3001110659 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110659 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | NO TIENE PEGADO / NO TIENE PEGADO | L1/C29 / L1/C29 | igual | = |
| C10 | Email exacto en ListaNegraCorreos ('no@gmail.com') | 3001110660 · ∅ · no@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | 3001110660 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CAJA NEGRA / CAJA NEGRA | L1/C29 / L1/C29 | difiere: destino | **N** el SP no tenía lista negra de correos (ver D1) |
| C11 | Fijo 60x en la columna de celular (con email) | ∅ · ∅ · c11@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · c11@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | FIJO EN CEL / FIJO EN CEL | L1/C29 / L1/C29 | igual | = |
| C12 | Celular en la columna de fijo (con email) | ∅ · ∅ · c12@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · c12@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | CEL EN FIJO / CEL EN FIJO | L1/C29 / L1/C29 | igual | = |
| C13 | Fijo de 7 que empieza en 6, departamento desconocido | ∅ · 6123456 · c13@gmail.com · ZZZ · ZZZ · ∅ · 1 · HOY | ∅ · ∅ · c13@gmail.com · ZZZ · ZZZ · ∅ · 1 · HOY | SIN PREFIJO / SIN PREFIJO | L1/C29 / L1/C29 | difiere: destino | R fijo sin prefijo → NULL (comentario impreciso, ver D2) |
| C14 | Fijo de 7 con 'Atlántico' (tilde) + 'Barranquilla' → rama municipio del SP | ∅ · 6054567890 · ∅ · Atlántico · Barranquilla · ∅ · 1 · HOY | ∅ · 6054567890 · ∅ · Atlántico · Barranquilla · ∅ · 1 · HOY | RAMA MUNICIPIO / RAMA MUNICIPIO | L1/C29 / L1/C29 | igual | = |
| C15 | Celular de 7 dígitos que empieza en 3 | 3001234 · ∅ · c15@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · c15@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | CEL CORTO / CEL CORTO | L1/C29 / L1/C29 | difiere: destino | P1 celular de 10 dígitos |
| C16 | Titular repetido; la 2ª fila tiene más datos | 3001110666 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110666 · ∅ · rep16@gmail.com · ∅ · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | E11 dedup |
| C17 | Solo Departamento (no entra al flag) | ∅ · ∅ · ∅ · ATLANTICO · ∅ · ∅ · 0 · HOY | ∅ · ∅ · ∅ · ATLANTICO · ∅ · ∅ · 0 · HOY | SIN DATOS / SIN DATOS | L1/C29 / L1/C29 | igual | = |
| C18 | Solo FechaNacimiento (entra al flag) | ∅ · ∅ · ∅ · ∅ · ∅ · 1990-03-03 · 1 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · 1990-03-03 · 1 · HOY | SOLO NACIMIENTO / SOLO NACIMIENTO | L1/C29 / L1/C29 | igual | = |
| C19 | Par Líder/Cartera inactivo | 3001110669 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110669 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 par inactivo |
| C20 | Titular AJENO: fijo de 7 sin prefijo + 'NOTIENE@X.COM', duplicado | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | ∅ · 2345678 · NOTIENE@X.COM · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · 2345678 · NOTIENE@X.COM · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | AJENO CAJA / AJENO CAJA | — / — | difiere: destino | E9 sin limpiezas globales |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 20, excluidas 1, duplicadas 1, expansión +0, candidatas 18, insertadas 17, refrescadas 1, vacías descartadas 0, vacías a purgar 0, titulares nuevos 16, nombres reparados 0, relaciones nuevas 17.
Carga (ejecución 179): insertadas 17, refrescadas 1, vacías descartadas 0, purgadas 0, titulares nuevos 16, nombres reparados 0, relaciones 17.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Email: 'NO TIENE' anulado
- aviso ×1: Email: email en lista negra anulado
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: NumeroTelefonoCelular: celular inválido anulado
- aviso ×1: NumeroTelefonoCelular: número fijo en la columna de celular, anulado
- aviso ×1: NumeroTelefonoFijo: celular en la columna de fijo, anulado
- aviso ×1: NumeroTelefonoFijo: fijo de 7 dígitos sin prefijo resoluble por departamento/municipio, anulado
- aviso ×1: NumeroTelefonoFijo: fijo de relleno ('11111') anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×17: Relaciones titular-cartera nuevas
- info ×16: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Cajacopi 1277608→1277608; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 17 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 19 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS
PRINT: 19 PERSONAS FUERON INGRESADAS A CAJACOPI
PRINT: 3 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```
</details>

## MutualSer

**Conclusión: paridad OK con 15 diferencias explicadas.** Los tres escenarios separan
comportamientos del SP que no se pueden ver juntos:

- **A**: sin conflictos de concatenación, así que los pasos 8 y 9 del SP corren completos. Las
  reubicaciones de teléfonos dan **idéntico** en todos los casos:
  - celular 2 = celular (M01);
  - celular en la columna de fijo → celular 2 (M02);
  - fijo = celular (M03);
  - 60x en celular o en celular 2 → fijo (M04, M05);
  - intercambio fijo/celular (M06);
  - fijo de 6 dígitos (M11);
  - celular 2 `''` (M19).
- **B**: un solo conflicto (celular 60x con el fijo ocupado) hace fallar el `UPDATE` del paso 8
  **entero**: el SP intenta `fijo + ', ' + celular` en un `varchar(10)` y da el error 2628. Lo
  mismo pasa con el paso 9. Consecuencia: las filas **sin** conflicto (MB2, MB4) quedan con su
  fijo 60x guardado como celular en `MutualSer`. El motor mueve cada una a fijo y en el conflicto
  descarta el 60x con advertencia.
- **C**: un celular 2 de más de 10 caracteres (cabe en `MutualSerAuxiliar` varchar(50)) hace
  fallar el `INSERT` a `MutualSer` **entero**: 0 filas del archivo. El motor anula ese celular 2
  e inserta las dos filas.

Detalles:

- **M08, M20.** El SP deja `''` y no NULL en el email vetado, como documenta la regla.
- **M06 (ver D3).** Da igual en los dos lados, lo que contradice el comentario de
  `ReglaMutualSer` según el cual el SP movía los teléfonos en el orden inverso.

#### Escenario MutualSer-A

Columnas del destino: `NumeroTelefonoCelular · NumeroTelefonoCelular2 · NumeroTelefonoFijo · Email · Regimen · TipoAfiliado · Departamento · Municipio · EstadoEnMutualSer · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| M01 | Nuevo; celular 2 = celular; fijo válido | 3001234567 · ∅ · 6041234567 · m1@gmail.com · SUBSIDIADO · ∅ · ∅ · ∅ · 1 · HOY | 3001234567 · ∅ · 6041234567 · m1@gmail.com · SUBSIDIADO · ∅ · ∅ · ∅ · 1 · HOY | Mónica Ser / MONICA SER | L1/C29 / L1/C29 | difiere: titular | E1 nombres |
| M02 | Celular en la columna fijo, celular 2 libre → a celular 2 | 3001112222 · 3009876543 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001112222 · 3009876543 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO ES CEL / FIJO ES CEL | L1/C29 / L1/C29 | igual | = |
| M03 | Fijo igual al celular | 3001112223 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001112223 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO REPITE / FIJO REPITE | L1/C29 / L1/C29 | igual | = |
| M04 | Celular 60x, fijo vacío → a fijo | ∅ · ∅ · 6015551234 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6015551234 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CEL ES FIJO / CEL ES FIJO | L1/C29 / L1/C29 | igual | = |
| M05 | Celular 2 60x, fijo vacío → a fijo | ∅ · ∅ · 6025551236 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6025551236 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CEL DOS ES FIJO / CEL DOS ES FIJO | L1/C29 / L1/C29 | igual | = |
| M06 | Celular en fijo + 60x en celular (intercambio) | ∅ · 3007778888 · 6017778888 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · 3007778888 · 6017778888 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | INTERCAMBIO / INTERCAMBIO | L1/C29 / L1/C29 | igual | = (ver D3) |
| M07 | Email que solo cae por LIKE ('juana@gmail.com') | 3001110707 · ∅ · ∅ · '' · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110707 · ∅ · ∅ · juana@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | JUANA LIKE / JUANA LIKE | L1/C29 / L1/C29 | difiere: destino | E2 LIKE |
| M08 | Solo email vetado ('no@gmail.com') | ∅ · ∅ · ∅ · '' · ∅ · ∅ · ∅ · ∅ · 0 · HOY | ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | SOLO NEGRO / SOLO NEGRO | L1/C29 / L1/C29 | difiere: destino | E2 + R '' vs NULL |
| M09 | Solo Regimen (no entra al flag) | ∅ · ∅ · ∅ · ∅ · SUBSIDIADO · ∅ · ∅ · ∅ · 0 · HOY | ∅ · ∅ · ∅ · ∅ · SUBSIDIADO · ∅ · ∅ · ∅ · 0 · HOY | SOLO REGIMEN / SOLO REGIMEN | L1/C29 / L1/C29 | igual | = |
| M10 | Celular corto '12345' | 12345 · ∅ · ∅ · m10@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · m10@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CEL CORTO / CEL CORTO | L1/C29 / L1/C29 | difiere: destino | P1 celular inválido |
| M11 | Fijo de 6 dígitos | ∅ · ∅ · ∅ · m11@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · m11@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO CORTO / FIJO CORTO | L1/C29 / L1/C29 | igual | = |
| M12 | Existente, contenido idéntico | 3001110712 · ∅ · ∅ · m12@gmail.com · CONTRIBUTIVO · ∅ · ∅ · ∅ · 1 · HOY | 3001110712 · ∅ · ∅ · m12@gmail.com · CONTRIBUTIVO · ∅ · ∅ · ∅ · 1 · HOY | MUTUAL EXISTENTE / MUTUAL EXISTENTE | L1/C29 / L1/C29 | igual | = |
| M13 | Vacía vieja; ahora trae datos | 3001110713 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110713 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | igual | = |
| M14 | Datos en destino; archivo con fila vacía | 3001110714 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110714 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | igual | = |
| M15 | Titular AJENO con duplicado + vacía | 3001110715 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110715 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3001110715 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | AJENO MUTUAL / AJENO MUTUAL | — / — | difiere: destino | E9 sin limpiezas globales |
| M16 | Titular repetido; la 2ª más completa | 3001110716 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110716 · ∅ · ∅ · rep16@gmail.com · ∅ · ∅ · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | E11 dedup |
| M17 | Par inactivo | 3001110717 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110717 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 par inactivo |
| M18 | Documento inválido | 3001234568 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| M19 | Celular 2 '' (import CSV) + celular en fijo → a celular 2 | ∅ · 3001119999 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · 3001119999 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CEL DOS VACIO / CEL DOS VACIO | L1/C29 / L1/C29 | igual | = |
| M20 | Email de dominio vetado | 3001110720 · ∅ · ∅ · '' · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 3001110720 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DOMINIO NEGRO / DOMINIO NEGRO | L1/C29 / L1/C29 | difiere: destino | R '' vs NULL |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 20, excluidas 1, duplicadas 1, expansión +0, candidatas 18, insertadas 16, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 15, nombres reparados 0, relaciones nuevas 17.
Carga (ejecución 180): insertadas 16, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 15, nombres reparados 0, relaciones 17.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Email: email en lista negra anulado
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: NumeroTelefonoCelular: celular inválido anulado
- aviso ×1: NumeroTelefonoFijo: fijo inválido anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×17: Relaciones titular-cartera nuevas
- info ×15: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.MutualSer
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; MutualSer 864703→864703; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 16 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 19 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS
PRINT: 19 PERSONAS FUERON INGRESADAS A MUTUALSER
PRINT: 2 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```
</details>

#### Escenario MutualSer-B

Columnas del destino: `NumeroTelefonoCelular · NumeroTelefonoCelular2 · NumeroTelefonoFijo · Email · Regimen · TipoAfiliado · Departamento · Municipio · EstadoEnMutualSer · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| MB1 | Celular 60x con fijo ocupado (concatenación) | 6015551235 · ∅ · 6045551235 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6045551235 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CONFLICTO CEL / CONFLICTO CEL | L1/C29 / L1/C29 | difiere: destino | E4 sin concatenar · F: UPDATE del paso 8 falla entero |
| MB2 | Celular 60x con fijo vacío (sin conflicto propio) | 6015551236 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6015551236 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | INOCENTE CEL / INOCENTE CEL | L1/C29 / L1/C29 | difiere: destino | E4 · F: víctima del fallo del paso 8 |
| MB3 | Celular 2 60x con fijo ocupado (concatenación) | ∅ · 6025551237 · 6045551237 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6045551237 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | CONFLICTO CEL DOS / CONFLICTO CEL DOS | L1/C29 / L1/C29 | difiere: destino | E4 sin concatenar · F: UPDATE del paso 9 falla entero |
| MB4 | Celular 2 60x con fijo vacío (sin conflicto propio) | ∅ · 6025551238 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · 6025551238 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | INOCENTE CEL DOS / INOCENTE CEL DOS | L1/C29 / L1/C29 | difiere: destino | E4 · F: víctima del fallo del paso 9 |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 4, excluidas 0, duplicadas 0, expansión +0, candidatas 4, insertadas 4, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 4, nombres reparados 0, relaciones nuevas 4.
Carga (ejecución 181): insertadas 4, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 4, nombres reparados 0, relaciones 4.
- aviso ×1: NumeroTelefonoCelular2: fijo en la columna de celular descartado (la columna de fijo ya tenía otro fijo)
- aviso ×1: NumeroTelefonoCelular: fijo en la columna de celular descartado (la columna de fijo ya tenía otro fijo)
- info ×4: Relaciones titular-cartera nuevas
- info ×4: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; MutualSer 864703→864703; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 4 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 4 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS
ERROR 2628 (línea 93): Los datos binarios o de la cadena se truncan en la columna "NumeroTelefonoFijo" de la tabla "#Aux". Valor truncado: "6045551235".
PRINT: Se terminó la instrucción.
ERROR 2628 (línea 110): Los datos binarios o de la cadena se truncan en la columna "NumeroTelefonoFijo" de la tabla "#Aux". Valor truncado: "6045551237".
PRINT: Se terminó la instrucción.
PRINT: 4 PERSONAS FUERON INGRESADAS A MUTUALSER
PRINT: 0 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```
</details>

#### Escenario MutualSer-C

Columnas del destino: `NumeroTelefonoCelular · NumeroTelefonoCelular2 · NumeroTelefonoFijo · Email · Regimen · TipoAfiliado · Departamento · Municipio · EstadoEnMutualSer · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| MC1 | Celular 2 con dos números '3001234567, 3007654321' | — | 3001110741 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DOS EN CEL DOS / DOS EN CEL DOS | L1/C29 / L1/C29 | difiere: destino | E4/P1 celular 2 inválido · F: INSERT falla entero |
| MC2 | Fila normal en el mismo archivo | — | 3001110742 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FILA NORMAL / FILA NORMAL | L1/C29 / L1/C29 | difiere: destino | F: víctima del fallo del INSERT |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 0, duplicadas 0, expansión +0, candidatas 2, insertadas 2, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 2, nombres reparados 0, relaciones nuevas 2.
Carga (ejecución 182): insertadas 2, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 2, nombres reparados 0, relaciones 2.
- aviso ×1: NumeroTelefonoCelular2: celular inválido anulado
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×2: Relaciones titular-cartera nuevas
- info ×2: Titulares nuevos que se crearán
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; MutualSer 864703→864703; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 2 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 2 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS
ERROR 2628 (línea 158): Los datos binarios o de la cadena se truncan en la columna "NumeroTelefonoCelular2" de la tabla "#Dest". Valor truncado: "3001234567".
PRINT: Se terminó la instrucción.
PRINT: 0 PERSONAS FUERON INGRESADAS A MUTUALSER
PRINT: 0 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```
</details>

## NuevaEps

**Conclusión: paridad OK con 9 diferencias explicadas.**

- **N01.** Con `'000999000000751'`, el SP guarda `NuevaEps.TitularID = '000999000000751'` (texto
  con ceros) y `Titular 999000000751` (conversión implícita). El motor guarda `'999000000751'`
  en los dos.
- **N04.** El SP deduplica con la clave (titular, teléfono, estado, tipo), así que dos teléfonos
  del mismo titular quedaban como dos filas. El motor deja una: la más completa y, a igualdad,
  la última del archivo.
- **N06.** El cambio de municipio: el SP borraba la fila vieja, porque su clave no tiene
  municipio. El motor conserva ambas.
- **N02, N03.** Los 7 dígitos y el fijo 60x en `NumeroTelefono` los guardaba el SP sin validar.

#### Escenario NuevaEps

Columnas del destino: `TitularID · NumeroTelefono · EstadoAfiliacion · TipoAfiliado · FechaNacimiento · Departamento · Municipio · EstadoEnNuevaEps · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| N01 | Documento con ceros a la izquierda '000999000000751' | 000999000000751 · 3001234567 · ACTIVO · COTIZANTE · ∅ · ∅ · ∅ · 1 · HOY | 999000000751 · 3001234567 · ACTIVO · COTIZANTE · ∅ · ∅ · ∅ · 1 · HOY | Nélida Ceros / NELIDA CEROS | L1/C29 / L1/C29 | difiere: destino, titular | E6 sin ceros + E1 nombres |
| N02 | Teléfono de 7 dígitos | 999000000752 · 1234567 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000752 · ∅ · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | SIETE DIGITOS / SIETE DIGITOS | L1/C29 / L1/C29 | difiere: destino | E6 7 dígitos |
| N03 | Fijo 60x en NumeroTelefono | 999000000753 · 6012345678 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000753 · ∅ · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | FIJO NUEVAEPS / FIJO NUEVAEPS | L1/C29 / L1/C29 | difiere: destino | P1 fijo en columna de celular |
| N04 | Dos filas del mismo titular con teléfonos distintos | 999000000754 · 3001110754 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY<br>999000000754 · 3002220754 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000754 · 3002220754 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | DOS TELEFONOS / DOS TELEFONOS | L1/C29 / L1/C29 | difiere: destino | E11 dedup por titular |
| N05 | Existente, contenido idéntico | 999000000755 · 3001110755 · ACTIVO · COTIZANTE · ∅ · ∅ · MONTERIA · 1 · HOY | 999000000755 · 3001110755 · ACTIVO · COTIZANTE · ∅ · ∅ · MONTERIA · 1 · HOY | NEPS EXISTENTE / NEPS EXISTENTE | L1/C29 / L1/C29 | igual | = |
| N06 | Existente; solo cambia Municipio | 999000000756 · 3001110756 · ACTIVO · COTIZANTE · ∅ · ∅ · CERETE · 1 · HOY | 999000000756 · 3001110756 · ACTIVO · COTIZANTE · ∅ · ∅ · CERETE · 1 · HOY<br>999000000756 · 3001110756 · ACTIVO · COTIZANTE · ∅ · ∅ · MONTERIA · 1 · viejo 2026-01-15 | CAMBIA MUNICIPIO / CAMBIA MUNICIPIO | L1/C29 / L1/C29 | difiere: destino | P4 clave completa |
| N07 | Solo FechaNacimiento y Departamento (no entran al flag) | 999000000757 · ∅ · ∅ · ∅ · 1970-07-07 · CORDOBA · ∅ · 0 · HOY | 999000000757 · ∅ · ∅ · ∅ · 1970-07-07 · CORDOBA · ∅ · 0 · HOY | SOLO NACIMIENTO / SOLO NACIMIENTO | L1/C29 / L1/C29 | igual | = |
| N08 | Vacía vieja; ahora trae datos | 999000000758 · 3001110758 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000758 · 3001110758 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | igual | = |
| N09 | Datos en destino; archivo con fila vacía | 999000000759 · 3001110759 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 999000000759 · 3001110759 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | igual | = |
| N10 | Titular AJENO con duplicado + vacía | 999000000760 · 3001110760 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 999000000760 · 3001110760 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>999000000760 · 3001110760 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>999000000760 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | AJENO NEPS / AJENO NEPS | — / — | difiere: destino | E9 sin limpiezas globales |
| N11 | Documento inválido | 1234 · 3001234568 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| N12 | Par inactivo | 999000000762 · 3001110762 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000762 · 3001110762 · ∅ · ∅ · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 par inactivo |
| N13 | Solo EstadoAfiliacion 'N/A' | 999000000763 · ∅ · N/A · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000763 · ∅ · ∅ · ∅ · ∅ · ∅ · ∅ · 0 · HOY | NA ESTADO / NA ESTADO | L1/C29 / L1/C29 | difiere: destino | P2 vacíos convencionales |
| N14 | Dos filas idénticas del mismo titular | 999000000764 · 3001110764 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | 999000000764 · 3001110764 · ACTIVO · ∅ · ∅ · ∅ · ∅ · 1 · HOY | IDENTICAS / IDENTICAS | L1/C29 / L1/C29 | igual | = |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 15, excluidas 1, duplicadas 2, expansión +0, candidatas 12, insertadas 10, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 8, nombres reparados 0, relaciones nuevas 11.
Carga (ejecución 183): insertadas 10, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 8, nombres reparados 0, relaciones 11.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Titular repetido en el archivo: se conserva la fila más completa
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: NumeroTelefono: celular inválido anulado
- aviso ×1: NumeroTelefono: número fijo en la columna de celular, anulado
- info ×11: Relaciones titular-cartera nuevas
- info ×8: Titulares nuevos que se crearán
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.NuevaEps
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; NuevaEps 62834→62834; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 9 PERSONAS FUERON INGRESADAS A TITULAR
PRINT: 13 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA
PRINT: 1 DUPLICADOS ELIMINADOS DE NUEVAEPSAUXILIAR
PRINT: 14 REGISTROS FUERON INGRESADOS A NUEVAEPS
PRINT: 3 DUPLICADOS ELIMINADOS EN NUEVAEPS
PRINT: 3 REGISTROS VACIOS ELIMINADOS
```
</details>

## SIMIT

**Conclusión: paridad OK con 13 diferencias explicadas.** Son idénticos los casos sin placas
(una fila NULL con flag 0) y el de placa existente (refresco = insert + dedup del SP).
Diferencias:

- **P03.** El `DISTINCT` del SP es CI: `'abc123'` y `'ABC123'` quedan como una sola placa, con
  la grafía que salga (aquí `'abc123'`).
- **P04.** `'ABC 123'` y `'ABC-123'` eran dos placas en el SP.
- **P06, P07.** `''` contaba como placa con flag 1, y `Placa15 nchar(10)` guardaba
  `'DEF456    '` con el relleno.
- **P08.** El SP deduplica antes de despivotar y pierde las placas de la otra fila.
- **P10, P11.** El SP de SIMIT **no tenía** paso de vacías: una fila "sin placa" convivía con
  las placas del titular. El motor aplica la política de vacías genérica (P5).

#### Escenario SIMIT

Columnas del destino: `Placa · EstadoEnSIMIT · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| P01 | Nuevo; ABC123, XYZ12D y ABC123 repetida | ABC123 · 1 · HOY<br>XYZ12D · 1 · HOY | ABC123 · 1 · HOY<br>XYZ12D · 1 · HOY | Pablo Plazas / PABLO PLAZAS | L1/C29 / L1/C29 | difiere: titular | E1 nombres |
| P02 | Sin placas | ∅ · 0 · HOY | ∅ · 0 · HOY | SIN PLACAS / SIN PLACAS | L1/C29 / L1/C29 | igual | = |
| P03 | 'abc123' y 'ABC123' | abc123 · 1 · HOY | ABC123 · 1 · HOY | MAYUS MINUS / MAYUS MINUS | L1/C29 / L1/C29 | difiere: destino | E7 placa canónica (el SP deja la grafía que salga) |
| P04 | 'ABC 123' y 'ABC-123' | ABC 123 · 1 · HOY<br>ABC-123 · 1 · HOY | ABC123 · 1 · HOY | ESPACIO GUION / ESPACIO GUION | L1/C29 / L1/C29 | difiere: destino | E7 sin espacio ni guion |
| P05 | Marca 'PAZ Y SALV' | PAZ Y SALV · 1 · HOY | ∅ · 0 · HOY | PAZ Y SALVO / PAZ Y SALVO | L1/C29 / L1/C29 | difiere: destino | E7 marca → sin placa |
| P06 | Placa2 '' (import CSV), resto NULL | '' · 1 · HOY | ∅ · 0 · HOY | PLACA VACIA / PLACA VACIA | L1/C29 / L1/C29 | difiere: destino | E7/R '' contaba como placa |
| P07 | Placa15 (nchar(10)) 'DEF456' | 'DEF456    ' · 1 · HOY | DEF456 · 1 · HOY | PLACA QUINCE / PLACA QUINCE | L1/C29 / L1/C29 | difiere: destino | E7/R relleno de nchar(10) |
| P08 | Mismo titular en dos filas con placas distintas | GHI789 · 1 · HOY | GHI789 · 1 · HOY<br>JKL012 · 1 · HOY | DOS FILAS / DOS FILAS | L1/C29 / L1/C29 | difiere: destino | R dedup antes de despivotar + E11 |
| P09 | Existente con la misma placa | MNO345 · 1 · HOY | MNO345 · 1 · HOY | SIMIT EXISTENTE / SIMIT EXISTENTE | L1/C29 / L1/C29 | igual | = |
| P10 | 'Sin placa' vieja; ahora trae placa | PQR678 · 1 · HOY<br>∅ · 0 · viejo 2026-01-15 | PQR678 · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | difiere: destino | P5/R el SP de SIMIT no purgaba vacías |
| P11 | Placa en destino; archivo sin placas | STU901 · 1 · viejo 2026-01-15<br>∅ · 0 · HOY | STU901 · 1 · viejo 2026-01-15 | TIENE PLACA / TIENE PLACA | L1/C29 / L1/C29 | difiere: destino | P5/R vacía de titular con placa |
| P12 | Titular AJENO con placa duplicada | VWX234 · 1 · viejo 2026-01-15 | VWX234 · 1 · viejo 2026-01-15<br>VWX234 · 1 · viejo 2026-01-15 | AJENO SIMIT / AJENO SIMIT | — / — | difiere: destino | E9 sin limpiezas globales |
| P13 | 'AB12345678' (10 caracteres) | AB12345678 · 1 · HOY | ∅ · 0 · HOY | PLACA LARGA / PLACA LARGA | L1/C29 / L1/C29 | difiere: destino | E7 placa inválida |
| P14 | Documento inválido | ZZZ999 · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | P3 documento inválido |
| P15 | Par inactivo | YZA567 · 1 · HOY | YZA567 · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | E10 par inactivo |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 15, excluidas 1, duplicadas 0, expansión +1, candidatas 15, insertadas 13, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 10, nombres reparados 0, relaciones nuevas 12.
Carga (ejecución 184): insertadas 13, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 10, nombres reparados 0, relaciones 12.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: Placa: valor que no es una placa descartado (p. ej. 'PAZ Y SALVO', 'NO CARGO', 'SIN', 'XXXXXX')
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- info ×12: Relaciones titular-cartera nuevas
- info ×10: Titulares nuevos que se crearán
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.SIMIT
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; SIMIT 301840→301840; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 11 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 14 PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA
PRINT: 2 DUPLICADOS ELIMINADOS EN #Dest
```
</details>

## DatosClaro

**Conclusión: paridad OK con 11 diferencias explicadas.** Hay 8 casos idénticos:

- N:1 con dos teléfonos;
- teléfono en las dos listas negras;
- `'juana@gmail.com'` (este SP ya comparaba por igualdad);
- email vetado en mayúsculas;
- fila repetida;
- fijo 60x;
- `'5551234567'` (ver B1);
- DB1 (por coincidencia: ver B3).

Diferencias:

- **D03, D04, D05.** El SP cruzaba contra `ListaNegraTelefonosAuxiliar` (1,2M filas, staging) y
  el motor contra `ListaNegraTelefonos`. Un teléfono vetado solo en la real se guardaba en el
  SP; uno vetado solo en la auxiliar se anulaba.
- **D12.** Contenido idéntico: el SP no inserta y deja la fecha vieja; el motor la refresca.
- **DatosClaro-B.** Un email de 110 caracteres (cabe en `DatosClaroAuxiliar.Email varchar(150)`,
  no en `DatosClaro.Email varchar(100)`) hace fallar el `INSERT` **entero**: DB2, una fila
  normal, no entra. El motor anula el email largo y carga el resto.
#### Escenario DatosClaro

Columnas del destino: `NumeroTelefono · Email · FechaInsercion`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| D01 | Dos filas del mismo titular (N:1) | 3001110851 · d1@gmail.com · HOY<br>3007654321 · ∅ · HOY | 3001110851 · d1@gmail.com · HOY<br>3007654321 · ∅ · HOY | — / — | — / — | igual | = |
| D02 | Teléfono en las dos listas negras | ∅ · d2@gmail.com · HOY | ∅ · d2@gmail.com · HOY | — / — | — / — | igual | = |
| D03 | Teléfono solo en ListaNegraTelefonos (real) | 3126321804 · d3@gmail.com · HOY | ∅ · d3@gmail.com · HOY | — / — | — / — | difiere: destino | E5 lista negra real |
| D04 | Teléfono solo en ListaNegraTelefonosAuxiliar | ∅ · d4@gmail.com · HOY | 3124906610 · d4@gmail.com · HOY | — / — | — / — | difiere: destino | E5 lista negra real |
| D05 | Solo teléfono, vetado solo en la real | 3117112083 · ∅ · HOY | — | — / — | — / — | difiere: destino | E5 lista negra real |
| D06 | Email 'juana@gmail.com' (el SP de DatosClaro ya usaba igualdad) | ∅ · juana@gmail.com · HOY | ∅ · juana@gmail.com · HOY | — / — | — / — | igual | = |
| D07 | Email de dominio vetado | ∅ · x@uniquindio.edu.co · HOY | — | — / — | — / — | difiere: destino | E2 dominio |
| D08 | Email vetado en mayúsculas 'NO@GMAIL.COM' | — | — | — / — | — / — | igual | = |
| D09 | Email 'a.b@c' (punto antes del @) | ∅ · a.b@c · HOY | — | — / — | — / — | difiere: destino | P1 email más estricto |
| D10 | Teléfono de 7 dígitos | 1234567 · d10@gmail.com · HOY | ∅ · d10@gmail.com · HOY | — / — | — / — | difiere: destino | E5 validación de teléfono |
| D11 | Teléfono de 10 dígitos que no empieza en 3 ni 6 ('5551234567') | 5551234567 · d11@gmail.com · HOY | 5551234567 · d11@gmail.com · HOY | — / — | — / — | igual | = (pero ver B1) |
| D12 | Fila idéntica ya en destino | 3001110862 · d12@gmail.com · viejo 2026-01-15 | 3001110862 · d12@gmail.com · HOY | — / — | — / — | difiere: destino | E8 refresca fecha |
| D13 | Titular AJENO con duplicado | 3001110863 · ∅ · viejo 2026-01-15 | 3001110863 · ∅ · viejo 2026-01-15<br>3001110863 · ∅ · viejo 2026-01-15 | — / — | — / — | difiere: destino | E9 sin limpiezas globales |
| D14 | Fila repetida exacta en el archivo | 3001110864 · ∅ · HOY | 3001110864 · ∅ · HOY | — / — | — / — | igual | = |
| D15 | Email con mayúsculas 'Juan@Gmail.com' | ∅ · Juan@Gmail.com · HOY | ∅ · juan@gmail.com · HOY | — / — | — / — | difiere: destino | P1 email en minúsculas |
| D16 | Documento inválido | 3001110866 · ∅ · HOY | — | — / — | — / — | difiere: destino | P3 documento inválido |
| D17 | Fijo 60x | 6012345678 · ∅ · HOY | 6012345678 · ∅ · HOY | — / — | — / — | igual | = |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 18, excluidas 5, duplicadas 1, expansión +0, candidatas 12, insertadas 11, refrescadas 1, vacías descartadas 0, vacías a purgar 0, titulares nuevos 0, nombres reparados 0, relaciones nuevas 0.
Carga (ejecución 185): insertadas 11, refrescadas 1, vacías descartadas 0, purgadas 0, titulares nuevos 0, nombres reparados 0, relaciones 0.
- error ×4: Sin teléfono ni email válidos tras la limpieza
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×12: Titular inexistente en Titular: la fila entra igual (esta fuente no crea titulares)
- aviso ×3: NumeroTelefono: teléfono en ListaNegraTelefonos anulado
- aviso ×2: Email: email en lista negra anulado
- aviso ×1: Email: email con formato inválido anulado
- aviso ×1: NumeroTelefono: teléfono inválido anulado
- info ×3: Teléfonos distintos del archivo que están en ListaNegraTelefonos
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×1: Fila repetida exacta en el archivo
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; DatosClaro 3528387→3528387; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 2 TELEFONOS EN LISTA NEGRA ELIMINADOS
PRINT: 1 CORREOS EN LISTA NEGRA ELIMINADOS
PRINT: 0 EMAILS INVALIDOS LIMPIADOS
PRINT: REGISTROS VACIOS ELIMINADOS
PRINT: 16 REGISTROS INSERTADOS EN PRINCIPAL
PRINT: 2 DUPLICADOS ELIMINADOS
```
</details>

#### Escenario DatosClaro-B

Columnas del destino: `NumeroTelefono · Email · FechaInsercion`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Clasificación |
|---|---|---|---|---|---|---|---|
| DB1 | Email de 110 caracteres, sin teléfono | — | — | — / — | — / — | igual | = por coincidencia (ver B3) |
| DB2 | Fila normal en el mismo archivo | — | 3001110892 · ∅ · HOY | — / — | — / — | difiere: destino | P1 email > 100 · F: en el SP el INSERT falla entero |

<details><summary>Lado nuevo: preview, carga y limpieza</summary>

```
Preview: leídas 2, excluidas 1, duplicadas 0, expansión +0, candidatas 1, insertadas 1, refrescadas 0, vacías descartadas 0, vacías a purgar 0, titulares nuevos 0, nombres reparados 0, relaciones nuevas 0.
Carga (ejecución 186): insertadas 1, refrescadas 0, vacías descartadas 0, purgadas 0, titulares nuevos 0, nombres reparados 0, relaciones 0.
- error ×1: Sin teléfono ni email válidos tras la limpieza
- aviso ×1: Email: email con formato inválido anulado
- aviso ×1: Titular inexistente en Titular: la fila entra igual (esta fuente no crea titulares)
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; DatosClaro 3528387→3528387; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```
</details>

<details><summary>Lado viejo: mensajes del batch (PRINT y errores)</summary>

```
── ejecución del cuerpo del SP ──
PRINT: 0 TELEFONOS EN LISTA NEGRA ELIMINADOS
PRINT: 0 CORREOS EN LISTA NEGRA ELIMINADOS
PRINT: 0 EMAILS INVALIDOS LIMPIADOS
PRINT: REGISTROS VACIOS ELIMINADOS
ERROR 2628 (línea 75): Los datos binarios o de la cadena se truncan en la columna "Email" de la tabla "#Dest". Valor truncado: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa".
PRINT: Se terminó la instrucción.
PRINT: 0 REGISTROS INSERTADOS EN PRINCIPAL
PRINT: 0 DUPLICADOS ELIMINADOS
```
</details>

## Hallazgos

Ninguno se corrigió en esta etapa. Los cambios de código quedan a criterio del coordinador.

### B1 (bajo): `TipoColumnaCargue.Telefono` acepta cualquier número de 10 dígitos

`CargueFuentesMotor.ValidarContacto`, `case TipoColumnaCargue.Telefono` (línea ~699), usa
`TelefonoColombiano.Normalizar(texto).Numero`, que devuelve cualquier cadena de 10 dígitos. La
definición de DatosClaro dice "Teléfono (celular 3xx o fijo 60x, 10 dígitos)" y el plan fija
"celular = empieza en 3; fijo = empieza en 6".

Reproducción mínima: DatosClaro, CSV `TitularID;NumeroTelefono;Email` con la fila
`999000000861;5551234567;`. El motor la inserta con `'5551234567'` y sin advertencia (caso D11;
igual pasa con `'1234567890'`).

Impacto hoy: nulo. `dbo.DatosClaro` tiene 2.576.595 teléfonos y todos son 3xx de 10 dígitos.

Arreglo sugerido (una línea, más un test en `tests/Zentinela.Infrastructure.Tests`):
`var numero = TelefonoColombiano.NormalizarCelular(texto) ?? TelefonoColombiano.NormalizarFijo(texto);`

**Corregido en** `CargueFuentesMotor.ValidarContacto` (tipo `Telefono`: celular 3xx o fijo 6xx; lo
demás se anula con "teléfono inválido anulado"), con tests en `CargueFuentesMotorValidacionTests`
y dos filas `'5551234567'` en `CasoDatosClaro` del harness (con email: entra sin teléfono; sin
email: se excluye).

### B2 (bajo): un nombre de más de 70 caracteres se trunca sin advertencia

`NormalizadorTitular.LimpiarNombre` recorta a `LargoMaximo = 70` (líneas 107-108) y no lo
informa. Las columnas de dato sí avisan ("valor truncado a N caracteres").

Reproducción: Sura-B, SB1. El nombre de 75 caracteres entra recortado a 70 y el preview no
muestra ningún aviso.

Sugerencia: devolver un indicador `Truncado` y advertir "Nombre truncado a 70 caracteres".

**Corregido en** `NormalizadorTitular.Resultado.Truncado` y `CargueFuentesMotor.AsignarNombre`, que
emite el aviso agrupado "Nombre: truncado a 70 caracteres"; tests en `NormalizadorTitularTests` y
`CargueFuentesMotorValidacionTests`.

### B3 (cosmético): un email más largo que la columna se informa como "formato inválido"

`EmailColombiano.Normalizar` devuelve null tanto por forma como por largo, y el motor dice
"email con formato inválido anulado" (línea ~710).

Reproducción: DatosClaro-B, DB1, con un email de 110 caracteres en `Email varchar(100)`.

**Corregido en** `CargueFuentesMotor.ValidarContacto` (tipo `Email`): primero valida la forma sin
tope de largo y después el largo, con el aviso aparte "email de más de N caracteres anulado (no
cabe en la columna)"; tests en `CargueFuentesMotorValidacionTests`.

### D1: el comentario de `ReglaCajacopi` atribuye al SP una lista negra de correos que no tenía

`ReglaCajacopi.cs:18` dice que el motor hace, "igual que [el SP]", "vacíos, lista negra de
correos…". `sp_ProcesarCajacopiAuxiliar` **no** cruza `ListaNegraCorreos` ni valida la forma del
email. El motor aplica ambas cosas a toda columna Email (P1).

Es el único caso sin documentar de la comparación (C10). El comportamiento es el del plan; lo
que hay que corregir es el comentario, y conviene nombrarlo en el ADR-039.

**Corregido en** el comentario de `ReglaCajacopi`: separa lo que el SP hacía y hoy hace el motor
(dedup, Titular, relaciones) de lo que el motor agrega sin que el SP lo hiciera (forma del email,
`ListaNegraCorreos`, política de vacías). El ADR-039 queda pendiente.

### D2: comentario impreciso en `ReglaCajacopi` sobre el fijo de 7 sin prefijo

`ReglaCajacopi.cs:29` dice que el SP dejaba el fijo de 7 "tal cual y el paso 9 lo anulaba
después". El paso 9 solo anula lo que `NOT LIKE '6%'`. Un fijo de 7 que empieza en 6 quedaba
guardado de 7 dígitos (C13: `'6123456'`).

**Corregido en** el comentario de `ReglaCajacopi`: el paso 9 solo anulaba los fijos de 7 que no
empiezan en 6; los que sí empiezan en 6 quedaban guardados de 7 dígitos.

### D3: comentario impreciso en `ReglaMutualSer` sobre el orden

`ReglaMutualSer.cs:25` dice que el SP movía los 60x antes que el fijo-celular ("lo hacía al
revés"). En el SP, el paso 6 (fijo que es celular → celular 2) corre **antes** que los pasos 8 y 9
(60x → fijo). El intercambio M06 da idéntico en los dos lados.

Lo que sí perdía datos en el SP era la concatenación: con el celular 2 ocupado, el fijo-celular se
quedaba en fijo y el paso 8 fallaba entero (escenario B).

**Corregido en** el comentario de `ReglaMutualSer`: el orden es el mismo del SP (paso 6 antes que 8
y 9), y la pérdida real estaba en la concatenación. De paso se corrigió el defecto "pasos 8 y 9
concatenaban…", que decía que el INSERT truncaba: es el UPDATE sobre la Auxiliar (`varchar(10)`)
el que falla entero.

### O1 (decisión pendiente): `UltimaFechaConsulta` en la clave de contenido de Sura

La columna tiene `EntraAlFlag = false` pero `EntraAClave = true` (valor por defecto).

- Con el mismo contacto y otra `UltimaFechaConsulta`, el motor inserta una fila nueva y la vieja
  sigue viva (S17).
- El SP excluía esa columna de su clave de dedup y dejaba solo la última.

Si Sura informa esa fecha en cada extracción, cada recarga duplicaría las filas del titular en
vez de refrescarlas. Eso va en contra de E8, que pide refrescar la fecha en vez de duplicar.

Hoy la columna es NULL en las 3.749 filas de `dbo.Sura`, así que no hay impacto actual.

Opciones:
- (a) `EntraAClave: false`: refresca la fecha, pero se pierde el valor nuevo de
  `UltimaFechaConsulta`, porque solo se actualiza la fecha de sello;
- (b) dejarlo así y documentarlo en el ADR.

### O2: validaciones genéricas que no están en la lista de diferencias esperadas

P1–P3 (validación de celular, fijo y email; vacíos convencionales; documento de 5 a 15 dígitos)
intervienen en 19 de las 77 diferencias. Todas vienen de decisiones del plan, pero conviene que el
ADR-039 las liste como "lo que el motor hace y los SP no hacían".

### O3: fragilidad de los SP (F)

En los SP, un solo valor que no cabe tumba la instrucción entera para todo el archivo. El
paso aborta, el resto del SP sigue sin transacción y queda un estado parcial:

| Qué dispara el fallo | Instrucción que falla entera |
|---|---|
| `SuraAuxiliar.Nombre` de 71 a 80 caracteres (`Titular.Nombre` es 70) | `INSERT Titular`. En BD real también fallan las relaciones, por FK |
| `MutualSerAuxiliar`: un celular 60x con el fijo ocupado | `UPDATE` de los pasos 8/9, por concatenar en varchar(10) |
| `MutualSerAuxiliar.NumeroTelefonoCelular2` de más de 10 caracteres | `INSERT MutualSer` |
| `DatosClaroAuxiliar.Email` de más de 100 caracteres | `INSERT DatosClaro` |

Por análisis, sin caso: también fallaba entero el `INSERT Titular` de cualquier SP con un titular
nuevo sin nombre (`Titular.Nombre` es NOT NULL). Igual el `INSERT Titular_Cartera` con una fila
sin CarteraID/LiderID (NOT NULL) o con un par que no existe en `Lider_Cartera` (FK).

El motor trata cada caso fila a fila, con exclusión o advertencia contada.

### O4: los SP deduplicaban la Auxiliar de forma no determinista

`ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC)` con `FechaConsulta`
NULL (o con el mismo `getdate()` del default en Cajacopi) conserva una fila cualquiera. En S13,
C16 y M16 el SP se quedó con la fila **menos** completa. Lo mismo pasa con el `DISTINCT` CI de
SIMIT (P03).

### O5: el nombre limpio quita dígitos y puntuación

"Inversiones 3A S.A.S." → "INVERSIONES A S A S" (S22). Es lo que decidió el plan ("solo letras y
espacio"), pero en razones sociales se pierde información (`3A`). Solo afecta a titulares nuevos,
o a nombres rotos que se reparan: un nombre sano nunca se sobrescribe (S23).
