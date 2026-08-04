# Schema — DBCallCenter

> Generado: 2026-04-02 — Actualizado: 2026-08-04  
> Servidor: 10.0.0.89,1433  
> Base de datos: DBCallCenter  
> Total tablas: 77 | Vistas: 32 | Stored Procedures: 21

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
Poblada por Zentinela vía `CargueReporteService` (SqlBulkCopy + MERGE). Ver ADR-011.  
⚠️ `FechaMejorPerfil` eliminada el 2026-04-24 (campo sin valor operativo — ver ADR-011).

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
| FechaActualizacion | datetime | NO | — | Timestamp del último cargue en Zentinela (DateTime.Now) |

### HistoricoEstadoTitular
Historial de estados de gestión por titular, acumulado con cada cargue de reportes.  
**Clave semántica:** `(TitularID, CarteraID, AgenteID, Rotacion)` — una fila por gestión única. Ver ADR-012.  
⚠️ `FechaMejorPerfil` eliminada el 2026-04-24.  
✅ `FechaActualizacion` añadida el 2026-04-24 (requiere `ALTER TABLE ... ADD FechaActualizacion DATETIME NOT NULL DEFAULT GETDATE()`).

| Columna | Tipo | Nullable | PK | Notas |
|---------|------|----------|----|-------|
| TitularID | bigint | NO | — | |
| CarteraID | int | NO | — | |
| AgenteID | int | NO | — | |
| UltimoPerfil | varchar(50) | SI | — | |
| MejorPerfil | varchar(50) | SI | — | |
| SaldoTotal | money | SI | — | |
| NumeroMarcado | varchar(10) | SI | — | |
| Rotacion | int | SI | — | Discriminador de unicidad histórica |
| FechaUltimaGestion | date | SI | — | |
| FechaPromesa | date | SI | — | |
| ValorPromesa | money | SI | — | |
| MotivoNoPago | varchar(100) | SI | — | |
| FechaActualizacion | datetime | NO | — | Timestamp del cargue en Zentinela — UPDATE si misma Rotacion, INSERT si nueva |

### HistoricoVirtualidad
Histórico consolidado de solicitudes de virtualidad (IVR / SMS / Correo) con resultado actualizable. Una sola tabla con discriminador `Canal`. `Estado = NULL` = fase Solicitud (sin resultado); se actualiza junto con `FechaResultado` en la fase Resultado (por PK). Único punto de escritura del módulo "Gestión de Histórico de Virtualidad" (Administración) — reemplazó las tablas legacy `IVREnviados`/`MensajesEnviados` (consolidación 2026-07-06). Renombrada de `SolicitudVirtualidad` el 2026-07-08 (`docs/sql/migrations/2026-07-08_rename_solicitud_a_historico_virtualidad.sql`), 0 filas al momento del rename.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| ID | bigint | NO | SI | — |
| Canal | varchar(10) | NO | — | CHECK IN (IVR, SMS, Correo) |
| NumeroDocumento | bigint | NO | — | — |
| Destino | varchar(255) | NO | — | teléfono (IVR/SMS) o email (Correo) |
| CarteraID | int | NO | — | FK compuesta → Lider_Cartera |
| LiderID | int | NO | — | FK compuesta → Lider_Cartera |
| FechaEnvio | date | NO | — | — |
| Estado | varchar(50) | SI | — | NULL = sin resultado (fase Solicitud) |
| FechaResultado | datetime2 | SI | — | poblada en fase Resultado |
| FechaCargue | datetime2 | NO | — | DEFAULT GETDATE() |

Índices: `IX_HistoricoVirtualidad_Canal_Cartera_Fecha` (Canal, CarteraID, FechaEnvio), `IX_HistoricoVirtualidad_Canal_Destino` (Canal, Destino).

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

### ARLSura
Datos de afiliacion a ARL Sura por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| EstadoEnSura | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

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
| FechaConsulta | date | NO | — | — |

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

### CamaraEmpresa
Datos de la Camara de Comercio por empresa (NIT).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NitEmpresa | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnCamara | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

### Compensados
Datos de compensacion laboral (cajas de compensacion) por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| EPS | varchar(50) | SI | — | — |
| UltimoPeriodoCompensado | date | SI | — | — |
| TipoAfiliado | varchar(50) | SI | — | — |
| Estado | int | SI | — | — |
| FechaConsulta | date | NO | — | — |

### DatosClaro
Contactabilidad telefónica y email obtenida del operador Claro. Cardinalidad ~955k filas (~775k titulares únicos, varios registros por titular). Se consume vía `vw_BaseMadre` con TOP 1 por `FechaInsercion DESC`. Tabla espejo `DatosClaroAuxiliar` (staging) y vista `vw_DatosClaro` (concatenado legacy) **no se usan en código**.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| FechaInsercion | date | SI | — | — |

> **Diferencia con el resto:** las demás fuentes ordenan por `FechaConsulta DESC`; DatosClaro usa `FechaInsercion DESC` (la tabla no tiene `FechaConsulta`). La fecha también se expone en la vista como `[Ultima Insercion Claro]` para medir vigencia de los datos.

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

### MiVacuna
Contactabilidad proveniente de la plataforma MiVacuna. Cardinalidad 1:1 por `TitularID` (36.985 filas == titulares únicos). Tabla heap (sin PK ni índices nativos) — la migración `2026-05-29_mivacuna_in_basemadre.sql` crea `IX_MiVacuna_TitularID_FechaConsulta` para soportar el JOIN del iTVF. Tabla espejo `MiVacunaAuxiliar` (staging).

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| FechaExpedicion | date | SI | — | — |
| Celular | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnMiVacuna | bit | SI | — | — |
| FechaConsulta | date | SI | — | — |

> **Expuesta en `vw_BaseMadre` + `fn_BaseMadrePorCartera`** con alias `[Celular MiVacuna]`, `[Email MiVacuna]`, `[Fecha Expedicion MiVacuna]`, `[Estado MiVacuna]` (bit → VARCHAR(1), patrón MutualSer). `Celular` entra al switch `ColumnaCelular` y `Email` al `ColumnaEmail` (clave `MiVacuna`). `FechaConsulta` queda como metadato interno (no se expone). No aporta localización. Sí entra al `UNION ALL` de Rastreo de Líneas (columna `Celular`).

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

### RGM
Datos del Registro de Garantias Mobiliarias por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| Marca | varchar(50) | SI | — | — |
| Modelo | int | SI | — | — |
| Placa | varchar(50) | SI | — | — |
| EstadoEnRGM | bit | SI | — | — |
| FechaConsulta | date | NO | — | — |

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
| FechaConsulta | datetime | NO | — | — |

### Runt
Datos del Registro Unico Nacional de Transito por titular.

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

### SIMIT
Datos del Sistema Integrado de Multas e Infracciones de Transito por titular.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| Placa | varchar(10) | SI | — | — |
| EstadoEnSIMIT | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

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

### Tigo
Datos de contacto obtenidos de Tigo.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigo | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

### TigoUne
Datos de contacto obtenidos de Tigo Une. Estructura idéntica a `Tigo` (fuente gemela). A la fecha de integración (2026-08-04) la tabla está **vacía** — el cableado queda listo para el primer cargue.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | Titular |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigoUne | bit | NO | — | — |
| FechaConsulta | date | NO | — | — |

---

## Tablas Auxiliar (staging)

Tablas intermedias de carga/importacion masiva. Patron: datos crudos antes de procesarse con sp_Procesar*.

### ARLSuraAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NIT | bigint | SI | — | — |
| Empresa | varchar(100) | SI | — | — |
| EstadoEnSura | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |

### CajacopiAuxiliar

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
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |

### CamaraAuxiliar

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

### CamaraEmpresaAuxiliar

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

### DatosClaroAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(150) | SI | — | — |
| FechaInsercion | date | SI | — | — |

### DatosExternosAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(max) | SI | — | — |
| Email | varchar(max) | SI | — | — |

### FosygaAuxiliar

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

### ListaNegraTelefonosAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Cartera | varchar(50) | SI | — | — |
| Codigo | varchar(20) | SI | — | — |

### MutualSerAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefonoCelular | varchar(10) | SI | — | — |
| NumeroTelefonoCelular2 | nchar(10) | SI | — | — |
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

### PositivaAuxiliar

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

### PositivaEmpresasAuxiliar

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

### RGMAuxiliar

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

### RUESAuxiliar

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

### RuntAuxiliar

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

### SIMITAuxiliar
Tabla de staging para SIMIT con soporte de hasta 15 placas por titular.

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
| Placa15 | nchar(20) | SI | — | — |
| EstadoEnSIMIT | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(50) | SI | — | — |
| CarteraID | int | SI | — | — |

### SOSAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NitEmpresa | bigint | SI | — | — |
| RazonSocial | varchar(100) | SI | — | — |
| Telefono | varchar(10) | SI | — | — |
| Direccion | varchar(150) | SI | — | — |
| EstadoEnSos | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |
| Nombre | varchar(80) | SI | — | — |
| CarteraID | int | SI | — | — |

### TigoAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(255) | SI | — | — |
| EstadoEnTigo | bit | SI | — | — |
| FechaConsulta | datetime | SI | — | — |

### TitularAuxiliar

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| NumeroDocumento | varchar(50) | SI | — | — |
| Nombre | varchar(70) | SI | — | — |
| CarteraID | int | SI | — | — |
| NitEmpresa | bigint | SI | — | — |
| FechaNacimiento | date | SI | — | — |
| FechaExpedicionDocumento | datetime | SI | — | — |

### Titular_CarteraAuxiliar

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

### Tablas consultadas por el módulo "Rastreo de Líneas"

El sub-módulo `/consultas/rastreo` busca un celular en cualquier tabla de `DBCallCenter` que exponga columna(s) de teléfono usable, vía un único `UNION ALL` parametrizado en `RastreoLineasService.SqlConsulta`. Resuelve nombre del titular con `LEFT JOIN dbo.Titular`.

**Tablas vigentes (9)** — fuente única de verdad: el código del service.

| Tabla | Columna(s) consultadas | Etiqueta(s) en UI |
|---|---|---|
| Cajacopi | `NumeroTelefonoCelular`, `NumeroTelefonoFijo` | `Cajacopi (celular)`, `Cajacopi (fijo)` |
| Camara | `NumeroTelefono` | `Cámara` |
| SaludTotal | `NumeroTelefono` | `Salud Total` |
| Positiva | `NumeroTelefonoCelular`, `NumeroTelefonoFijo` | `Positiva (celular)`, `Positiva (fijo)` |
| Tigo | `NumeroTelefono` | `Tigo` |
| TigoUne | `NumeroTelefono` | `TigoUne` |
| SOS | `Telefono` | `SOS` |
| DatosClaro | `NumeroTelefono` | `Claro` |
| MutualSer | `NumeroTelefonoCelular`, `NumeroTelefonoCelular2`, `NumeroTelefonoFijo` | `MutualSer (celular 1)`, `MutualSer (celular 2)`, `MutualSer (fijo)` |

**Excluidas explícitamente** (no son fuentes con teléfono usable para rastreo): auditorías (`AuditoriaMensajeria`, `AuditoriaIVR`), logs salientes (`MensajesEnviados`, `IVREnviados`), listas negras (`ListaNegraTelefonos`), directorios (`NumeroTelefonoGeneral`), `CamaraEmpresa`, staging (`*Auxiliar`), respaldos (`*_bak`, `Staging_*`), `DatosExternos` (memoria del proyecto: ignorada) y vistas `vw_*`.

> ADR-015 originalmente restringió el set a "solo tablas de Localización". Ese criterio quedó superado en la práctica (ver `ADR-015 → Nota 2026-05-07`); regla vigente: cualquier fuente con teléfono usable se incluye. Toda fuente nueva con columna de teléfono debe extender `RastreoLineasService.SqlConsulta` — ver skill `/agregar-fuente-datos`, paso 5d.

### ListaNegraCorreos
Correos marcados como erroneos / invalidos.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| CorreoErroneo | varchar(60) | NO | SI | — |

### ListaNegraTelefonos
Telefonos marcados como invalidos o en lista negra.

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

Estos SPs procesan las tablas *Auxiliar (staging) y transfieren los datos a las tablas de produccion correspondientes. Son el nucleo del flujo de carga masiva.

| Stored Procedure | Descripcion |
|-----------------|-------------|
| sp_ProcesarArlSuraAuxiliar | Procesa ARLSuraAuxiliar → ARLSura |
| sp_ProcesarCajacopiAuxiliar | Procesa CajacopiAuxiliar → Cajacopi |
| sp_ProcesarCamaraAuxiliar | Procesa CamaraAuxiliar → Camara |
| sp_ProcesarCamaraEmpresaAuxiliar | Procesa CamaraEmpresaAuxiliar → CamaraEmpresa |
| sp_ProcesarCompensadosAuxiliar | Procesa CompensadosAuxiliar → Compensados |
| sp_ProcesarDatosClaroAuxiliar | Procesa DatosClaroAuxiliar → DatosClaro |
| sp_ProcesarFosygaAuxiliar | Procesa FosygaAuxiliar → Fosyga |
| sp_ProcesarPositivaAuxiliar | Procesa PositivaAuxiliar → Positiva |
| sp_ProcesarPositivaEmpresasAuxiliar | Procesa PositivaEmpresasAuxiliar → PositivaEmpresas |
| sp_ProcesarRGMAuxiliar | Procesa RGMAuxiliar → RGM |
| sp_ProcesarRUESAuxiliar | Procesa RUESAuxiliar → RUES |
| sp_ProcesarSaludTotalAuxiliar | Procesa SaludTotalAuxiliar → SaludTotal |
| sp_ProcesarSOSAuxiliar | Procesa SOSAuxiliar → SOS |
| sp_ProcesarTitularAuxiliar | Procesa TitularAuxiliar → Titular + Titular_Cartera |
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

### Flujo de carga masiva (patron Auxiliar)
```
Fuente externa (archivo/API)
  → Tabla*Auxiliar  (staging, sin constraints)
  → sp_Procesar*Auxiliar
  → Tabla produccion (con FK a Titular)
```

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
| Runt | `IX_Runt_TitularID_Fecha` |
| SaludTotal | `IX_SaludTotal_TitularID_Fecha` |
| SIMIT | `IX_SIMIT_TitularID_Fecha` |
| SOS | `IX_SOS_TitularID_Fecha` |
| Tigo | `IX_Tigo_TitularID_Fecha` |
| TigoUne | `IX_TigoUne_TitularID_FechaConsulta` |

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
| `[Celular SOS]` | `CelularSOS` | SOS |

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
| TigoUne | Inactivo | Gemela de Tigo; tabla vacía al integrarse (2026-08-04), pendiente primer cargue |
| SOS | Funcional | — |
| Claro | Inactivo | — |
| MutualSer | Funcional | 1:1 — aporta 2 celulares + email + régimen + dept/municipio + estado |
| Sura (ARL) | Funcional | Solo aporta nombre de empresa, no contactabilidad |
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
