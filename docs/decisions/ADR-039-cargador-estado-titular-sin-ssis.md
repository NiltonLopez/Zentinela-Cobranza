# ADR-039 — El flujo de datos del ETL de estado de titulares sale de SSIS

**Estado:** Aceptado — desplegado 2026-09-25
**Fecha:** 2026-09-25
**Módulo afectado:** `ETL_UltimoEstadoTitular` → `UltimoEstadoTitular`, `HistoricoEstadoTitular`
**Continúa:** [ADR-038](ADR-038-etl-estado-titular-reemplazo-acotado-y-compuertas.md) (compuertas y reemplazo acotado)

---

## Contexto

Después de ADR-038 el ETL ya no podía destruir datos, pero seguía sin poder cargar: en 32 bits
revienta por memoria con todos los archivos juntos, y el paso a 64 bits no prosperó porque el
proveedor ACE 16.0 está hardcodeado en el código de un Script Task. La salida de emergencia fue
cargar por lotes, a unos **57 minutos** por corrida.

Se pidió la mayor optimización posible. Antes de proponer nada se leyó el paquete entero (el
`.dtsx` está en `\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\`) y apareció
bastante más de lo que se veía desde afuera.

## Lo que hacía realmente el paquete

Flujo `Cargar Excel a Staging`, reconstruido de sus rutas:

```
Excel → Componente de script (fecha de promesa) → Columna derivada → División condicional
          ├─ Tipo_Cartera vacío  → Búsqueda CarteraID   (por nombre de archivo) ─┐
          └─ Tipo_Cartera con valor → Búsqueda Cartera ID 2 (por nombre de cartera) ┴→ Unión de todo
                                                                    → Búsqueda Agente → Destino (staging)
```

Cinco hallazgos, todos verificados en el XML del paquete o contra los datos:

**1. Las tres búsquedas eran sin caché** (`CacheType = 2`). Cada fila disparaba una consulta a la
base en cada búsqueda: del orden de **6 millones de consultas por carga** para ~1,9 millones de
filas. Es la explicación principal de los ~23 minutos que tardaba el paquete.

**2. El Excel era un intermediario sin función.** El conversor de Python ya leía el CSV bien (con
pandas) y ya limpiaba la gestión: su expresión regular deja solo letras, números, `,` `.` `-` `@` y
tildes, así que el `;`, las tabulaciones y los saltos de línea ya no existen cuando llega al Excel.
La protección contra esos caracteres la daba Python, no el Excel. El Excel solo servía para pasarle
los datos a SSIS, y es el que arrastraba el ACE → los 32 bits → los lotes.

**3. El ACE truncaba la gestión a 255 caracteres.** Para decidir el tipo de una columna, el ACE
mira solo las primeras 8 filas de la hoja. Si ninguna trae una gestión de más de 255 caracteres,
declara la columna como texto corto y **recorta en silencio todas las del archivo**. Por eso en la
tabla convivían gestiones de 500 y de exactamente 255. Y como `transformar()` conserva los
*últimos* 500 caracteres, el recorte se quedaba con el comienzo: **se perdía justo lo más reciente
de la gestión**. Comprobado: en todos los casos, lo que guardó SSIS es exactamente el comienzo de
la gestión completa.

**4. Cada búsqueda fallaba distinto.**

| Búsqueda | Sin coincidencia |
|---|---|
| CarteraID (por archivo) | `FailComponent`: tumba el paquete. Es el caso de `celucredit` |
| Cartera ID 2 (por tipo) | `IgnoreFailure`: la fila sigue con `CarteraID` nulo |
| Agente | `IgnoreFailure`: `AgenteID` nulo |

Las filas con `CarteraID` nulo chocan con el `NOT NULL` del staging, y el destino las manda a una
salida de error **que no está conectada**: se pierden sin dejar rastro. Pasa con
`Tipo_Cartera = 'ALIARCOM CASTIGADA'`, una cartera inactiva que el conversor sigue asignando en
`grupo_ceiba`.

**5. Todas las conversiones estaban en `FailComponent`.** Una sola identificación con una letra, o
una fecha rara, tumba la carga de las 19 carteras.

## Decisión

Sacar el flujo de datos de SSIS, conservando **exactamente** la limpieza:

```
limpiar.py    lee los CSV y aplica transformar() — IMPORTADO del conversor, no copiado
              → un .part por archivo, con separadores de control (U+001F campo, U+001E fila)
cargar.ps1    SqlBulkCopy de los .part a una tabla temporal (lector en streaming)
procesar.sql  la columna derivada, el script y las tres búsquedas como operaciones sobre conjuntos
              → staging → sp_Insertar_UltimoEstadoTitular / Historico (las compuertas de ADR-038)
```

Detalles que importan:

- **Cero divergencia en la limpieza.** `transformar()` se importa del archivo original. El cargador
  no tiene su propia copia de ninguna regla.
- **Separadores de control y no CSV entrecomillado.** U+001E y U+001F no aparecen en texto real; si
  alguno apareciera, el archivo se rechaza en vez de escribir una fila corrida.
- **Vacío = NULL**, igual que antes: xlsxwriter no escribía celdas con texto vacío y el ACE las
  leía como NULL.
- **Conversiones con estilo explícito.** El servidor corre en español (`dmy`): un literal
  `'2026-09-24'` en `datetime` se lee como año-*día*-mes.
- **Búsquedas con varias coincidencias.** SSIS tomaba la primera fila que devolvía la consulta; se
  imita con `MIN(ID)`, el orden del índice agrupado de `Cartera`. Afecta, por ejemplo, a las filas
  de `Delmar_Cast` sin `Tipo_Cartera`, que el nombre de archivo mapea a tres carteras.

### Cambios de comportamiento deliberados

| Caso | Antes | Ahora |
|---|---|---|
| Cartera sin registrar | Se caía todo | Se aparta y se informa |
| Tipo de cartera inactivo | Se perdía en silencio | Se aparta y se informa |
| Identificación inválida | Se caía todo | Se aparta y se informa |
| Gestión de más de 255 | Truncada según el archivo | Completa |
| Titular repetido en el archivo | Ganaba una fila al azar | Gestión más reciente; a igualdad, la última del archivo |

Lo apartado queda en `EtlEstadoTitular_Rechazo` (30 días, solo archivo, línea y motivo).

Con esto **dejan de hacer falta**: el Excel, el ACE, los 32 bits, la carga por lotes, mover
archivos entre carpetas y el alistamiento previo de carteras sin registrar.

### La compuerta también cambia

La referencia de "cuántas carteras trae una carga completa" pasa a salir de `Cartera` (activas con
archivo de origen), no de la historia de corridas. La historia se pudo contaminar dos veces el
2026-09-24: una carga por lotes que terminó OK con 2 carteras, y el último día del histórico con
~4 tras recargas parciales, que dejó al paquete publicar solo un lote de 7 (auditoría,
`EjecucionID 16`). La referencia de volumen solo toma corridas que fueron completas. Cubierto por
los casos 8 y 9 del banco de pruebas (24 de 24).

## Validación

Contra la carga que hizo SSIS el 2026-09-24 **a partir de los mismos CSV**, con el modo
`comparar` (no escribe nada permanente, no devuelve datos personales).

**Subconjunto** (Delmar_prej, Nova_Garant, Paradas_Credito, Rio_Verde — incluye la rama por
`Tipo_Cartera`):

- Conjuntos de titulares **idénticos** en las 5 carteras (0 de más, 0 de menos).
- Todas las columnas idénticas, salvo:
  - 518 titulares **repetidos** en el archivo: difieren en saldo, promesa o gestión porque SSIS
    elegía al azar cuál fila quedaba.
  - 354 gestiones que SSIS **truncó a 255**: en las 354, lo de SSIS es exactamente el comienzo de
    lo nuevo.
- Ninguna diferencia en titulares únicos fuera del recorte de gestión.

**Completa (19 archivos, 22 carteras)**, contra lo que dejó en `UltimoEstadoTitular` la carga por
lotes del 2026-09-24:

- 2.584.023 filas leídas → 41.339 apartadas → **2.321.834 titulares únicos**.
- Apartadas: las 41.339 son `tipo-cartera-inactiva` (`ALIARCOM CASTIGADA`, de `grupo_ceiba`). Es
  exactamente lo que SSIS perdía en silencio.
- **`solo_en_ssis = 0` en las 22 carteras**: no hay un solo titular que SSIS cargara y el cargador no.
- **19 carteras con conjuntos idénticos.** Las otras 3 (las de `Delmar_Cast`: IDs 8, 11 y 33) traen
  +324.087, +24.839 y +120.459 titulares que **no estaban** en la tabla. Ver abajo.
- Diferencias de columna, todas explicadas:
  - En titulares **repetidos** en el archivo (fila al azar en SSIS): saldo 21.946, fecha de promesa
    1.113, valor de promesa 1.139, gestión 2.671.
  - En titulares únicos: **45.287 gestiones truncadas a 255**; en el 100 % lo de SSIS es el comienzo
    exacto de lo nuevo.
  - 5 fechas de promesa en titulares únicos, todas con año de 3 dígitos (`0202-06-30` y similares):
    el cargador las dejaba en `NULL` porque `strftime('%Y')` en Windows no rellena con ceros.
    **Corregido** en `limpiar.py` (año siempre con 4 dígitos, como SSIS).

**Lo que destapó la comparación en `Delmar_Cast`.** Esas 469 mil filas faltantes no son un error
del cargador nuevo: la carga por lotes (`cargar-por-lotes.ps1`) procesó `Delmar_Cast` en un lote
propio, el paquete se quedó sin memoria a las ~271 mil filas, **y el script publicó igual** lo que
había llegado al staging. Como el reemplazo es por cartera (ADR-038), borró de la tabla los
titulares que no alcanzaron a entrar. Las compuertas no lo frenaron porque la referencia de volumen
era de otra cartera. Quedó corregido en el script: antes de publicar un lote comprueba en `SSISDB`
que el `Contenedor Foreach Por Cartera` terminó con éxito; si no, lo marca `INCOMPLETO` y no
publica. Verificado contra las ejecuciones reales: la de `Delmar_Cast` da *no se publicaría*, las
otras tres, *se publicaría*.

**Tiempo, desde la estación de trabajo:** 30 min 20 s (limpieza 23 min 23 s, subida 4 min 55 s,
proceso en SQL 1 min 58 s). La subida y buena parte de la limpieza son el enlace de 0,4 MB/s: los
CSV se leen por la red.

**Modo publicar**, en una base de prueba de LocalDB con las carteras reales del subconjunto:
publica de punta a punta (staging, compuertas, histórico) en 1 segundo, y la comparación de lo
publicado consigo mismo da cero diferencias en todas las columnas.

## Despliegue

Todo en `etl/estado-titular/desplegar.ps1`, desde la máquina del desarrollador:

```powershell
powershell -ExecutionPolicy Bypass -File etl\estado-titular\desplegar.ps1            # 1 a 4
powershell -ExecutionPolicy Bypass -File etl\estado-titular\desplegar.ps1 -Publicar  # 5
```

1. Copia el cargador a `\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\cargador\`
   (`E:\BASES\...` en el servidor).
2. Aplica, en orden: `2026-09-23_rediseno_etl_estado_titular.sql` (trae la compuerta nueva),
   `2026-09-25_cargador_estado_titular.sql` y `2026-09-23_avisos_etl_estado_titular.sql`.
3. Aplica `2026-09-25_trabajos_cargador_estado_titular.sql`, que crea dos trabajos con un paso
   `CmdExec` **ejecutado como `ProxySSIS`** (la cuenta que hoy ya corre el paquete y lee
   `E:\BASES`):
   - `ETL_UltimoEstadoTitular_Cargador` — modo `publicar`.
   - `ETL_UltimoEstadoTitular_Cargador_Comparar` — modo `comparar`, no escribe nada.

   Rutas **locales** (`E:\`); Python por el mismo `py.exe` que usa hoy la "Tarea Ejecutar
   proceso" del paquete; conexión integrada con la cuenta del proxy: **la contraseña de `sa` no
   aparece en ningún paso**. La salida de cada corrida queda en `cargador\logs\<modo>.log`.
4. Corre el trabajo de comparar y muestra el log.
5. Con `-Publicar`, corre el trabajo de publicar y espera a que termine.

Después: que Power Automate dispare `ETL_UltimoEstadoTitular_Cargador` en vez de
`Ejecutar_ETL_UltimoEstadoTitular`. Los CSV no se mueven ni se borran: Power Automate los
sobrescribe en cada descarga, igual que hoy.

**El trabajo viejo no se toca**: si algo sale mal, volver a disparar ese es el plan B completo.

## Primera corrida en el servidor (2026-09-25)

- **Comparar** en el servidor: resultado idéntico, cifra por cifra, al de la estación de trabajo
  con los mismos CSV → el Python y el pandas del servidor limpian igual. 1 min 17 s en total.
- **Publicar**: OK, 22 carteras, 2.322.388 titulares, 83.821 filas apartadas. Las carteras de
  `Delmar_Cast` volvieron completas (519.352 / 43.935 / 177.100).

| Fase | Estación de trabajo | Servidor |
|---|---|---|
| Limpieza (21 CSV) | 16 min | 43 s |
| Subida | 2 min 56 s | 10 s |
| Proceso del cargador | 40 s | 22 s |
| `sp_Insertar_UltimoEstadoTitular` | — | 11 min 31 s |
| `sp_Insertar_HistoricoEstadoTitular` | — | 5 min 37 s |
| **Total publicar** | | **18 min 32 s** |

Contra ~57 minutos por lotes, y sin 32 bits ni lotes. Lo que queda lento son **los dos
procedimientos de ADR-038**, no el cargador: van a ~3.400 filas por segundo, la misma tasa que
tenían con SSIS. Son el próximo objetivo de optimización.

Un ajuste que solo apareció en el servidor: la autenticación integrada **contra sí mismo por IP**
falla ("dominio que no es de confianza"); el cargador se conecta a la instancia local `.`, como el
`/SERVER "."` del paquete.
