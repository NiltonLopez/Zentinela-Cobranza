# ADR-031 — Histórico de Virtualidad: IVR pasa a carga única de resultados

**Estado:** Aceptada
**Fecha:** 2026-08-14
**Módulos afectados:** Administración → Gestión de Histórico de Virtualidad

---

## Contexto

El sub-módulo trabajaba en dos fases por canal: primero se cargaba la *Solicitud* (filas con
`Estado = NULL`) y después el *Resultado* (actualizaba `Estado` por `ID`).

En la práctica la segunda fase nunca se completó. Medido contra producción: **239.701 filas, todas
IVR, todas con `Estado NULL`**, con `FechaResultado` vacía en el 100 %, cargadas entre el 2026-07-17
y el 2026-08-04.

Mientras tanto el marcador (Asterisk/Issabel) ya entrega un archivo que trae **todo junto**: cartera,
líder, fecha de marcado, número, documento y el resultado de la llamada. La fase de Solicitud sobra.

---

## Decisiones

### 1. IVR pasa a carga única; SMS y Correo siguen en dos fases

El archivo del marcador se carga en un solo paso e inserta filas completas con `Estado` ya traducido.
SMS y Correo conservan el flujo de dos fases hasta que se definan sus formatos.

El modo vive en `CatalogoVirtualidad.ModoDe(canal)` (`ArchivoUnico` | `DosFases`), de modo que sumar
un canal es agregar un catálogo, no reescribir el módulo. Los seis endpoints de dos fases devuelven
**400 si el canal es IVR**, para que un cliente desactualizado no siga insertando por el camino viejo.

### 2. Catálogo de traducción `Last App` → `Estado`

11 códigos. Dos traducciones se apartan a propósito de lo que sugería el código crudo:

| `Last App` | `Estado` | Por qué |
|---|---|---|
| BackGround | **OPCION INCORRECTA** | La propuesta original era "ERROR". Pero su significado es "marcaron la opción que no era": el cliente **contestó e interactuó**. Llamarlo ERROR lo mezclaba con `ERROR PROCESO` (AGI) y `ERROR SISTEMA` (MixMonitor), que sí son fallas técnicas, y en los informes se habría perdido que hubo contacto real. |
| Dial | **OPCION 1** | La propuesta era "MARCAR". Es el estado de **éxito** (el cliente pulsó la opción 1), pero "marcar" se lee como "se discó la llamada", justo lo contrario. |

Los otros nueve: `AGI`→ERROR PROCESO, `Hangup`→COLGAR, `MixMonitor`→ERROR SISTEMA,
`Playback`→REPRODUCIR, `Set`→PROCESO INTERNO, `StopMixMonitor`→FIN GRABACION, `Wait`→ESCUCHANDO,
`WaitExten`→ESPERAR OPCION, `Congestion`→CONGESTIONADO.

El más largo mide 17 caracteres y `Estado` es `varchar(50)` — **sin DDL**. El `CHECK` de la tabla solo
restringe `Canal`. Las 4 etiquetas IVR anteriores (`Contestada`/`No contestada`/`Buzón`/
`Número errado`) se eliminan sin migración: nunca se escribieron (todas las filas tienen `NULL`).

El catálogo es **fuente única**: el frontend lo consume por `GET /api/virtualidad/historico/catalogo`
en vez de duplicarlo, y la UI muestra el mapeo completo en un desplegable para poder auditarlo.

### 3. Un código `Last App` desconocido descarta la fila

Decisión del negocio. **Lo que se pierde con eso**: el Informe de Virtualidad IVR mide *volumen* de
llamadas y no lee `Estado`, así que si el marcador agrega un código nuevo que aparece en miles de
filas, esas llamadas no entran y el informe queda subestimado.

La alternativa evaluada era cargarlas con `Estado = NULL` (que es exactamente lo que ya tienen las
239.701 filas existentes, o sea que no habría sido un valor inventado). Se descartó a favor de
mantener la tabla con solo estados del catálogo.

**Mitigación exigida:** las filas con código desconocido se cuentan aparte (`TotalCodigoDesconocido`),
sus códigos se listan (`CodigosDesconocidos`), y la UI los muestra en una caja ámbar **arriba de
todo** que dice cuántas filas NO se van a cargar. El botón de confirmar también lo dice. Debe ser
imposible confirmar sin haberlo visto.

### 4. Se re-sube el archivo al confirmar, en vez de devolver las filas

El flujo viejo devuelve `FilasValidas` completo al cliente y este se lo reenvía al confirmar. Con
archivos de 200k+ filas eso son ~100 MB de JSON de ida y vuelta.

Ahora `validar` devuelve solo resumen, agregados, muestra de 10 y una **huella SHA-256**; `confirmar`
reenvía el mismo `File` (que el navegador ya tiene) más la huella. El backend recalcula, compara y
**re-parsea**. Si no coincide → `409 Conflict`.

Beneficio adicional que no era el objetivo: hoy `confirmar` inserta el `CarteraID`/`LiderID` que
**dicta el cliente**. Al re-parsear en el servidor, lo insertado es por construcción lo validado.

Se descartó el staging server-side (memoria: ~60-100 MB retenidos por cargue en un host `inprocess`,
que se pierden con un reciclado; tabla de staging: DDL sobre una BD de otro equipo).

### 5. `SqlBulkCopy` con sello de lote uniforme

`AddRange` + `SaveChangesAsync` es inviable a 200k filas. Se inserta con `SqlBulkCopy` directo a la
tabla (no hace falta `#temp`: es un INSERT puro), con `SqlBulkCopyOptions.CheckConstraints` — sin esa
opción `SqlBulkCopy` **desactiva** la verificación y dejaría `FK_HistoricoVirtualidad_LiderCartera`
marcada como *untrusted*.

**`FechaCargue` lleva el MISMO valor en todas las filas del lote** (no el `DEFAULT GETDATE()` por
fila). Cuesta cero y convierte un cargue equivocado en reversible:

```sql
DELETE FROM dbo.HistoricoVirtualidad WHERE Canal='IVR' AND FechaCargue='<sello>';
```

La respuesta devuelve el sello y la UI lo muestra en la pantalla de éxito.

### 6. No se deduplica; se avisa

En IVR el mismo número **puede** marcarse legítimamente varias veces el mismo día (reintentos) y el
archivo no trae un ID de llamada: no hay clave natural, y cualquier dedupe estricto borraría datos
reales. En su lugar el preview muestra cuántos registros **ya existen** para esas carteras y ese rango
de fechas (query agrupada, servida por `IX_HistoricoVirtualidad_Canal_Cartera_Fecha`), más el conteo
de filas idénticas repetidas dentro del propio archivo. No bloquea.

### 7. Se elimina `FechaResultado` — código primero, SQL después

Era write-only: **1 escritura, 0 lecturas** en todo el repositorio. Script:
`docs/sql/migrations/2026-08-14_drop_fecharesultado_historicovirtualidad.sql`.

El orden importa. Con la columna ya borrada y la app vieja arriba, EF seguiría generando
`SELECT ... FechaResultado ...` en **toda** lectura de la tabla y romperían `VirtualidadIVRService`,
`VirtualidadMensajeriaService`, `VirtualidadPagosImpactoService` y el guard de borrado de
`GestionCarterasService`. Al revés el intervalo es inofensivo: una columna nullable que nadie mapea.

### 8. Detalles del parseo

- **CSV y XLSX** vía `Services/Shared/ArchivoTabular` (antes solo XLSX). Columnas resueltas por nombre
  de encabezado sin importar mayúsculas, tildes ni orden (ADR-004).
- Si **falta una columna obligatoria**, se responde nombrándola junto con las encontradas. **No se
  adivina ninguna columna** — mismo criterio que ADR-030.
- *Fecha Marcado* se guarda en la columna existente **`FechaEnvio`** (es la fecha del evento).
  Renombrarla habría roto tres servicios sin ganancia.
- **`Destino` no se normaliza**: solo `trim` + quitar el sufijo `.0` de Excel. Los 239.701 destinos
  ya guardados están en formato nacional limpio de 10 dígitos (236.708 de ellos), y normalizar solo
  lo nuevo partiría la misma línea en dos poblaciones, rompiendo la alerta de saturación y el índice
  `IX_HistoricoVirtualidad_Canal_Destino`.
- Filas con menos columnas que el encabezado se rechazan con error agrupado: `ExtraerFilasCsvAsync`
  hace `Split` plano y una fila corrida por comillas mapearía valores equivocados en silencio.

### 9. El informe de IVR se rehace como análisis de embudo

`Informes → Informes de Virtualidad → IVR` deja de ser un conteo de envíos por líder-cartera y pasa
a reproducir el informe "ANALISIS ROBOCALL": resumen global, embudo, desenlaces, comparativo por
líder y detalle por cartera. Endpoint `GET /api/virtualidad/ivr/analisis`.

**Sin "Solicitadas" ni "Enviadas"** (no existen en el modelo de carga única) y **sin "Atendidos" ni
"% Atención"**, que salen del CDR — Zentinela no lo guarda todavía (`LlamadaEntrante`/`LlamadaPerdida`
son agregados semanales, no registros por llamada). Se agregarán cuando exista la tabla de CDR.
"Campaña" se sustituye por **Cartera**, que es el dato que el archivo del marcador sí trae.

**Clientes únicos con jerarquía de mejor desenlace.** Un cliente marcado varias veces cuenta una sola
vez, con su mejor desenlace: `marcó 1 > escuchó sin marcar > sin marcación válida > solo audio > colgó`.
Los 11 estados se agrupan en 5 desenlaces; `CONGESTIONADO`, `ERROR PROCESO`, `ERROR SISTEMA`,
`PROCESO INTERNO` y `FIN GRABACION` quedan fuera de "contactados" (no hubo contacto real).

La deduplicación se resuelve en SQL con `MIN(rango)` por `(cartera, documento)`, y el resultado se
agrega a un cubo `(líder, cartera, rango)` de a lo sumo unas decenas de filas, del cual se derivan
todos los niveles del informe sin volver a la base.

**"Sin resultado" se separa de "sin contacto"** (rango 98 vs 99). Las 239.701 filas del flujo viejo
tienen `Estado NULL`: contarlas como no contactadas daría una contactabilidad del 0 % y se leería como
desempeño pésimo cuando en realidad es información faltante. Se excluyen de la base de los porcentajes
y se avisa arriba del informe cuántos clientes quedaron fuera y por qué.

---

## Consecuencias

**Positivas**
- Desaparece un paso manual que nunca se completaba.
- El cargue es reversible por sello de lote, sin DDL.
- El catálogo deja de estar duplicado entre backend y frontend.
- El servidor deja de confiar en el `CarteraID`/`LiderID` que manda el cliente.

**Negativas**
- Un código `Last App` nuevo descarta filas y subestima el informe de volumen hasta que se agregue al
  catálogo (ver decisión 3).
- Las 239.701 filas viejas quedan con `Estado NULL` para siempre: cualquier informe futuro que agrupe
  por estado verá ese bucket para el período jul-ago 2026.
- SMS y Correo conservan el round-trip de `FilasValidas` y el que el cliente dicte los IDs. Hay que
  migrarlos con este mismo diseño cuando se definan sus formatos.
- El archivo se sube dos veces (validar + confirmar). En LAN es ruido frente al insert.

---

## Verificación

Probado con archivos reales contra la BD de producción (solo lectura):

- CSV con separador `;` y encabezados `CARTERA` / `Líder` / `FECHA MARCADO` / `Número` / `Last App` /
  `Cédula` → las 6 columnas resuelven.
- Casing variado (`Playback`, `playback`, `STOPMIXMONITOR`) → traduce al mismo estado, y el desglose
  los agrupa en **una** fila mostrando el código canónico.
- XLSX con `Número` y `Cédula` en formato **numérico** → sin notación científica.
- Código inventado (`Verbose`) → fila rechazada, agrupada y listada aparte.
- Fecha inválida, cédula vacía, número vacío y cartera inexistente → un error agrupado cada uno.
- Huella incorrecta al confirmar → `CargaIvrConflictoException` (409).
- Solapamiento: contrastado contra la BD — reporta 19.986 filas para BERTAL el 4-ago y nada para el
  rango 1-3 ago, que es exactamente lo que hay en la tabla.

**Dos bugs encontrados y corregidos en esta verificación:**

1. **Corrupción silenciosa de fechas.** `TryParseFecha` intentaba `DateTime.TryParse` con
   `InvariantCulture` **antes** que los formatos explícitos, y esa cultura lee `01/08/2026` como
   *MM/dd* → 8 de enero en vez del 1 de agosto. Se invirtió el orden: los formatos explícitos
   (ISO primero, luego `dd/MM/yyyy`) van antes, y `TryParse` queda como último recurso.
2. El desglose agrupaba por `(Estado, CodigoCrudo)`, así que `Playback` y `playback` salían como dos
   resultados distintos. Ahora agrupa por `Estado` y muestra el código canónico del catálogo.

**Pendiente de medir con el archivo real:** el consumo de memoria de ClosedXML con un XLSX de ~200k
filas en un host IIS `inprocess`. La ruta CSV no tiene ese problema.
