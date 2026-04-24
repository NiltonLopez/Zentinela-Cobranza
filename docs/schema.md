# Schema — DBCallCenter

> Generado: 2026-04-02 — Actualizado: 2026-04-24  
> Servidor: 10.0.0.89,1433  
> Base de datos: DBCallCenter  
> Total tablas: 76 | Vistas: 32 | Stored Procedures: 21

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
| ID | int (identity) | NO | SI | — |
| Nombre | varchar(100) | NO | — | — |
| Email | varchar(150) | SI | — | — |
| Activo | bit | NO | — | — |

### Lider_Cartera
Relación muchos-a-muchos entre Lider y Cartera.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| LiderID | int | NO | SI | Lider |
| CarteraID | int | NO | SI | Cartera |

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
| AgenteID | int | NO | — | Actualizable via MERGE |
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
Datos de contacto obtenidos de Claro.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | bigint | NO | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |
| Email | varchar(100) | SI | — | — |
| FechaInsercion | date | SI | — | — |

### DatosExternos
Datos de contacto de fuentes externas genericas.

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
Mensajes enviados a titulares.

| Columna | Tipo | Nullable | PK | FK → Tabla |
|---------|------|----------|----|------------|
| TitularID | varchar(150) | SI | — | — |
| NumeroTelefono | varchar(10) | SI | — | — |

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
| Fosyga | `IX_Fosyga_TitularID_Fecha` |
| Positiva | `IX_Positiva_TitularID_Fecha` |
| PositivaEmpresas | `IX_PositivaEmpresas_TitularID_Fecha` |
| RGM | `IX_RGM_TitularID_Fecha` |
| RUES | `IX_RUES_TitularID_Fecha` |
| Runt | `IX_Runt_TitularID_Fecha` |
| SaludTotal | `IX_SaludTotal_TitularID_Fecha` |
| SIMIT | `IX_SIMIT_TitularID_Fecha` |
| SOS | `IX_SOS_TitularID_Fecha` |
| Tigo | `IX_Tigo_TitularID_Fecha` |

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
| AgenteControlID | bigint | NO | — | Agente (cross-DB ref) |
| UsuarioControlID | int | NO | — | ApplicationUser (DBZentinela, sin FK) |
| FechaRegistro | datetime | NO | — | — |

**Notas:**
- `LiderID` se resuelve automáticamente desde `Titular_Cartera` usando `(CedulaTitular, CarteraID)`.
- `AgenteControlID` se resuelve desde `Agente.UsuarioControlNext` (valor textual del CSV).
- `UsuarioControlID` referencia a `ApplicationUser` en `DBZentinela` — no existe FK a nivel de BD por ser cross-database.
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
