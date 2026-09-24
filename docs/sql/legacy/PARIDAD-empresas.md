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
