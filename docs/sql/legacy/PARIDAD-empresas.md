# Paridad: SP legacy de la Fase 2 (grupo empresas) vs Cargue de Fuentes de Datos (ADR-039)

Comparación, con los mismos datos de entrada, de lo que dejaban `sp_ProcesarRUESAuxiliar`,
`sp_ProcesarCamaraAuxiliar`, `sp_ProcesarCamaraEmpresaAuxiliar`, `sp_ProcesarPositivaEmpresasAuxiliar`,
`sp_ProcesarArlSuraAuxiliar` y `sp_ProcesarCompensadosAuxiliar` (respaldados en esta carpeta) y lo que deja
el motor nuevo (`CargueFuentesMotor` + `Regla<Fuente>`). Corrida del **2026-09-24** contra DBCallCenter
(10.0.0.89), rama `feature/cargue-f2-empresas`.

Mismo método, misma herramienta y misma leyenda que la paridad de la Fase 1
([`PARIDAD.md`](PARIDAD.md)); aquí solo lo que cambia.

## Resumen

| Fuente | Casos | Iguales | Diferencias | Sin explicar | Conclusión |
|---|---:|---:|---:|---:|---|
| RUES | 16 | 6 | 10 | 0 | Paridad OK (E1, E9, E10, E11, P2, P3, P5; sello P6) |
| Camara | 23 | 7 | 16 | 0 | Paridad OK (E1, E9, E10, E11, P1, P3, P4, P5, P7, R) |
| CamaraEmpresa | 15 | 4 | 11 | 0 | Paridad OK (E9, E11, P1, P3, P5, P7, R); clave `NitEmpresa` |

## Método

Igual que en [`PARIDAD.md` § Método](PARIDAD.md#método). **Ningún SP se ejecutó**, ni siquiera con
ROLLBACK: el cuerpo de cada `sp_Procesar*.sql` de esta carpeta corre como batch ad-hoc con toda tabla
que escribe sustituida por `#temp` (`<Fuente>Auxiliar → #Aux`, `<Destino> → #Dest`,
`Titular → #Titular`, `Titular_Cartera → #Titular_Cartera`); las que solo lee (`PrefijosDepartamento`)
se leen directo. La herramienta aborta si, sin comentarios ni literales, el batch todavía nombra una
tabla real escribible o si un verbo de escritura apunta a algo que no es `#temp`, alias o CTE. El lado
nuevo es el motor real con carga real **dentro del rango del grupo empresas** y limpieza verificada.

Diferencias con la corrida de la Fase 1:

- **Sustituciones sin distinguir mayúsculas.** `sp_ProcesarArlSuraAuxiliar` escribe la misma tabla
  como `ARLSuraAuxiliar` y `ArlSuraAuxiliar`, y el destino como `ArlSura`; SQL Server no distingue
  mayúsculas en los identificadores. La verificación anti-tablas-reales ya era insensible.
- **Rango y auditoría del grupo.** Lado nuevo en 999000002601..2899 (sub-rangos de 50 por fuente,
  fuera de los del harness 2000..2599), usuario `harness-cargue-empresas`. La verificación de limpieza
  cuenta solo la auditoría de ese usuario: otros grupos corren sus harness en paralelo.
- **Clave NIT.** En CamaraEmpresa el titular es `NitEmpresa` (ver su sección): semillas, lectura y
  comparación usan esa columna.
- **Par líder/cartera**: activo (1, 29); inactivo (5, 16), igual que en la Fase 1.

Herramienta, batches (`batches/*.sql`) y salidas crudas en el scratchpad de la sesión
(`paridad-empresas/`), **fuera del repo**. No se modificó código del repo para la paridad.

## Leyenda de clasificación

Las de [`PARIDAD.md` § Leyenda](PARIDAD.md#leyenda-de-clasificación) (**E1–E11**, **P1–P5**, **R**,
**N**, **F**, **=**). Aquí además:

| | |
|---|---|
| P5 | Política de vacías genérica. **Ninguno** de los seis SP del grupo purgaba vacías ni descartaba la fila vacía de un titular con datos: toda fuente con flag de este grupo lo hereda del motor, como SIMIT y Cajacopi en la Fase 1 |
| P6 | Sello `datetime` truncado al segundo (RUES): el SP guardaba `GETDATE()` con milisegundos; el motor guarda la hora al segundo porque la auditoría (`SelloFechaConsulta`, `FechaAnterior`) es `datetime2(0)` y el script de reversión ubica las filas por igualdad exacta |
| P7 | Prefijo del fijo de 7 dígitos por departamento **o** municipio, sin tildes, con `PrefijoMunicipio` + `PrefijosDepartamento`; si no se resuelve, el fijo se anula con advertencia (el SP dejaba el número de 7 dígitos) |

## RUES

**Conclusión: paridad OK — 16 casos, 6 iguales, 10 diferencias explicadas (E1, E9, E10, E11, P2, P3, P5), 0 sin explicar.**
Iguales: titular nuevo completo, contenido idéntico (el SP inserta y deduplica; el motor refresca
la fecha), estado que cambia (conviven las dos filas), titular sin datos (fila vacía con flag 0 en
los dos), solo `NombreEmpresa` (fila con flag 0 en los dos: el SP no lo contaba para el flag y el
motor tampoco) y `NombreEmpresa` distinto (entra en la clave de los dos).

Detalles:

- **Sello (P6).** Los dos lados escriben la fecha de hoy con hora (`datetime`); el SP con
  milisegundos, el motor truncado al segundo. La comparación solo mira el día (`HOY`).
- **R05/R06 (P5).** El SP de RUES no tenía `DELETE … EstadoEnRUES = 0`: la vacía vieja quedaba y la
  fila vacía de un titular con datos se insertaba. Hoy `dbo.RUES` tiene 809.306 filas con flag 0 de
  873.188: el motor ya no suma vacías a titulares con datos.
- **R10 (P2).** Matrícula `0`: el SP la guardaba y el flag quedaba en 1 sin dato real.
- **R08.** El SP deduplica la Auxiliar con `ORDER BY FechaConsulta` sobre NULL: fila arbitraria.

#### Escenario RUES


Columnas del destino: `NumeroMatricula · EstadoEmpresa · ActividadEconomica · NombreEmpresa · EstadoEnRUES · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Predicción |
|---|---|---|---|---|---|---|---|
| R01 | Titular nuevo, todos los campos | 4042292 · Activa · Expendio a la mesa de comidas preparadas · LA ESQUINA SAS · 1 · HOY | 4042292 · Activa · Expendio a la mesa de comidas preparadas · LA ESQUINA SAS · 1 · HOY | RUES COMPLETO / RUES COMPLETO | L1/C29 / L1/C29 | igual | = |
| R02 | Documento inválido (4 dígitos) | 121046 · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | ≠ P3 documento inválido: el nuevo excluye la fila |
| R03 | Titular existente, contenido idéntico en destino | 96168 · Activa · Gestion de instalaciones deportivas · ∅ · 1 · HOY | 96168 · Activa · Gestion de instalaciones deportivas · ∅ · 1 · HOY | CARLOS EXISTENTE / CARLOS EXISTENTE | L1/C29 / L1/C29 | igual | = (SP: insert + dedup; nuevo: refresca la fecha) |
| R04 | Titular existente, estado distinto (Activa → Cancelada) | 207114 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15<br>207114 · Cancelada · ∅ · ∅ · 1 · HOY | 207114 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15<br>207114 · Cancelada · ∅ · ∅ · 1 · HOY | DIANA CAMBIA / DIANA CAMBIA | L1/C29 / L1/C29 | igual | = (conviven las dos filas) |
| R05 | Vacía vieja en destino; ahora trae datos | 467920 · Activa · ∅ · ∅ · 1 · HOY<br>∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | 467920 · Activa · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | difiere: destino | ≠ P5 política de vacías: el nuevo purga la vacía vieja (el SP de RUES no purgaba vacías) |
| R06 | Datos en destino; el archivo trae fila vacía | 943682 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · ∅ · ∅ · 0 · HOY | 943682 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | difiere: destino | ≠ P5 política de vacías: el nuevo no inserta la vacía (el SP la insertaba con flag 0) |
| R07 | Titular AJENO al archivo con duplicado exacto en destino | 121046 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15 | 121046 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15<br>121046 · Activa · ∅ · ∅ · 1 · viejo 2026-01-15 | AJENO AL ARCHIVO / AJENO AL ARCHIVO | — / — | difiere: destino | ≠ E9 sin limpiezas globales (el SP deduplica toda la tabla) |
| R08 | Titular repetido; la 2ª fila tiene más datos | 3551457 · ∅ · ∅ · ∅ · 1 · HOY | 3551457 · Activa · Confeccion de articulos textiles · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | ? SP conserva una fila arbitraria (FechaConsulta NULL en la Auxiliar); nuevo la más completa (E11) |
| R09 | Titular nuevo sin ningún dato | ∅ · ∅ · ∅ · ∅ · 0 · HOY | ∅ · ∅ · ∅ · ∅ · 0 · HOY | SIN DATOS / SIN DATOS | L1/C29 / L1/C29 | igual | = (fila vacía con flag 0 en los dos) |
| R10 | Solo matrícula '0' | 0 · ∅ · ∅ · ∅ · 1 · HOY | ∅ · ∅ · ∅ · ∅ · 0 · HOY | MATRICULA CERO / MATRICULA CERO | L1/C29 / L1/C29 | difiere: destino | ≠ P2 vacíos convencionales (matrícula 0 → NULL; el SP la guardaba y daba flag 1) |
| R11 | Estado 'N/A' y actividad real | ∅ · N/A · Comercio al por menor · ∅ · 1 · HOY | ∅ · ∅ · Comercio al por menor · ∅ · 1 · HOY | ESTADO NA / ESTADO NA | L1/C29 / L1/C29 | difiere: destino | ≠ P2 vacíos convencionales ('N/A' → NULL) |
| R12 | Par Líder/Cartera inactivo | 189859 · ∅ · ∅ · ∅ · 1 · HOY | 189859 · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | ≠ E10 sin relación con par inactivo |
| R13 | Nombre con tilde ('José Pérez') | 1001 · ∅ · ∅ · ∅ · 1 · HOY | 1001 · ∅ · ∅ · ∅ · 1 · HOY | José Pérez / JOSE PEREZ | L1/C29 / L1/C29 | difiere: titular | ≠ E1 nombre limpio |
| R14 | Solo NombreEmpresa (no entra al flag, como en el SP) | ∅ · ∅ · ∅ · TIENDA SIN MATRICULA · 0 · HOY | ∅ · ∅ · ∅ · TIENDA SIN MATRICULA · 0 · HOY | SOLO EMPRESA / SOLO EMPRESA | L1/C29 / L1/C29 | igual | = (flag 0 con la empresa guardada) |
| R15 | Mismo dato con otro NombreEmpresa | 5005 · Activa · ∅ · NUEVA SAS · 1 · HOY<br>5005 · Activa · ∅ · VIEJA SAS · 1 · viejo 2026-01-15 | 5005 · Activa · ∅ · NUEVA SAS · 1 · HOY<br>5005 · Activa · ∅ · VIEJA SAS · 1 · viejo 2026-01-15 | EMPRESA CAMBIA / EMPRESA CAMBIA | L1/C29 / L1/C29 | igual | = (NombreEmpresa está en la clave del SP y del nuevo) |
| R16 | Estado con espacios de sobra ('  Activa  ') | 6006 · '  Activa  ' · ∅ · ∅ · 1 · HOY | 6006 · Activa · ∅ · ∅ · 1 · HOY | ESPACIOS / ESPACIOS | L1/C29 / L1/C29 | difiere: destino | ≠ P2 limpieza de texto (trim; el SP guardaba los espacios) |

Lado nuevo:

```
Preview: leídas 16, excluidas 1, duplicadas 1, expansión +0, candidatas 14, insertadas 12, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 9, nombres reparados 0, relaciones nuevas 13.
Carga (ejecución 245): insertadas 12, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 9, nombres reparados 0, relaciones 13.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×13: Relaciones titular-cartera nuevas
- info ×9: Titulares nuevos que se crearán
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.RUES
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; RUES 873188→873188; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

Lado viejo (mensajes del batch):

```
── ejecución del cuerpo del SP ──
PRINT: 10 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 15 PERSONAS FUERON RELACIONADAS EN #Titular CARTERA EN LA BASE DE DATOS
PRINT: 2 DUPLICADOS FUERON ELIMINADOS DE #Dest
```

## Camara

**Conclusión: paridad OK — 23 casos, 7 iguales, 16 diferencias explicadas (E1, E9, E10, E11, P1, P3, P4, P5, P7, R), 0 sin explicar.**
Iguales: titular nuevo completo, fijo de 7 con 'ANTIOQUIA' (604) y con 'NORTE DE SANTANDER'
(607: el SP lo sacaba de `PrefijosDepartamento`; el motor también, porque `ConsultarPrefijos` suma
esa tabla a `PrefijoMunicipio`, que lo escribe 'N. DE SANTANDER'), número de 7 dígitos que empieza
en 3 (los dos lo prefijan), `maria@hotmail.com` (el SP de Cámara no cruzaba la lista negra; el motor
la cruza exacta y no la bloquea), contenido idéntico y email nuevo (conviven dos filas).

Detalles:

- **El SP de Cámara no validaba nada de contacto (P1).** Ni forma del teléfono, ni del email, ni
  `ListaNegraCorreos` (K08, K09, K11, K12). Hoy `dbo.Camara` tiene 33 emails sin `@…`, 13.037 con
  mayúsculas y 4.082 teléfonos que no son celular 3xx ni fijo 6xx de 10 dígitos (3.189 de ellos, de 7 dígitos).
- **K05/K07 (P7).** El SP casaba `UPPER(Departamento)` exacto contra `PrefijosDepartamento` (con
  tildes, collation sensible a acentos): 'BOGOTA D.C.' sin tilde o un departamento del exterior
  dejaban el número de 7 dígitos guardado (hoy hay 3.189 así). El motor prefija sin tildes y, si no
  resuelve, anula con advertencia.
- **K06 (R).** 'Seleccione un departamento' (227 filas hoy) y 'Cargando...' (15) son textos del
  formulario del proveedor: el motor los anula y por eso el fijo queda sin indicativo y el flag en 0.
- **K15 (P4).** El SP deduplicaba por titular + teléfono + email: una mudanza (municipio nuevo)
  borraba la fila vieja. El motor guarda las dos (historial).

#### Escenario Camara


Columnas del destino: `NumeroTelefono · Email · Municipio · Departamento · EstadoEnCamara · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Predicción |
|---|---|---|---|---|---|---|---|
| K01 | Titular nuevo, celular + email + localización | 3001234567 · camara1@gmail.com · MEDELLIN · ANTIOQUIA · 1 · HOY | 3001234567 · camara1@gmail.com · MEDELLIN · ANTIOQUIA · 1 · HOY | CAMARA COMPLETO / CAMARA COMPLETO | L1/C29 / L1/C29 | igual | = |
| K02 | Documento inválido (4 dígitos) | 3001234568 · ∅ · ∅ · ∅ · 1 · HOY | — | DOC CORTO / — | L1/C29 / — | difiere: destino, titular, relación | ≠ P3 documento inválido: el nuevo excluye la fila |
| K03 | Fijo de 7 + 'ANTIOQUIA' | 6042345678 · ∅ · ∅ · ANTIOQUIA · 1 · HOY | 6042345678 · ∅ · ∅ · ANTIOQUIA · 1 · HOY | FIJO ANTIOQUIA / FIJO ANTIOQUIA | L1/C29 / L1/C29 | igual | = (604 en los dos) |
| K04 | Fijo de 7 + 'NORTE DE SANTANDER' | 6072345679 · ∅ · ∅ · NORTE DE SANTANDER · 1 · HOY | 6072345679 · ∅ · ∅ · NORTE DE SANTANDER · 1 · HOY | FIJO NORTE / FIJO NORTE | L1/C29 / L1/C29 | igual | = (607 en los dos; PrefijoMunicipio lo escribe 'N. DE SANTANDER') |
| K05 | Fijo de 7 + 'BOGOTA D.C.' sin tilde | 2345670 · ∅ · ∅ · BOGOTA D.C. · 1 · HOY | 6012345670 · ∅ · ∅ · BOGOTA D.C. · 1 · HOY | FIJO BOGOTA / FIJO BOGOTA | L1/C29 / L1/C29 | difiere: destino | ≠ P7 prefijo sin tildes (el SP no casaba con 'BOGOTÁ D.C.' y guardaba 7 dígitos) |
| K06 | Fijo de 7 + 'Seleccione un departamento' | 2345671 · ∅ · ∅ · Seleccione un departamento · 1 · HOY | ∅ · ∅ · ∅ · ∅ · 0 · HOY | RELLENO / RELLENO | L1/C29 / L1/C29 | difiere: destino | ≠ R relleno del formulario anulado + P7 fijo sin prefijo anulado (flag 0; el SP guardaba ambos y flag 1) |
| K07 | Fijo de 7 + departamento del exterior ('HESSE') | 2345672 · ∅ · ∅ · HESSE · 1 · HOY | ∅ · ∅ · ∅ · HESSE · 0 · HOY | EXTERIOR / EXTERIOR | L1/C29 / L1/C29 | difiere: destino | ≠ P7 fijo sin prefijo anulado (el SP guardaba 7 dígitos) |
| K08 | Email 'NO TIENE' + celular | 3001234569 · NO TIENE · ∅ · ∅ · 1 · HOY | 3001234569 · ∅ · ∅ · ∅ · 1 · HOY | NO TIENE / NO TIENE | L1/C29 / L1/C29 | difiere: destino | ≠ P1 email con formato inválido anulado |
| K09 | Email en mayúsculas | ∅ · JUAN.CAMARA@GMAIL.COM · ∅ · ∅ · 1 · HOY | ∅ · juan.camara@gmail.com · ∅ · ∅ · 1 · HOY | MAYUSCULAS / MAYUSCULAS | L1/C29 / L1/C29 | difiere: destino | ≠ P1 email en minúsculas |
| K10 | Email 'maria@hotmail.com' (contiene la entrada 'a@hotmail.com') | ∅ · maria@hotmail.com · ∅ · ∅ · 1 · HOY | ∅ · maria@hotmail.com · ∅ · ∅ · 1 · HOY | MARIA / MARIA | L1/C29 / L1/C29 | igual | = (el SP no cruzaba la lista; el nuevo la cruza exacta) |
| K11 | Email exacto en ListaNegraCorreos ('no@gmail.com') | 3001234560 · no@gmail.com · ∅ · ∅ · 1 · HOY | 3001234560 · ∅ · ∅ · ∅ · 1 · HOY | VETADO / VETADO | L1/C29 / L1/C29 | difiere: destino | ≠ P1 lista negra de correos (el SP de Cámara no cruzaba ListaNegraCorreos) |
| K12 | Teléfono de 10 dígitos que no es celular ni fijo | 5551234567 · raro@gmail.com · ∅ · ∅ · 1 · HOY | ∅ · raro@gmail.com · ∅ · ∅ · 1 · HOY | RARO / RARO | L1/C29 / L1/C29 | difiere: destino | ≠ P1 teléfono validado |
| K13 | Titular existente, contenido idéntico en destino | 3001110013 · carlos@gmail.com · CALI · VALLE DEL CAUCA · 1 · HOY | 3001110013 · carlos@gmail.com · CALI · VALLE DEL CAUCA · 1 · HOY | CARLOS EXISTENTE / CARLOS EXISTENTE | L1/C29 / L1/C29 | igual | = (SP: insert + dedup; nuevo: refresca) |
| K14 | Titular existente, email nuevo | 3001110014 · diana.nueva@gmail.com · ∅ · ∅ · 1 · HOY<br>3001110014 · diana@gmail.com · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110014 · diana.nueva@gmail.com · ∅ · ∅ · 1 · HOY<br>3001110014 · diana@gmail.com · ∅ · ∅ · 1 · viejo 2026-01-15 | DIANA CAMBIA / DIANA CAMBIA | L1/C29 / L1/C29 | igual | = (conviven las dos filas) |
| K15 | Mismo teléfono y email, municipio nuevo | 3001110015 · mudanza@gmail.com · ENVIGADO · ANTIOQUIA · 1 · HOY | 3001110015 · mudanza@gmail.com · BELLO · ANTIOQUIA · 1 · viejo 2026-01-15<br>3001110015 · mudanza@gmail.com · ENVIGADO · ANTIOQUIA · 1 · HOY | SE MUDO / SE MUDO | L1/C29 / L1/C29 | difiere: destino | ≠ P4 clave completa (el SP deduplicaba por titular+teléfono+email y borraba la fila vieja) |
| K16 | Vacía vieja en destino; ahora trae datos | 3001110016 · ∅ · ∅ · ∅ · 1 · HOY<br>∅ · ∅ · ∅ · ∅ · 0 · viejo 2026-01-15 | 3001110016 · ∅ · ∅ · ∅ · 1 · HOY | VACIA VIEJA / VACIA VIEJA | L1/C29 / L1/C29 | difiere: destino | ≠ P5 política de vacías (purga) |
| K17 | Datos en destino; el archivo trae fila vacía (solo localización) | 3001110017 · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · CALI · ∅ · 0 · HOY | 3001110017 · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | TIENE DATOS / TIENE DATOS | L1/C29 / L1/C29 | difiere: destino | ≠ P5 política de vacías (el nuevo no inserta la vacía) |
| K18 | Titular AJENO con duplicado exacto en destino | 3001110018 · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | 3001110018 · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15<br>3001110018 · ∅ · ∅ · ∅ · 1 · viejo 2026-01-15 | AJENO / AJENO | — / — | difiere: destino | ≠ E9 sin limpiezas globales |
| K19 | Titular repetido; la 2ª fila tiene más datos | 3001110019 · ∅ · ∅ · ∅ · 1 · HOY | 3001110019 · repetido@gmail.com · ∅ · ∅ · 1 · HOY | REPETIDO / REPETIDO | L1/C29 / L1/C29 | difiere: destino | ? SP fila arbitraria; nuevo la más completa (E11) |
| K20 | Par Líder/Cartera inactivo | 3001110020 · ∅ · ∅ · ∅ · 1 · HOY | 3001110020 · ∅ · ∅ · ∅ · 1 · HOY | PAR INACTIVO / PAR INACTIVO | L5/C16 / — | difiere: relación | ≠ E10 sin relación con par inactivo |
| K21 | Nombre con tilde ('María Gómez') | 3001110021 · ∅ · ∅ · ∅ · 1 · HOY | 3001110021 · ∅ · ∅ · ∅ · 1 · HOY | María Gómez / MARIA GOMEZ | L1/C29 / L1/C29 | difiere: titular | ≠ E1 nombre limpio |
| K22 | Número de 7 dígitos que empieza en 3 + 'ANTIOQUIA' | 6043123456 · ∅ · ∅ · ANTIOQUIA · 1 · HOY | 6043123456 · ∅ · ∅ · ANTIOQUIA · 1 · HOY | SIETE CON TRES / SIETE CON TRES | L1/C29 / L1/C29 | igual | = (los dos lo prefijan: 6043123456) |
| K23 | Municipio '.' | 3001110023 · ∅ · . · CESAR · 1 · HOY | 3001110023 · ∅ · ∅ · CESAR · 1 · HOY | MUNICIPIO PUNTO / MUNICIPIO PUNTO | L1/C29 / L1/C29 | difiere: destino | ≠ R municipio sin letras anulado |

Lado nuevo:

```
Preview: leídas 23, excluidas 1, duplicadas 1, expansión +0, candidatas 21, insertadas 19, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 16, nombres reparados 0, relaciones nuevas 20.
Carga (ejecución 269): insertadas 19, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 16, nombres reparados 0, relaciones 20.
- error ×1: Documento inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×2: NumeroTelefono: fijo de 7 dígitos sin prefijo resoluble por departamento/municipio, anulado
- aviso ×1: Departamento: texto de relleno del formulario ('Seleccione…', 'Cargando…') anulado
- aviso ×1: Email: email con formato inválido anulado
- aviso ×1: Email: email en lista negra anulado
- aviso ×1: Líder 5 y cartera 16 no son un par activo de Lider_Cartera: la fila entra sin relación
- aviso ×1: Municipio: valor sin letras anulado
- aviso ×1: NumeroTelefono: teléfono inválido anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×20: Relaciones titular-cartera nuevas
- info ×16: Titulares nuevos que se crearán
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.Camara
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; Camara 1860429→1860429; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

Lado viejo (mensajes del batch):

```
── ejecución del cuerpo del SP ──
PRINT: 17 PERSONAS FUERON INGRESADAS A LA BASE DE DATOS
PRINT: 22 PERSONAS FUERON RELACIONADAS EN #Titular_Cartera LA BASE DE DATOS
PRINT: 22 PERSONAS FUERON INGRESADAS A CÁMARA
PRINT: 3 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL
```

## CamaraEmpresa

**Conclusión: paridad OK — 15 casos, 4 iguales, 11 diferencias explicadas (E9, E11, P1, P3, P5, P7, R), 0 sin explicar.**
Iguales: NIT nuevo completo, fijo de 7 con 'ANTIOQUIA' (604 en los dos), contenido idéntico y
email nuevo (conviven dos filas).

Detalles:

- **Clave NIT (extensión `ColumnaClaveTitular`).** `dbo.CamaraEmpresa` no tiene `TitularID`: su
  clave es `NitEmpresa` (bigint NULL; 71.424 filas, 69.389 NIT distintos, solo 2.999 coinciden con
  un `Titular`). El SP no tocaba `Titular` ni `Titular_Cartera`, y el motor tampoco: ninguna fila del
  lado nuevo creó titulares, relaciones ni avisos de "titular inexistente". Refresco, inserción,
  purga de vacías y reversión se hacen por `NitEmpresa`.
- **E15 (R).** 'NIT-DV' (`…-7`) no cabía en `CamaraEmpresaAuxiliar.NitEmpresa bigint`: el import
  fallaba y el SP nunca veía la fila. El motor le quita el DV (si no, lo pegaría al NIT y la fila no
  cruzaría con `ARLSura.NIT` en `vw_DatosCamaraEmpresa`) y lo informa.
- **E04/E05 (P7, R).** Igual que Cámara: el SP casaba el departamento exacto con tildes y guardaba 7
  dígitos si no casaba; el Departamento del archivo no se guarda en el destino, solo resuelve el indicativo.
- **E06–E08 (P1).** El SP no validaba teléfono ni email ni cruzaba `ListaNegraCorreos`.

#### Escenario CamaraEmpresa


Columnas del destino: `NumeroTelefono · Email · EstadoEnCamara · FechaConsulta`

| Caso | Descripción | SP viejo: destino | Nuevo: destino | Titular (SP / nuevo) | Relación (SP / nuevo) | Resultado | Predicción |
|---|---|---|---|---|---|---|---|
| E01 | NIT nuevo, celular + email + departamento | 3001234567 · empresa1@gmail.com · 1 · HOY | 3001234567 · empresa1@gmail.com · 1 · HOY | — / — | — / — | igual | = |
| E02 | NIT inválido (4 dígitos) | 3001234568 · ∅ · 1 · HOY | — | — / — | — / — | difiere: destino | ≠ P3 NIT inválido: el nuevo excluye la fila |
| E03 | Fijo de 7 + 'ANTIOQUIA' | 6042345678 · ∅ · 1 · HOY | 6042345678 · ∅ · 1 · HOY | — / — | — / — | igual | = (604 en los dos) |
| E04 | Fijo de 7 + 'BOGOTA D.C.' sin tilde | 2345679 · ∅ · 1 · HOY | 6012345679 · ∅ · 1 · HOY | — / — | — / — | difiere: destino | ≠ P7 prefijo sin tildes (el SP guardaba 7 dígitos) |
| E05 | Fijo de 7 + 'Seleccione un departamento' | 2345670 · ∅ · 1 · HOY | ∅ · ∅ · 0 · HOY | — / — | — / — | difiere: destino | ≠ R relleno + P7 fijo sin prefijo anulado (flag 0; el SP flag 1) |
| E06 | Email en mayúsculas | ∅ · VENTAS@EMPRESA.COM · 1 · HOY | ∅ · ventas@empresa.com · 1 · HOY | — / — | — / — | difiere: destino | ≠ P1 email en minúsculas |
| E07 | Email exacto en ListaNegraCorreos ('no@gmail.com') | 3001234569 · no@gmail.com · 1 · HOY | 3001234569 · ∅ · 1 · HOY | — / — | — / — | difiere: destino | ≠ P1 lista negra (el SP no la cruzaba) |
| E08 | Teléfono de 10 dígitos que no es celular ni fijo | 5551234567 · raro@empresa.com · 1 · HOY | ∅ · raro@empresa.com · 1 · HOY | — / — | — / — | difiere: destino | ≠ P1 teléfono validado |
| E09 | NIT existente, contenido idéntico | 3001110709 · igual@empresa.com · 1 · HOY | 3001110709 · igual@empresa.com · 1 · HOY | — / — | — / — | igual | = (SP: insert + dedup; nuevo: refresca) |
| E10 | NIT existente, email nuevo | 3001110710 · nuevo@empresa.com · 1 · HOY<br>3001110710 · viejo@empresa.com · 1 · viejo 2026-01-15 | 3001110710 · nuevo@empresa.com · 1 · HOY<br>3001110710 · viejo@empresa.com · 1 · viejo 2026-01-15 | — / — | — / — | igual | = (conviven las dos filas) |
| E11 | Vacía vieja; ahora trae datos | 3001110711 · ∅ · 1 · HOY<br>∅ · ∅ · 0 · viejo 2026-01-15 | 3001110711 · ∅ · 1 · HOY | — / — | — / — | difiere: destino | ≠ P5 política de vacías (purga) |
| E12 | Datos en destino; el archivo trae fila vacía | 3001110712 · ∅ · 1 · viejo 2026-01-15<br>∅ · ∅ · 0 · HOY | 3001110712 · ∅ · 1 · viejo 2026-01-15 | — / — | — / — | difiere: destino | ≠ P5 política de vacías (no inserta la vacía) |
| E13 | NIT AJENO con duplicado exacto en destino | 3001110713 · ∅ · 1 · viejo 2026-01-15 | 3001110713 · ∅ · 1 · viejo 2026-01-15<br>3001110713 · ∅ · 1 · viejo 2026-01-15 | — / — | — / — | difiere: destino | ≠ E9 sin limpiezas globales |
| E14 | NIT repetido; la 2ª fila tiene más datos | 3001110714 · ∅ · 1 · HOY | 3001110714 · repetida@empresa.com · 1 · HOY | — / — | — / — | difiere: destino | ? SP fila arbitraria; nuevo la más completa (E11) |
| E15 | 'NIT-DV' ('…-7') | — | 3001110715 · ∅ · 1 · HOY | — / — | — / — | difiere: destino | ≠ R el nuevo quita el DV (en la Auxiliar NitEmpresa era bigint: la fila no se podía importar) |

Lado nuevo:

```
Preview: leídas 15, excluidas 1, duplicadas 1, expansión +0, candidatas 13, insertadas 11, refrescadas 1, vacías descartadas 1, vacías a purgar 1, titulares nuevos 0, nombres reparados 0, relaciones nuevas 0.
Carga (ejecución 292): insertadas 11, refrescadas 1, vacías descartadas 1, purgadas 1, titulares nuevos 0, nombres reparados 0, relaciones 0.
- error ×1: NitEmpresa inválido o vacío (se esperan de 5 a 15 dígitos)
- aviso ×1: Departamento: texto de relleno del formulario ('Seleccione…', 'Cargando…') anulado
- aviso ×1: Email: email en lista negra anulado
- aviso ×1: NumeroTelefono: fijo de 7 dígitos sin prefijo resoluble por departamento/municipio, anulado
- aviso ×1: NumeroTelefono: teléfono inválido anulado
- aviso ×1: Titular repetido en el archivo: se conserva la fila más completa
- info ×2: Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)
- info ×1: Columna del archivo que no se usa: NombreEmpresa
- info ×1: Filas vacías que no entran porque el titular ya tiene datos
- info ×1: Filas vacías viejas que se eliminarán de dbo.CamaraEmpresa
- info ×1: NitEmpresa con dígito de verificación ('NIT-DV'): se guardó el NIT sin el DV
Limpieza: rango 0 filas; CargueFuenteEjecucion 0→0; CargueFuenteReversion 0→0; CamaraEmpresa 71424→71424; fuera de rango (doc inválido) destino 0→0, Titular 0→0.
```

Lado viejo (mensajes del batch):

```
E15: fila no importable a CamaraEmpresaAuxiliar (NitEmpresa='999000002715-7' no es bigint); el SP no la recibe
── ejecución del cuerpo del SP ──
PRINT: 13 EMPRESAS FUERON INGRESADAS A CÁMARA DE EMPRESAS
PRINT: 2 DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL DE EMPRESAS
```
