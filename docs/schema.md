# Schema — DBCallCenter

> Generado: 2026-04-02 — Actualizado: 2026-09-24 (Fase 2 del Cargue de Fuentes de Datos, ADR-039: las 22 fuentes; tipos verificados con `sys.columns`)  
> Servidor: 10.0.0.89,1433  
> Base de datos: DBCallCenter  
> Total tablas: 88 | Vistas: 40 | Stored Procedures: 33 (conteo de `sys.tables` / `sys.views` / `sys.procedures` del 2026-09-24; no todas están documentadas aquí)

---

## Tablas de negocio principales

### Titular
Tabla central del sistema. Representa al deudor/cliente de cobranza.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | bigint | NO | SI | — |
| Nombre | varchar(70) | NO | — | — |
| NitEmpresa | bigint | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| FechaExpedicionDocumento | date | SI | — | — |

### Cartera
Define las carteras de cobranza disponibles en el sistema.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | int (identity) | NO | SI | — |
| NombreCartera | varchar(100) | NO | — | — |
| Activa | bit | NO | — | — |

### Lider
Líderes de cobranza del sistema.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | int | NO | SI | — |
| Nombre | varchar(100) | NO | — | — |
| Email | varchar(150) | SI | — | — |
| Activo | bit | NO | — | — |
| NumeroDocumento | bigint | SI | — | — |

`ID` **NO es IDENTITY**: se asigna manualmente como `MAX(ID)+1` al dar de alta un líder
(mismo patrón que `Cartera`). El mapping EF usa `ValueGeneratedNever()`.

`NumeroDocumento` es la cédula del empleado en `DBRecursosHumanos.Empleado` (vínculo lógico
líder ↔ empleado). Es **nullable**: los líderes externos (agencias, no empleados) pueden no
tener documento. Ver ADR-027.

### Lider_Cartera
Relación muchos-a-muchos entre Lider y Cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| LiderID | int | NO | SI | Lider |
| CarteraID | int | NO | SI | Cartera |
| EsExterna | bit | NO | — | — |
| Activo | bit | NO | — | — (default 1) |

`EsExterna = 1` marca la relación como operada por agencia externa o personal externo. Vive en la relación (no en `Cartera`) porque la misma cartera puede tener simultáneamente relaciones internas y externas según el líder que la trabaje. Las relaciones externas quedan excluidas de informes administrativos, metas, Dashboard Director, Comparador de Carteras y selector de Cargue de Pagos; se listan en `Informes → Agencias Externas`. Ortogonal al rol del usuario (`LiderExterno` / `AsesorExterno`). Migración: `docs/sql/migrations/2026-05-20_lider_cartera_es_externa.sql`.

`Activo = 0` retira (soft-delete) la relación sin borrarla, conservando el histórico anclado a `(LiderID, CarteraID)` —pagos, metas, mensajería, IVR— que el borrado físico rompería. Una relación inactiva desaparece de lo **operativo** (generación de bases, selectores, dashboards, metas) pero sigue visible en lo **histórico**: `InformesRecaudoService` NO filtra por `Activo`. Reversible (Reactivar). Ortogonal a `EsExterna`. La X de "Gestión de Líderes y Carteras" inactiva si hay histórico y elimina físicamente solo si la relación está limpia. Migración: `docs/sql/migrations/2026-06-11_lider_cartera_activo.sql` (aplicada 2026-06-11). Ver ADR-022.

### Titular_Cartera
Asignación de titulares a carteras y líderes. Tabla pivote principal del negocio.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | SI | Titular |
| LiderID | int | NO | SI | Lider_Cartera |
| CarteraID | int | NO | SI | Lider_Cartera |

### Agente
Agentes de cobranza vinculados al sistema ControlNext.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| AgenteID | bigint | NO | SI | — |
| UsuarioControlNext | varchar(100) | SI | — | — |
| Activo | bit | NO | — | — |
| NumeroCorporativo | varchar(10) | SI | — | CHECK: 10 dígitos, empieza en 3 (2026-09-18, ADR-037) |
| LinkWhatsapp | varchar(100) | SI | — | wa.me generado o acortado pegado a mano |
| ContactoActualizadoEn | datetime2 | SI | — | — |
| ContactoActualizadoPor | varchar(100) | SI | — | — |

### Cartera_Agente
Asignación de agentes a carteras y líderes.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| AgenteID | bigint | NO | SI | Agente |
| LiderID | int | NO | SI | Lider_Cartera |
| CarteraID | int | NO | SI | Lider_Cartera |

### Empresa
Empresas registradas en el sistema.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NIT | bigint | NO | SI | — |
| Nombre | varchar(255) | NO | — | — |

### Codeudor
Codeudores vinculados a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | bigint | NO | SI | — |
| Nombre | varchar(70) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| FechaExpedicionDocumento | date | SI | — | — |
| TitularRelacionado | bigint | SI | — | Titular |

### UltimoEstadoTitular
Estado actual de cada titular por cartera (snapshot más reciente de gestión).  
**PK real:** `(TitularID, CarteraID)` — AgenteID es campo actualizable, no parte de la clave.  
⚠️ **La escriben DOS procesos distintos**, y confundirlos ya costó un incidente:
1. El **ETL externo** `ETL_UltimoEstadoTitular` (paquete SSIS, trabajo del Agente), que es la
   vía principal: carga los Excel del día a `Staging_UltimoEstadoTitular` y de ahí publica con
   `sp_Insertar_UltimoEstadoTitular`. **No es código de Zentinela.**
   Ver `docs/dependencia-etl-ultimoestadotitular.md` y ADR-038.
2. Zentinela vía `CargueReporteService` (SqlBulkCopy + MERGE), para cargues manuales. Ver ADR-011.

⚠️ `FechaMejorPerfil` eliminada el 2026-04-24 (campo sin valor operativo — ver ADR-011).

**Índices:** `PK_UltimoEstadoTitular` clustered `(TitularID, CarteraID)`;
`IX_UltimoEstado_CarteraID (CarteraID, FechaUltimaGestion)`;
`IX_UltimoEstado_AgenteID (AgenteID, FechaUltimaGestion)`;
`IX_UltimoEstado_FechaPromesa (FechaPromesa)`. Todos en `PRIMARY`.

| Columna | Tipo | Nullable | PK | Notas |
|---------|------|----------|----|-------|
| TitularID | bigint | NO | SI | FK → Titular |
| CarteraID | int | NO | SI | FK → Cartera |
| AgenteID | int | SI | — | Actualizable via MERGE — NULL para titulares cargados sin asesor asignado |
| UltimoPerfil | varchar(50) | SI | — | |
| MejorPerfil | varchar(50) | SI | — | |
| SaldoTotal | money | SI | — | |
| NumeroMarcado | varchar(10) | SI | — | Solo celulares colombianos (10 dígitos, inicia en 3) |
| Rotacion | int | SI | — | Contador de gestiones |
| FechaUltimaGestion | date | SI | — | |
| FechaPromesa | date | SI | — | |
| ValorPromesa | money | SI | — | |
| MotivoNoPago | varchar(100) | SI | — | |
| Gestion | nvarchar(1000) | SI | — | Texto libre de la última gestión |
| FechaActualizacion | datetime | NO | — | Timestamp del último cargue (ETL o Zentinela) |

### Staging_UltimoEstadoTitular
Zona de aterrizaje del ETL externo: el paquete SSIS vuelca acá los Excel de todas las carteras
y recién después se publica a `UltimoEstadoTitular`. Mismas columnas y mismos tipos que ella.
**Heap sin índices** — es de escritura masiva y lectura de una sola pasada.
La vacía `sp_Insertar_HistoricoEstadoTitular` al terminar bien; si el ciclo falla, los datos
quedan ahí para poder reintentar sin volver a leer los Excel.

### HistoricoEstadoTitular
Historial de estados de gestión por titular, acumulado con cada cargue.  
**Clave semántica:** `(TitularID, CarteraID, AgenteID, Rotacion)` — una fila por gestión única. Ver ADR-012.  
⚠️ `FechaMejorPerfil` eliminada el 2026-04-24.  
✅ `FechaActualizacion` añadida el 2026-04-24 (requiere `ALTER TABLE ... ADD FechaActualizacion DATETIME NOT NULL DEFAULT GETDATE()`).

**Almacenamiento:** índice **clustered columnstore** `CCI_EstadoTitular` sobre el esquema de
partición `PS_EstadoTitular_Mensual` (función `PF_EstadoTitular_Mensual`, RANGE RIGHT mensual,
48 límites de abr-2023 a sep-2026).  
⚠️ **La columna de particionamiento es `FechaUltimaGestion`, no `FechaActualizacion`.** La purga
de retención filtra por `FechaActualizacion`, así que **no está alineada a la partición** y no
puede hacerse con un switch: es un DELETE sobre el columnstore. Verificado 2026-09-23.

| Columna | Tipo | Nullable | PK | Notas |
|---------|------|----------|----|-------|
| TitularID | bigint | NO | — | |
| CarteraID | int | NO | — | |
| AgenteID | int | SI | — | |
| UltimoPerfil | varchar(50) | SI | — | |
| MejorPerfil | varchar(50) | SI | — | |
| SaldoTotal | decimal(18,0) | SI | — | ⚠️ **Sin decimales**: en `UltimoEstadoTitular` es `money`, al pasar acá se pierden los centavos |
| NumeroMarcado | varchar(10) | SI | — | |
| Rotacion | int | SI | — | Discriminador de unicidad histórica |
| FechaUltimaGestion | date | SI | — | Columna de particionamiento |
| FechaPromesa | date | SI | — | |
| ValorPromesa | decimal(18,0) | SI | — | ⚠️ Mismo truncamiento que SaldoTotal |
| MotivoNoPago | varchar(100) | SI | — | |
| Gestion | nvarchar(1000) | SI | — | Texto libre de la gestión |
| FechaActualizacion | datetime | NO | — | UPDATE si misma Rotacion, INSERT si nueva |

### EtlEstadoTitular_Ejecucion
Auditoría de cada corrida del ETL de estado de titulares: qué trajo el staging, qué se reemplazó,
y por qué abortó cuando abortó. Creada el 2026-09-23 (ADR-038) porque SSISDB reporta éxito aunque
la corrida no haya hecho nada. Es además la referencia contra la que se comparan las compuertas
de volumen del siguiente cargue.

| Columna | Tipo | Notas |
|---------|------|-------|
| EjecucionID | bigint identity | PK |
| Paso | varchar(30) | `UltimoEstadoTitular` \| `HistoricoEstadoTitular` |
| Origen | varchar(30) | `SSIS` \| `Manual` \| `Recuperacion` |
| Forzada | bit | 1 = se saltaron las compuertas a propósito |
| FechaInicio / FechaFin | datetime2(3) | |
| Resultado | varchar(12) | `EN CURSO` \| `OK` \| `ABORTADA` \| `ERROR` |
| Motivo | nvarchar(600) | Por qué abortó, o el mensaje de error |
| FilasStaging, CarterasStaging | int | Lo que trajo la corrida |
| FilasAntes, FilasBorradas, FilasInsertadas, FilasDespues | int | Lo que hizo |
| FilasActualizadas, FilasPurgadas | int | Solo el paso de histórico |

**Vista `vw_EtlEstadoTitularEstado`** — una sola fila con la última carga buena, las horas
transcurridas y el resultado del último intento. Pensada para el aviso de frescura en la UI.
Devuelve **cero filas** mientras no haya ninguna corrida registrada: la app debe tolerarlo.

### CargueFuenteEjecucion
Auditoría de cada confirmación del módulo *Administración → Cargue de Fuentes de Datos* (ADR-039).
Una fila por cargue: se inserta `EN CURSO` **antes** de lanzar la carga (si el proceso muere, queda
la evidencia) y se cierra con los conteos del preview. Creada el 2026-09-23
(`docs/sql/migrations/2026-09-24_cargue_fuentes.sql`); mismo patrón que `EtlEstadoTitular_Ejecucion`.
Solo registra confirmaciones: una validación que no se confirma no deja fila.

| Columna | Tipo | Nullable | Notas |
|---------|------|----------|-------|
| EjecucionID | bigint identity | NO | PK clustered |
| JobID | uniqueidentifier | NO | Id del job en memoria de la API: enlaza la fila con el preview confirmado |
| Fuente | varchar(30) | NO | Clave del catálogo (`CatalogoCargueFuentes`, 22 fuentes): `Sura`, `Cajacopi`, `MutualSer`, `NuevaEps`, `SIMIT`, `DatosClaro`, `Positiva`, `SaludTotal`, `SOS`, `Coosalud`, `SaviaSalud`, `MiVacuna`, `Fosyga`, `RGM`, `TigoUne`, `Tigo`, `RUES`, `Camara`, `CamaraEmpresa`, `PositivaEmpresas`, `ARLSura`, `Compensados` |
| Usuario | varchar(100) | NO | Claim `userName` de quien confirmó |
| NombreArchivo | nvarchar(260) | NO | Nombre original (solo para mostrar; en disco se guarda como `archivo.<ext>`) |
| HuellaArchivo | char(64) | NO | SHA-256 hex; la confirmación exige que el archivo re-leído tenga la misma |
| FechaInicio / FechaFin | datetime2(3) | NO / SI | `FechaFin` NULL mientras está EN CURSO |
| Estado | varchar(12) | NO | CHECK `EN CURSO` \| `OK` \| `ERROR` \| `CANCELADA`; default `EN CURSO` |
| SelloFechaConsulta | datetime2(0) | SI | Valor exacto escrito en `FechaConsulta`/`FechaInsercion` (fecha del servidor SQL). En RUES (destino `datetime`) lleva hora, truncada al segundo |
| TotalLeidas, Excluidas, DuplicadasArchivo, Candidatas | int | SI | Invariante: `TotalLeidas + expansión = Excluidas + DuplicadasArchivo + Candidatas` |
| Insertadas, Refrescadas, DescartadasVacias | int | SI | Invariante: `Candidatas = Insertadas + Refrescadas + DescartadasVacias` |
| VaciasPurgadas | int | SI | Filas vacías viejas del destino eliminadas por la política de vacías |
| TitularesNuevos, NombresReparados, RelacionesNuevas | int | SI | Escrituras en `Titular` / `Titular_Cartera` |
| Advertencias | int | SI | Total de advertencias del preview |
| Error | nvarchar(1000) | SI | Mensaje si `ERROR`/`CANCELADA`. `Interrumpida (reinicio del servidor)` = murió con un reciclaje de IIS. Si empieza con `REVERTIDA …`, la ejecución (OK) se deshizo con `docs/sql/revertir_cargue_fuente.sql` |

Índice: `IX_CargueFuenteEjecucion_Fuente` (Fuente, EjecucionID DESC). La lista "en curso / últimas 48 h"
de la UI lee esta tabla y, si encuentra una fila `EN CURSO` sin job en memoria, la muestra como
**Interrumpido** y la cierra como `ERROR` con ese motivo.

### CargueFuenteReversion
Qué filas de la tabla de la fuente tocó cada ejecución, para poder deshacerla (ADR-039). La llena el
motor con `OUTPUT … INTO` dentro de la misma transacción del cargue. **Sin FK, sin CHECK y sin triggers a
propósito**: SQL Server los prohíbe en el destino de `OUTPUT INTO`; agregar cualquiera rompe el cargue en
runtime. No registra `Titular` ni `Titular_Cartera` (no se revierten).

| Columna | Tipo | Nullable | Notas |
|---------|------|----------|-------|
| EjecucionID | bigint | NO | `CargueFuenteEjecucion.EjecucionID` (sin FK) |
| TitularID | varchar(30) | NO | Clave de la fila tocada, como texto: el TitularID (NuevaEps lo guarda como varchar) o, en CamaraEmpresa, el `NitEmpresa` |
| HashContenido | binary(32) | NO | `HASHBYTES('SHA2_256', CONCAT_WS(N'\|', ISNULL(CONVERT(nvarchar(400), col), N'~') …))` sobre TODAS las columnas de dato, en el orden del catálogo (una sola columna: sin `CONCAT_WS`). `0x00` para `D` |
| Accion | char(1) | NO | `I` insertada · `U` refrescada · `D` vacía purgada |
| FechaAnterior | datetime2(0) | SI | `U`: fecha previa al refresco · `D`: fecha de la fila purgada · `I`: NULL. De RUES queda redondeada al segundo (el `GETDATE()` legacy tenía milisegundos) |

Índice: `IX_CargueFuenteReversion` clustered (EjecucionID, TitularID). Una ejecución puede tener más `U` que
`Refrescadas`: una fila del archivo refresca todas las filas idénticas del destino (duplicados legacy).
De una `D` solo se guarda la fecha: al revertir se reinserta como fila vacía y las columnas que no entran
al flag vuelven en NULL.

### HistoricoVirtualidad
Histórico consolidado de virtualidad (IVR / SMS / Correo). Una sola tabla con discriminador `Canal`. Único punto de escritura del módulo "Gestión de Histórico de Virtualidad" (Administración) — reemplazó las tablas legacy `IVREnviados`/`MensajesEnviados` (consolidación 2026-07-06). Renombrada de `SolicitudVirtualidad` el 2026-07-08 (`docs/sql/migrations/2026-07-08_rename_solicitud_a_historico_virtualidad.sql`), 0 filas al momento del rename.

**Dos formas de carga según el canal (ADR-031):**
- **IVR — carga única:** el archivo del marcador ya trae el resultado, así que la fila se inserta completa con `Estado` traducido desde el código `Last App`. `FechaCargue` lleva el MISMO valor en todas las filas del lote, lo que permite revertir un cargue con `DELETE ... WHERE Canal='IVR' AND FechaCargue='<sello>'`.
- **SMS / Correo — dos fases:** se inserta con `Estado = NULL` y una carga posterior lo actualiza por PK.

`Estado = NULL` significa "sin resultado conocido". Las 239.701 filas IVR cargadas entre 2026-07-17 y 2026-08-04 por el flujo viejo de dos fases quedaron con `Estado NULL` de forma permanente (nunca recibieron su fase Resultado); cualquier informe que agrupe por `Estado` verá ese bucket. La columna `FechaResultado` se eliminó el 2026-08-14 (`docs/sql/migrations/2026-08-14_drop_fecharesultado_historicovirtualidad.sql`): era write-only, sin ninguna lectura en el código.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | bigint | NO | SI | — |
| Canal | varchar(10) | NO | — | CHECK IN (IVR, SMS, Correo) |
| NumeroDocumento | bigint | NO | — | — |
| Destino | varchar(255) | NO | — | teléfono (IVR/SMS) o email (Correo) |
| CarteraID | int | NO | — | FK compuesta → Lider_Cartera |
| LiderID | int | NO | — | FK compuesta → Lider_Cartera |
| FechaEnvio | date | NO | — | — |
| Estado | varchar(50) | SI | — | NULL = sin resultado conocido |
| FechaCargue | datetime2 | NO | — | DEFAULT GETDATE(); el cargue IVR escribe un sello uniforme por lote |

Índices: `IX_HistoricoVirtualidad_Canal_Cartera_Fecha` (Canal, CarteraID, FechaEnvio), `IX_HistoricoVirtualidad_Canal_Destino` (Canal, Destino).

### MensajeAutorizado
Catálogo cerrado de textos SMS (ADR-037). Los escriben abogados/encargados (Admin, Director) en *Administración → Mensajes Autorizados*; el líder solo elige uno en *Cartera → Creador de Virtualidad*. Texto con placeholders `{NOMBRE}`, `{CARTERA}`, `{EMPRESA}`, `{CELULAR_ASESOR}`, `{LINK_ASESOR}` — los dos del asesor son obligatorios (CHECK); Cartera y Empresa dependen de columnas opcionales del archivo de entrada. Solo alfabeto GSM-7 (validado en la API). **Sin borrado físico**: `EstrategiaSms` lo referencia; se desactiva. Creada el 2026-09-18 (`docs/sql/migrations/2026-09-18_creador_virtualidad_sms.sql`).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | int (identity) | NO | SI | — |
| Titulo | varchar(100) | NO | — | — |
| Texto | varchar(320) | NO | — | CHECK: contiene `{CELULAR_ASESOR}` y `{LINK_ASESOR}` |
| Activo | bit | NO | — | DEFAULT 1 |
| FechaCreacion | datetime2 | NO | — | DEFAULT GETDATE() |
| UsuarioCreacion | varchar(100) | NO | — | — |
| FechaActualizacion | datetime2 | NO | — | DEFAULT GETDATE() |
| UsuarioActualizacion | varchar(100) | NO | — | — |

Índice: `IX_MensajeAutorizado_Activo` (Activo) INCLUDE (Titulo).

### EstrategiaSms
Auditoría de cada archivo SMS generado por el Creador de Virtualidad (ADR-037): quién, cuándo, con qué mensaje, y cuántos titulares entraron o se excluyeron por cada motivo. **No registra envíos** — eso lo hace virtualidad en `HistoricoVirtualidad`. Creada el 2026-09-18.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | bigint (identity) | NO | SI | — |
| LiderID | int | NO | — | Lider |
| UsuarioGenero | varchar(100) | NO | — | — |
| FechaGeneracion | datetime2 | NO | — | DEFAULT GETDATE() |
| MensajeAutorizadoID | int | NO | — | MensajeAutorizado |
| TotalLeidos | int | NO | — | — |
| TotalGenerados | int | NO | — | — |
| ExcluidosPerfil | int | NO | — | archivo + histórico BD |
| ExcluidosListaNegra | int | NO | — | tabla + archivo |
| ExcluidosLongitud | int | NO | — | superaban 160 con el peor link |
| ExcluidosOtros | int | NO | — | cédula/teléfono inválido, fijo, nombre vacío, duplicado |
| HuellaArchivo | char(64) | NO | — | SHA-256 hex del archivo de entrada |
| NombreArchivo | varchar(255) | NO | — | — |

Índices: `IX_EstrategiaSms_Lider_Fecha` (LiderID, FechaGeneracion DESC), `IX_EstrategiaSms_Huella` (HuellaArchivo).

### PORTAFOLIO
Tabla de portafolio completo con datos de cliente, financiacion y contacto (importada desde fuente externa).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Tipo de documento cliente | nvarchar(510) | SI | — | — |
| Nit / Cedula | float | SI | — | — |
| nombre_cliente | nvarchar(510) | SI | — | — |
| Nombre 1 | nvarchar(510) | SI | — | — |
| Nombre 2 | nvarchar(510) | SI | — | — |
| Apellido 1 | nvarchar(510) | SI | — | — |
| Apellido 2_ | nvarchar(510) | SI | — | — |
| factura_obligacion | nvarchar(510) | SI | — | — |
| Telefono 1 SAC | float | SI | — | — |
| Telefono 2 SAC | float | SI | — | — |
| Celular 2 SAC | float | SI | — | — |
| Celular 1 SAC | float | SI | — | — |
| Direccion SAC | nvarchar(510) | SI | — | — |
| email SAC | nvarchar(510) | SI | — | — |
| Ciudad / Municipio SAC | nvarchar(510) | SI | — | — |
| BARRIO SAC | nvarchar(510) | SI | — | — |
| Telefono 1 Reconocer | float | SI | — | — |
| Departamento | nvarchar(510) | SI | — | — |
| Celular 1 Reconocer | float | SI | — | — |
| Telefono 2 Reconocer | float | SI | — | — |
| email Reconocer | nvarchar(510) | SI | — | — |
| Celular 2 Reconocer | float | SI | — | — |
| Empresa donde labora el cliente | nvarchar(510) | SI | — | — |
| Actividad laboral | nvarchar(510) | SI | — | — |
| TIPO DE CREDITO | nvarchar(510) | SI | — | — |
| Ingresos | float | SI | — | — |
| dias_de_mora | float | SI | — | — |
| fecha_de_vencimiento | datetime | SI | — | — |
| Aval | float | SI | — | — |
| Capital | float | SI | — | — |
| Nota debito | nvarchar(510) | SI | — | — |
| Intereses corrientes | nvarchar(510) | SI | — | — |
| Seguros | float | SI | — | — |
| Abonos | float | SI | — | — |
| Valor de la Cuota | float | SI | — | — |
| Otros | nvarchar(510) | SI | — | — |
| Interes de mora | float | SI | — | — |
| Iva Interes | float | SI | — | — |
| Interes Neto | float | SI | — | — |
| Honorarios con iva | float | SI | — | — |
| Vlr_Saldo total | float | SI | — | — |
| Marca | nvarchar(510) | SI | — | — |
| Regional | nvarchar(510) | SI | — | — |
| Nombre de la empresa | nvarchar(510) | SI | — | — |
| Ciudad del Punto de credito | nvarchar(510) | SI | — | — |
| Region | nvarchar(510) | SI | — | — |
| Estado de la cuota | nvarchar(510) | SI | — | — |
| Empresa que reporta | nvarchar(510) | SI | — | — |
| Fecha financiacion | datetime | SI | — | — |
| Periodicidad del pago | nvarchar(510) | SI | — | — |
| Nombre ref familiar | nvarchar(510) | SI | — | — |
| Parentesco Ref 1-Familiar | nvarchar(510) | SI | — | — |
| Celular/telefono ref 1 familiar | float | SI | — | — |
| Nombre ref 2 familiar | nvarchar(510) | SI | — | — |
| Parentesco Ref 2-Familiar | nvarchar(510) | SI | — | — |
| Celular/telefono ref 2 familiar | nvarchar(510) | SI | — | — |
| Fecha de nacimiento | datetime | SI | — | — |
| Genero | nvarchar(510) | SI | — | — |
| Estado Civil | nvarchar(510) | SI | — | — |
| Antiguedad del cliente | datetime | SI | — | — |
| Score cobranza | nvarchar(510) | SI | — | — |
| tipo_de_credito | nvarchar(510) | SI | — | — |
| Segmento mayor | nvarchar(510) | SI | — | — |
| fecha_del_reporte | datetime | SI | — | — |
| fecha_promesa1 | datetime | SI | — | — |
| fecha_promesa2 | nvarchar(510) | SI | — | — |
| Cod de gestion Positivo ultimos 3 meses | nvarchar(510) | SI | — | — |
| Ultimo codigo de gestion | nvarchar(510) | SI | — | — |
| Telefono mas efectivo | float | SI | — | — |
| Casa de cobro | nvarchar(510) | SI | — | — |

---

## Tablas fuentes externas

Estas tablas almacenan datos consultados desde entidades externas (ARL, EPS, Camara de Comercio, etc.).

> **Cómo se cargan (2026-09-24, ADR-039):** **todas** las fuentes de esta sección se cargan desde
> **Administración → Cargue de Fuentes de Datos** — Fase 1: `Sura`, `Cajacopi`, `MutualSer`, `NuevaEps`,
> `SIMIT`, `DatosClaro`; Fase 2: `Positiva`, `SaludTotal`, `SOS`, `Coosalud`, `SaviaSalud`, `MiVacuna`,
> `Fosyga`, `RGM`, `TigoUne`, `Tigo`, `RUES`, `Camara`, `CamaraEmpresa`, `PositivaEmpresas`, `ARLSura`,
> `Compensados` —, salvo `Runt` (vacía, fuera del módulo) y `DatosExternos` (no consumida). Los
> `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar` quedan sin uso hasta su DROP. En todas el modelo es
> **historial acumulativo**: nunca se modifica el contenido de una fila; si llega igual se refresca su
> `FechaConsulta`, si cambió se inserta otra fila, y las vistas toman la última por fecha. Ver
> "Flujo de carga masiva" al final. En cada tabla, la nota **Carga** dice qué hace la regla de la fuente;
> "FK → Titular" solo donde la FK existe en la BD (verificado con `sys.foreign_keys` el 2026-09-24).

### ARLSura
Datos de afiliacion a ARL Sura por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| EstadoEnSura | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Alias renombrados el 2026-09-02** (migración `2026-09-02_sura_in_basemadre.sql`): esta fuente
> exponía `[NIT Sura]` / `[Empresa Sura]`, nombres que en realidad le corresponden a la fuente `Sura`
> (ver más abajo). Pasaron a `[NIT ARL Sura]` / `[Empresa ARL Sura]`, con las propiedades C#
> `NITArlSura` / `EmpresaArlSura`. En el panel de localización del Calificador la etiqueta pasó de
> `Sura (ARL)` a `ARL Sura` y su slug de `sura` a `arlsura`.
>
> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, fuente `ARLSura` ("ARL Sura (empleador)"),
> `ReglaGenerica`. **Otra fuente que `Sura` (la EPS)**: otra clave de ruta, otra etiqueta, otra tabla; el
> flag se llama igual (`EstadoEnSura`) pero es de esta tabla. FK `FK_Titular_ArlSura`, índice
> `IX_ARLSura_TitularID_Fecha`; 1.565.455 filas. Flag = NIT o Empresa. NIT `0` → NULL; un 'NIT-DV' se anula
> con advertencia (columna `Entero`). Clave de contenido = NIT + Empresa (el SP deduplicaba por titular + NIT y
> un cambio de razón social borraba la fila vieja).

### Cajacopi
Datos de afiliacion a Cajacopi (caja de compensacion).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoFijo | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| EstadoEnCajacopi | bit | NO | — | — |
| FechaConsulta | date | NO | — | DEFAULT `getdate()` |

> **Carga (ADR-039):** Cargue de Fuentes de Datos, `ReglaCajacopi`: fijo de 7 dígitos → indicativo por
> departamento/municipio (`PrefijoMunicipio`, clave sin tildes/puntos/comas; si no se resuelve sin
> ambigüedad, se anula con advertencia), fijo con `11111` y correo "NO TIENE" → NULL. El flag
> `EstadoEnCajacopi` se calcula después de limpiar (el SP lo hacía antes) y no cuenta Departamento ni
> Municipio.

### Camara
Datos de la Camara de Comercio por titular (persona natural).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| Departamento | varchar(70) | SI | — | — |
| EstadoEnCamara | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaCamara`. `NumeroTelefono` es tipo
> `Telefono` (mezcla celulares 3xx y fijos 60x). Fijo de 7 dígitos → indicativo por Departamento o
> Municipio, sin tildes (`PrefijoMunicipio` + `PrefijosDepartamento`); si no se resuelve, se anula con
> advertencia (el SP casaba el departamento exacto con tildes y dejaba 7 dígitos: 3.189 filas legacy). Los
> textos de relleno del formulario del proveedor (`'Seleccione un departamento'`, `'Cargando...'`) y una
> localización sin letras se anulan con advertencia. Correo validado y cruzado con `ListaNegraCorreos`
> exacta (el SP no hacía ninguna de las dos cosas). Flag = teléfono o email. FK `FK_Titular_Camara`,
> índice `IX_Camara_TitularID_Fecha`.

### CamaraEmpresa
Datos de la Camara de Comercio por empresa (NIT). Heap **sin índices, sin FK y sin `TitularID`**: la fila
es de una empresa, identificada por `NitEmpresa` (71.424 filas, 69.389 NIT distintos; solo 2.999 coinciden
con un `Titular`). Se cruza con ARLSura por NIT en `vw_DatosCamaraEmpresa` (`ARLSura.NIT = CamaraEmpresa.NitEmpresa`).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NitEmpresa | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnCamara | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaCamaraEmpresa`. **Clave = `NitEmpresa`**
> (`ColumnaClaveTitular`, única fuente cuya clave no es `TitularID`): el archivo trae el NIT (alias
> `nitempresa`, `nit`…), 'NIT-DV' se acepta y se le quita el DV (`NormalizarClave`); **no crea `Titular`
> ni `Titular_Cartera`** (su SP tampoco) y no se cruza con `dbo.Titular`. Refresco, inserción, purga de
> vacías y reversión van por `NitEmpresa` (en `CargueFuenteReversion.TitularID` como texto). Departamento y
> Municipio del archivo solo resuelven el indicativo del fijo de 7 dígitos; **no se guardan**. Correo
> validado y cruzado con la lista negra exacta.

### Compensados
Datos de compensacion laboral (cajas de compensacion) por titular. 366.072 filas.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| EPS | varchar(50) | SI | — | — |
| UltimoPeriodoCompensado | date | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| EstadoEnCompensados | **int** | NO | — | — (flag; `int`, no `bit`) |
| FechaConsulta | date | NO | — | — |

> ⚠️ **Corregido el 2026-09-24:** este documento decía `Estado int NULL`; la columna real es
> `EstadoEnCompensados int NOT NULL` (`sys.columns`). `Estado` es el nombre de la columna en
> `CompensadosAuxiliar`, no en esta tabla.
>
> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaCompensados`. El flag **no** viene del
> archivo: el motor escribe 0/1 según EPS o período (lo que se guarda); la columna Estado del archivo se
> informa como "no se usa" (el SP la usaba solo para dar flag 1 a filas sin ningún dato: 2.315 hoy;
> decisión pendiente H2 de `PARIDAD-empresas.md`). La marca del proveedor `'NO SE ENCUENTRA'` en EPS o
> TipoAfiliado se anula con aviso (594 filas legacy la tienen como tipo de afiliado, y el Calificador las
> cuenta como "encontrado"). Un período inválido se anula y la fila sigue (el SP perdía el archivo entero,
> error 241). FK `FK_Compensados_Titular`, índice `IX_Compensados_TitularID_Fecha`.

### DatosClaro
Contactabilidad telefónica y email obtenida del operador Claro. Cardinalidad ~955k filas (~775k titulares únicos, varios registros por titular). Se consume vía `vw_BaseMadre` con TOP 1 por `FechaInsercion DESC`. Tabla espejo `DatosClaroAuxiliar` (staging) y vista `vw_DatosClaro` (concatenado legacy) **no se usan en código**.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| FechaInsercion | date | SI | — | — |

> **Diferencia con el resto:** las demás fuentes ordenan por `FechaConsulta DESC`; DatosClaro usa `FechaInsercion DESC` (la tabla no tiene `FechaConsulta`). La fecha también se expone en la vista como `[Ultima Insercion Claro]` para medir vigencia de los datos.
>
> **Carga (ADR-039):** Cargue de Fuentes de Datos. N:1, sin flag ni política de vacías, **no crea `Titular`
> ni `Titular_Cartera`** (sin FK: un titular inexistente entra con advertencia). `FechaInsercion` = sello
> del servidor SQL. `NumeroTelefono` acepta celular 3xx o fijo 60x de 10 dígitos; se cruza con
> `ListaNegraTelefonos` real (no con `ListaNegraTelefonosAuxiliar` como el SP) y la fila sin teléfono ni
> email válidos se excluye. Contenido idéntico (titular + teléfono + email) refresca `FechaInsercion`.

### DatosExternos
Datos de contacto de fuentes externas genericas. **No consumida** por ningún módulo de Zentinela. Desde 2026-05-07 tampoco la referencian `vw_BaseMadre` ni `dbo.fn_BaseMadrePorCartera` (ver `docs/sql/migrations/2026-05-07_remove_datos_externos_from_basemadre.sql`).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| Fecha | date | SI | — | — |

### Fosyga
Datos de afiliacion al SGSSS (Fosyga/ADRES).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| Estado | varchar(30) | SI | — | — |
| EPS | varchar(120) | SI | — | — |
| Regimen | varchar(30) | SI | — | — |
| FechaAfiliacionEfectiva | date | SI | — | — |
| FechaFinalizacionAfiliacion | date | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Departamento | varchar(70) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnFosyga | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaFosyga`: Estado 'AFILIADO FALLECIDO' →
> TipoAfiliado 'FALLECIDO' (sin distinguir mayúsculas y después de colapsar saltos de línea: las 69 filas
> legacy con 'AFILIADO↵FALLECIDO' que el SP no casaba no se tocan). Flag = Estado, EPS o Régimen; fechas,
> tipo de afiliado y localización no entran. Clave de contenido = las 8 columnas. FK `FK_Titular_Fosyga`
> (la fila sin nombre de un titular nuevo se excluye con error; en el SP tumbaba el archivo: es la carga
> del 2026-09-23 que quedó en `FosygaAuxiliar`), índice `IX_Fosyga_TitularID_Fecha`; 2.140.272 filas.
> Legacy sin tocar: 901 filas del 2025-10-23 con fechas en `Regimen` (mapeo de columnas errado de ese
> import; el motor resuelve cada columna por su encabezado).

### MiVacuna
Contactabilidad proveniente de la plataforma MiVacuna. Cardinalidad 1:1 por `TitularID` (36.985 filas == titulares únicos). Tabla heap (sin PK ni índices nativos) — la migración `2026-05-29_mivacuna_in_basemadre.sql` crea `IX_MiVacuna_TitularID_FechaConsulta` para soportar el JOIN del iTVF. Tabla espejo `MiVacunaAuxiliar` (staging).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — (sin FK en la BD; lógicamente Titular) |
| FechaExpedicion | date | SI | — | — |
| Celular | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnMiVacuna | bit | SI | — | — |
| FechaConsulta | date | SI | — | — |

> **Expuesta en `vw_BaseMadre` + `fn_BaseMadrePorCartera`** con alias `[Celular MiVacuna]`, `[Email MiVacuna]`, `[Fecha Expedicion MiVacuna]`, `[Estado MiVacuna]` (bit → VARCHAR(1), patrón MutualSer). `Celular` entra al switch `ColumnaCelular` y `Email` al `ColumnaEmail` (clave `MiVacuna`). `FechaConsulta` queda como metadato interno (no se expone). No aporta localización. Sí entra al `UNION ALL` de Rastreo de Líneas (columna `Celular`).
>
> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. `Celular` se valida como
> **celular**. Flag = celular o email. Sin FK, pero crea `Titular` y `Titular_Cartera` (como el SP). El
> correo vetado queda NULL (el SP lo dejaba en `''`, que contaba como dato: 1.118 filas legacy con
> `Email = ''`; la primera recarga de esos titulares inserta una fila nueva en vez de refrescar). Sin
> purgas ni dedup sobre toda la tabla.

### MutualSer
Contactabilidad y calificación socioeconómica del afiliado a MutualSer EPS. Cardinalidad 1:1 por `TitularID` (614.145 filas == titulares únicos). Tabla heap (sin PK ni índices nativos) — la migración `2026-05-07_mutualser_in_basemadre.sql` crea `IX_MutualSer_TitularID_FechaConsulta` para soportar el JOIN del iTVF. Tabla espejo `MutualSerAuxiliar` (staging).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoCelular2 | varchar(10) | SI | — | — |
| NumeroTelefonoFijo | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| Regimen | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnMutualSer | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Excepción al patrón "1 fuente = 1 entrada en switch celular"**: MutualSer expone 2 celulares como columnas independientes (no por prioridad). El switch `BaseMadreRepository.ColumnaCelular` admite las claves `MutualSer` y `MutualSer2`. El `NumeroTelefonoFijo` se expone como columna pero NO entra al selector — el switch existe para marcado masivo a móviles. Ver `docs/para-tener-en-cuenta.md`.
>
> **Carga (ADR-039):** Cargue de Fuentes de Datos, `ReglaMutualSer`: celular 2 repetido → NULL; fijo que es
> celular (3xx) → celular 2 si está libre; celular que empieza en 60 → fijo (si el fijo ya tiene otro, se
> descarta con advertencia, **nunca se concatena** como hacía el SP en un `varchar(10)`); fijo igual a un
> celular → NULL. El flag solo mira teléfonos y email.

### NuevaEps
Contactabilidad + localización + afiliación a Nueva EPS. Cardinalidad 1:1 por `TitularID` (3.557 filas == titulares únicos). Tabla heap — la migración `2026-06-12_nuevaeps_in_basemadre.sql` crea `IX_NuevaEps_TitularID_FechaConsulta`. Tabla espejo `NuevaEpsAuxiliar` (staging).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | varchar(30) | NO | — | (Titular, vía TRY_CAST a bigint) |
| NumeroTelefono | varchar(10) | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnNuevaEps | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Expuesta en `vw_BaseMadre` + `fn_BaseMadrePorCartera`** (migración `2026-06-12_nuevaeps_in_basemadre.sql`, tras MutualSer) con alias `[Celular NuevaEps]`, `[Estado Afiliacion NuevaEps]`, `[Tipo Afiliado NuevaEps]`, `[Fecha Nacimiento NuevaEps]`, `[Departamento NuevaEps]`, `[Municipio NuevaEps]`, `[Estado NuevaEps]` (bit→VARCHAR(1)), `[Ultima Consulta NuevaEps]`. **Excepción: `TitularID` es `varchar(30)`** (las demás fuentes son bigint) → se usa `TRY_CAST(TitularID AS BIGINT)` en CTE/JOINs/Rastreo. `NumeroTelefono` entra al switch `ColumnaCelular` (clave `NuevaEps`); **sin email**. Aporta **localización** (Departamento/Municipio en el panel del Calificador) y entra al `UNION ALL` de Rastreo de Líneas. Ver `docs/para-tener-en-cuenta.md`.
>
> **Carga (ADR-039):** Cargue de Fuentes de Datos, `ReglaGenerica` + `TitularIdComoTexto`: el documento se
> valida como número y se guarda `CONVERT(varchar(30), bigint)` (sin ceros a la izquierda); los joins del
> motor convierten del lado de `#Carga` y usan `IX_NuevaEps_TitularID_FechaConsulta`. `Titular` y
> `Titular_Cartera` siguen con el bigint. `NumeroTelefono` se valida como **celular**: los ~3.000
> números de 7 dígitos que el SP guardaba sin validar ya no entran (se anulan con advertencia); los que
> ya están en la tabla no se tocan. El flag mira teléfono, estado y tipo de afiliación.

### Positiva
Datos de afiliacion a Positiva (ARL) por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoFijo | varchar(30) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnPositiva | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaPositiva`: un fijo 60x que llega en la
> columna de celular pasa al fijo **sin concatenar** (el SP escribía `'fijo, celular'` en el `varchar(30)`:
> 522 filas legacy); si el fijo ya trae otro fijo se conserva el suyo con advertencia; si trae un celular
> (columnas intercambiadas) cada número va a su columna; si trae basura, el fijo la reemplaza. Flag =
> celular, fijo o email. Legacy: 140.758 filas con `Email = ''` y 30.056 con `NumeroTelefonoCelular = '0'`
> (el motor guarda NULL: la primera recarga de esos titulares inserta una fila nueva). FK
> `FK_Titular_Positiva`, índice `IX_Positiva_TitularID_Fecha`; 2.213.891 filas.

### PositivaEmpresas
Datos de afiliacion a Positiva por empresa vinculada al titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NIT | bigint | SI | — | — |
| Empresa | varchar(230) | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| EstadoEnPositivaEmpresa | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. Flag = NIT o Empresa
> (`EstadoAfiliacion` no entra). La razón social llega partida en líneas (texto de un PDF; 94.096 filas
> legacy): el motor colapsa los saltos a un espacio. NIT `0` → NULL; 'NIT-DV' se anula con advertencia.
> `EstadoAfiliacion` se guarda como texto libre (160 filas legacy con basura del PDF, H3 de
> `PARIDAD-empresas.md`). FK `FK_PositivaEmpresas_Titular`, índice `IX_PositivaEmpresas_TitularID_Fecha`
> (TitularID, FechaConsulta — ascendente, a diferencia de los demás); 2.013.318 filas.

### RGM
Datos del Registro de Garantias Mobiliarias por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| Marca | varchar(50) | SI | — | — |
| Modelo | int | SI | — | — |
| Placa | varchar(50) | SI | — | — |
| EstadoEnRGM | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaRgm`. Placa con la validación de SIMIT
> (`PlacaVehiculo`: sin espacios ni guiones, mayúsculas; lo que no es placa se anula con aviso). Modelo =
> año entre 1900 y el actual + 2 (el destino es `int`: un número enorme tumbaría el `INSERT` de todo el
> cargue; `'2021.0'` de Excel se lee como 2021). Flag = marca, modelo o placa. La relación usa el par
> líder/cartera del archivo validado contra `Lider_Cartera` activo (el SP insertaba `LiderID = CarteraID`).
> FK `FK_RGM`, índice `IX_RGM_TitularID_Fecha`; 1.663.543 filas (8.774 con datos). *Corregido el
> 2026-09-24: `EstadoEnRGM` es NOT NULL.*

### RUES
Datos del Registro Unico Empresarial y Social por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroMatricula | bigint | SI | — | — |
| EstadoEmpresa | varchar(20) | SI | — | — |
| ActividadEconomica | varchar(250) | SI | — | — |
| NombreEmpresa | varchar(70) | SI | — | — |
| EstadoEnRUES | bit | NO | — | — |
| FechaConsulta | **datetime** | NO | — | — ⚠️ única fuente con sello con hora |

> ⚠️ **`FechaConsulta` es `datetime`, no `date`** — la única de las 22 fuentes del cargue (el SP guardaba
> `GETDATE()`; 799.156 de las 873.188 filas tienen hora). Quien la compare con una fecha debe convertir o
> usar rangos.
>
> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`, `TipoFecha = DateTime`: el
> sello es `GETDATE()` **truncado al segundo** (`GeneradorSqlCargue.LeerSello`), para que
> `SelloFechaConsulta`/`FechaAnterior` (`datetime2(0)`) lo ubiquen por igualdad al revertir. Flag =
> matrícula, estado o actividad (`NombreEmpresa` no entra, como en el SP). Matrícula `0` → NULL. Aplica la
> política de vacías (el SP no purgaba: 809.306 filas legacy con flag 0). FK `FK_Titular_RUES`, índice
> `IX_RUES_TitularID_Fecha`.

### Runt
Datos del Registro Unico Nacional de Transito por titular. **0 filas** (creada 2026-02-18), heap **sin
índices y sin FK**, sin SP ni consumidores (no está en la BaseMadre ni en ningún servicio; el front es un
stub). **No está en el Cargue de Fuentes de Datos** (ADR-039, Fase 2; lo fija
`CatalogoCoherenciaTests.Runt_no_esta_en_el_catalogo`). Si se activa: `FechaConsulta` es `datetime` y el
flag se llama `EstadoRunt`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| Placa | varchar(50) | SI | — | — |
| NumeroLicencia | bigint | SI | — | — |
| TipoVehiculo | varchar(50) | SI | — | — |
| Marca | varchar(50) | SI | — | — |
| Modelo | bigint | SI | — | — |
| NumeroChasis | varchar(50) | SI | — | — |
| EstadoSoat | varchar(50) | SI | — | — |
| EntidadSoat | varchar(50) | SI | — | — |
| EstadoTecnicomecanica | varchar(50) | SI | — | — |
| EstadoRunt | bit | SI | — | — |
| FechaConsulta | datetime | NO | — | — |

### SaludTotal
Datos de afiliacion a EPS Salud Total por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Ciudad | varchar(50) | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| Cargo | varchar(60) | SI | — | — |
| UltimaFechaPago | date | SI | — | — |
| IngresoBase | varchar(20) | SI | — | — |
| EstadoEnSaludTotal | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. `NumeroTelefono` se valida
> como **celular** (expuesto como `[Celular Salud Total]`; el 100 % de las filas con dato es 3xx): un fijo
> se anula con advertencia. Correo validado y cruzado con `ListaNegraCorreos` exacta (el SP no hacía
> ninguna de las dos). Flag = teléfono, email o NIT. NIT `0`, `IngresoBase` `''`/`'0'` y la fecha
> `1900-01-01` → NULL. FK `FK_Titular_SaludTotal`, índice `IX_SaludTotal_TitularID_Fecha`; 1.586.180 filas.

### SIMIT
Datos del Sistema Integrado de Multas e Infracciones de Transito por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| Placa | varchar(10) | SI | — | — |
| EstadoEnSIMIT | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039):** Cargue de Fuentes de Datos, `ReglaSimit`. El archivo trae `Placa1..Placa15` (o `Placa`)
> por titular y el destino guarda **una fila por placa distinta** (`VariasPorTitular`). Placa válida: sin
> espacios ni guiones, mayúsculas, 5 a 7 caracteres A-Z/0-9 con al menos una letra y un dígito
> (`PlacaVehiculo.EsValida`). Marcas como `PAZ Y SALVO`, `NO CARGO`, `SIN`, `N/A`, `XXXXXX` se descartan con
> advertencia; un titular sin placas válidas entra como una fila `Placa NULL, EstadoEnSIMIT = 0`. Los datos
> legacy (marcas guardadas como placa, placas con espacio o guion) se corrigen con
> `docs/sql/migrations/2026-09-24_simit_placas_legacy.sql`, que debe aplicarse para que la próxima carga
> reconozca esas placas y no las duplique.
### SOS
Datos del Sistema de Ocupacion y Seguridad laboral por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NitEmpresa | bigint | SI | — | — |
| RazonSocial | varchar(100) | SI | — | — |
| Telefono | varchar(10) | SI | — | — |
| Direccion | varchar(70) | SI | — | — |
| EstadoEnSOS | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. `Telefono` se valida como
> **celular** (la BaseMadre lo expone como `[Celular SOS]`): un fijo de 7 dígitos (SOS no trae
> departamento para el indicativo) o un 60x se anula con advertencia; los legacy (625 de 7 dígitos y 34
> 60x de 2.099 con dato) no se tocan. `razonsocial` es alias de `RazonSocial`, no del nombre del titular.
> `Direccion` larga se trunca a 70 con aviso (la Auxiliar admitía 150 y el SP perdía el archivo entero).
> Flag = las cuatro columnas. FK `FK_SOS_Titular`, índice `IX_SOS_TitularID_Fecha`; 226.946 filas.

### Sura
Contactabilidad + datos laborales de Sura (EPS/aseguradora). Fuente 1:1. Único teléfono en
`NumeroCelular` (no hay columna de fijo). Integrada el 2026-09-02 con la tabla **vacía (0 filas)**;
desde el 2026-09-24 se carga desde **Administración → Cargue de Fuentes de Datos** (ADR-039: primera
fuente del módulo y su plantilla, sin regla propia). No confundir con **ARLSura**, que es otra tabla y
otra fuente (clave de ruta `ARLSura`, también en el módulo desde la Fase 2).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroCelular | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| UltimaFechaConsulta | date | SI | — | — |
| EstadoAfiliacion | varchar(50) | SI | — | — |
| EstadoEnSura | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Expuesta en `vw_BaseMadre` + los TRES iTVF** (migración `2026-09-02_sura_in_basemadre.sql`) con
> alias `[Celular Sura]`, `[Email Sura]`, `[NIT Sura]`, `[Empresa Sura]`, `[Tipo Afiliado Sura]`,
> `[Estado Afiliacion Sura]`. Entra al switch `ColumnaCelular` (clave `Sura`) y a `ColumnaEmail`.
> Aporta **localización** en el panel del Calificador y entra al `UNION ALL` de Rastreo de Líneas y de
> Uso de Base de Datos. Tomar los alias `[NIT Sura]`/`[Empresa Sura]` obligó a renombrar los de
> ARLSura — ver la nota en esa sección y `docs/para-tener-en-cuenta.md`.

### Tigo
Datos de contacto obtenidos de Tigo. 1.026.333 filas, llenadas **a mano** en feb-mar de 2025 desde
`TigoAuxiliar` (no hay `sp_ProcesarTigoAuxiliar`); nada la escribía desde el 2025-03-14. La primera columna
de la tabla se eliminó (`column_id` empieza en 2).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular (`FK_Titular_Tigo`) |
| NumeroTelefono | varchar(10) | SI | — | — CHECK `chk_Telefono` (7 o 10 caracteres que empiezan en 3 o 6) |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigo | bit | NO | — | — (default 0) |
| FechaConsulta | date | NO | — | — (default `getdate()`) |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`, definición por analogía con
> TigoUne (sin SP que traducir). `NumeroTelefono` se valida como **celular** (`[Celular Tigo]`); todo
> celular que pasa cumple `chk_Telefono`; los 1.101 fijos y 55 de 7 dígitos legacy no se tocan. Con FK a
> `Titular` crea titulares, así que **el archivo debe traer Nombre**: el formato de `TigoAuxiliar` (sin
> Nombre) se rechaza. Flag = teléfono o email (el motor escribe flag y fecha: no usa los defaults).
> Índice `IX_Tigo_TitularID_Fecha`. Legacy: 2.579 filas con flag 1 y sin datos (no se tocan).

### TigoUne
Datos de contacto obtenidos de Tigo Une. Misma forma que `Tigo` pero **otra fuente** (tabla, SP, columnas
de la BaseMadre `[Celular TigoUne]`/`[Email TigoUne]` y fila del Calificador propias), y a diferencia de
ella **sin FK a `Titular`**. Cargas del 2026-08-04 (5.192), 2026-09-15 (88.586) y 2026-09-22 (30.005):
123.783 filas.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — (sin FK en la BD; lógicamente Titular) |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigoUne | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. `NumeroTelefono` se valida
> como **celular** (el SP solo miraba el largo: pasaban fijos y `'555…'`). Correo validado, en minúsculas y
> con lista negra exacta. Crea `Titular` y `Titular_Cartera` aunque no haya FK (como el SP). El contenido
> idéntico refresca la fecha (el SP no lo hacía). Legacy: 27.166 filas de la carga del 2026-09-15 con `''`
> en teléfono o email (la primera recarga de esos titulares inserta una fila nueva). Índice
> `IX_TigoUne_TitularID_FechaConsulta`.

### Coosalud
Contactabilidad + afiliación Coosalud (EPS). Fuente 1:1 tipo NuevaEps ampliada (celular, email, régimen, tipo afiliado, estado afiliación, nivel Sisbén, geografía, fecha nacimiento). Con datos desde 2026-08-05 (369.049 titulares; vacía al integrarse el 2026-08-04).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — (sin FK en la BD; lógicamente Titular) |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| MunicipioOrigen | varchar(50) | SI | — | — |
| DepartamentoOrigen | varchar(50) | SI | — | — |
| Regimen | varchar(50) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| EstadoAfiliacion | varchar(50) | SI | — | — |
| NivelSisben | varchar(50) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| EstadoEnCoosalud | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaGenerica`. `NumeroTelefono` se valida
> como **celular** (77.977 de las 77.983 filas con dato son 3xx; `[Celular Coosalud]`). Correo con lista
> negra exacta (el SP usaba `LIKE`). Flag = teléfono o email. Crea `Titular` y `Titular_Cartera` aunque
> no haya FK (como el SP). Clave de contenido completa (el SP descartaba un municipio nuevo con el mismo
> contacto) y el contenido idéntico refresca la fecha (el SP no). Legacy: 32.698 filas con `Email = ''`
> (la primera recarga de esos titulares inserta una fila nueva). Índice
> `IX_Coosalud_TitularID_FechaConsulta`; 1.037.887 filas.

### SaviaSalud
Contactabilidad + afiliación Savia Salud (EPS). Fuente 1:1 **con datos** (193.752 filas al integrarse, 2026-08-05; 719.743 el 2026-09-24). Celular en `NumeroCelular`; `NumeroTelefono` NO se mapea en la BaseMadre (tiene dato en 4.123 filas, incluidos municipios truncados de un import corrido: `COPACABANA`, `LA ESTRELL`…). Sin email. El flag se llama `Estado`, no `EstadoEnSaviaSalud`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — (sin FK en la BD; lógicamente Titular) |
| FechaDeNacimiento | date | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| EstadoDeAfiliacion | varchar(50) | SI | — | — |
| Regimen | varchar(70) | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — (no mapeado en la BaseMadre) |
| NumeroCelular | varchar(100) | SI | — | — |
| Estado | bit | SI | — | — (flag) |
| FechaConsulta | date | SI | — | — |

> **Carga (ADR-039, Fase 2):** Cargue de Fuentes de Datos, `ReglaSaviaSalud`: una celda con varios
> números separados por coma se reparte **sin concatenar** (primer celular en `NumeroCelular`, el teléfono
> propio o el siguiente de la lista en `NumeroTelefono`, el resto se descarta con aviso); un fijo que llega
> solo en `NumeroCelular` pasa a `NumeroTelefono` (tipo `Telefono`: celular o fijo). Flag `Estado` = uno
> de los dos teléfonos (el SP contaba el correo, que esta tabla no guarda). El correo del archivo se
> informa como "no se usa". Crea `Titular` y `Titular_Cartera` aunque no haya FK. Legacy sin tocar: 161
> filas con listas en `NumeroCelular`, 175 con un fijo en `NumeroCelular`, 2.051 con `Estado = 1` y sin
> teléfonos. Índice `IX_SaviaSalud_TitularID_FechaConsulta`.

---

## Tablas Auxiliar (staging)

Tablas intermedias de carga/importacion masiva. Patron: datos crudos antes de procesarse con sp_Procesar*.

> **Fase 1 de ADR-039 (2026-09-24):** `SuraAuxiliar`, `CajacopiAuxiliar`, `MutualSerAuxiliar`,
> `NuevaEpsAuxiliar`, `SIMITAuxiliar` y `DatosClaroAuxiliar` (y sus seis `sp_Procesar*`) quedan **sin uso**:
> esas fuentes se cargan desde Administración → Cargue de Fuentes de Datos. Las seis tenían 0 filas.
> **Se eliminarán con `docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql`** (a mano, después de
> validar el cargue en producción); definición íntegra respaldada en `docs/sql/legacy/`. Las columnas de
> abajo están tomadas de ese respaldo. `ARLSuraAuxiliar` NO es de la Fase 1.
>
> **Fase 2 de ADR-039 (2026-09-24):** `ARLSuraAuxiliar`, `CamaraAuxiliar`, `CamaraEmpresaAuxiliar`,
> `CompensadosAuxiliar`, `CoosaludAuxiliar`, `FosygaAuxiliar`, `MiVacunaAuxiliar`, `PositivaAuxiliar`,
> `PositivaEmpresasAuxiliar`, `RGMAuxiliar`, `RUESAuxiliar`, `SaludTotalAuxiliar`, `SaviaSaludAuxiliar`,
> `SOSAuxiliar`, `TigoUneAuxiliar` (y sus 15 `sp_Procesar*`) y `TigoAuxiliar` (sin SP) quedan **sin uso**.
> **Se eliminarán con `docs/sql/migrations/2026-09-26_drop_auxiliares_fase2.sql`** (a mano, después de
> validar el cargue en producción); estructura y SP respaldados en `docs/sql/legacy/`. Hoy tres tienen
> filas y la guarda del DROP las bloquea: `FosygaAuxiliar` (18.598, carga fallida del 2026-09-23),
> `RGMAuxiliar` (30.144) y `TigoAuxiliar` (661.072), estas dos residuo ya contenido en su destino.
> **Siguen en uso / fuera del alcance:** `TitularAuxiliar`, `Titular_CarteraAuxiliar`,
> `ListaNegraTelefonosAuxiliar`, `DatosExternosAuxiliar` y `RuntAuxiliar` (motivos en la cabecera del DROP).
> *Corregido el 2026-09-24:* las `<Fuente>Auxiliar` de la Fase 2 (todas menos `CamaraEmpresaAuxiliar`,
> que no crea relaciones) tienen además `LiderID int NULL` como última columna, que no estaba documentada;
> `CoosaludAuxiliar`, `MiVacunaAuxiliar`, `SaviaSaludAuxiliar` y `TigoUneAuxiliar` no estaban documentadas.

### ARLSuraAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| EstadoEnSura | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### CajacopiAuxiliar
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoFijo | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| FechaNacimiento | datetime | SI | — | — |
| EstadoEnCajacopi | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | DEFAULT `getdate()` |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### CamaraAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| Departamento | varchar(70) | SI | — | — |
| EstadoEnCamara | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### CamaraEmpresaAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NitEmpresa | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| Departamento | varchar(60) | SI | — | — |
| EstadoEnCamara | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| NombreEmpresa | varchar(120) | SI | — | — |

### CompensadosAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| EPS | varchar(50) | SI | — | — |
| UltimoPeriodoCompensado | varchar(50) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Estado | int | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(50) | SI | — | — |
| CarteraID | int | SI | — | — |
| FechaInsercion | datetime | SI | — | — |
| LiderID | int | SI | — | — |

### CoosaludAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> (No estaba documentada: columnas de `sys.columns` del 2026-09-24.)

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| MunicipioOrigen | varchar(50) | SI | — | — |
| DepartamentoOrigen | varchar(50) | SI | — | — |
| Regimen | varchar(50) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| EstadoAfiliacion | varchar(50) | SI | — | — |
| NivelSisben | varchar(50) | SI | — | — |
| FechaNacimiento | datetime | SI | — | — |
| EstadoEnCoosalud | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(50) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### DatosClaroAuxiliar
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(150) | SI | — | — |
| FechaInsercion | date | SI | — | — |

### DatosExternosAuxiliar
> Fuera de ADR-039 (1.046.532 filas): staging de `DatosExternos`, fuente que Zentinela no consume y que
> no está en el catálogo del cargue. Borrarla es otra decisión.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(max) | SI | — | — |
| Email | varchar(max) | SI | — | — |

### FosygaAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> ⚠️ Tiene **18.598 filas** (2026-09-24): una carga del 2026-09-23 que el SP no pudo pasar a `Fosyga`
> (sin Nombre ni cartera/líder; 4.124 titulares inexistentes → FK). ~18.053 filas de afiliación que no
> están en el destino: hablar con quien carga Fosyga antes de vaciarla.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| Estado | varchar(30) | SI | — | — |
| EPS | varchar(200) | SI | — | — |
| Regimen | varchar(30) | SI | — | — |
| FechaAfiliacionEfectiva | date | SI | — | — |
| FechaFinalizacionAfiliacion | date | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Departamento | varchar(70) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnFosyga | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(60) | SI | — | — |
| CarteraID | int | SI | — | — |
| FechaInsercion | datetime | SI | — | — |
| LiderID | int | SI | — | — |

### ListaNegraTelefonosAuxiliar
> **No es del flujo de fuentes** (fuera de los DROP de ADR-039): staging de la lista negra de teléfonos,
> 1.204.923 filas. Su único lector en la BD era `sp_ProcesarDatosClaroAuxiliar` (Fase 1); el cargue cruza
> contra `ListaNegraTelefonos` real.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Cartera | varchar(50) | SI | — | — |
| Codigo | varchar(20) | SI | — | — |

### MiVacunaAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> (No estaba documentada: columnas de `sys.columns` del 2026-09-24.)

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| FechaExpedicion | date | SI | — | — |
| Celular | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnMiVacuna | bit | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| FechaConsulta | date | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### MutualSerAuxiliar
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoCelular2 | varchar(50) | SI | — | — |
| NumeroTelefonoFijo | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| Regimen | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnMutualSer | bit | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| FechaConsulta | date | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### NuevaEpsAuxiliar
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.
> Ojo: aquí `TitularID` es `varchar(10)` y en `NuevaEps` es `varchar(30)`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | varchar(10) | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnNuevaEps | bit | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |
| FechaConsulta | date | SI | — | — |

### PositivaAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoFijo | varchar(30) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| FechaNacimiento | datetime | SI | — | — |
| Departamento | varchar(50) | SI | — | — |
| Municipio | varchar(50) | SI | — | — |
| EstadoEnPositiva | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### PositivaEmpresasAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(230) | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| EstadoEnPositivaEmpresa | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| FechaInsercion | datetime | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### RGMAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> Tiene **30.144 filas** (2026-09-24): residuo de la carga del 2026-08-31, con el 100 % de su contenido ya
> en `RGM`. Vaciarla antes del DROP, con confirmación del usuario.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| Marca | varchar(50) | SI | — | — |
| Modelo | varchar(50) | SI | — | — |
| Placa | varchar(6) | SI | — | — |
| EstadoEnRGM | bit | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| FechaInsercion | datetime | SI | — | — |
| LiderID | int | SI | — | — |

### RUESAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroMatricula | bigint | SI | — | — |
| EstadoEmpresa | varchar(20) | SI | — | — |
| ActividadEconomica | varchar(250) | SI | — | — |
| NombreEmpresa | varchar(70) | SI | — | — |
| EstadoEnRUES | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### RuntAuxiliar
> 0 filas, sin SP. **No entra en ningún DROP de ADR-039**: Runt no se agregó al cargue; si se activa, el
> módulo la reemplaza, y si se abandona se borran `Runt` y `RuntAuxiliar` juntas (decisión aparte).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| Placa | varchar(50) | SI | — | — |
| NumeroLicencia | bigint | SI | — | — |
| TipoVehiculo | varchar(50) | SI | — | — |
| Marca | varchar(50) | SI | — | — |
| Modelo | bigint | SI | — | — |
| NumeroChasis | varchar(50) | SI | — | — |
| EstadoSoat | varchar(50) | SI | — | — |
| EntidadSoat | varchar(50) | SI | — | — |
| EstadoTecnicomecanica | varchar(50) | SI | — | — |
| EstadoRunt | bit | SI | — | — |
| Nombre | varchar(50) | SI | — | — |
| CarteraID | int | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| FechaInsercion | datetime | SI | — | — |

### SaludTotalAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| EstadoAfiliacion | varchar(30) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Ciudad | varchar(50) | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| Cargo | varchar(60) | SI | — | — |
| UltimaFechaPago | datetime | SI | — | — |
| IngresoBase | varchar(20) | SI | — | — |
| EstadoEnSaludTotal | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### SaviaSaludAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> (No estaba documentada: columnas de `sys.columns` del 2026-09-24.) Ojo: los nombres no son los del
> destino (`FechaNacimiento`/`EstadoAfiliacion` aquí, `FechaDeNacimiento`/`EstadoDeAfiliacion` en
> `SaviaSalud`), tiene `Email` (el destino no) y no tiene `FechaConsulta`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| FechaNacimiento | datetime | SI | — | — |
| TipoAfiliado | varchar(70) | SI | — | — |
| EstadoAfiliacion | varchar(70) | SI | — | — |
| Regimen | varchar(70) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| NumeroCelular | varchar(30) | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| Estado | bit | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### SIMITAuxiliar
Tabla de staging para SIMIT con soporte de hasta 15 placas por titular.
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.
> El archivo del cargue nuevo conserva la forma `Placa1..Placa15`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| Placa1 | varchar(10) | SI | — | — |
| Placa2 | varchar(10) | SI | — | — |
| Placa3 | varchar(10) | SI | — | — |
| Placa4 | varchar(10) | SI | — | — |
| Placa5 | varchar(10) | SI | — | — |
| Placa6 | varchar(10) | SI | — | — |
| Placa7 | varchar(10) | SI | — | — |
| Placa8 | varchar(10) | SI | — | — |
| Placa9 | varchar(10) | SI | — | — |
| Placa10 | varchar(10) | SI | — | — |
| Placa11 | varchar(10) | SI | — | — |
| Placa12 | varchar(10) | SI | — | — |
| Placa13 | varchar(10) | SI | — | — |
| Placa14 | varchar(10) | SI | — | — |
| Placa15 | nchar(10) | SI | — | — |
| EstadoEnSIMIT | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(50) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### SOSAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NitEmpresa | bigint | SI | — | — |
| RazonSocial | varchar(100) | SI | — | — |
| Telefono | varchar(10) | SI | — | — |
| Dirección | varchar(150) | SI | — | — (con tilde en el nombre; en `SOS` es `Direccion varchar(70)`) |
| EstadoEnSos | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### SuraAuxiliar
> Fase 1 (ADR-039): sin uso; se eliminará con `2026-09-25_drop_auxiliares_fase1.sql`; definición en `docs/sql/legacy/`.
> No confundir con `ARLSuraAuxiliar` (otra fuente; queda sin uso en la Fase 2 y se elimina con el DROP de la Fase 2).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroCelular | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| EstadoAfiliacion | varchar(50) | SI | — | — |
| UltimaFechaConsulta | datetime | SI | — | — |
| EstadoEnSura | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### TigoAuxiliar
> Fase 2 (ADR-039): sin uso (nunca tuvo SP: `Tigo` se llenó a mano desde aquí en 2025); se eliminará con
> `2026-09-26_drop_auxiliares_fase2.sql`; estructura en `docs/sql/legacy/TigoAuxiliar_CREATE.sql`.
> Tiene **661.072 filas** (2026-09-24): residuo de feb-mar de 2025; todos sus titulares están en `Tigo`
> (661.005 con el mismo contenido, 67 con otro). Vaciarla antes del DROP, con confirmación del usuario.
> *Corregido el 2026-09-24:* faltaban `CarteraID` y `LiderID`. No tiene `Nombre` (el cargue nuevo sí lo exige).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigo | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### TigoUneAuxiliar
> Fase 2 (ADR-039): sin uso; se eliminará con `2026-09-26_drop_auxiliares_fase2.sql`; definición en `docs/sql/legacy/`.
> (No estaba documentada: columnas de `sys.columns` del 2026-09-24.)

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigoUne | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| LiderID | int | SI | — | — |

### TitularAuxiliar
> **Sigue en uso, fuera de ADR-039** (47.531 filas): carga de titulares y asignaciones con
> `sp_ProcesarTitularAuxiliar` (→ `Titular` + `Titular_Cartera`); la lee `vw_BaseMadreTitularAuxiliar`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | varchar(50) | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| NitEmpresa | bigint | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| FechaExpedicionDocumento | datetime | SI | — | — |

### Titular_CarteraAuxiliar
> Fuera de ADR-039 (2 filas): staging de asignaciones, mismo dominio que `TitularAuxiliar`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| CarteraID | int | SI | — | — |

---

## Tablas de auditoria / comunicaciones

### AuditoriaCorreos
Registro de correos enviados a titulares, con control semanal y flags de fuente de datos consultada.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| Email | varchar(100) | NO | — | — |
| NombreCartera | varchar(50) | NO | — | — |
| FechaEnvio | date | NO | — | — |
| EnviadoPorSemana | int | SI | — | — |
| ConsultaTigo | bit | SI | — | — |
| ConsultaCamara | bit | SI | — | — |
| ConsultaSaludTotal | bit | SI | — | — |

### AuditoriaIVR
Registro de llamadas IVR enviadas a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | NO | — | — |
| NombreCartera | varchar(50) | NO | — | — |
| FechaEnvio | date | NO | — | — |
| EnviadoPorSemana | int | SI | — | — |
| ConsultaTigo | bit | SI | — | — |
| ConsultaCamara | bit | SI | — | — |
| ConsultaSaludTotal | bit | SI | — | — |

### AuditoriaMensajeria
Registro de mensajes SMS/mensajeria enviados a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | NO | — | — |
| NombreCartera | varchar(50) | NO | — | — |
| FechaEnvio | datetime | NO | — | — |
| EnviadoPorSemana | int | SI | — | — |
| ConsultaTigo | bit | SI | — | — |
| ConsultaCamara | bit | SI | — | — |
| ConsultaSaludTotal | bit | SI | — | — |

### EmailEnviados
Registro simple de emails enviados.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Email | varchar(150) | SI | — | — |

### EmailGeneral
Emails generales asociados a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | varchar(150) | SI | — | — |
| Correo | varchar(100) | SI | — | — |

### IVREnviados
Numeros de telefono a los que se envio IVR.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroTelefono | varchar(10) | SI | — | — |

### MensajesEnviados
Mensajes enviados a titulares. Carga alimentada desde el módulo de Mensajería seleccionando Líder + Cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | bigint | NO | — | — |
| NumeroTelefono | nvarchar(20) | NO | — | — |
| LiderID | int | NO | — | Lider_Cartera |
| CarteraID | int | NO | — | Lider_Cartera |
| FechaEnvio | date | NO | — | — |
| Canal | nvarchar(20) | NO | — | — (default 'SMS') |
| FechaCargue | datetime2 | NO | — | — (default GETDATE()) |

**FK compuesta:** `(LiderID, CarteraID) → Lider_Cartera (LiderID, CarteraID)` — `ON DELETE RESTRICT`.
**Sin PK surrogate** — `HasNoKey()` en EF Core, solo inserción y consultas agregadas.
**NombreCartera no se almacena** — se obtiene siempre con `JOIN dbo.Cartera ON Cartera.ID = MensajesEnviados.CarteraID` para evitar inconsistencias si se renombra la cartera.
**Script:** `docs/sql/create_MensajesEnviados.sql`

### IVREnviados
Envíos IVR (voz automatizada) a titulares. Espejo de `MensajesEnviados`. Carga alimentada desde el módulo de Virtualidad → IVR seleccionando Líder + Cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | bigint | NO | — | — |
| NumeroTelefono | nvarchar(20) | NO | — | — |
| LiderID | int | NO | — | Lider_Cartera |
| CarteraID | int | NO | — | Lider_Cartera |
| FechaEnvio | date | NO | — | — |
| Canal | nvarchar(20) | NO | — | — (default 'IVR') |
| FechaCargue | datetime2 | NO | — | — (default GETDATE()) |

**FK compuesta:** `(LiderID, CarteraID) → Lider_Cartera (LiderID, CarteraID)` — `ON DELETE RESTRICT`.
**Sin PK surrogate** — `HasNoKey()` en EF Core, solo inserción y consultas agregadas.
**NombreCartera no se almacena** — se obtiene con JOIN a `dbo.Cartera`.
**Script:** `docs/sql/create_IVREnviados.sql` (drop también la tabla legacy `dbo.IVREnvio`).

### Tablas consultadas por búsqueda inversa de teléfono

Dos módulos comparten EXACTAMENTE este set de tablas y columnas, y deben dar la misma
respuesta para el mismo número:

- **Rastreo de Líneas** (`/consultas/rastreo`) — un número por vez, `WHERE col = @numero`
  en `RastreoLineasService.SqlConsulta`.
- **Informes Generales → Uso de Base de Datos** (`/informes/generales`) — masivo, los mismos
  `UNION ALL` pero como `JOIN` contra una tabla `#Telefonos`, en `UsoBaseDatosService.SqlCruce`
  (ver ADR-030).

Ambos resuelven el titular con `LEFT JOIN dbo.Titular`. **Si se agrega una fuente, hay que
extender los DOS services.**

**Tablas vigentes (13 tablas / 18 columnas)** — fuente única de verdad: el código de los services.

| Tabla | Columna(s) consultadas | Etiqueta(s) en UI |
|---|---|---|
| Cajacopi | `NumeroTelefonoCelular`, `NumeroTelefonoFijo` | `Cajacopi (celular)`, `Cajacopi (fijo)` |
| Camara | `NumeroTelefono` | `Cámara` |
| SaludTotal | `NumeroTelefono` | `Salud Total` |
| Positiva | `NumeroTelefonoCelular`, `NumeroTelefonoFijo` | `Positiva (celular)`, `Positiva (fijo)` |
| Tigo | `NumeroTelefono` | `Tigo` |
| TigoUne | `NumeroTelefono` | `Tigo Une` |
| SOS | `Telefono` | `SOS` |
| DatosClaro | `NumeroTelefono` | `Claro` |
| MutualSer | `NumeroTelefonoCelular`, `NumeroTelefonoCelular2`, `NumeroTelefonoFijo` | `MutualSer (celular 1)`, `MutualSer (celular 2)`, `MutualSer (fijo)` |
| MiVacuna | `Celular` | `MiVacuna (celular)` |
| NuevaEps | `NumeroTelefono` | `NuevaEps (teléfono)` |
| Coosalud | `NumeroTelefono` | `Coosalud` |
| SaviaSalud | `NumeroCelular`, `NumeroTelefono` | `Savia Salud (celular)`, `Savia Salud (teléfono)` |

> ⚠ `NuevaEps.TitularID` es `varchar(30)`: ambos services lo envuelven en `TRY_CAST(... AS BIGINT)`
> y descartan los no numéricos.
>
> ⚠ `SaviaSalud` se integró consultando solo `NumeroCelular`, con la nota de que
> `NumeroTelefono` estaba vacío. Eso dejó de ser cierto cuando llegaron los datos —
> verificado 2026-08-14: `NumeroCelular` 6.989 filas con dato, `NumeroTelefono` 3.950.
> Ambas columnas se consultan; quitar la segunda vuelve a perder esos 3.950 números.
>
> ⚠ Ninguna columna de teléfono tiene índice (verificado 2026-08-10). El rastreo escalar
> funciona igual, y el cruce masivo resuelve por hash join en ~1 s para 60 000 números.

**Excluidas explícitamente** (no son fuentes con teléfono usable para rastreo): auditorías (`AuditoriaMensajeria`, `AuditoriaIVR`), logs salientes (`MensajesEnviados`, `IVREnviados`), listas negras (`ListaNegraTelefonos`), directorios (`NumeroTelefonoGeneral`), `CamaraEmpresa`, staging (`*Auxiliar`), respaldos (`*_bak`, `Staging_*`), `DatosExternos` (memoria del proyecto: ignorada) y vistas `vw_*`.

> ADR-015 originalmente restringió el set a "solo tablas de Localización". Ese criterio quedó superado en la práctica (ver `ADR-015 → Nota 2026-05-07`); regla vigente: cualquier fuente con teléfono usable se incluye. Toda fuente nueva con columna de teléfono debe extender `RastreoLineasService.SqlConsulta` — ver skill `/agregar-fuente-datos`, paso 5d.

### ListaNegraCorreos
Correos marcados como erroneos / invalidos.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| CorreoErroneo | varchar(60) | NO | SI | — |

### ListaNegraTelefonos
Telefonos marcados como invalidos o en lista negra. 660.602 filas / 103.250 números distintos (2026-09-18). `Cartera` es texto con guiones bajos (`Pelicano_Propia`), no `CarteraID`; `Codigo` ∈ {EQUIVOCADO, FALLECIDO, NUMERO EQUIVOCADO, NO GESTIONAR, LINEA ERRADA, FRAUDE O SUPLANTACION DE IDENTIDAD}. Sin PK ni índices. **Consumida desde 2026-09-18 por el Creador de Virtualidad** (ADR-037) por SQL crudo con `#Telefonos` + `#CarterasLN` + hash join — no tiene entidad EF. El usuario elige qué carteras cruzar. Sigue excluida del Rastreo de Líneas.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | NO | — | — |
| Cartera | varchar(50) | NO | — | — |
| Codigo | varchar(20) | NO | — | — |

### NumeroTelefonoGeneral
Telefonos generales asociados a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | varchar(50) | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |

---

## Tablas de referencia geografica

### PrefijoMunicipio
Prefijos telefonicos por municipio y departamento.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Departamento | varchar(50) | NO | — | — |
| Municipio | varchar(50) | NO | — | — |
| Prefijo | varchar(3) | NO | — | — |

### PrefijosDepartamento
Prefijos telefonicos por departamento.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Departamento | varchar(60) | NO | — | — |
| Prefijo | int | NO | — | — |

---

## Tablas de datos generales

### BaseGeneral
Base general de deudores con numero de documento y cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | bigint | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| Cartera | varchar(50) | SI | — | — |

---

## Tablas Identity (ASP.NET)

Tablas estandar de ASP.NET Core Identity con extensiones propias del sistema.

### AspNetUsers
Usuarios del sistema. Extendido con `LiderID` y `NombreCompleto`.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Id | int (identity) | NO | SI | — |
| LiderID | int | SI | — | Lider |
| NombreCompleto | nvarchar(max) | NO | — | — |
| UserName | nvarchar(512) | SI | — | — |
| NormalizedUserName | nvarchar(512) | SI | — | — |
| Email | nvarchar(512) | SI | — | — |
| NormalizedEmail | nvarchar(512) | SI | — | — |
| EmailConfirmed | bit | NO | — | — |
| PasswordHash | nvarchar(max) | SI | — | — |
| SecurityStamp | nvarchar(max) | SI | — | — |
| ConcurrencyStamp | nvarchar(max) | SI | — | — |
| PhoneNumber | nvarchar(max) | SI | — | — |
| PhoneNumberConfirmed | bit | NO | — | — |
| TwoFactorEnabled | bit | NO | — | — |
| LockoutEnd | datetimeoffset | SI | — | — |
| LockoutEnabled | bit | NO | — | — |
| AccessFailedCount | int | NO | — | — |

### AspNetRoles

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Id | int (identity) | NO | SI | — |
| Name | nvarchar(512) | SI | — | — |
| NormalizedName | nvarchar(512) | SI | — | — |
| ConcurrencyStamp | nvarchar(max) | SI | — | — |

### AspNetUserRoles

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| UserId | int | NO | SI | AspNetUsers |
| RoleId | int | NO | SI | AspNetRoles |

### AspNetUserClaims

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Id | int (identity) | NO | SI | — |
| UserId | int | NO | — | AspNetUsers |
| ClaimType | nvarchar(max) | SI | — | — |
| ClaimValue | nvarchar(max) | SI | — | — |

### AspNetRoleClaims

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| Id | int (identity) | NO | SI | — |
| RoleId | int | NO | — | AspNetRoles |
| ClaimType | nvarchar(max) | SI | — | — |
| ClaimValue | nvarchar(max) | SI | — | — |

### AspNetUserLogins

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| LoginProvider | nvarchar(900) | NO | SI | — |
| ProviderKey | nvarchar(900) | NO | SI | — |
| ProviderDisplayName | nvarchar(max) | SI | — | — |
| UserId | int | NO | — | AspNetUsers |

### AspNetUserTokens

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| UserId | int | NO | SI | AspNetUsers |
| LoginProvider | nvarchar(900) | NO | SI | — |
| Name | nvarchar(900) | NO | SI | — |
| Value | nvarchar(max) | SI | — | — |

---

## Tablas _bak y staging

Tablas de respaldo y carga temporal.

### Cartera_bak
Respaldo de la tabla Cartera (sin constraints de FK activos).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | int (identity) | NO | — | — |
| NombreCartera | varchar(50) | NO | — | — |
| LiderID | int | SI | — | — |

### Lider_bak
Respaldo de la tabla Lider.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | int (identity) | NO | — | — |
| Nombre | varchar(50) | NO | — | — |
| Email | varchar(100) | SI | — | — |

### Lider_Cartera_bak
Respaldo de la relacion Lider-Cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| LiderID | int | NO | — | — |
| CarteraID | int | NO | — | — |

### Titular_Cartera_bak
Respaldo de la tabla Titular_Cartera con datos de gestion.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| CarteraID | int | NO | — | — |
| MejorPerfil | varchar(50) | SI | — | — |
| UltimoPerfil | varchar(50) | SI | — | — |
| UltimaFechaGestion | date | SI | — | — |
| UltimoNumeroMarcado | varchar(10) | SI | — | — |
| Rotacion | int | SI | — | — |
| Saldo | money | SI | — | — |

### Staging_ListaNegraTelefonos
Tabla de staging para carga de lista negra de telefonos desde fuentes externas.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | nvarchar(max) | SI | — | — |
| UltimoNumeroMarcado | nvarchar(max) | SI | — | — |
| Tipificacion | nvarchar(max) | SI | — | — |
| Cartera | nvarchar(max) | SI | — | — |

---

## Tablas del sistema

### __EFMigrationsHistory
Historial de migraciones de Entity Framework Core.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| MigrationId | nvarchar(300) | NO | SI | — |
| ProductVersion | nvarchar(64) | NO | — | — |

### sysdiagrams
Diagramas de base de datos (sistema SQL Server Management Studio).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| name | sysname(256) | NO | — | — |
| principal_id | int | NO | — | — |
| diagram_id | int (identity) | NO | SI | — |
| version | int | SI | — | — |
| definition | varbinary(max) | SI | — | — |

---

## Vistas (Views)

### Vistas de datos con informacion consolidada por titular

| Vista | Descripcion |
|-------|-------------|
| vw_DatosARLSura | Datos de titulares con registro en ARL Sura |
| vw_DatosCajacopi | Datos de titulares afiliados en Cajacopi |
| vw_DatosCamara | Datos de titulares con registro en Camara de Comercio (persona natural) |
| vw_DatosCamaraEmpresa | Datos de empresas con registro en Camara de Comercio |
| vw_DatosClaro | Datos de contacto provenientes de Claro |
| vw_DatosCompensados | Datos de titulares con compensacion laboral |
| vw_DatosFosyga | Datos de afiliacion SGSSS (Fosyga/ADRES) |
| vw_DatosPositiva | Datos de titulares afiliados a Positiva ARL |
| vw_DatosPositivaEmpresas | Datos de empresas vinculadas a titulares en Positiva |
| vw_DatosRGM | Datos del Registro de Garantias Mobiliarias |
| vw_DatosRUES | Datos del Registro Unico Empresarial y Social |
| vw_DatosSaludTotal | Datos de titulares afiliados a Salud Total EPS |
| vw_DatosSIMIT | Datos de titulares con multas/infracciones en SIMIT |
| vw_DatosSOS | Datos de titulares con registro en SOS (sistema laboral) |
| vw_DatosTigo | Datos de contacto provenientes de Tigo |

### Vistas de titulares SIN datos en fuente externa

| Vista | Descripcion |
|-------|-------------|
| vw_SinDatosARLSura | Titulares sin consulta o sin registro en ARL Sura |
| vw_SinDatosCajacopi | Titulares sin registro en Cajacopi |
| vw_SinDatosCamara | Titulares sin registro en Camara de Comercio |
| vw_SinDatosCompensados | Titulares sin compensacion laboral registrada |
| vw_SinDatosFosyga | Titulares sin afiliacion SGSSS |
| vw_SinDatosPositiva | Titulares sin registro en Positiva |
| vw_SinDatosPositivaEmpresas | Titulares sin empresas en Positiva |
| vw_SinDatosRGM | Titulares sin registro en RGM |
| vw_SinDatosRUES | Titulares sin registro en RUES |
| vw_SinDatosSaludTotal | Titulares sin registro en Salud Total |
| vw_SinDatosSIMIT | Titulares sin multas/infracciones en SIMIT |
| vw_SinDatosSOS | Titulares sin registro en SOS |
| vw_SinDatosTigo | Titulares sin datos en Tigo |

### Vistas de base general y estadisticas

| Vista | Descripcion |
|-------|-------------|
| vw_BaseMadreBaseGeneral | Vista maestra sobre BaseGeneral |
| vw_BaseMadreTitularAuxiliar | Vista maestra sobre TitularAuxiliar |
| vw_EnviosFrecuentesSemana | Estadisticas de envios (correo/IVR/SMS) agrupados por semana |

---

## Funciones (Functions)

### Inline Table-Valued Functions

| Función | Descripcion |
|---------|-------------|
| dbo.fn_BaseMadrePorCartera(@CarteraID int, @LiderID int = NULL) | Replica funcional de `vw_BaseMadre` filtrada por `(CarteraID, LiderID)` con pre-restricción del dominio en cada CTE Ultimo*. Acepta `@LiderID = NULL` para admin/director sin restricción. Mejora medida P90 ~3×, P99 ~17× vs llamar a la vista directa. Aplicada 2026-05-06 (`docs/sql/migrations/2026-05-06_fn_BaseMadrePorCartera.sql`). Actualizaciones: 2026-05-07 — fuente MutualSer agregada (10 columnas, ver `2026-05-07_mutualser_in_basemadre.sql`); 2026-05-07 — DatosExternos removida del CTE/JOIN/columnas (`2026-05-07_remove_datos_externos_from_basemadre.sql`). Consumida por `CalificadorCarteraService` y `BaseMadreRepository`. |

---

## Stored Procedures

### Stored Procedures de negocio — Procesamiento de datos auxiliares

Estos SPs procesaban las tablas *Auxiliar (staging) y transferían los datos a las tablas de producción correspondientes. Desde la Fase 2 de ADR-039 (2026-09-24) **todas las fuentes de localización se cargan por el módulo** y los 21 SP de fuentes quedan sin uso (respaldo íntegro en `docs/sql/legacy/`); solo `sp_ProcesarTitularAuxiliar` sigue vivo. Lista verificada contra `sys.procedures` el 2026-09-24 (22 `sp_Procesar*`).

| Stored Procedure | Descripcion |
|-----------------|-------------|
| sp_ProcesarArlSuraAuxiliar | Procesa ARLSuraAuxiliar → ARLSura. **Fase 2: sin uso, se elimina con `2026-09-26_drop_auxiliares_fase2.sql`; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarCajacopiAuxiliar | Procesa CajacopiAuxiliar → Cajacopi. **Fase 1: sin uso, se elimina con `2026-09-25_drop_auxiliares_fase1.sql`; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarCamaraAuxiliar | Procesa CamaraAuxiliar → Camara. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarCamaraEmpresaAuxiliar | Procesa CamaraEmpresaAuxiliar → CamaraEmpresa. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarCompensadosAuxiliar | Procesa CompensadosAuxiliar → Compensados. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarCoosaludAuxiliar | Procesa CoosaludAuxiliar → Coosalud. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarDatosClaroAuxiliar | Procesa DatosClaroAuxiliar → DatosClaro. **Fase 1: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarFosygaAuxiliar | Procesa FosygaAuxiliar → Fosyga. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** (alguien lo usaba a mano la semana del 2026-09-24) |
| sp_ProcesarMiVacunaAuxiliar | Procesa MiVacunaAuxiliar → MiVacuna. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarMutualSerAuxiliar | Procesa MutualSerAuxiliar → MutualSer. **Fase 1: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarNuevaEpsAuxiliar | Procesa NuevaEpsAuxiliar → NuevaEps. **Fase 1: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarPositivaAuxiliar | Procesa PositivaAuxiliar → Positiva. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarPositivaEmpresasAuxiliar | Procesa PositivaEmpresasAuxiliar → PositivaEmpresas. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarRGMAuxiliar | Procesa RGMAuxiliar → RGM. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarRUESAuxiliar | Procesa RUESAuxiliar → RUES. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarSaludTotalAuxiliar | Procesa SaludTotalAuxiliar → SaludTotal. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** (su texto nombra `RUESAuxiliar` en un comentario) |
| sp_ProcesarSaviaSaludAuxiliar | Procesa SaviaSaludAuxiliar → SaviaSalud. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarSIMITAuxiliar | Procesa SIMITAuxiliar → SIMIT (despivota Placa1..15). **Fase 1: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarSOSAuxiliar | Procesa SOSAuxiliar → SOS. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarSuraAuxiliar | Procesa SuraAuxiliar → Sura. **Fase 1: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarTigoUneAuxiliar | Procesa TigoUneAuxiliar → TigoUne. **Fase 2: sin uso, se elimina; respaldo en `docs/sql/legacy/`** |
| sp_ProcesarTitularAuxiliar | Procesa TitularAuxiliar → Titular + Titular_Cartera. **Sigue en uso** (fuera de ADR-039) |
| sp_Actualizar_AuditoriaMensajeria | Actualiza la tabla AuditoriaMensajeria con los envios recientes |

### Stored Procedures del sistema (SQL Server Management Studio)

Generados automaticamente por SSMS para gestion de diagramas de BD. No deben modificarse manualmente.

| Stored Procedure | Descripcion |
|-----------------|-------------|
| sp_alterdiagram | Modifica un diagrama existente |
| sp_creatediagram | Crea un nuevo diagrama |
| sp_dropdiagram | Elimina un diagrama |
| sp_helpdiagramdefinition | Devuelve la definicion de un diagrama |
| sp_helpdiagrams | Lista los diagramas disponibles |
| sp_renamediagram | Renombra un diagrama |
| sp_upgraddiagrams | Actualiza diagramas a la version actual |

---

## Relaciones principales del sistema

```
Lider
  └──< Lider_Cartera >──── Cartera
           └──< Titular_Cartera >──── Titular
                     └──< [fuentes externas] (ARLSura, Cajacopi, Camara, Fosyga, etc.)

AspNetUsers ──── Lider (LiderID nullable)
Cartera_Agente ──── Agente + Lider_Cartera
UltimoEstadoTitular: snapshot por (TitularID, CarteraID)
HistoricoEstadoTitular: historial por (TitularID, CarteraID, AgenteID)
```

### Flujo de carga masiva de fuentes externas

Desde el 2026-09-24 (Fase 2 de ADR-039) **todas las fuentes de localización se cargan por el módulo**.
El modelo es un **historial acumulativo** en la tabla de la fuente (contenido igual → se refresca la
fecha; contenido nuevo → fila nueva; las vistas toman la última por `FechaConsulta`), con transacción,
conteos y reversión.

**Módulo Cargue de Fuentes de Datos (ADR-039) — 22 fuentes.** Fase 1: `Sura`, `Cajacopi`, `MutualSer`,
`NuevaEps`, `SIMIT`, `DatosClaro`. Fase 2: `Positiva`, `SaludTotal`, `SOS`, `Coosalud`, `SaviaSalud`,
`MiVacuna`, `Fosyga`, `RGM`, `TigoUne`, `Tigo`, `RUES`, `Camara`, `CamaraEmpresa` (clave `NitEmpresa`, no
crea titulares), `PositivaEmpresas`, `ARLSura`, `Compensados`. Fuera: `Runt` (vacía, sin consumidores) y
`DatosExternos` (no consumida).
```
Archivo CSV / XLSX (Administración → Cargue de Fuentes de Datos, Admin + Auxiliar de Sistemas)
  → validar en segundo plano: limpiar, deduplicar, cruzar con Titular / Lider_Cartera / listas negras
      (#Carga, #Relaciones en tempdb; nada se escribe) → preview con conteos
  → confirmar: una transacción
      Titular (nuevos) → Titular.Nombre (solo rotos) → Titular_Cartera (solo pares activos)
      → refrescar fecha del contenido idéntico → INSERT del contenido nuevo → purgar vacías viejas
      (OUTPUT → CargueFuenteReversion)
  → CargueFuenteEjecucion (auditoría) + invalidar caché del Calificador
```
Revertir un cargue: `docs/sql/revertir_cargue_fuente.sql` (lista blanca de las 22). Una fuente nueva
sigue el paso 5f de `/agregar-fuente-datos`.

**Patrón Auxiliar (legacy, sin uso)** — lo que había antes del módulo, y que solo sigue vivo para
`TitularAuxiliar` → `sp_ProcesarTitularAuxiliar` (titulares y asignaciones, fuera de ADR-039):
```
Fuente externa (archivo/API)
  → Tabla*Auxiliar  (staging, sin constraints; se llena a mano con SSMS)
  → sp_Procesar*Auxiliar  (sin transacción ni conteos)
  → Tabla produccion
```
Las 21 `<Fuente>Auxiliar` con SP y `TigoAuxiliar` (Tigo se llenaba a mano, sin SP) quedan sin uso:
respaldo en `docs/sql/legacy/`, DROP en `2026-09-25_drop_auxiliares_fase1.sql` (6) y
`2026-09-26_drop_auxiliares_fase2.sql` (15 + `TigoAuxiliar`), a mano y después de validar el cargue en
producción. El de la Fase 2 se bloquea mientras `FosygaAuxiliar`, `RGMAuxiliar` o `TigoAuxiliar` tengan
filas (ver su cabecera).

---

## Índices creados (optimizaciones 2026-04-13)

### Dashboard — UltimoEstadoTitular

Creados para soportar las consultas del Dashboard Director y Asesor. Sin ellos, cada consulta hacía un full scan de la tabla.

```sql
-- Consultas del Dashboard Asesor (filtro por AgenteID)
CREATE INDEX IX_UltimoEstadoTitular_AgenteID
    ON UltimoEstadoTitular(AgenteID)
    INCLUDE (FechaUltimaGestion, UltimoPerfil, FechaActualizacion,
             FechaPromesa, ValorPromesa, SaldoTotal, TitularID);

-- Consultas del Dashboard Director filtradas por cartera + fecha
CREATE INDEX IX_UltimoEstadoTitular_CarteraID_Fecha
    ON UltimoEstadoTitular(CarteraID, FechaUltimaGestion)
    INCLUDE (AgenteID, UltimoPerfil);

-- Consultas del Dashboard Director sin filtro de cartera (vista global)
CREATE INDEX IX_UltimoEstadoTitular_Fecha
    ON UltimoEstadoTitular(FechaUltimaGestion)
    INCLUDE (CarteraID, AgenteID, UltimoPerfil);
```

### Calificador de Cartera — tablas fuente de vw_BaseMadre

`vw_BaseMadre` utiliza `ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC)` en cada tabla de datos externos. Sin índices, cada ejecución hacía full scans de ~10M+ filas totales en todas las tablas combinadas.

Patrón aplicado: `CREATE INDEX IX_<Tabla>_TitularID_Fecha ON <Tabla>(TitularID, FechaConsulta DESC)`

| Tabla | Índice creado |
|-------|---------------|
| ARLSura | `IX_ARLSura_TitularID_Fecha` |
| Cajacopi | `IX_Cajacopi_TitularID_Fecha` |
| Camara | `IX_Camara_TitularID_Fecha` |
| Compensados | `IX_Compensados_TitularID_Fecha` |
| DatosClaro | `IX_DatosClaro_TitularID_FechaInsercion` (sobre `TitularID, FechaInsercion DESC`) |
| Fosyga | `IX_Fosyga_TitularID_Fecha` |
| MutualSer | `IX_MutualSer_TitularID_FechaConsulta` (creado en migración `2026-05-07_mutualser_in_basemadre.sql`) |
| Positiva | `IX_Positiva_TitularID_Fecha` |
| PositivaEmpresas | `IX_PositivaEmpresas_TitularID_Fecha` (la tabla seguía siendo HEAP sin este índice en producción pese a estar documentado aquí; backfill en migración `2026-07-08_index_positivaempresas_titularid.sql`) |
| RGM | `IX_RGM_TitularID_Fecha` |
| RUES | `IX_RUES_TitularID_Fecha` |
| Runt | ~~`IX_Runt_TitularID_Fecha`~~ **no existe** (verificado con `sys.indexes` el 2026-09-24: `dbo.Runt` es un heap sin índices, 0 filas) |
| SaludTotal | `IX_SaludTotal_TitularID_Fecha` |
| SIMIT | `IX_SIMIT_TitularID_Fecha` |
| SOS | `IX_SOS_TitularID_Fecha` |
| Tigo | `IX_Tigo_TitularID_Fecha` |
| TigoUne | `IX_TigoUne_TitularID_FechaConsulta` |
| Coosalud | `IX_Coosalud_TitularID_FechaConsulta` |
| SaviaSalud | `IX_SaviaSalud_TitularID_FechaConsulta` |
| Sura | `IX_Sura_TitularID_FechaConsulta` (la tabla era HEAP sin índices; creado en `2026-09-02_sura_in_basemadre.sql`) |

#### Columnas expuestas por `vw_BaseMadre` (alias en SQL)

Las propiedades C# correspondientes están en `BaseMadreRow.cs` y el mapping en `BaseMadreRowMapping.cs`. Bloque de contactabilidad:

| Alias en vista | Propiedad C# | Fuente |
|----------------|--------------|--------|
| `[Celular Cajacopi]`, `[Email CajaCopi]` | `CelularCajacopi`, `EmailCajacopi` | Cajacopi |
| `[Celular Camara]`, `[Email Camara]` | `CelularCamara`, `EmailCamara` | Camara |
| `[Celular Salud Total]`, `[Email Salud Total]` | `CelularSaludTotal`, `EmailSaludTotal` | SaludTotal |
| `[Celular Claro]`, `[Email Claro]`, `[Ultima Insercion Claro]` | `CelularClaro`, `EmailClaro`, `UltimaInsercionClaro` | DatosClaro |
| `[Celular MutualSer]`, `[Celular 2 MutualSer]`, `[Telefono Fijo MutualSer]`, `[Email MutualSer]`, `[Regimen MutualSer]`, `[Tipo Afiliado MutualSer]`, `[Departamento MutualSer]`, `[Municipio MutualSer]`, `[Estado MutualSer]`, `[Ultima Consulta MutualSer]` | `CelularMutualSer`, `Celular2MutualSer`, `TelefonoFijoMutualSer`, `EmailMutualSer`, `RegimenMutualSer`, `TipoAfiliadoMutualSer`, `DepartamentoMutualSer`, `MunicipioMutualSer`, `EstadoMutualSer`, `UltimaConsultaMutualSer` | MutualSer |
| `[Celular Positiva]`, `[Email Positiva]` | `CelularPositiva`, `EmailPositiva` | Positiva |
| `[Celular Tigo]`, `[Email Tigo]` | `CelularTigo`, `EmailTigo` | Tigo |
| `[Celular TigoUne]`, `[Email TigoUne]` | `CelularTigoUne`, `EmailTigoUne` | TigoUne |
| `[Celular Coosalud]`, `[Email Coosalud]`, `[Regimen Coosalud]`, `[Tipo Afiliado Coosalud]`, `[Estado Afiliacion Coosalud]`, `[Nivel Sisben Coosalud]`, `[Departamento Coosalud]`, `[Municipio Coosalud]`, `[Fecha Nacimiento Coosalud]`, `[Estado Coosalud]`, `[Ultima Consulta Coosalud]` | `CelularCoosalud`, `EmailCoosalud`, `RegimenCoosalud`, `TipoAfiliadoCoosalud`, `EstadoAfiliacionCoosalud`, `NivelSisbenCoosalud`, `DepartamentoCoosalud`, `MunicipioCoosalud`, `FechaNacimientoCoosalud`, `EstadoCoosalud`, `UltimaConsultaCoosalud` | Coosalud |
| `[Celular SaviaSalud]`, `[Regimen SaviaSalud]`, `[Tipo Afiliado SaviaSalud]`, `[Estado Afiliacion SaviaSalud]`, `[Fecha Nacimiento SaviaSalud]`, `[Estado SaviaSalud]`, `[Ultima Consulta SaviaSalud]` | `CelularSaviaSalud`, `RegimenSaviaSalud`, `TipoAfiliadoSaviaSalud`, `EstadoAfiliacionSaviaSalud`, `FechaNacimientoSaviaSalud`, `EstadoSaviaSalud`, `UltimaConsultaSaviaSalud` | SaviaSalud |
| `[Celular SOS]` | `CelularSOS` | SOS |
| `[Celular Sura]`, `[Email Sura]`, `[NIT Sura]`, `[Empresa Sura]`, `[Tipo Afiliado Sura]`, `[Estado Afiliacion Sura]` | `CelularSura`, `EmailSura`, `NITSura`, `EmpresaSura`, `TipoAfiliadoSura`, `EstadoAfiliacionSura` | Sura |
| `[NIT ARL Sura]`, `[Empresa ARL Sura]` | `NITArlSura`, `EmpresaArlSura` | ARLSura (renombrados el 2026-09-02, antes `[NIT Sura]` / `[Empresa Sura]`) |

#### Estado operativo de las fuentes (visual en Calificador)

Indicador junto al nombre de cada fuente en la tabla de localización (`CalificadorCarteraPage → ESTADO_FUENTE`). Refleja si la tabla origen recibe actualizaciones en producción. **Es metadato visual** — no participa de cálculos ni filtros, y se mantiene a mano cuando el equipo de datos confirma cambios de cadencia.

| Fuente | Estado | Notas |
|--------|--------|-------|
| Cajacopi | Funcional | — |
| Cámara | Intermitente | — |
| Salud Total | Funcional | — |
| Fosyga | Intermitente | — |
| Compensados | Intermitente | — |
| Positiva | Funcional | — |
| Tigo | Inactivo | — |
| TigoUne | Funcional | Gemela de Tigo; con datos desde 2026-08-05 (5.192 titulares) |
| Coosalud | Funcional | EPS tipo NuevaEps ampliada; con datos desde 2026-08-05 (369.049 titulares) |
| Savia Salud | Funcional | EPS con datos (193.752 filas al integrarse 2026-08-05); celular en NumeroCelular, sin email |
| SOS | Funcional | — |
| Claro | Inactivo | — |
| MutualSer | Funcional | 1:1 — aporta 2 celulares + email + régimen + dept/municipio + estado |
| ARL Sura | Funcional | Solo aporta nombre de empresa, no contactabilidad. Etiqueta anterior: `Sura (ARL)` |
| Sura | Funcional | 1:1 — celular + email + NIT/empresa + tipo y estado de afiliación. Tabla vacía al integrarse (2026-09-02) |
| Positiva Emp. | Funcional | — |
| RUES | Funcional | — |
| SIMIT | Funcional | — |
| RGM | Funcional | — |

Colores fijos: `#16A34A` (Funcional), `#CA8A04` (Intermitente), `#DC2626` (Inactivo). Para agregar/cambiar un estado: editar `ESTADO_FUENTE` en `src/Zentinela.Web/src/pages/cartera/CalificadorCarteraPage.tsx`.

Índice adicional en `Titular_Cartera` para filtros del Calificador por líder/cartera:

```sql
CREATE INDEX IX_TitularCartera_LiderID_CarteraID
    ON Titular_Cartera(LiderID, CarteraID);
```

---

### Pago
Pagos confirmados cargados manualmente por cartera desde la UI. Alimenta los informes de recaudo.

> **Creada:** 2026-04-15 — script en `docs/migrations/create_Pago_table.sql`

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| PagoID | int (identity) | NO | SI | — |
| CedulaTitular | bigint | NO | — | Titular |
| NombreCliente | varchar(100) | NO | — | — |
| ValorPagado | decimal(18,2) | NO | — | — |
| FechaPago | date | NO | — | — |
| Honorarios | decimal(18,2) | NO | — | — |
| LiderID | int | NO | — | Lider_Cartera |
| CarteraID | int | NO | — | Cartera |
| AgenteControlID | bigint | SÍ | — | Agente (cross-DB ref) |
| UsuarioRegistro | nvarchar(256) | NO | — | ApplicationUser.UserName (DBZentinela, sin FK) |
| FechaRegistro | datetime | NO | — | — |

**Notas:**
- `LiderID` se resuelve automáticamente desde `Titular_Cartera` usando `(CedulaTitular, CarteraID)`. Si la cédula tiene múltiples líderes en la cartera, se desempata con `Cartera_Agente.LiderID` del agente resuelto (excepto en pagos BASE GENERAL, que requieren unicidad).
- `AgenteControlID` se resuelve desde `Agente.UsuarioControlNext` (valor textual del CSV). **NULL** cuando el campo Usuario Control del CSV es `BASE GENERAL` (case-insensitive) — el pago se atribuye solo al líder. Ver ADR-018 y migración `2026-05-21_pago_agentecontrolid_nullable.sql`.
- `UsuarioRegistro` guarda el `UserName` (login de Identity, `ApplicationUser.UserName`) del usuario que confirmó el cargue — no su `Id`. Reside en `DBZentinela`; no existe FK a nivel de BD por ser cross-database. Ver migración `2026-05-27_pago_usuarioregistro.sql`.
- La tabla usa `ExcludeFromMigrations` en EF Core; el schema se gestiona con el script SQL manual.

---

### Meta_Agente_Mes
Metas mensuales de recaudo y honorarios por agente individual. Alimenta el sub-informe de Agentes.

> **Creada:** 2026-04-15 — script en `docs/migrations/create_Meta_tables.sql`  
> **Actualizada:** 2026-04-20 — se agregó `MetaHonorarios` (columna gestionada desde módulo Metas del mes)

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| MetaID | int (identity) | NO | SI | — |
| AgenteID | bigint | NO | — | Agente |
| Año | smallint | NO | — | — (columna en BD: `Anio`) |
| Mes | tinyint | NO | — | — (1-12) |
| MetaRecaudo | decimal(18,2) | NO | — | — |
| MetaHonorarios | decimal(18,2) | NO | — | — |

**Constraint UNIQUE:** `(AgenteID, Año, Mes)`  
**Gestión:** UPSERT desde el módulo Administración → Metas del mes. Solo se pueden establecer metas para el ciclo (mes/año) actual.

---

### Meta_Lider_Cartera_Mes
Metas mensuales de recaudo y honorarios por combinación líder-cartera. Alimenta el sub-informe de Líderes.

> **Creada:** 2026-04-15 — script en `docs/migrations/create_Meta_tables.sql`  
> **Actualizada:** 2026-04-20 — se agregó `MetaHonorarios` (columna gestionada desde módulo Metas del mes)

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| MetaID | int (identity) | NO | SI | — |
| LiderID | int | NO | — | Lider_Cartera |
| CarteraID | int | NO | — | Lider_Cartera |
| Año | smallint | NO | — | — (columna en BD: `Anio`) |
| Mes | tinyint | NO | — | — (1-12) |
| MetaRecaudo | decimal(18,2) | NO | — | — |
| MetaHonorarios | decimal(18,2) | NO | — | — |

**Constraint UNIQUE:** `(LiderID, CarteraID, Año, Mes)`  
**Gestión:** UPSERT desde el módulo Administración → Metas del mes. Solo se pueden establecer metas para el ciclo (mes/año) actual.
