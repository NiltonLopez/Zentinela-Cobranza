# ADR-029 — Comparador de Carteras: categorías estandarizadas de gestión

**Estado:** Aceptado
**Fecha:** 2026-07-23
**Sustituye a:** ADR-017 (tres grupos + peso estricto)
**Módulos afectados:** Cartera → Comparador de Carteras (backend `api/comparador`, frontend, Excel)

---

## Contexto

El comparador clasificaba `UltimoPerfil` en solo 3 grupos (Paz y Salvo / Contacto
Positivo / Contacto Negativo, ADR-017). Negocio pidió estandarizar cómo se muestra
la información de perfiles con más granularidad: distinguir promesas vigentes,
contactos directos, contactos con terceros, marcaciones sin contacto y titulares
nunca gestionados. Además, la clasificación anterior solo miraba el texto del
perfil; la nueva necesita fechas (`FechaPromesa`) y `Rotacion`, y pasa a basarse
en `MejorPerfil` (no `UltimoPerfil`).

Primera iteración de las reglas: Contacto directo exigía `FechaUltimaGestion`
vigente y el "sin gestión" incompleto caía en Otros. Negocio corrigió: las 6
categorías reales deben cubrir la mayor parte de los datos y Otros debe quedar
como bucket residual mínimo. Se ajustó a la segunda iteración documentada abajo.

## Decisión

### 1. Siete categorías excluyentes, evaluadas en orden (la primera que aplica gana)

Definidas en `PerfilClasificacion.Clasificar(mejorPerfil, ultimoPerfil,
fechaPromesa, rotacion, fechaUltimaGestion, hoy)`. El valor del enum
`CategoriaPerfil` ES el peso del comparador. El orden de EVALUACIÓN (qué regla
se prueba primero) es independiente del orden de PESO (qué categoría es
"mejor" para el comparador):

| Orden eval. | Peso | Categoría | Regla |
|---|------|-----------|-------|
| 1 | 6 | **Ya pago** | `MejorPerfil` ∈ {Paz y salvo, Consignación por aplicar, Ya pago, legacy: Pago Total, Normalizado} |
| 2 | 5 | **Promesa** | `FechaPromesa` vigente (con cualquier tipificación) **O** `MejorPerfil` de acuerdo/compromiso ∈ {Acuerdo de pago, Acuerdo de pago total, Acuerdo de pago abono, Compromiso de pago total, Compromiso de abono, legacy: Promesa de Pago, Acuerdo} **con `FechaUltimaGestion` también vigente** (misma ventana; ver más abajo) |
| 3 | 3 | **Tercero** | `MejorPerfil` *contiene* "tercero" (Contacto con tercero, Mensaje con tercero, …) |
| 4 | 2 | **Sin contacto** | `MejorPerfil` ∈ {Sin contacto, No contacto, Mensaje en buzón, Mensaje contestadora/buzon, Numero equivocado, Ilocalizado, Fallecido, Datos de localización} **O** un caso incompleto de "sin gestión" (ver más abajo) |
| 5 | 4 | **Contacto directo** | `MejorPerfil` de contacto con el titular ∈ {Sin acuerdo/No hubo acuerdo, Renuente, Fraude o suplantación (de identidad), No gestionar, Volver a llamar, Realizo abono, Abonando, Recordar, Incumplido, Potencial acuerdo, Negociación vigente, Caso especial, PQR} **O** un acuerdo/compromiso de pago (set de la regla 2) que cayó de Promesa por no tener `FechaUltimaGestion` vigente — **sin filtrar por fecha** — **O** cualquier `FechaPromesa` que llegue hasta aquí sin ser vigente (catch-all, ver más abajo) |
| 6 | 1 | **Sin gestión** | `MejorPerfil` **y** `UltimoPerfil` sin gestión (o null/vacío) **y** `Rotacion` = 0 (null cuenta como 0) — el único caso realmente "nunca tocado" |
| 7 | 0 | **Otros** | `MejorPerfil` con un valor real no mapeado a ninguna categoría **y sin** `FechaPromesa` |

**Ventana de vigencia dinámica** de `FechaPromesa`: del 1 de enero del año
pasado al 31 de diciembre del año actual, calculada con la fecha del sistema
(nunca quemada, `hoy.Year - 1` .. `hoy.Year`). Un solo helper `EsVigente`.

**Acuerdo/compromiso de pago viejo → ya no es Promesa (2026-07-24).** La regla
original clasificaba cualquier `MejorPerfil` de acuerdo/compromiso como Promesa
sin importar la fecha (regla inclusiva confirmada por negocio en la iteración
anterior). Se detectó un caso real (TitularID 40771493, cartera LUMEN:
`MejorPerfil` = "Compromiso de pago total", sin `FechaPromesa`, última gestión
2022-12-20) donde eso hacía competir como Promesa un compromiso de hace 4 años
sin ninguna gestión desde entonces. Negocio confirmó: un acuerdo/compromiso
solo cuenta como Promesa si además `FechaUltimaGestion` está en la misma
ventana de vigencia (año pasado o actual); si no, cae a Contacto directo (hubo
contacto real, solo que viejo), nunca a Otros — se agregó `esAcuerdo` como
fallback explícito en la regla 5 para no perderlo.

**Contacto directo ya no depende de `FechaUltimaGestion`.** La primera iteración
excluía contactos directos con gestión vieja, y eso era la principal fuente de
"Otros". Ahora entra TODO perfil de contacto directo sin importar la fecha.

**Catch-all de `FechaPromesa` vencida → Contacto directo.** Si una fila llega
a la regla 5 con `FechaPromesa` poblada (no vigente, porque si lo fuera ya la
habría capturado Promesa en la regla 2), se clasifica como Contacto directo
aunque el perfil no esté en ningún set: haber tenido una fecha de promesa,
así sea vieja, implica que hubo contacto directo con el titular en algún
momento. Esto resuelve el caso "promesa 2024 para atrás" sin depender de un
año quemado — es simplemente "no vigente" en la ventana dinámica.

**"Sin gestión" incompleto → Sin contacto, no Otros.** La categoría Sin gestión
exige las 3 condiciones a la vez (`MejorPerfil` y `UltimoPerfil` sin gestión Y
`Rotacion` = 0). Si falta una pieza — p. ej. `Rotacion` > 0, o solo uno de los
dos perfiles está vacío — ya hubo al menos un intento de marcación registrado,
así que se trata como Sin contacto en vez de Otros.

Con estos dos cambios, la única vía real hacia **Otros** es un `MejorPerfil`
con un valor concreto que no está en ningún set (una tipificación nueva sin
mapear) y que además no tiene `FechaPromesa` — el bucket queda como canario de
QA para detectar códigos faltantes, no como refugio de casos mal cubiertos.

**Fraude/No gestionar dentro de Contacto directo** fue decisión explícita de
negocio (se evaluó excluirlo del comparativo y se descartó).

### 2. El comparador usa las categorías como peso

- **Base del comparativo**: todo titular cuya categoría en la cartera
  seleccionada sea < Ya pago (antes: peso < 2). El DTO renombra
  `TotalSinGestion` → `TotalBaseComparativo`.
- **Coincidencia**: categoría en otra cartera estrictamente mayor. Con más
  escalones el comparador es más fino (p. ej. Sin contacto → Contacto directo
  ahora sí es oportunidad).
- Nuevo `DesglosePorCategoria` en la respuesta: total de titulares de toda la
  cartera seleccionada por categoría (incluye Ya pago), para la tarjeta resumen.

### 3. El backend envía la categoría; el frontend no espeja listas

`TitularComparativoDto` agrega `CategoriaSeleccionada` / `CategoriaOtraCartera`
(etiqueta estandarizada) junto al perfil crudo, más `FechaPromesa`. La página
elimina los sets espejo de ADR-017: las chips muestran la categoría (color por
etiqueta, tooltip con el perfil crudo). Se paga la deuda de sincronización
aceptada en ADR-017.

Los Excel (sección y completo) agregan columnas "Categoría en …" junto a los
perfiles crudos y "Fecha Promesa".

### 4. Refactor del servicio

`ComparadorCarterasService` queda con un núcleo compartido
(`ObtenerMejorPorTitularAsync` + `ObtenerCoincidenciasAsync`) que usan
`CompararAsync`, `ObtenerParaExcelAsync` y `ObtenerTodosParaExcelAsync`.
Se elimina la re-consulta redundante y el código muerto que tenía
`ObtenerTodosParaExcelAsync`, y su dependencia del preview de 5.

### 5. Segunda tanda de tipificaciones (cobertura ampliada, 2026-07-24)

Negocio identificó, con la consulta de diagnóstico de "Otros", tipificaciones
reales del sistema que aún no estaban mapeadas. Se agregaron:

- **Contacto directo**: En acuerdo, Cliente pacta plan de pago, Posible
  acuerdo, Compromiso de pago, Compromiso de pago con campaña(descuento),
  Abonado, Recordacion de pago, Equivocado, Recordatorio, Recordatorio pago,
  Corre fecha, Solicitud de información.
- **Ya pago**: Pago reflejado.
- **Sin contacto**: Mensaje de voz, Ilocalizado total, Datos localización,
  Datos Localizacion (variantes de acento/redacción — la comparación es
  case-insensitive pero NO acento-insensitive, así que cada variante real
  necesita su propia entrada), No contestan, Correo, SMS, Carteo, Reclamación
  o derecho de petición, IVR, Se deja mensaje en contestadora, Numero fuera de
  servicio, Linea errada, Ilocalizado parcial, No contactado, Solicitud
  Masivo, Se solicita carta, No contesta, Fuera de servicio, Envio masivo :
  WPP-Mail, Telefono ocupado.
- **Tercero**: la regla de substring se amplió de solo "tercero" a
  "tercero" **O** "familiar" (para cubrir "Familiar" y variantes como
  "Contacto con familiar").

## Cómo agregar/quitar códigos

Todo en `src/Zentinela.Application/Config/PerfilClasificacion.cs` (sets
`YaPago`, `Acuerdos`, `ContactoDirecto`, `SinContacto`, `SinGestion`, más el
substring-check de Tercero). El frontend solo mapea etiqueta → color
(`COLOR_CATEGORIA` en `ComparadorCarterasPage.tsx`); una categoría nueva
requiere agregar su color ahí.

La comparación de perfiles es case-insensitive pero **no** acento-insensitive:
"Datos localización" y "Datos Localizacion" son strings distintos para el
clasificador y ambos deben estar en el set si ambas variantes existen en la
BD. Si aparece una tipificación real con una variante de acento/redacción no
cubierta, hay que agregarla explícitamente — no basta con que "se parezca" a
una ya mapeada.

Consulta de diagnóstico para detectar perfiles sin mapear (candidatos a
"Otros"): agrupa `UltimoEstadoTitular.MejorPerfil` excluyendo todos los sets
de este archivo (más los substring-check de "tercero"/"familiar"), separando
cuántas filas tienen `FechaPromesa` (esas se rescatan a Contacto directo) de
las que no (esas sí son "Otros"). Ver script en la conversación de
implementación — usa un `NOT EXISTS` contra un `VALUES` con todos los strings
de los sets, con `COLLATE Latin1_General_CI_AI` para que la comparación sea
también acento-insensitive (el clasificador en C# NO lo es, así que la
consulta debe ser más permisiva que el código para encontrar candidatos).

### 6. Tercera tanda: hallazgo con consulta de diagnóstico (2026-07-24)

Al correr la consulta de diagnóstico para el caso "Luma / Diego" (cartera +
líder específicos, 1.493 en Otros), la causa completa resultó ser **un solo
valor**: `"Mensaje en buzon"` (sin tilde) — ya existía `"Mensaje en buzón"`
(con tilde) en `SinContacto`, pero al no ser acento-insensitive la variante
sin tilde no calzaba con nada. Se agregó la variante.

Con la misma consulta corrida sin filtro (toda `DBCallCenter`), aparecieron
además, con volumen real:

- **Sin contacto**: `DATOS DE LOCALIZACION` (variante con "de", sin tilde),
  `CLIENTE FALLECIDO`, `Contestar Buzon`, `Se Dejo Mensaje`, `No Se Dejo
  Mensaje`, `No lo conocen`, `RECLAMACION O DERECHO DE PETICION` (sin tildes).
- **Contacto directo**: `RECORDATORIO DE PAGO`, `Solicitud De Informacion`
  (sin tilde).
- **Ya pago**: `"Pago  reflejado"` — la BD real tiene DOBLE espacio entre
  "Pago" y "reflejado"; el valor de la 2ª tanda (espacio simple) no lo cubría.
- **Promesa**: `GESTION WHATSAPP / PROMESA DE PAGO` (dice literalmente
  "promesa de pago", se agregó a `Acuerdos`).

Verificado corriendo la consulta antes y después del cambio: los 8 valores
desaparecieron de la lista de "sin mapear".

**Quedan 3 con corrupción de encoding, no de clasificación** (bajo volumen,
~19 filas en total): `Negociaci�n vigente`, `COMPROMISO DE PAGO CON
CAMPA�A(DESCUENTO)`, `Consignaci�n por Aplicar` — el carácter `�` reemplaza
una tilde por un problema de codificación en el cargue de datos de origen, no
una variante de texto real. No se intentó "parchear" esto con un string más
en el set porque el byte corrupto puede variar; es un problema para quien
administra el cargue, no del clasificador.

**Quedan 4 pendientes de decisión de negocio, por volumen alto** (no se
mapearon a ciegas):

| Valor | Filas sin FechaPromesa (caerían en Otros) |
|---|---|
| `Informativo Chat` | 15.130 — el mayor contribuyente de toda la BD |
| `GESTION WHATSAPP / NO INTENCION PAGO` | 116 |
| `REFINANCIACION SIN PAGO` | 20 |
| `PAGO NO REFLEJADO` | 11 |

Ojo con `PAGO NO REFLEJADO`: es semánticamente lo OPUESTO a `"Pago
reflejado"` (que sí está en YaPago) — no debe confundirse ni mapearse ahí.

### 7. Cuarta tanda: decisión de negocio sobre los 4 pendientes (2026-07-24)

Negocio resolvió los 4 casos de alto volumen que quedaron pendientes en la
tercera tanda:

| Valor | Categoría | Nota |
|---|---|---|
| `Informativo Chat` (15.130 filas) | Sin contacto | el mayor contribuyente de toda la BD |
| `GESTION WHATSAPP / NO INTENCION PAGO` | Sin contacto | |
| `REFINANCIACION SIN PAGO` | Contacto directo | |
| `PAGO NO REFLEJADO` | Contacto directo | no confundir con "Pago reflejado" (YaPago) |

De paso, negocio también movió **"Caso especial"**: estaba en Contacto
directo por criterio de la primera tanda, se reubicó a **Ya pago**.

Verificado con el proyecto de consola de verificación (`ClasificadorCheck`)
que los 5 cambios clasifican como se espera y que ningún caso existente se
rompió (en particular, que "Pago reflejado" sigue en Ya pago sin
confundirse con "Pago no reflejado").

## Supuestos a validar con negocio

Perfiles ubicados por criterio (primera tanda) que pueden moverse de set sin
tocar lógica:

- En **Contacto directo**: Incumplido, Potencial acuerdo, Negociación vigente,
  PQR.
- En **Sin contacto**: Mensaje en buzón, Mensaje contestadora/buzon,
  Ilocalizado, Fallecido, Datos de localización.
- Pendiente de negocio: cómo destacar en la UI el grupo Sin gestión
  (mejor+último perfil sin gestión y rotación 0); por ahora es una categoría
  más con chip propio.

**Casos confirmados con negocio:** "En acuerdo" se movió de Contacto directo a
`Acuerdos` (Promesa) — es la misma idea que "Acuerdo". "Numero equivocado" se
confirmó en Sin contacto (ya estaba así, quedó validado sin cambio de
código). **"Equivocado"** (sin la palabra "Numero") se confirmó que es la
misma idea que "Numero equivocado" — se movió de Contacto directo a Sin
contacto (2026-07-24).

Con esto no queda ningún caso pendiente de esta lista original de supuestos.

## Consecuencias

- Alcance limitado al Comparador y sus Excel; ningún otro módulo usaba
  `PerfilClasificacion` (verificado), los dashboards no cambian.
- `FechaUltimaGestion` deja de ser input de la clasificación (sigue viajando
  en el DTO solo como dato informativo en la tabla y el Excel).
- Lo que caiga en "Otros" debe revisarse en QA — es exclusivamente perfil sin
  mapear y sin fecha promesa; si el volumen es alto, falta agregar un código
  a algún set en `PerfilClasificacion.cs`.
- Tests unitarios del clasificador en
  `tests/Zentinela.API.Tests/PerfilClasificacionTests.cs`.

## Referencias

- `src/Zentinela.Application/Config/PerfilClasificacion.cs` — clasificador y enum `CategoriaPerfil`.
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/ComparadorCarterasService.cs`.
- `src/Zentinela.Application/DTOs/Carteras/ComparadorCarterasDto.cs`.
- `src/Infrastructure/Zentinela.Infrastructure.FileExporter/ExcelExporter.cs` — `GenerarComparativoCartera` / `GenerarComparativoCompleto`.
- `src/Zentinela.Web/src/pages/cartera/ComparadorCarterasPage.tsx`, `src/Zentinela.Web/src/types/comparador.ts`.
- ADR-017 (sustituido).
