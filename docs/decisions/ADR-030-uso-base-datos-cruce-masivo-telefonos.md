# ADR-030 — Uso de Base de Datos: cruce masivo de teléfonos y acreditación por líder

**Estado:** Aceptada
**Fecha:** 2026-08-10
**Módulos afectados:** Informes → Informes Generales (sub-módulo nuevo)

---

## Contexto

Los teléfonos de los titulares viven dispersos en varias tablas de fuentes externas de
`DBCallCenter` (13 tablas / 18 columnas al 2026-08-14, ~12,2M filas; el set vigente es el del
código, ver `docs/schema.md`). El negocio necesita responder, para una lista de
números que se marcaron o usaron: **¿cuáles salieron de nuestra base de datos, y qué líder los
usó?** Eso permite acreditar el "uso de base de datos" y atribuirlo por líder.

`Rastreo de Líneas` (ADR-015) ya responde esa pregunta, pero **de a un número por vez**. No hay
forma de procesar una lista.

---

## Decisiones

### 1. Sub-módulo nuevo "Informes Generales", con tabs

Se crea `/informes/generales` como contenedor de informes transversales, con `Uso de Base de
Datos` como primer tab. Sigue el patrón de shell + carpeta por sub-módulo de ADR-023
(`pages/informes/informes-generales/`) — es la **primera** carpeta por sub-módulo dentro de
`pages/informes/`, alineada con la deuda documentada en `docs/para-tener-en-cuenta.md`.

Roles: **Admin y Director**. El informe mide el consumo de la base por líder, así que un líder
no debería auditarse a sí mismo.

### 2. Sin persistencia — staging en `#temp` de sesión

Todo ocurre en un request: parseo → `SqlBulkCopy` a `#Telefonos` → cruce → agregación. Las tres
tablas `#temp` mueren al cerrar la conexión (mismo patrón que `CargueReporteService`, ADR-011).
No se crea ninguna tabla nueva ni se toca `DBCallCenter`, que es propiedad de otro equipo.

**Consecuencia aceptada:** no queda registro de quién consultó qué. Se mitiga con un
`LogInformation`. Si el cruce llega a alimentar facturación, esta decisión hay que revisarla.

### 3. Cruce masivo por hash join, no consulta por número

El SQL de `RastreoLineasService.SqlConsulta` se adapta cambiando cada `WHERE col = @numero` por
`JOIN #Telefonos t ON col = t.Numero`. SQL Server resuelve las 14 ramas con hash joins,
escaneando cada tabla **una sola vez** sin importar cuántos números se carguen.

Medido contra producción: **1,06 s para 60 000 números** contra los ~12,2M registros. Consultar
número por número habría sido 14 scans **por número** — inviable.

Tope de entrada: **1 000 000 de números distintos** por consulta (`MaxTelefonosDistintos`). Es una
guarda de memoria y tiempo, no un límite del algoritmo. Medido en el peor caso — 1 000 000 de
números con 100 % de coincidencia, que maximiza el tamaño de `#Acreditacion`: **115 s**. De ahí
salen dos timeouts que hay que mantener por encima de ese número:

- `CommandTimeout = 600` s en el `SqlCommand` del cruce.
- `requestTimeout="00:20:00"` en `<aspNetCore>` de `web.config` — el default de ANCM es **2 min**,
  peligrosamente cerca de los 115 s medidos.

`#Telefonos.Numero` es `varchar(10)`, no `nvarchar`: `nvarchar` tiene mayor precedencia de tipo y
forzaría un `CONVERT_IMPLICIT` sobre la *columna de origen* en cada fila, anulando el hash join.

**No se crean índices** sobre las columnas de teléfono: no hacen falta con estos tiempos, y
`DBCallCenter` no es nuestra.

### 4. El set de tablas es EXACTAMENTE el de Rastreo de Líneas

Ambos módulos deben dar la misma respuesta para el mismo número. Se verificó por equivalencia:
para el mismo conjunto de números, el cruce masivo y el rastreo escalar producen filas
idénticas (`EXCEPT` vacío en ambas direcciones). Si se agrega una fuente, **hay que extender los
dos services** — ver `docs/schema.md → Tablas consultadas por búsqueda inversa de teléfono`.

### 4-bis. Dos porcentajes distintos por líder, y ambos hacen falta

- **Uso de su base** = `titulares usados / titulares asignados` (`Titular_Cartera`). Responde
  "¿qué tanto se explotó la base que le corresponde a este líder?".
- **Cuota del total** = `números del líder / total encontrado`. Responde "¿qué parte del resultado
  aportó?".

Se miden **en titulares, no en números**: la asignación se cuenta en titulares, y un titular puede
tener varios teléfonos, así que un numerador en números daría coberturas superiores al 100 %.

No son intercambiables. Medido sobre 1 000 000 de números reales: VICKI ACEVEDO tiene la mayor
cuota del total (10,00 %) pero un uso de su base **por debajo** del promedio (21,89 %), mientras
BASE GENERAL aporta apenas el 2,00 % del total y sin embargo es la que más explota su base
(29,71 %). El rango observado por líder fue 19-30 %.

La fila "(sin líder asignado)" no tiene asignación contra la cual medir: se muestra `—`, nunca 0 %.
Y un valor real pero menor a 0,01 % se muestra como `<0,01%` en vez de `0,00%`, para no leerse
como cero absoluto.

### 5. Conteo: total distinto + acreditación compartida explicada

- El KPI principal cuenta números **distintos** encontrados.
- El desglose por líder acredita cada número **íntegro a cada líder** que tenga a ese titular
  (multititularidad, ADR-015). Por eso `SumaAcreditaciones ≥ NumerosConLider`.
- La UI **explica la diferencia en pantalla** en vez de dejar que el usuario descubra el
  descuadre: se muestra cuántos números son compartidos y se aclara que la diferencia es
  acreditación compartida, no números adicionales.

### 6. `Titular_Cartera` es la tabla de iteración — nunca `Lider_Cartera`

`Titular_Cartera` ya tiene `LiderID` y `CarteraID`. Se parte de ahí con `LEFT JOIN`, y
`Lider_Cartera` entra **solo como enriquecimiento** (`EsExterna`, `Activo`), jamás como filtro.

Esto es una consecuencia directa del bug hallado en
`InformesRecaudoService.GetInformeLideresAsync`, donde iterar desde `Lider_Cartera` hacía
desaparecer del total, en silencio, todo pago cuyo par `(LiderID, CarteraID)` no existiera allí
(~63M COP). Aquí se agregan además dos salvaguardas:

- El bucket **"(sin líder asignado)"** se calcula explícitamente y se renderiza siempre, fijado
  al final de la tabla y nunca filtrable. **No es un caso de borde:** medido sobre 50 000 números
  reales, sólo el 13,7 % tiene líder — un `INNER JOIN` habría descartado el 86 % en silencio.
- El badge **"Sin relación en Lider_Cartera"** marca cualquier par presente en `Titular_Cartera`
  y ausente de `Lider_Cartera` (hoy son 0, es un centinela).

### 7. Se aceptan teléfonos fijos, no solo celulares

El único normalizador previo (`CargueReporteService.ValidarNumeroMarcado`) exige 10 dígitos
empezando en 3, lo que descarta los fijos. Medido: `Positiva.NumeroTelefonoFijo` tiene 331 049
registros, `Cajacopi` 20 371, `MutualSer` 6 783 — y `SOS.Telefono` guarda mayoritariamente
celulares pese al nombre. Filtrar por "empieza en 3" habría descartado ~357 000 registros
legítimos.

`TelefonoColombiano.Normalizar` acepta cualquier número de 10 dígitos, tolera el indicativo `57`
y el sufijo `.0` de Excel, y clasifica celular vs. fijo para mostrarlos por separado.

### 8. Multi-archivo, resiliente por archivo, sin adivinar la columna

- **Primer upload multi-archivo del proyecto** (`List<IFormFile>`; antes no había ninguno).
  Máx. 20 archivos / 120 MB. El frontend acumula selecciones en vez de reemplazarlas.
  El tope de 120 MB tiene que estar en tres capas o el upload muere antes de llegar al controller:
  `maxAllowedContentLength` en `web.config` (IIS filtra **antes** del pipeline y devuelve 404.13),
  `[RequestSizeLimit]` y `[RequestFormLimits]`.
- Un archivo que falle **no tumba el request**: se marca `Ok = false` con su motivo y los demás
  se procesan. Con 15 archivos, fallar todo por uno sería inutilizable.
- La columna se resuelve por alias de encabezado (ADR-004). Si ninguno matchea, el archivo falla
  indicando los encabezados que traía. **Deliberadamente no se adivina la columna**: en un
  informe de acreditación, elegir mal produciría cifras incorrectas en silencio.

---

## Consecuencias

**Positivas**
- Responde una pregunta de negocio que antes requería consultar número por número a mano.
- Cero entidades EF nuevas, cero DDL, cero cambios en `DBCallCenter`.
- Equivalencia demostrada con Rastreo de Líneas, así que ambos módulos no pueden divergir en
  silencio sin que la prueba lo detecte.
- Los helpers de parseo quedan en `Services/Shared/`, disponibles para el próximo módulo.

**Negativas**
- El set de fuentes queda duplicado en dos services: agregar una fuente exige tocar ambos.
  Documentado en `schema.md`; unificarlos exigiría un helper de SQL generado, que hoy no se justifica.
- Es el 6.º duplicado de `Normalizar` en el repo. Se extrajo a `Services/Shared/` solo para el
  código nuevo; consolidar los otros 5 es un refactor aparte con su propio riesgo.
- Sin persistencia no hay trazabilidad de consultas (ver decisión 2).
- Coincidencia exacta: no se hace `LTRIM/RTRIM` sobre la columna de origen (sería una función
  sobre 12,2M filas). Un teléfono guardado como `' 3001234567'` no se encuentra. Es el mismo
  comportamiento que Rastreo de Líneas.

---

## Verificación

Endpoint: `POST /api/informes/generales/uso-base-datos` (multipart, campo `archivos`).

**Equivalencia con Rastreo de Líneas** (la prueba clave): para un mismo conjunto de números, las
filas `(Numero, Etiqueta, TitularID, Apariciones)` del cruce masivo y del rastreo escalar deben
ser idénticas — `EXCEPT` vacío en ambas direcciones.

**Invariantes aritméticos** de la respuesta:
- `numerosEnBaseDatos + numerosNoEncontrados == telefonosDistintos`
- `numerosConLider + numerosSinLider == numerosEnBaseDatos`
- `numerosCompartidos + numerosUnSoloLider == numerosConLider`
- `sumaAcreditaciones >= numerosConLider`

**Funcionales**: multi-archivo con solapamiento (no debe doble-contar); archivo malo entre buenos;
XLSX con la columna en formato numérico (no debe producir `3,00123E+09`); un número de
`Positiva.NumeroTelefonoFijo` (debe encontrarse y contarse como fijo); rol `Líder` → 403.

---

## Pendientes

- Exportar el resultado a Excel/PDF (`buildExportarPDF` + `ModalColumnas` ya existen).
- Descargar la lista **completa** de no encontrados (hoy la respuesta trae una muestra de 100).
- Si el negocio necesita facturar sobre esto, persistir el lote con usuario y fecha.
