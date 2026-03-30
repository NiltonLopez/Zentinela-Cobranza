# Esquema de base de datos — DBCallCenter

**Servidor:** `10.0.0.129,1433`
**Base de datos:** `DBCallCenter`

---

## Índice

1. [Modelo conceptual](#modelo-conceptual)
2. [Tablas principales](#tablas-principales)
3. [Tablas de datos externos por fuente](#tablas-de-datos-externos-por-fuente)
4. [Tablas auxiliares de carga (staging)](#tablas-auxiliares-de-carga-staging)
5. [Tablas de comunicaciones y auditoría de envíos](#tablas-de-comunicaciones-y-auditoría-de-envíos)
6. [Tablas de referencia y catálogos](#tablas-de-referencia-y-catálogos)
7. [Tablas de proyectos externos en integración](#tablas-de-proyectos-externos-en-integración)
8. [Vistas](#vistas)
9. [Relaciones entre tablas (Foreign Keys)](#relaciones-entre-tablas-foreign-keys)
10. [Índices y restricciones únicas](#índices-y-restricciones-únicas)
11. [Deuda técnica conocida](#deuda-técnica-conocida)

---

## Modelo conceptual

```
Lider ──< Cartera ──< Titular_Cartera >── Titular ──< [fuentes de datos externas]
                                              │
                                          Codeudor
                                          Empresa (referencia por NitEmpresa)
```

- **`Titular`** es la entidad central del sistema. Representa al deudor o cliente en gestión de cobro.
- **`Cartera`** agrupa titulares bajo una cartera de cobro específica, gestionada por un `Lider`.
- **`Titular_Cartera`** es la tabla de relación N:M entre titulares y carteras. Almacena atributos de gestión: saldo, perfil, rotación y último número marcado.
- Las fuentes de datos externas (Fosyga, Tigo, Cajacopi, etc.) enriquecen la información de contacto y situación laboral de cada titular.
- Las tablas con sufijo **`Auxiliar`** son staging: reciben datos crudos sin validar para luego ser procesados e insertados a las tablas reales mediante Procedimientos Almacenados.
- **`PORTAFOLIO`** y **`BaseGeneral`** pertenecen a proyectos en proceso de integración vía SSIS y no tienen relaciones formales con el resto del esquema.

---

## Tablas principales

### `Titular`
Entidad central del sistema. Representa al deudor o cliente bajo gestión de cobro.

| Columna | Tipo | Nulo | Restricción | Descripción |
|---------|------|------|-------------|-------------|
| `NumeroDocumento` | `bigint` | NO | **Clave primaria** | Número de documento de identidad. Clave de negocio y clave primaria clustered |
| `Nombre` | `varchar(70)` | NO | — | Nombre completo del titular |
| `NitEmpresa` | `bigint` | YES | — | NIT de la empresa empleadora. Referencia lógica a `Empresa.NIT` sin FK declarada |
| `FechaNacimiento` | `date` | YES | — | |
| `FechaExpedicionDocumento` | `date` | YES | — | Fecha de expedición del documento de identidad |

**Restricciones:**
- `PK_Titular` (PRIMARY KEY CLUSTERED) → `NumeroDocumento`

---

### `Cartera`
Carteras de cobro. Cada cartera agrupa un conjunto de titulares bajo la responsabilidad de un líder.

| Columna | Tipo | Nulo | Restricción | Descripción |
|---------|------|------|-------------|-------------|
| `ID` | `int` | NO | **Clave primaria** | |
| `NombreCartera` | `varchar(50)` | NO | — | Nombre identificador de la cartera |
| `LiderID` | `int` | YES | **FK** → `Lider.ID` | Líder responsable de la cartera |

**Restricciones:**
- `PK_Cartera` → `ID`
- `NombreCartera_LiderID_Unique` (UNIQUE NONCLUSTERED) → `(NombreCartera, LiderID)`

---

### `Titular_Cartera`
Tabla de relación N:M entre `Titular` y `Cartera`. Almacena el estado de gestión de cobro de cada titular dentro de una cartera específica.

| Columna | Tipo | Nulo | Restricción | Descripción |
|---------|------|------|-------------|-------------|
| `TitularID` | `bigint` | NO | **PK / FK** → `Titular.NumeroDocumento` | |
| `CarteraID` | `int` | NO | **PK / FK** → `Cartera.ID` | |
| `MejorPerfil` | `varchar(50)` | YES | — | Mejor tipificación de gestión registrada históricamente |
| `UltimoPerfil` | `varchar(50)` | YES | — | Tipificación de la gestión más reciente |
| `UltimaFechaGestion` | `date` | YES | — | Fecha de la última gestión de cobro realizada |
| `UltimoNumeroMarcado` | `varchar(10)` | YES | — | Último número telefónico contactado en el discador |
| `Rotacion` | `int` | YES | — | Número de veces que el titular ha rotado dentro de la cartera |
| `Saldo` | `money` | YES | — | Saldo de la deuda en cartera |

**Restricciones:**
- `PK_Titular_Cartera` (PRIMARY KEY CLUSTERED) → `(TitularID, CarteraID)`

---

### `Lider`
Líderes o supervisores responsables de carteras de cobro.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `ID` | `int` | NO | **Clave primaria** |
| `Nombre` | `varchar(50)` | NO | Nombre completo del líder |
| `Email` | `varchar(100)` | YES | Correo electrónico institucional |

---

### `Empresa`
Empresas empleadoras referenciadas desde `Titular.NitEmpresa`.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `NIT` | `bigint` | NO | **Clave primaria** |
| `Nombre` | `varchar(255)` | NO | Razón social de la empresa |

---

### `Codeudor`
Codeudores vinculados a un titular. Comparten responsabilidad sobre la deuda.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `NumeroDocumento` | `bigint` | NO | **Clave primaria** |
| `Nombre` | `varchar(70)` | YES | Nombre completo del codeudor |
| `FechaNacimiento` | `date` | YES | |
| `FechaExpedicionDocumento` | `date` | YES | |
| `TitularRelacionado` | `bigint` | YES | **FK** → `Titular.NumeroDocumento`. Titular al que está vinculado |

---

## Tablas de datos externos por fuente

Cada tabla almacena el resultado de consultar una fuente de datos externa para enriquecer la información de contacto o situación laboral del titular. Todas referencian a `Titular.NumeroDocumento` a través de la columna `TitularID`.

El campo `EstadoEn[Fuente]` indica si el titular fue **encontrado** (`1`) o **no encontrado** (`0`) en esa fuente durante la última consulta.

---

### `Fosyga`
Estado de afiliación al sistema de salud (BDUA / Fosyga).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `Estado` | `varchar(30)` | YES | Estado de afiliación en el sistema de salud |
| `EPS` | `varchar(120)` | YES | Entidad Promotora de Salud a la que está afiliado |
| `Regimen` | `varchar(30)` | YES | Régimen de afiliación (contributivo, subsidiado, etc.) |
| `FechaAfiliacionEfectiva` | `date` | YES | Fecha desde la cual está vigente la afiliación |
| `FechaFinalizacionAfiliacion` | `date` | YES | Fecha en que finalizó o finalizará la afiliación |
| `TipoAfiliado` | `varchar(50)` | YES | Tipo de afiliado (cotizante, beneficiario, etc.) |
| `Departamento` | `varchar(70)` | YES | Departamento de residencia registrado en Fosyga |
| `Municipio` | `varchar(50)` | YES | Municipio de residencia registrado en Fosyga |
| `EstadoEnFosyga` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | Fecha en que se realizó la consulta a la fuente |

---

### `Compensados`
Estado de compensación familiar (afiliación a caja de compensación).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `EPS` | `varchar(50)` | YES | EPS vinculada al periodo compensado |
| `UltimoPeriodoCompensado` | `date` | YES | Último periodo en que se registró compensación |
| `TipoAfiliado` | `varchar(50)` | YES | Tipo de afiliado en la caja de compensación |
| `Estado` | `int` | YES | Estado numérico de la afiliación |
| `FechaConsulta` | `date` | NO | Fecha en que se realizó la consulta a la fuente |

---

### `Cajacopi`
Datos de contacto y ubicación obtenidos desde Cajacopi (caja de compensación familiar).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroTelefonoCelular` | `varchar(10)` | YES | Número de teléfono celular |
| `NumeroTelefonoFijo` | `varchar(10)` | YES | Número de teléfono fijo |
| `Email` | `varchar(100)` | YES | Correo electrónico |
| `Departamento` | `varchar(50)` | YES | Departamento de residencia |
| `Municipio` | `varchar(50)` | YES | Municipio de residencia |
| `FechaNacimiento` | `date` | YES | Fecha de nacimiento registrada en Cajacopi |
| `EstadoEnCajacopi` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | `DEFAULT getdate()` |

---

### `Camara`
Datos de contacto obtenidos desde Cámara de Comercio para personas naturales.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroTelefono` | `varchar(10)` | YES | Número telefónico registrado en Cámara |
| `Email` | `varchar(255)` | YES | Correo electrónico |
| `Municipio` | `varchar(50)` | YES | |
| `Departamento` | `varchar(70)` | YES | |
| `EstadoEnCamara` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `CamaraEmpresa`
Datos de contacto obtenidos desde Cámara de Comercio para empresas vinculadas al titular.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `NitEmpresa` | `bigint` | YES | NIT de la empresa consultada |
| `NumeroTelefono` | `varchar(10)` | YES | Teléfono de la empresa registrado en Cámara |
| `Email` | `varchar(255)` | YES | Correo de la empresa |
| `EstadoEnCamara` | `bit` | NO | `1` si fue encontrada, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `Positiva`
Datos de contacto y ubicación obtenidos desde ARL Positiva para personas.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroTelefonoCelular` | `varchar(10)` | YES | |
| `NumeroTelefonoFijo` | `varchar(30)` | YES | |
| `Email` | `varchar(100)` | YES | |
| `FechaNacimiento` | `date` | YES | Fecha de nacimiento registrada en Positiva |
| `Departamento` | `varchar(50)` | YES | |
| `Municipio` | `varchar(50)` | YES | |
| `EstadoEnPositiva` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `PositivaEmpresas`
Empresa vinculada al titular según el registro de ARL Positiva.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NIT` | `bigint` | YES | NIT de la empresa empleadora registrada en Positiva |
| `Empresa` | `varchar(230)` | YES | Razón social de la empresa |
| `EstadoAfiliacion` | `varchar(30)` | YES | Estado de la afiliación a ARL |
| `EstadoEnPositivaEmpresa` | `bit` | NO | `1` si fue encontrada, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `ARLSura`
Empresa empleadora del titular según ARL Sura.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NIT` | `bigint` | YES | NIT de la empresa empleadora registrada en Sura |
| `Empresa` | `varchar(100)` | YES | Razón social de la empresa |
| `EstadoEnSura` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `SaludTotal`
Datos de contacto, laborales y de afiliación obtenidos desde EPS Salud Total.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroTelefono` | `varchar(10)` | YES | |
| `Email` | `varchar(255)` | YES | |
| `FechaNacimiento` | `date` | YES | |
| `EstadoAfiliacion` | `varchar(30)` | YES | Estado de afiliación a la EPS |
| `TipoAfiliado` | `varchar(50)` | YES | Cotizante, beneficiario, etc. |
| `Ciudad` | `varchar(50)` | YES | Ciudad de residencia registrada en Salud Total |
| `NIT` | `bigint` | YES | NIT de la empresa empleadora registrada en Salud Total |
| `Empresa` | `varchar(100)` | YES | Razón social de la empresa empleadora |
| `Cargo` | `varchar(60)` | YES | Cargo del titular en la empresa |
| `UltimaFechaPago` | `date` | YES | Último periodo con pago registrado a la EPS |
| `IngresoBase` | `varchar(20)` | YES | Ingreso base de cotización |
| `EstadoEnSaludTotal` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `Tigo`
Datos de contacto obtenidos desde Tigo (operador de telecomunicaciones).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroTelefono` | `varchar(10)` | YES | |
| `Email` | `varchar(255)` | YES | |
| `EstadoEnTigo` | `bit` | NO | `DEFAULT 0`. `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | `DEFAULT getdate()` |

---

### `DatosClaro`
Datos de contacto obtenidos desde Claro (operador de telecomunicaciones).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | Referencia lógica a `Titular.NumeroDocumento` (sin FK declarada) |
| `NumeroTelefono` | `varchar(10)` | YES | |
| `Email` | `varchar(100)` | YES | |
| `FechaInsercion` | `date` | YES | |

---

### `DatosExternos`
Datos de contacto provenientes de fuentes externas genéricas no clasificadas.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | YES | Referencia lógica a `Titular.NumeroDocumento` (sin FK declarada) |
| `NumeroTelefono` | `varchar(10)` | YES | |
| `Email` | `varchar(100)` | YES | |
| `Fecha` | `date` | YES | Fecha de obtención del dato |

---

### `RGM`
Vehículos registrados a nombre del titular según el Registro General de Matrícula (RGM).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `Marca` | `varchar(50)` | YES | |
| `Modelo` | `int` | YES | Año modelo del vehículo |
| `Placa` | `varchar(50)` | YES | |
| `EstadoEnRGM` | `bit` | YES | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `Runt`
Información vehicular y de licencias de conducción del titular desde el Registro Único Nacional de Tránsito (RUNT).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | Referencia lógica a `Titular.NumeroDocumento` (sin FK declarada) |
| `Placa` | `varchar(50)` | YES | |
| `NumeroLicencia` | `bigint` | YES | Número de licencia de conducción |
| `TipoVehiculo` | `varchar(50)` | YES | |
| `Marca` | `varchar(50)` | YES | |
| `Modelo` | `bigint` | YES | Año modelo del vehículo |
| `NumeroChasis` | `varchar(50)` | YES | |
| `EstadoSoat` | `varchar(50)` | YES | Estado vigencia del SOAT |
| `EntidadSoat` | `varchar(50)` | YES | Aseguradora que emitió el SOAT |
| `EstadoTecnicomecanica` | `varchar(50)` | YES | Estado de la revisión técnico-mecánica |
| `EstadoRunt` | `bit` | YES | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `datetime` | NO | |

---

### `RUES`
Actividad empresarial del titular como comerciante según el Registro Único Empresarial y Social (RUES).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NumeroMatricula` | `bigint` | YES | Número de matrícula mercantil |
| `EstadoEmpresa` | `varchar(20)` | YES | Estado del registro (activo, cancelado, etc.) |
| `ActividadEconomica` | `varchar(250)` | YES | Descripción del objeto social o actividad económica |
| `NombreEmpresa` | `varchar(70)` | YES | Nombre o razón social registrada |
| `EstadoEnRUES` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `datetime` | NO | |

---

### `SIMIT`
Multas e infracciones de tránsito asociadas a un vehículo del titular, según el Sistema Integrado de Multas e Infracciones de Tránsito (SIMIT).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `Placa` | `varchar(10)` | YES | Placa del vehículo con infracciones. Un titular puede tener múltiples registros (uno por placa) |
| `EstadoEnSIMIT` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

### `SOS`
Empresa empleadora del titular según el Sistema Obligatorio de Salud (SOS).

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | **FK** → `Titular.NumeroDocumento` |
| `NitEmpresa` | `bigint` | YES | NIT de la empresa empleadora |
| `RazonSocial` | `varchar(100)` | YES | Razón social de la empresa |
| `Telefono` | `varchar(10)` | YES | Teléfono de la empresa |
| `Direccion` | `varchar(70)` | YES | Dirección de la empresa |
| `EstadoEnSOS` | `bit` | NO | `1` si fue encontrado, `0` si no |
| `FechaConsulta` | `date` | NO | |

---

## Tablas auxiliares de carga (staging)

Las tablas con sufijo `Auxiliar` son tablas de staging. Reciben los datos crudos tal como llegan del proceso de consulta a fuentes externas, sin validaciones ni restricciones de integridad referencial. Un Procedimiento Almacenado se encarga de validarlos e insertarlos o actualizarlos en la tabla real correspondiente.

**Diferencias sistemáticas respecto a sus tablas reales:**

| Característica | Tabla real | Tabla auxiliar |
|---|---|---|
| Nulabilidad | Columnas clave NOT NULL | Todos los campos admiten NULL |
| Restricciones | FK declaradas | Sin FK ni PK |
| Columna `Nombre` | No existe (se obtiene por FK) | Presente — nombre del titular en texto plano para facilitar el SP |
| Columna `CarteraID` | No existe en la mayoría | Presente — indica la cartera de origen del proceso de carga |
| Columna `FechaInsercion` | No existe en la mayoría | Presente en varias — timestamp de llegada del registro al staging |
| Tipo de fechas | `date` | `datetime` |

---

### `TitularAuxiliar`
Staging para la carga masiva de nuevos titulares.

| Columna | Tipo | Nulo | Nota |
|---------|------|------|------|
| `NumeroDocumento` | `varchar(50)` | YES | Tipo `varchar` a diferencia de `bigint` en la tabla real — permite recibir formatos variables antes de normalizar |
| `Nombre` | `varchar(70)` | YES | |
| `CarteraID` | `int` | YES | Cartera a la que se asignará el titular |
| `NitEmpresa` | `bigint` | YES | |
| `FechaNacimiento` | `date` | YES | |
| `FechaExpedicionDocumento` | `datetime` | YES | |

---

### `Titular_CarteraAuxiliar`
Staging para la creación de relaciones titular-cartera.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `CarteraID` | `int` | YES |

> Contiene solo las claves de relación, sin los atributos de gestión que tiene `Titular_Cartera`.

---

### `ARLSuraAuxiliar`
Staging para datos de ARL Sura.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NIT` | `bigint` | YES |
| `Empresa` | `varchar(100)` | YES |
| `EstadoEnSura` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(80)` | YES |
| `CarteraID` | `int` | YES |

---

### `CajacopiAuxiliar`
Staging para datos de Cajacopi.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefonoCelular` | `varchar(10)` | YES |
| `NumeroTelefonoFijo` | `varchar(10)` | YES |
| `Email` | `varchar(100)` | YES |
| `Departamento` | `varchar(50)` | YES |
| `Municipio` | `varchar(50)` | YES |
| `FechaNacimiento` | `datetime` | YES |
| `EstadoEnCajacopi` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(80)` | YES |
| `CarteraID` | `int` | YES |

---

### `CamaraAuxiliar`
Staging para datos de Cámara de Comercio (personas).

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |
| `Email` | `varchar(255)` | YES |
| `Municipio` | `varchar(50)` | YES |
| `Departamento` | `varchar(70)` | YES |
| `EstadoEnCamara` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(80)` | YES |
| `CarteraID` | `int` | YES |

---

### `CamaraEmpresaAuxiliar`
Staging para datos de Cámara de Comercio (empresas).

| Columna | Tipo | Nulo | Diferencia con tabla real |
|---------|------|------|--------------------------|
| `NitEmpresa` | `bigint` | YES | |
| `NumeroTelefono` | `varchar(10)` | YES | |
| `Email` | `varchar(255)` | YES | |
| `Departamento` | `varchar(60)` | YES | Columna extra no presente en la tabla real |
| `EstadoEnCamara` | `bit` | YES | |
| `FechaConsulta` | `datetime` | YES | |
| `NombreEmpresa` | `varchar(120)` | YES | Columna extra no presente en la tabla real |

---

### `CompensadosAuxiliar`
Staging para datos de compensación familiar.

| Columna | Tipo | Nulo | Diferencia con tabla real |
|---------|------|------|--------------------------|
| `TitularID` | `bigint` | YES | |
| `EPS` | `varchar(50)` | YES | |
| `UltimoPeriodoCompensado` | `varchar(50)` | YES | `varchar` en lugar de `date` — admite formatos de texto variables desde la fuente |
| `TipoAfiliado` | `varchar(50)` | YES | |
| `Estado` | `int` | YES | |
| `FechaConsulta` | `datetime` | YES | |
| `Nombre` | `varchar(50)` | YES | |
| `CarteraID` | `int` | YES | |
| `FechaInsercion` | `datetime` | YES | |

---

### `DatosClaroAuxiliar`
Staging para datos de Claro.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |
| `Email` | `varchar(150)` | YES |
| `FechaInsercion` | `date` | YES |

---

### `DatosExternosAuxiliar`
Staging para datos de fuentes externas genéricas.

| Columna | Tipo | Nulo | Nota |
|---------|------|------|------|
| `TitularID` | `bigint` | YES | |
| `NumeroTelefono` | `varchar(MAX)` | YES | Tamaño irrestricto para admitir formatos variables |
| `Email` | `varchar(MAX)` | YES | |

---

### `FosygaAuxiliar`
Staging para datos de Fosyga.

| Columna | Tipo | Nulo | Diferencia con tabla real |
|---------|------|------|--------------------------|
| `TitularID` | `bigint` | YES | |
| `Estado` | `varchar(30)` | YES | |
| `EPS` | `varchar(200)` | YES | Más ancho que en la tabla real (120) |
| `Regimen` | `varchar(30)` | YES | |
| `FechaAfiliacionEfectiva` | `date` | YES | |
| `FechaFinalizacionAfiliacion` | `date` | YES | |
| `TipoAfiliado` | `varchar(50)` | YES | |
| `Departamento` | `varchar(70)` | YES | |
| `Municipio` | `varchar(50)` | YES | |
| `EstadoEnFosyga` | `bit` | YES | |
| `FechaConsulta` | `datetime` | YES | |
| `Nombre` | `varchar(60)` | YES | |
| `CarteraID` | `int` | YES | |
| `FechaInsercion` | `datetime` | YES | `DEFAULT getdate()` |

---

### `PositivaAuxiliar`
Staging para datos de ARL Positiva (personas).

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefonoCelular` | `varchar(10)` | YES |
| `NumeroTelefonoFijo` | `varchar(30)` | YES |
| `Email` | `varchar(100)` | YES |
| `FechaNacimiento` | `datetime` | YES |
| `Departamento` | `varchar(50)` | YES |
| `Municipio` | `varchar(50)` | YES |
| `EstadoEnPositiva` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(70)` | YES |
| `CarteraID` | `int` | YES |

---

### `PositivaEmpresasAuxiliar`
Staging para datos de empresas en ARL Positiva.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NIT` | `bigint` | YES |
| `Empresa` | `varchar(230)` | YES |
| `EstadoAfiliacion` | `varchar(30)` | YES |
| `EstadoEnPositivaEmpresa` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `FechaInsercion` | `datetime` | YES |
| `Nombre` | `varchar(70)` | YES |
| `CarteraID` | `int` | YES |

---

### `RGMAuxiliar`
Staging para datos del Registro General de Matrícula vehicular.

| Columna | Tipo | Nulo | Diferencia con tabla real |
|---------|------|------|--------------------------|
| `TitularID` | `bigint` | YES | |
| `Marca` | `varchar(50)` | YES | |
| `Modelo` | `varchar(50)` | YES | `varchar` en lugar de `int` — el SP convierte al insertar |
| `Placa` | `varchar(6)` | YES | Limitado a 6 caracteres (longitud estándar de placa) |
| `EstadoEnRGM` | `bit` | YES | |
| `Nombre` | `varchar(70)` | YES | |
| `CarteraID` | `int` | YES | |
| `FechaConsulta` | `datetime` | YES | |
| `FechaInsercion` | `datetime` | YES | `DEFAULT getdate()` |

---

### `RUESAuxiliar`
Staging para datos del Registro Único Empresarial y Social.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroMatricula` | `bigint` | YES |
| `EstadoEmpresa` | `varchar(20)` | YES |
| `ActividadEconomica` | `varchar(250)` | YES |
| `NombreEmpresa` | `varchar(70)` | YES |
| `EstadoEnRUES` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(70)` | YES |
| `CarteraID` | `int` | YES |

---

### `RuntAuxiliar`
Staging para datos del Registro Único Nacional de Tránsito.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `Placa` | `varchar(50)` | YES |
| `NumeroLicencia` | `bigint` | YES |
| `TipoVehiculo` | `varchar(50)` | YES |
| `Marca` | `varchar(50)` | YES |
| `Modelo` | `bigint` | YES |
| `NumeroChasis` | `varchar(50)` | YES |
| `EstadoSoat` | `varchar(50)` | YES |
| `EntidadSoat` | `varchar(50)` | YES |
| `EstadoTecnicomecanica` | `varchar(50)` | YES |
| `EstadoRunt` | `bit` | YES |
| `Nombre` | `varchar(50)` | YES |
| `CarteraID` | `int` | YES |
| `FechaConsulta` | `datetime` | YES |
| `FechaInsercion` | `datetime` | YES |

---

### `SaludTotalAuxiliar`
Staging para datos de EPS Salud Total.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |
| `Email` | `varchar(255)` | YES |
| `FechaNacimiento` | `date` | YES |
| `EstadoAfiliacion` | `varchar(30)` | YES |
| `TipoAfiliado` | `varchar(50)` | YES |
| `Ciudad` | `varchar(50)` | YES |
| `NIT` | `bigint` | YES |
| `Empresa` | `varchar(100)` | YES |
| `Cargo` | `varchar(60)` | YES |
| `UltimaFechaPago` | `datetime` | YES |
| `IngresoBase` | `varchar(20)` | YES |
| `EstadoEnSaludTotal` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(70)` | YES |
| `CarteraID` | `int` | YES |

---

### `SIMITAuxiliar`
Staging para datos del SIMIT. A diferencia de la tabla real (una fila por placa), aquí se reciben hasta 15 placas en columnas separadas tal como devuelve la fuente. El Procedimiento Almacenado normaliza esto insertando una fila por placa en `SIMIT`.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | NO |
| `Placa1` – `Placa14` | `varchar(10)` | YES |
| `Placa15` | `nchar(10)` | YES |
| `EstadoEnSIMIT` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |
| `Nombre` | `varchar(50)` | YES |
| `CarteraID` | `int` | YES |

---

### `SOSAuxiliar`
Staging para datos del Sistema Obligatorio de Salud (SOS).

| Columna | Tipo | Nulo | Diferencia con tabla real |
|---------|------|------|--------------------------|
| `TitularID` | `bigint` | NO | |
| `NitEmpresa` | `bigint` | YES | |
| `RazonSocial` | `varchar(100)` | YES | |
| `Telefono` | `varchar(10)` | YES | |
| `Dirección` | `varchar(150)` | YES | Más ancho (150 vs 70) |
| `EstadoEnSos` | `bit` | YES | |
| `FechaConsulta` | `datetime` | YES | |
| `Nombre` | `varchar(80)` | YES | |
| `CarteraID` | `int` | YES | |

---

### `TigoAuxiliar`
Staging para datos de Tigo.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |
| `Email` | `varchar(255)` | YES |
| `EstadoEnTigo` | `bit` | YES |
| `FechaConsulta` | `datetime` | YES |

---

### `ListaNegraTelefonosAuxiliar`
Staging para la carga de teléfonos a lista negra.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `bigint` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |
| `Cartera` | `varchar(50)` | YES |
| `Codigo` | `varchar(20)` | YES |

---

### `Staging_ListaNegraTelefonos`
Staging alternativo para lista negra de teléfonos. Recibe los datos directamente desde el discador con la tipificación original antes de mapear al código estándar.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `nvarchar(MAX)` | YES | |
| `UltimoNumeroMarcado` | `nvarchar(MAX)` | YES | Número tal como lo registra el discador |
| `Tipificacion` | `nvarchar(MAX)` | YES | Código de tipificación original del discador |
| `Cartera` | `nvarchar(MAX)` | YES | |

---

## Tablas de comunicaciones y auditoría de envíos

### `AuditoriaCorreos`
Registro histórico de correos electrónicos enviados a titulares. Permite controlar la frecuencia de envío y evitar saturación.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | |
| `Email` | `varchar(100)` | NO | Dirección a la que se envió |
| `NombreCartera` | `varchar(50)` | NO | Cartera desde la que se originó el envío |
| `FechaEnvio` | `date` | NO | |
| `EnviadoPorSemana` | `int` | YES | Número de envíos realizados al titular en la semana |
| `ConsultaTigo` | `bit` | YES | Indica si el email provino de la fuente Tigo |
| `ConsultaCamara` | `bit` | YES | Indica si el email provino de Cámara de Comercio |
| `ConsultaSaludTotal` | `bit` | YES | Indica si el email provino de Salud Total |

---

### `AuditoriaIVR`
Registro histórico de envíos IVR (mensajes de voz pregrabados) a titulares.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | |
| `NumeroTelefono` | `varchar(10)` | NO | Número al que se envió el IVR |
| `NombreCartera` | `varchar(50)` | NO | Cartera desde la que se originó el envío |
| `FechaEnvio` | `date` | NO | |
| `EnviadoPorSemana` | `int` | YES | Número de IVR enviados al titular en la semana |
| `ConsultaTigo` | `bit` | YES | Indica si el número provino de Tigo |
| `ConsultaCamara` | `bit` | YES | Indica si el número provino de Cámara de Comercio |
| `ConsultaSaludTotal` | `bit` | YES | Indica si el número provino de Salud Total |

---

### `AuditoriaMensajeria`
Registro histórico de mensajes de texto (SMS) enviados a titulares.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | |
| `NumeroTelefono` | `varchar(10)` | NO | Número al que se envió el SMS |
| `NombreCartera` | `varchar(50)` | NO | Cartera desde la que se originó el envío |
| `FechaEnvio` | `datetime` | NO | |
| `EnviadoPorSemana` | `int` | YES | Número de SMS enviados al titular en la semana |
| `ConsultaTigo` | `bit` | YES | Indica si el número provino de Tigo |
| `ConsultaCamara` | `bit` | YES | Indica si el número provino de Cámara de Comercio |
| `ConsultaSaludTotal` | `bit` | YES | Indica si el número provino de Salud Total |

---

### `EmailEnviados`
Control de deduplicación de correos despachados en el proceso de envío actual.

| Columna | Tipo | Nulo |
|---------|------|------|
| `Email` | `varchar(150)` | YES |

---

### `IVREnviados`
Control de deduplicación de teléfonos a los que ya se envió IVR en el proceso actual.

| Columna | Tipo | Nulo |
|---------|------|------|
| `NumeroTelefono` | `varchar(10)` | YES |

---

### `MensajesEnviados`
Control de deduplicación de SMS despachados en el proceso de envío actual.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `varchar(150)` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |

---

### `ListaNegraCorreos`
Correos electrónicos inválidos o con rebote permanente. Son excluidos de todos los envíos futuros.

| Columna | Tipo | Nulo | Restricción |
|---------|------|------|-------------|
| `CorreoErroneo` | `varchar(60)` | NO | **Clave primaria** |

---

### `ListaNegraTelefonos`
Teléfonos que no deben ser contactados, bien sea por solicitud del titular o por tipificaciones de no contacto registradas en el discador.

| Columna | Tipo | Nulo | Descripción |
|---------|------|------|-------------|
| `TitularID` | `bigint` | NO | |
| `NumeroTelefono` | `varchar(10)` | NO | |
| `Cartera` | `varchar(50)` | NO | Cartera en la que se registró la restricción |
| `Codigo` | `varchar(20)` | NO | Código de tipificación que originó el bloqueo |

**Restricción:** `PK_ListaNegraTelefonos` → `(TitularID, NumeroTelefono, Cartera, Codigo)`

---

## Tablas de referencia y catálogos

### `PrefijoMunicipio`
Catálogo de prefijos telefónicos fijos por municipio. Usado para normalizar y completar números de teléfono fijo.

| Columna | Tipo | Nulo |
|---------|------|------|
| `Departamento` | `varchar(50)` | NO |
| `Municipio` | `varchar(50)` | NO |
| `Prefijo` | `varchar(3)` | NO |

---

### `PrefijosDepartamento`
Catálogo de prefijos telefónicos por departamento.

| Columna | Tipo | Nulo |
|---------|------|------|
| `Departamento` | `varchar(60)` | NO |
| `Prefijo` | `int` | NO |

---

### `EmailGeneral`
Correos electrónicos recolectados de fuentes que no pudieron vincularse directamente a un titular confirmado. Se usan en procesos de limpieza y cruce posterior.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `varchar(150)` | YES |
| `Correo` | `varchar(100)` | YES |

---

### `NumeroTelefonoGeneral`
Teléfonos recolectados de fuentes que no pudieron vincularse directamente a un titular confirmado.

| Columna | Tipo | Nulo |
|---------|------|------|
| `TitularID` | `varchar(50)` | YES |
| `NumeroTelefono` | `varchar(10)` | YES |

---

## Tablas de proyectos externos en integración

Estas tablas pertenecen a proyectos que están pendientes de integración vía SSIS con el aplicativo de telecomunicaciones. Mientras se obtiene acceso a dicho aplicativo, son alimentadas manualmente. No tienen claves primarias ni relaciones formales con el resto del esquema.

---

### `PORTAFOLIO`
Tabla de importación plana que recibe el portafolio de cartera tal como lo entrega el cliente (típicamente desde Excel). Contiene datos sin normalizar de deudas, contactos, referencias familiares y gestiones previas. Sirve como insumo para poblar `TitularAuxiliar` y `Titular_CarteraAuxiliar`.

70 columnas, todas `nvarchar(255)` o `float`, sin restricciones. Las columnas más relevantes:

| # | Columna | Tipo | Descripción |
|---|---------|------|-------------|
| 2 | `Nit / Cedula` | `float` | Número de documento del deudor |
| 3 | `nombre_cliente` | `nvarchar(255)` | |
| 9–12 | `Teléfono 1/2 SAC`, `Celular 1/2 SAC` | `float` | Teléfonos del sistema del cliente |
| 17–22 | `Teléfono/Celular 1/2 Reconocer` | `float` | Teléfonos de la firma de cobranza |
| 26 | `Ingresos` | `float` | |
| 27 | `dias_de_mora` | `float` | |
| 30 | `Capital` | `float` | Valor del capital adeudado |
| 41 | `Vlr_Saldo total` | `float` | Saldo total de la deuda |
| 51–56 | Referencias familiares (nombre, parentesco, teléfono) | mixto | |
| 61 | `Score cobranza` | `nvarchar(255)` | |
| 65–66 | `fecha_promesa1`, `fecha_promesa2` | mixto | Fechas de promesas de pago anteriores |
| 67–68 | Códigos de gestión (positivo últimos 3 meses, último código) | `nvarchar(255)` | |

---

### `BaseGeneral`
Tabla de trabajo que contiene documentos y nombres de titulares con su cartera asignada. Usada como base de consulta para procesos que aún no tienen acceso directo a `Titular`.

| Columna | Tipo | Nulo |
|---------|------|------|
| `NumeroDocumento` | `bigint` | YES |
| `Nombre` | `varchar(70)` | YES |
| `Cartera` | `varchar(50)` | YES |

---

## Vistas

### Vistas de consolidación (base madre)

Consolidan en una sola fila por titular los datos más recientes de todas las fuentes externas, usando `ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC)` para seleccionar el último registro por fuente.

| Vista | Fuente base | Descripción |
|-------|-------------|-------------|
| `vw_BaseMadreTitularAuxiliar` | `TitularAuxiliar` | Consolida todas las fuentes para los titulares en staging. Útil durante procesos de carga masiva. |
| `vw_BaseMadreBaseGeneral` | `BaseGeneral` | Consolida todas las fuentes para el universo de `BaseGeneral`. Incluye cálculo de edad desde Registraduría, Cajacopi y Positiva. |

---

### Vistas de titulares encontrados en fuente (`vw_Datos*`)

Devuelven los titulares que **fueron consultados y encontrados** en cada fuente externa, con los datos del registro más reciente.

| Vista | Fuente |
|-------|--------|
| `vw_DatosARLSura` | `ARLSura` |
| `vw_DatosCajacopi` | `Cajacopi` |
| `vw_DatosCamara` | `Camara` |
| `vw_DatosCamaraEmpresa` | `CamaraEmpresa` |
| `vw_DatosClaro` | `DatosClaro` |
| `vw_DatosCompensados` | `Compensados` |
| `vw_DatosFosyga` | `Fosyga` |
| `vw_DatosPositiva` | `Positiva` |
| `vw_DatosPositivaEmpresas` | `PositivaEmpresas` |
| `vw_DatosRGM` | `RGM` |
| `vw_DatosRUES` | `RUES` |
| `vw_DatosSaludTotal` | `SaludTotal` |
| `vw_DatosSIMIT` | `SIMIT` |
| `vw_DatosSOS` | `SOS` |
| `vw_DatosTigo` | `Tigo` |

---

### Vistas de titulares consultados y no encontrados en fuente (`vw_SinDatos*`)

Devuelven los titulares que **ya fueron consultados en la fuente pero no se encontró información** (`EstadoEn[Fuente] = 0`). Sirven para análisis de cobertura y para identificar qué segmento del universo no tiene datos en cada fuente.

| Vista | Fuente |
|-------|--------|
| `vw_SinDatosARLSura` | `ARLSura` |
| `vw_SinDatosCajacopi` | `Cajacopi` |
| `vw_SinDatosCamara` | `Camara` |
| `vw_SinDatosCompensados` | `Compensados` |
| `vw_SinDatosFosyga` | `Fosyga` |
| `vw_SinDatosPositiva` | `Positiva` |
| `vw_SinDatosPositivaEmpresas` | `PositivaEmpresas` |
| `vw_SinDatosRGM` | `RGM` |
| `vw_SinDatosRUES` | `RUES` |
| `vw_SinDatosSaludTotal` | `SaludTotal` |
| `vw_SinDatosSIMIT` | `SIMIT` |
| `vw_SinDatosSOS` | `SOS` |
| `vw_SinDatosTigo` | `Tigo` |

---

### Otras vistas

| Vista | Descripción |
|-------|-------------|
| `vw_EnviosFrecuentesSemana` | Titulares con alta frecuencia de envíos en la semana en curso. Usada para controlar la saturación de comunicaciones y aplicar restricciones de envío. |

---

## Relaciones entre tablas (Foreign Keys)

| Nombre de la restricción | Tabla hija | Columna | Tabla padre | Columna referenciada |
|---|---|---|---|---|
| `FK_Titular_ArlSura` | `ARLSura` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Cajacopi` | `Cajacopi` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Camara` | `Camara` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Compensados_Titular` | `Compensados` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Fosyga` | `Fosyga` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Positiva` | `Positiva` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_PositivaEmpresas_Titular` | `PositivaEmpresas` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_RGM` | `RGM` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_RUES` | `RUES` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_SaludTotal` | `SaludTotal` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_SIMIT` | `SIMIT` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_SOS_Titular` | `SOS` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Tigo` | `Tigo` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Titular_Codeudor` | `Codeudor` | `TitularRelacionado` | `Titular` | `NumeroDocumento` |
| `FK_Lider_Cartera` | `Cartera` | `LiderID` | `Lider` | `ID` |
| `FK_Titular_Intermedio` | `Titular_Cartera` | `TitularID` | `Titular` | `NumeroDocumento` |
| `FK_Cartera_Intermedio` | `Titular_Cartera` | `CarteraID` | `Cartera` | `ID` |

**Tablas sin FK declarada** que usan `TitularID` como referencia lógica:
`DatosClaro`, `DatosExternos`, `Runt`, `CamaraEmpresa`, y todas las tablas `*Auxiliar`.

---

## Índices y restricciones únicas

| Tabla | Nombre | Tipo | Columnas |
|-------|--------|------|---------|
| `Titular` | `PK_Titular` | PRIMARY KEY CLUSTERED | `NumeroDocumento` |
| `Cartera` | `PK_Cartera` | PRIMARY KEY CLUSTERED | `ID` |
| `Cartera` | `NombreCartera_LiderID_Unique` | UNIQUE NONCLUSTERED | `(NombreCartera, LiderID)` |
| `Titular_Cartera` | `PK_Titular_Cartera` | PRIMARY KEY CLUSTERED | `(TitularID, CarteraID)` |
| `Lider` | `PK_Lider` | PRIMARY KEY CLUSTERED | `ID` |
| `Empresa` | `PK_Empresa` | PRIMARY KEY CLUSTERED | `NIT` |
| `Codeudor` | `PK_Codeudor` | PRIMARY KEY CLUSTERED | `NumeroDocumento` |
| `ListaNegraCorreos` | `PK_ListaNegraCorreos` | PRIMARY KEY CLUSTERED | `CorreoErroneo` |

---

## Deuda técnica conocida

No hay deuda técnica estructural pendiente en este momento.
