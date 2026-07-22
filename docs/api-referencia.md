# Referencia de API — Zentinela

**Base URL:** `http://localhost:5153/api`
**Formato:** JSON
**Autenticación:** Bearer Token (JWT)

Los endpoints marcados como protegidos requieren el header:
```
Authorization: Bearer <token>
```

---

## POST /api/registro

Registra un nuevo usuario a partir de su cédula y usuario ControlNext. No requiere autenticación previa.

**Autenticación requerida:** No

### Flujo interno

1. Valida la cédula en `DBRecursosHumanos.Empleado`
2. Deriva el rol desde `Empleado.CargoID` (mapa `CargoRoles`)
3. Crea `Agente` en `DBCallCenter.Agente` (AgenteID = cédula)
4. Crea `ApplicationUser` en `DBZentinela` con `RequiereCambioPassword = true`
5. Contraseña inicial = cédula (el usuario la cambia en el primer login)

### Request body

```json
{
  "cedula": 1234567890,
  "usuarioControlNext": "string"
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `cedula` | `long` | Sí | Número de documento del empleado |
| `usuarioControlNext` | `string` | Sí | Usuario en el sistema ControlNext |

### Response body — 200 OK

```json
{
  "nombreCompleto": "string",
  "rol": "string",
  "mensaje": "string"
}
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Usuario registrado exitosamente |
| `400 Bad Request` | Cédula no encontrada en RRHH, usuario ya registrado, o error de validación. Body: `{ "mensaje": "..." }` |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/registro \
  -H "Content-Type: application/json" \
  -d '{"cedula": 1234567890, "usuarioControlNext": "jperez"}'
```

---

## POST /api/auth/login

Autentica un usuario con email/username y contraseña. Devuelve un token JWT y los datos del usuario.

**Autenticación requerida:** No

### Request body

```json
{
  "email": "string",
  "password": "string"
}
```

| Campo | Tipo | Requerido | Descripción |
|---|---|---|---|
| `email` | `string` | Sí | Email o username del usuario |
| `password` | `string` | Sí | Contraseña |

### Response body — 200 OK

```json
{
  "token": "string",
  "email": "string",
  "nombreCompleto": "string",
  "rol": "string",
  "liderID": "int | null",
  "expiracion": "datetime (UTC, ISO 8601)",
  "requiereCambioPassword": "bool"
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `token` | `string` | JWT firmado con HMAC-SHA256 |
| `email` | `string` | Email del usuario |
| `nombreCompleto` | `string` | Nombre para mostrar en la UI |
| `rol` | `string` | `Admin`, `Líder`, `Director` o `Asesor` |
| `liderID` | `int \| null` | ID del líder vinculado; `null` para Admin/Director |
| `expiracion` | `datetime` | Fecha/hora UTC de expiración del token |
| `requiereCambioPassword` | `bool` | `true` en el primer login — frontend redirige a `/cambiar-password` |

### Claims incluidos en el JWT

| Claim | Valor |
|---|---|
| `sub` | UsuarioID (string numérico) |
| `email` | Email del usuario |
| `role` | Rol del usuario |
| `liderID` | ID del líder (string vacío si no aplica) |
| `nombreCompleto` | Nombre completo |
| `numeroDocumento` | Número de cédula |
| `jti` | UUID único del token |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Credenciales correctas |
| `401 Unauthorized` | Credenciales incorrectas. Body: `{ "mensaje": "Credenciales incorrectas" }` |
| `400 Bad Request` | Campos vacíos o modelo inválido |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@zentinela.com", "password": "Admin1234!"}'
```

---

## POST /api/auth/cambiar-password

Cambia la contraseña del usuario autenticado. En el primer login (`requiereCambioPassword = true`) la contraseña actual es la cédula del usuario. Al completar, desactiva el flag `RequiereCambioPassword`.

**Autenticación requerida:** Sí (cualquier rol)

### Request body

```json
{
  "passwordActual": "string",
  "nuevoPassword": "string"
}
```

### Response body — 200 OK

```json
{ "mensaje": "Contraseña cambiada exitosamente" }
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Contraseña cambiada |
| `400 Bad Request` | Contraseña actual incorrecta o no cumple política. Body: `{ "mensaje": "..." }` |
| `401 Unauthorized` | Token ausente o inválido |

### Política de contraseñas

| Regla | Valor |
|---|---|
| Longitud mínima | 8 caracteres |
| Requiere dígito | Sí |
| Requiere mayúscula | No |
| Requiere carácter especial | No |

### Ejemplo curl

```bash
curl -X POST http://localhost:5153/api/auth/cambiar-password \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"passwordActual": "1234567890", "nuevoPassword": "MiPassword1"}'
```

---

## GET /api/auth/me

Devuelve los datos del usuario autenticado desde los claims del JWT. No consulta la base de datos.

**Autenticación requerida:** Sí (cualquier rol)

### Response body — 200 OK

```json
{
  "email": "string | null",
  "rol": "string | null",
  "liderID": "string | null",
  "nombreCompleto": "string | null"
}
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Token válido |
| `401 Unauthorized` | Token ausente, expirado o firma inválida |

---

## GET /api/dashboard/asesor

Devuelve el dashboard del asesor autenticado: acuerdos vigentes y métricas de rendimiento del último ciclo de actualización.

**Autenticación requerida:** Sí
**Roles permitidos:** `Asesor`

### Response body — 200 OK

```json
{
  "nombreSupervisor": "string | null",
  "acuerdosVigentes": [
    {
      "titularID": "int",
      "nombreTitular": "string",
      "ultimoPerfil": "string",
      "fechaPromesa": "date (ISO 8601)",
      "valorPromesa": "decimal | null",
      "saldoTotal": "decimal | null",
      "semaforo": "rojo | amarillo | verde"
    }
  ],
  "rendimiento": {
    "fechaActualizacion": "datetime | null",
    "totalGestiones": "int",
    "tasaContactabilidad": "decimal",
    "tasaEfectividad": "decimal"
  }
}
```

#### Lógica del semáforo (acuerdos)

| Semáforo | Condición |
|---|---|
| `rojo` | `fechaPromesa <= hoy` — vencido |
| `amarillo` | `fechaPromesa <= hoy + 3 días` — próximo a vencer |
| `verde` | `fechaPromesa > hoy + 3 días` — vigente |

#### Fuentes de datos

| Campo | Fuente |
|---|---|
| `nombreSupervisor` | `DBRecursosHumanos.Empleado` (relación supervisor) |
| `acuerdosVigentes` | `DBCallCenter.UltimoEstadoTitular` (tipificaciones en `Tipificaciones.AcuerdosVigentes`) |
| `rendimiento` | `DBCallCenter.HistoricoEstadoTitular` (día del último ciclo) |

#### Métricas de rendimiento

| Métrica | Cálculo |
|---|---|
| `fechaActualizacion` | `MAX(FechaActualizacion)` en `UltimoEstadoTitular` para el asesor |
| `totalGestiones` | Registros en `HistoricoEstadoTitular` del día de actualización |
| `tasaContactabilidad` | `contactosPositivos / totalGestiones` |
| `tasaEfectividad` | `acuerdosAbonos / contactosPositivos` |

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Dashboard devuelto (puede tener listas vacías si no hay datos) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | El usuario no tiene rol `Asesor` |

### Ejemplo curl

```bash
curl -X GET http://localhost:5153/api/dashboard/asesor \
  -H "Authorization: Bearer <token-asesor>"
```

---

## GET /api/dashboard/lider/carteras

Devuelve las carteras propias del líder autenticado para el dropdown de filtros.

**Autenticación requerida:** Sí
**Roles permitidos:** `Líder`

### Response body — 200 OK

```json
[
  { "id": 3, "nombreCartera": "Cartera Bogotá Norte" }
]
```

---

## GET /api/dashboard/lider

Dashboard del líder autenticado. El `liderID` se extrae del JWT — el líder solo ve sus propias carteras.

**Autenticación requerida:** Sí
**Roles permitidos:** `Líder`

### Query params

| Param | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int?` | Filtra por una cartera específica |
| `fechaDesde` | `DateOnly?` | Inicio del período (`YYYY-MM-DD`). Sin valor → último ciclo |
| `fechaHasta` | `DateOnly?` | Fin del período (`YYYY-MM-DD`). Sin valor → último ciclo |

### Response body — 200 OK

Misma estructura que `GET /api/dashboard/director`. `FilasLider` siempre vacío; `FilasAsesor` contiene el desglose de los asesores del líder incluyendo `proyeccionPromesas`.

---

## GET /api/dashboard/lider/pagos

Pagos confirmados de las carteras del líder autenticado.

**Autenticación requerida:** Sí
**Roles permitidos:** `Líder`

### Query params

| Param | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int?` | Filtra por cartera |
| `fechaDesde` | `DateOnly?` | Fecha de pago desde |
| `fechaHasta` | `DateOnly?` | Fecha de pago hasta |

### Response body — 200 OK

Array de `PagoDirectorDto` (mismo esquema que `GET /api/dashboard/director/pagos`) pero restringido a las carteras del líder autenticado. El campo `nombreLider` siempre corresponde al líder del JWT.

---

## GET /api/carteras

Devuelve las carteras del usuario autenticado filtradas por rol.

- **Admin**: todas las carteras
- **Líder / Director**: solo las carteras asignadas al líder
- **Sin liderID válido**: `403 Forbidden`

**Autenticación requerida:** Sí

### Response body — 200 OK

```json
[
  { "id": 3, "nombreCartera": "Cartera Bogotá Norte" },
  { "id": 7, "nombreCartera": "Cartera Medellín Sur" }
]
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Lista de carteras (puede ser `[]`) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | Rol sin `liderID` parseable y no es Admin |

---

---

## GET /api/calificador/opciones

Lista las combinaciones Cartera+Líder disponibles para el usuario autenticado.

- **Líder**: solo sus asignaciones expuestas en `vw_BaseMadre`.
- **Admin / Director**: todas las asignaciones activas de `Lider_Cartera`.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Response body — 200 OK

```json
[
  {
    "carteraID": 25,
    "liderID": 1,
    "etiqueta": "Cartera Bogotá — Juan Pérez"
  }
]
```

---

## GET /api/calificador/{carteraID}/{liderID}

Calcula todas las métricas del Calificador de Cartera para la asignación indicada.
El timeout de BD se amplía a 5 minutos para carteras grandes.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Parámetros de ruta

| Parámetro | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int` | ID de la cartera |
| `liderID` | `int` | ID del líder |

### Response body — 200 OK

```json
{
  "totalTitulares": 1500,

  "gestionDisponible": true,
  "titularesGestionados": 820,
  "saldoTotal": 1250000000.00,
  "saldoPromedio": 833333.33,
  "titularesConAcuerdoVigente": 45,
  "valorTotalPromesas": 12500000.00,

  "rotacionPromedio": 3.2,
  "desgloseRotacion": [
    { "etiqueta": "1", "cantidad": 200, "porcentaje": 13.3 }
  ],

  "desglosePerfil": [
    { "etiqueta": "No Contesta", "cantidad": 410, "porcentaje": 27.3 }
  ],

  "titularesConFosyga": 1100,
  "desgloseTipoAfiliado": [
    { "etiqueta": "Cotizante", "cantidad": 800, "porcentaje": 53.3 }
  ],

  "desgloseRegimen": [
    { "etiqueta": "Contributivo", "cantidad": 1050, "porcentaje": 70.0 }
  ],

  "desgloseEPS": [
    { "etiqueta": "Sanitas", "cantidad": 300, "porcentaje": 20.0 }
  ],

  "desgloseDepartamento": [
    { "etiqueta": "Atlántico", "cantidad": 700, "porcentaje": 46.7 }
  ],

  "conCelular": 980,
  "conEmail": 430,
  "conCelularYEmail": 380,
  "sinContacto": 120,
  "desgloseFuentesCelular": [
    { "etiqueta": "Cajacopi", "cantidad": 750, "porcentaje": 50.0 }
  ],

  "conEdadRegistraduria": 620,
  "sinEdadRegistraduria": 880,
  "edadPromedioRegistraduria": 47.3,

  "localizacion": [
    {
      "fuente": "Cajacopi",
      "encontrados": 750,
      "noEncontrados": 120,
      "faltanPorBuscar": 630
    }
  ]
}
```

#### Lógica de localización por fuente

Distingue 3 estados por fuente de datos:

| Estado | Condición |
|---|---|
| `encontrados` | Titular tiene datos reales en `vw_BaseMadre` para esa fuente |
| `noEncontrados` | Sin datos en la vista, pero existe fila en la tabla fuente (fue buscado) |
| `faltanPorBuscar` | `total - encontrados - noEncontrados` — aún no se ha consultado esa fuente |

Fuentes analizadas: Cajacopi, Cámara, Salud Total, Fosyga, Compensados, Positiva, Tigo, SOS, Sura (ARL), Positiva Emp., RUES, SIMIT, RGM.

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Métricas calculadas (puede tener `totalTitulares: 0`) |
| `401 Unauthorized` | Token ausente o inválido |
| `403 Forbidden` | Rol sin acceso |

---

## GET /api/creador/columnas

Retorna el catálogo de columnas disponibles en la Base Madre para construir el selector de columnas en el frontend.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Response body — 200 OK

```json
[
  { "nombre": "NumeroDocumento", "etiqueta": "Número Documento", "tipo": "Numero" },
  { "nombre": "Nombre",          "etiqueta": "Nombre",           "tipo": "Texto"  },
  { "nombre": "CelularCajacopi", "etiqueta": "Celular CajaCopi", "tipo": "Texto"  }
]
```

---

## POST /api/creador/preview

Retorna hasta 100 filas que cumplen los filtros aplicados, más el total real de la consulta.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Request body — `FiltroBaseMadreRequest`

```json
{
  "carteraID": 25,
  "ultimoPerfil": "No Contesta",
  "mejorPerfil": null,
  "promesasVigentes": null,
  "rotacionMaxima": 5,
  "saldoMinimo": 500000,
  "tipoAfiliado": "Cotizante",
  "estadoFosyga": "Activo",
  "fuentesCelular": ["Cajacopi", "Tigo"],
  "coincidenciasCelular": ["Cajacopi", "Tigo"],
  "fuentesEmail": [],
  "coincidenciasEmail": [],
  "columnasSeleccionadas": ["NumeroDocumento", "Nombre", "CelularCajacopi"]
}
```

| Campo | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int` | Cartera a filtrar |
| `ultimoPerfil` | `string?` | Tipificación exacta del último perfil de gestión |
| `mejorPerfil` | `string?` | Tipificación exacta del mejor perfil histórico |
| `promesasVigentes` | `bool?` | `true` = solo con promesa vigente; `false` = sin promesa; `null` = sin filtro |
| `rotacionMaxima` | `int?` | Rotación máxima permitida (filtro en memoria) |
| `saldoMinimo` | `decimal?` | Saldo mínimo en pesos (filtro en memoria) |
| `tipoAfiliado` | `string?` | Tipo de afiliado Fosyga (Cotizante, Beneficiario, Pensionado) |
| `estadoFosyga` | `string?` | Estado Fosyga (Activo, Inactivo, Retirado) |
| `fuentesCelular` | `string[]` | El titular debe tener celular en TODAS las fuentes listadas. Claves: `Cajacopi`, `Camara`, `SaludTotal`, `Positiva`, `Tigo`, `SOS`, `Claro`, `MiVacuna`, `MutualSer` (cel 1), `MutualSer2` (cel 2). MutualSer expone 2 celulares como entradas independientes — ver `docs/para-tener-en-cuenta.md`. |
| `coincidenciasCelular` | `string[]` | El número debe ser idéntico en todas las fuentes seleccionadas (mínimo 2) |
| `fuentesEmail` | `string[]` | El titular debe tener email en TODAS las fuentes listadas. Claves: `Cajacopi`, `Camara`, `SaludTotal`, `Positiva`, `Tigo`, `Claro`, `MiVacuna`, `MutualSer` |
| `coincidenciasEmail` | `string[]` | El email debe ser idéntico en todas las fuentes seleccionadas (mínimo 2) |
| `columnasSeleccionadas` | `string[]` | Nombres de propiedad C# a incluir. Lista vacía = todas las columnas |

### Response body — 200 OK

```json
{
  "total": 380,
  "filas": [
    {
      "numeroDocumento": 1234567890,
      "nombre": "María López",
      "celularCajacopi": "3001234567"
    }
  ]
}
```

---

## POST /api/creador/exportar

Genera y descarga el Excel completo con todas las filas que cumplen los filtros y las columnas seleccionadas.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Request body

Misma estructura que `POST /api/creador/preview`.

### Response — 200 OK

Archivo `.xlsx` con Content-Type `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
Nombre del archivo: `BaseMadre_<NombreCartera>_<yyyyMMdd>.xlsx`.

**Internamente** el endpoint usa `BaseMadreRepository.StreamFilasAsync` con proyección
SQL solo de las columnas pedidas (no `SELECT *`) y `DbDataReader` directo (sin
materializar `BaseMadreRow` ni reflexión por celda). ClosedXML sigue construyendo
el workbook en memoria — para carteras grandes preferí `/exportar-csv`.

---

## POST /api/creador/exportar-csv

Descarga **streaming** en CSV. No materializa la cartera en memoria — escribe fila por
fila desde `SqlDataReader` directo a `Response.Body`. Recomendado para carteras
grandes (≥100k filas) o cuando `/exportar` se acerca al timeout de SQL.

**Autenticación requerida:** Sí
**Roles permitidos:** `Admin`, `Líder`, `Director`

### Request body

Misma estructura que `POST /api/creador/preview` (mismo `FiltroBaseMadreRequest`,
incluyendo `columnasSeleccionadas`).

### Response — 200 OK

Archivo `.csv` UTF-8 (con BOM) con Content-Type `text/csv; charset=utf-8`.
Nombre del archivo: `BaseMadre_<NombreCartera>_<yyyyMMdd>.csv`.

Formato:
- Separador `;` (Excel ES-CO lo abre con doble click).
- Encabezados = etiquetas humanas (con tildes), igual que en el Excel.
- Quoting RFC 4180: campos con `;`, `"`, salto de línea o retorno se envuelven en `"` y las comillas internas se duplican.
- Fechas en `dd/MM/yyyy`. Decimales con punto (cultura invariante).

---

## GET /api/admin/usuarios

Lista todos los usuarios registrados en Zentinela con su rol y estado de contraseña.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response body — 200 OK

```json
[
  {
    "id": 1,
    "nombreCompleto": "Juan Pérez",
    "usuario": "juan.perez",
    "numeroDocumento": 1234567890,
    "rol": "Líder",
    "requiereCambioPassword": false
  }
]
```

---

## POST /api/admin/usuarios/{id}/reset-password

Restablece la contraseña del usuario a su número de cédula y activa el flag de cambio obligatorio en el próximo login.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response — 200 OK

```json
{ "mensaje": "Contraseña restablecida correctamente. El usuario deberá cambiarla en su próximo ingreso." }
```

---

## GET /api/admin/usuarios/{id}/supervisor

Retorna el supervisor actual del usuario según `Empleado.IDSupervisor` en DBRecursosHumanos.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response — 200 OK

```json
{
  "id": 42,
  "nombreCompleto": "Laura Gómez",
  "cargo": "Líder de Cobranza",
  "numeroDocumento": 9876543210
}
```

> Devuelve `null` si el usuario no tiene supervisor asignado o si no existe en DBRecursosHumanos.

---

## GET /api/admin/supervisores

Retorna los empleados activos con cargos que pueden supervisar (`CargoID` ∈ {2, 7, 8, 13, 18}).

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response — 200 OK

```json
[
  {
    "id": 42,
    "nombreCompleto": "Laura Gómez",
    "cargo": "Líder de Cobranza",
    "numeroDocumento": 9876543210
  }
]
```

---

## POST /api/admin/usuarios/{id}/reasignar-supervisor

Reasigna el supervisor de un usuario. Actualiza `Empleado.IDSupervisor` en DBRecursosHumanos y elimina todas sus filas en `Cartera_Agente` (el nuevo supervisor deberá reasignarle carteras).

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Request body

```json
{ "nuevoSupervisorId": 42 }
```

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `nuevoSupervisorId` | `int` | `Empleado.ID` del nuevo supervisor en DBRecursosHumanos |

### Response — 200 OK

```json
{ "mensaje": "Supervisor reasignado correctamente. Las asignaciones de cartera anteriores fueron eliminadas." }
```

---

## DELETE /api/admin/usuarios/{id}

Inactiva un usuario: marca `EstadoEmpleado = false` en DBRecursosHumanos y elimina el `ApplicationUser` de Zentinela (incluye roles y claims por cascada de Identity).

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

> Si el usuario no tiene registro en DBRecursosHumanos, solo se elimina de Zentinela.

### Response — 200 OK

```json
{ "mensaje": "Usuario inactivado y eliminado de Zentinela correctamente." }
```

---

## PATCH /api/admin/usuarios/{id}/usuario

Cambia el `UserName` del `ApplicationUser`. Valida formato (3-50 chars, no vacío) y unicidad case-insensitive vía `UserManager.FindByNameAsync` antes de delegar a `UserManager.SetUserNameAsync` (Identity recalcula `NormalizedUserName`).

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

### Request body

```json
{ "nuevoUsuario": "ana.lopez" }
```

### Response — 200 OK

```json
{ "mensaje": "Usuario actualizado correctamente." }
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Cambio aplicado (o no-op si el username era idéntico) |
| `400 Bad Request` | Username vacío, fuera de rango (3-50), ya en uso, o usuario no encontrado |

---

## GET /api/admin/agentes

Lista los agentes del sistema. Solo aparecen usuarios con **rol `Asesor`** en Identity (perfil de "Agente" en Zentinela), que además tengan `Agente.Activo = true` en DBCallCenter y `Empleado.EstadoEmpleado = true` en DBRecursosHumanos. Devuelve además el supervisor, el líder resuelto y el conteo de carteras asignadas.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

### Response — 200 OK

```json
[
  {
    "agenteID": 1020304050,
    "nombreCompleto": "MARIA RODRIGUEZ",
    "numeroDocumento": 1020304050,
    "supervisorNombre": "JUAN PEREZ",
    "esLiderSupervisor": true,
    "liderID": 7,
    "motivoNoAsignable": null,
    "totalCarterasAsignadas": 2
  }
]
```

`motivoNoAsignable` se completa cuando no se puede gestionar carteras: `"Sin supervisor asignado"` o `"Supervisor no es líder activo"`. Para esos casos `liderID` es `null` y la UI deshabilita el botón "Gestionar carteras".

`totalCarterasAsignadas` cuenta solo filas de `Cartera_Agente` con `LiderID = liderActual` (filas huérfanas con líderes anteriores se ignoran).

---

## GET /api/admin/agentes/{agenteID}/carteras

Devuelve las carteras del **líder del supervisor del agente**, marcando cuáles ya están asignadas en `Cartera_Agente`.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

### Response — 200 OK

```json
{
  "liderID": 7,
  "liderNombre": "JUAN PEREZ",
  "motivoNoAsignable": null,
  "carteras": [
    { "carteraID": 1, "nombre": "Cartera Empresas Q1", "asignada": true },
    { "carteraID": 5, "nombre": "Cartera VIP",          "asignada": false }
  ]
}
```

Si el agente no tiene supervisor o el supervisor no es líder activo, `liderID = null`, `carteras = []` y `motivoNoAsignable` describe la causa.

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Carteras devueltas (lista vacía cuando no hay líder o el líder no tiene carteras) |
| `400 Bad Request` | Agente no encontrado o inactivo |

---

## POST /api/admin/agentes/{agenteID}/carteras/{carteraID}

Asigna una cartera al agente. Inserta una fila en `Cartera_Agente` con `(AgenteID, LiderID, CarteraID)`. **El `LiderID` se resuelve server-side** desde el supervisor del agente; el cliente no lo envía. Idempotente: si la fila ya existe, no inserta y devuelve 200.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

### Validaciones
- Agente existe y activo en RRHH (`EstadoEmpleado = true`).
- Tiene supervisor (`Empleado.IDSupervisor != null`).
- El supervisor está registrado como `Lider` activo en DBCallCenter.
- La FK compuesta `(LiderID, CarteraID) → Lider_Cartera` valida a nivel BD que la cartera pertenezca al líder. Si no, se traduce a `400 "La cartera no pertenece al líder del agente."`.

### Response — 200 OK

```json
{ "asignada": true }
```

---

## DELETE /api/admin/agentes/{agenteID}/carteras/{carteraID}

Quita una cartera del agente. Idempotente: si la fila no existe, devuelve 200 sin error. Resuelve `LiderID` server-side igual que el POST.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `Auxiliar de Sistemas`

### Response — 200 OK

```json
{ "asignada": false }
```

---

---

## GET /api/gestion-carteras/relaciones

Lista todas las relaciones Líder_Cartera con el conteo de titulares asignados en Titular_Cartera.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response body — 200 OK

```json
[
  {
    "liderID": 3,
    "carteraID": 12,
    "nombreLider": "Laura Gómez",
    "nombreCartera": "CARTERA VENCIDA 2024",
    "totalAsignaciones": 438
  }
]
```

---

## GET /api/gestion-carteras/lideres

Lista todos los líderes activos para el dropdown de creación de relaciones.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response body — 200 OK

```json
[
  { "id": 3, "nombre": "Laura Gómez" }
]
```

---

## GET /api/gestion-carteras/carteras/buscar

Busca carteras por nombre para el autocompletado (máximo 10 resultados).

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Query params

| Param | Tipo | Descripción |
|-------|------|-------------|
| `q` | `string` | Texto a buscar en `NombreCartera` |

### Response body — 200 OK

```json
[
  { "id": 12, "nombreCartera": "CARTERA VENCIDA 2024" }
]
```

---

## POST /api/gestion-carteras/relaciones

Crea una relación Líder_Cartera. Si `carteraID` es null, crea primero una nueva Cartera con `nombreCartera` y luego la relación. Valida que la relación no exista ya y que el nombre de cartera nuevo no sea duplicado.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Request body

```json
{ "carteraID": 12, "liderID": 3 }
```

_o para cartera nueva:_

```json
{ "nombreCartera": "NUEVA CARTERA 2026", "liderID": 3 }
```

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `carteraID` | `int?` | ID de cartera existente. Mutuamente excluyente con `nombreCartera` |
| `nombreCartera` | `string?` | Nombre para cartera nueva. Mutuamente excluyente con `carteraID` |
| `liderID` | `int` | ID del líder al que se asigna la cartera |

### Response — 200 OK

```json
{
  "liderID": 3,
  "carteraID": 12,
  "nombreLider": "Laura Gómez",
  "nombreCartera": "CARTERA VENCIDA 2024",
  "totalAsignaciones": 0
}
```

### Errores posibles — 400 Bad Request

| Condición | Mensaje |
|-----------|---------|
| Relación ya existe | `"Esta relación ya existe."` |
| Nombre de cartera duplicado | `"Ya existe una cartera con el nombre '...'."` |
| Líder no encontrado | `"El líder seleccionado no existe."` |

---

## DELETE /api/gestion-carteras/relaciones/{liderID}/{carteraID}

Elimina una relación `Lider_Cartera` y, en cascada, los `Titular_Cartera` asociados a esa combinación.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`, `AuxiliarSistemas`

### Pre-validación de dependientes

`Lider_Cartera` tiene FK entrantes desde varias tablas. Antes de borrar, el servicio cuenta los dependientes y si hay alguno, **rechaza la operación con 400 y un mensaje detallado** (en vez de un error genérico de FK).

| Tabla bloqueante | Significado |
|---|---|
| `Cartera_Agente` | Asesores asignados a la combinación líder+cartera |
| `Pago` | Pagos cargados con esa combinación |
| `Meta_Lider_Cartera_Mes` | Metas mensuales registradas |
| `MensajeriaEnviada` | Envíos de mensajería (`ON DELETE RESTRICT`) |
| `IVREnviado` | Envíos de IVR (`ON DELETE RESTRICT`) |

Solo `Titular_Cartera` se limpia automáticamente.

### Response — 200 OK (sin dependientes)

```json
{ "mensaje": "Relación eliminada correctamente." }
```

### Response — 400 Bad Request (con dependientes)

```json
{
  "mensaje": "No se puede eliminar la relación porque tiene: 12 asesor(es) asignado(s), 230 pago(s) registrado(s), 3 meta(s) mensual(es). Reasigna o elimina esos registros primero."
}
```

---

---

## GET /api/pagos/carteras

Devuelve la lista de carteras disponibles para el selector de Cargue de Pagos.

**Autenticación requerida:** Sí — `Admin`

### Response body — 200 OK

```json
[
  { "id": 1, "nombre": "Cartera Empresas 2024" },
  { "id": 2, "nombre": "Cartera Hogares Q1" }
]
```

---

## POST /api/pagos/validar

Parsea y valida un archivo CSV/XLSX de pagos. No persiste nada en BD. Devuelve un preview con las primeras 10 filas válidas, **TODAS** las filas con error (para inspección por el usuario), y la lista completa de filas válidas para el paso de confirmación.

**Autenticación requerida:** Sí — `Admin`  
**Content-Type:** `multipart/form-data`  
**Límite de tamaño:** 10 MB

### Form fields

| Campo | Tipo | Descripción |
|---|---|---|
| `archivo` | `File (.csv)` | Archivo CSV de pagos |
| `carteraID` | `int` | ID de la cartera seleccionada en la UI |

### Columnas reconocidas en el CSV

El sistema busca columnas por nombre (no por posición). Acepta mayúsculas, tildes y espacios indistintamente.

| Campo interno | Alias aceptados |
|---|---|
| CedulaTitular | `cedula`, `numerodocumento`, `documento` |
| NombreCliente | `nombre`, `nombrecliente`, `cliente` |
| ValorPagado | `valor`, `valorpagado`, `valorpago`, `montopagado`, `monto` |
| FechaPago | `fecha`, `fechapago`, `fechadepago` |
| Honorarios | `honorarios`, `honorario` |
| UsuarioControl | `usuariocontrol`, `usuario`, `usuariocontrolnext`, `control` |

### Response body — 200 OK

```json
{
  "totalFilas": 150,
  "cantidadValidas": 148,
  "cantidadErrores": 2,
  "preview": [
    {
      "cedulaTitular": 1020304050,
      "nombreCliente": "PEREZ JUAN",
      "valorPagado": 500000.00,
      "fechaPago": "2026-04-10",
      "honorarios": 75000.00,
      "usuarioControl": "jperez",
      "esValida": true,
      "errorMensaje": null
    }
  ],
  "errores": [
    {
      "cedulaTitular": 1020304055,
      "nombreCliente": "GOMEZ ANA",
      "esValida": false,
      "errorMensaje": "Cédula 1020304055 no está asignada a esta cartera."
    }
  ],
  "todasLasValidas": [ ... ]
}
```

- `preview` — primeras 10 filas válidas para vista previa (regla del proyecto).
- `errores` — **todas** las filas inválidas con `errorMensaje` para que el usuario las pueda corregir.
- `todasLasValidas` — lista completa de filas válidas, se envía en el paso de confirmación.

**Validaciones por fila:**
1. Cédula numérica válida
2. Valor pagado y honorarios parseables como decimal
3. Fecha en formato reconocible por `DateOnly.TryParse`
4. `CedulaTitular` asignada a la cartera seleccionada en `Titular_Cartera`
5. `UsuarioControl` existe como `Agente.UsuarioControlNext` en BD

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Archivo procesado (puede tener filas inválidas) |
| `400 Bad Request` | Archivo vacío, extensión incorrecta o `carteraID` inválido |

---

## POST /api/pagos/confirmar

Inserta en la tabla `Pago` las filas válidas devueltas por `/validar`. El `UsuarioRegistro` (UserName del usuario autenticado) se toma del JWT.

**Autenticación requerida:** Sí — `Admin`

### Request body

```json
{
  "carteraID": 1,
  "filasValidas": [
    {
      "cedulaTitular": 1020304050,
      "nombreCliente": "PEREZ JUAN",
      "valorPagado": 500000.00,
      "fechaPago": "2026-04-10",
      "honorarios": 75000.00,
      "liderID": 3,
      "agenteControlID": 98765,
      "esValida": true
    }
  ]
}
```

### Response body — 200 OK

```json
{ "mensaje": "Se registraron 148 pagos correctamente." }
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Pagos insertados |
| `400 Bad Request` | Lista de filas vacía |

---

## POST /api/pagos/registrar-individual

Registra **un** pago desde el formulario "Pago individual" (tab del módulo Cargue de Pagos). Valida e inserta en un solo paso, reutilizando exactamente la misma resolución de líder/agente y detección de duplicados del cargue masivo (ADR-023). El `UsuarioRegistro` se toma del JWT.

**Autenticación requerida:** Sí — `Admin`, `Auxiliar de Sistemas`

### Request body

```json
{
  "carteraID": 1,
  "cedulaTitular": 1020304050,
  "nombreCliente": "PEREZ JUAN",
  "valorPagado": 500000,
  "fechaPago": "2026-06-11",
  "honorarios": 75000,
  "usuarioControl": "jperez",
  "esBaseGeneral": false
}
```

- `usuarioControl` — username del asesor (Agente.UsuarioControlNext). Se ignora si `esBaseGeneral = true`.
- `esBaseGeneral` — `true` ⇒ pago sin asesor; `agenteControlID` queda NULL y el líder debe ser único.

### Response body — 200 OK

```json
{ "pagoID": 12345, "liderID": 3, "liderNombre": "ANA LÓPEZ", "agenteControlID": 98765, "esBaseGeneral": false }
```

### Códigos HTTP

| Código | Condición |
|---|---|
| `200 OK` | Pago registrado |
| `400 Bad Request` | Campos inválidos, o la fila no resuelve (cédula no asignada, asesor no encontrado, ambigüedad de líder, pago duplicado) — `{ mensaje }` con el motivo |
| `401 Unauthorized` | Sin claim `userName` en el token |

---

## GET /api/pagos/asesores

Asesores de una cartera para el autocomplete del pago individual: agentes en `Cartera_Agente` con `UsuarioControlNext`, con nombre desde DBRecursosHumanos. Filtra por `q` sobre el nombre (máx. 10).

**Autenticación requerida:** Sí — `Admin`, `Auxiliar de Sistemas`

### Query params

| Campo | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int` | Cartera seleccionada |
| `q` | `string` | Texto de búsqueda por nombre (opcional) |

### Response body — 200 OK

```json
[ { "agenteID": 98765, "nombre": "JUAN PEREZ", "usuarioControl": "jperez" } ]
```

---

## GET /api/pagos/titular

Busca un titular por cédula dentro de una cartera, para autocompletar su nombre en el pago individual.

**Autenticación requerida:** Sí — `Admin`, `Auxiliar de Sistemas`

### Query params

| Campo | Tipo | Descripción |
|---|---|---|
| `carteraID` | `int` | Cartera seleccionada |
| `cedula` | `long` | Cédula del titular |

### Response body — 200 OK

```json
{ "existe": true, "nombre": "PEREZ JUAN" }
```

`existe = false` cuando la cédula no está asignada a la cartera (el pago no resolvería líder).

---

## GET /api/reportes/plantilla

Descarga la plantilla Excel para Cargue de Reportes.  
La columna `AgenteID` debe contener el **username de ControlNext** (`Agente.UsuarioControlNext`), no la cédula numérica. Ver ADR-005.

### Response — 200 OK

Archivo `plantilla_reporte.xlsx`.

---

## POST /api/reportes/validar

Parsea el Excel y valida cada fila. Resuelve `UsuarioControl → AgenteID` via `Agente.UsuarioControlNext` (batch lookup, OrdinalIgnoreCase).

**Roles permitidos:** `Admin`

### Request — `multipart/form-data`

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `archivo` | file | Excel con filas de gestión |
| `carteraID` | int | Cartera a la que pertenecen los registros |

### Validaciones por fila

1. `TitularID` existe en `Titular` → `Ok`
2. `TitularID` no existe pero hay `Nombre` → `NuevoTitular` (se creará en BD)
3. `TitularID` no existe y sin `Nombre` → `SinNombreParaCrear` (error)
4. `UsuarioControl` no resuelve a ningún `Agente.UsuarioControlNext` → `AgenteNoEncontrado` (error)

### Response body — 200 OK

```json
{
  "totalValidos": 180,
  "totalNuevos": 5,
  "totalInvalidos": 3,
  "filas": [
    {
      "titularID": "1020304050",
      "nombre": "PEREZ JUAN",
      "usuarioControl": "jperez",
      "estado": "Ok",
      "razon": null
    }
  ],
  "filasConError": [
    {
      "titularID": "1020304055",
      "nombre": null,
      "usuarioControl": "xxxx",
      "estado": "AgenteNoEncontrado",
      "razon": "Usuario Control 'xxxx' no encontrado en el sistema"
    }
  ],
  "filasValidasRaw": [ ]
}
```

- `filas` — primeras 10 filas válidas (Ok / NuevoTitular) para vista previa.
- `filasConError` — **todas** las filas con error, con la `razon` específica de cada una.
- `filasValidasRaw` — payload completo que se envía a `/api/reportes/confirmar`.

---

## POST /api/reportes/confirmar

Procesa las filas válidas devueltas por `/validar`:
1. `TRUNCATE` de `ReporteEstadoAuxiliar`
2. Bulk INSERT en chunks de 66 filas
3. `EXEC sp_ProcesarUltimoEstado` → UPSERT en `UltimoEstadoTitular` + inserta titulares nuevos en `Titular`
4. `EXEC sp_ProcesarHistoricoEstado` → INSERT append en `HistoricoEstadoTitular`

### Request body

```json
{ "filasValidas": [ ] }
```

### Response body — 200 OK

```json
{
  "procesados": 180,
  "titularesNuevos": 5,
  "mensaje": "Se procesaron 180 registro(s): UltimoEstadoTitular actualizado e HistoricoEstadoTitular enriquecido."
}
```

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Cargue exitoso |
| `400 Bad Request` | Lista de filas vacía |

---

## GET /api/informes/recaudo/agentes

Retorna el consolidado de recaudo del mes por agente, ordenado de menor a mayor.  
Incluye meta unificada (`valorMeta` + `tipoMeta`), cumplimiento y proyección diaria.

### Query params

| Param | Tipo | Requerido | Descripción |
|-------|------|-----------|-------------|
| `año` | int | Sí | Año del período |
| `mes` | int | Sí | Mes del período (1-12) |

### Response body — 200 OK

```json
{
  "totalRecaudo": 15000000,
  "totalHonorarios": 1500000,
  "totalAgentes": 8,
  "totalGestionado": 142,
  "agentes": [
    {
      "agenteID": 1020304050,
      "nombreAgente": "Juan Pérez García",
      "nombreLider": "María López",
      "nombreCartera": "Cartera Bogotá Norte",
      "gestionado": 18,
      "recaudo": 1200000,
      "honorarios": 120000,
      "valorMeta": 3000000,
      "tipoMeta": "Honorarios",
      "tieneMeta": true,
      "deberiaLlevarHoy": 1850000,
      "faltaParaMeta": 1800000,
      "promedioPagoDia": 85714
    }
  ]
}
```

> **`tipoMeta`**: `"Recaudo"` o `"Honorarios"`. Determina contra qué métrica se compara `valorMeta` para calcular cumplimiento y semáforo.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Informe generado (puede tener `agentes: []` si no hay pagos en el período) |
| `400 Bad Request` | Mes fuera de rango |

---

## GET /api/informes/recaudo/lideres

Retorna una fila por combinación líder-cartera con meta, capacity (titulares asignados) y proyección de cierre.

### Query params

| Param | Tipo | Requerido | Descripción |
|-------|------|-----------|-------------|
| `año` | int | Sí | Año del período |
| `mes` | int | Sí | Mes del período (1-12) |

### Response body — 200 OK

```json
{
  "totalRecaudo": 15000000,
  "totalMeta": 20000000,
  "totalLideres": 3,
  "liderCarteras": [
    {
      "liderID": 1,
      "nombreLider": "María López",
      "carteraID": 2,
      "nombreCartera": "Cartera A",
      "valorMeta": 5000000,
      "tipoMeta": "Recaudo",
      "tieneMeta": true,
      "asignaciones": 320,
      "recaudo": 3200000,
      "honorarios": 320000,
      "deberiaLlevarHoy": 2750000,
      "proyeccionCierre": 4640000
    }
  ]
}
```

**Cálculos (backend):**
- `deberiaLlevarHoy` = `valorMeta × (díaActual / díasDelMes)`; si el mes ya pasó, usa el mes completo.
- `proyeccionCierre` = `(recaudo / díasTranscurridos) × díasDelMes`; 0 si el mes no ha comenzado.
- `asignaciones`: titulares asignados en `Titular_Cartera` para esa combinación líder-cartera (antes llamado `capacidad`).

**Cálculo frontend — KPI "% vs Meta Global" (Tab Líderes):**

Los líderes se agregan por `liderID` sumando sus carteras. El KPI global **no** usa `totalRecaudo / totalMeta` (eso sesgaría a favor de las carteras con mayor valor monetario absoluto). En su lugar se calcula el **promedio aritmético de los cumplimientos individuales** de los líderes que tienen meta:

```
avgCumplimiento = Σ(cumplimiento_i) / N_con_meta
cumplimiento_i  = (honorarios_i | recaudo_i) / valorMeta_i
```

Solo participan en el promedio los líderes con `tieneMeta = true`. Si ninguno tiene meta, se muestra `—`.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Informe generado |
| `400 Bad Request` | Mes fuera de rango |

---

## GET /api/informes/recaudo/carteras

Comparativo de recaudo y honorarios por cartera entre dos períodos.  
Sirve tanto para "mes pasado vs este mes" (Tab Carteras) como para el comparativo libre (Tab Comparativo).

### Query params

| Param | Tipo | Requerido | Descripción |
|-------|------|-----------|-------------|
| `añoA` | int | Sí | Año del período A |
| `mesA` | int | Sí | Mes del período A (1-12) |
| `añoB` | int | Sí | Año del período B |
| `mesB` | int | Sí | Mes del período B (1-12) |

### Response body — 200 OK

```json
{
  "periodoA": { "año": 2026, "mes": 3, "nombre": "Marzo 2026" },
  "periodoB": { "año": 2026, "mes": 4, "nombre": "Abril 2026" },
  "totalRecaudoA": 12000000,
  "totalRecaudoB": 15000000,
  "variacionTotal": 3000000,
  "carteras": [
    {
      "carteraID": 1,
      "nombreCartera": "Cartera A",
      "recaudoA": 5000000,
      "honorariosA": 500000,
      "recaudoB": 6500000,
      "honorariosB": 650000,
      "variacionRecaudo": 1500000,
      "porcentajeVariacion": 30.0
    }
  ]
}
```

`porcentajeVariacion` es `null` cuando `recaudoA = 0`.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Comparativo generado |
| `400 Bad Request` | Algún mes fuera de rango |

---

## GET /api/virtualidad/historico/plantilla

Descarga la plantilla XLSX para cargar solicitudes (fase Solicitud) de un canal.

**Roles:** Admin, AuxiliarSistemas

### Query params

| Param | Tipo | Requerido | Descripción |
|---|---|---|---|
| `canal` | `string` | Sí | `IVR` \| `SMS` \| `Correo` |

### Response — 200 OK

Archivo `.xlsx` (`plantilla_solicitud_{canal}.xlsx`) con columnas `NumeroDocumento`, `Telefono`/`Email` (según canal), `Cartera`, `Lider`, `FechaEnvio` y una fila de ejemplo.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Plantilla generada |
| `400 Bad Request` | Canal inválido |

---

## POST /api/virtualidad/historico/validar

Valida el archivo de solicitudes cargado: resuelve Cartera/Líder por nombre (`Normalizar`) contra `Lider_Cartera` activo, valida el par, y calcula alertas de saturación (envíos acumulados por destino sobre el umbral del canal: IVR=5, SMS=4, Correo=sin alerta).

**Roles:** Admin, AuxiliarSistemas · **Content-Type:** `multipart/form-data`

### Request — `multipart/form-data`

| Campo | Tipo | Requerido |
|---|---|---|
| `archivo` | `.xlsx` | Sí |
| `canal` | `string` | Sí |

### Response body — 200 OK

```json
{
  "totalFilas": 120,
  "totalValidas": 115,
  "totalErrores": 5,
  "preview": [ { "numeroDocumento": 123456, "destino": "3001234567", "nombreCartera": "SG+", "nombreLider": "VANESA ARANGO", "carteraID": 28, "liderID": 14, "fechaEnvio": "2026-07-08", "esValida": true, "errorMensaje": null } ],
  "filasValidas": [ ],
  "alertas": [ { "destino": "3001234567", "nombreCartera": "SG+", "totalEnvios": 6 } ]
}
```

`preview` trae máximo 10 filas. Errores comunes en `errorMensaje`: `"Cartera '{x}' no encontrada (o inactiva)."`, `"Líder '{x}' no encontrado."`, `"'{Líder}' no está asignado a la cartera '{Cartera}'."`.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Validado (puede traer errores por fila, no es fallo del request) |
| `400 Bad Request` | Canal inválido, archivo ausente/vacío, o extensión distinta de `.xlsx` |

---

## POST /api/virtualidad/historico/confirmar

Inserta las filas válidas en `HistoricoVirtualidad` con `Estado = NULL` (sin resultado — se completa en la fase Resultado).

**Roles:** Admin, AuxiliarSistemas

### Request body

```json
{ "canal": "SMS", "filasValidas": [ ] }
```

### Response body — 200 OK

```json
{ "insertados": 115, "mensaje": "Se registraron 115 solicitudes de SMS correctamente." }
```

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Inserción exitosa |
| `400 Bad Request` | Canal inválido o `filasValidas` vacío |

---

## GET /api/virtualidad/historico/pendientes

Descarga XLSX con las solicitudes de un canal que aún no tienen `Estado` (fase Resultado), filtrable por cartera y rango de fechas.

**Roles:** Admin, AuxiliarSistemas

### Query params

| Param | Tipo | Requerido | Descripción |
|---|---|---|---|
| `canal` | `string` | Sí | `IVR` \| `SMS` \| `Correo` |
| `carteraID` | `int` | No | Filtra por cartera |
| `desde` | `date` | No | `FechaEnvio >= desde` |
| `hasta` | `date` | No | `FechaEnvio <= hasta` |

### Response — 200 OK

Archivo `.xlsx` (`pendientes_{canal}.xlsx`) con columnas `ID`, `NumeroDocumento`, `Destino`, `Cartera`, `Lider`, `FechaEnvio`, `Estado` (vacía — a diligenciar). **No modificar la columna `ID`**, es la clave para el UPDATE.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Archivo generado (puede venir sin filas si no hay pendientes) |
| `400 Bad Request` | Canal inválido |

---

## POST /api/virtualidad/historico/resultado/validar

Valida el archivo de resultados: confirma que cada `ID` existe, pertenece al canal indicado, y que `Estado` está en la lista blanca del canal (IVR: Contestada/No contestada/Buzón/Número errado — SMS: Entregado/No entregado — Correo: Entregado/Rebotado/Abierto).

**Roles:** Admin, AuxiliarSistemas · **Content-Type:** `multipart/form-data`

### Request — `multipart/form-data`

| Campo | Tipo | Requerido |
|---|---|---|
| `archivo` | `.xlsx` | Sí |
| `canal` | `string` | Sí |

### Response body — 200 OK

```json
{
  "totalFilas": 40,
  "totalValidas": 38,
  "totalErrores": 2,
  "preview": [ { "id": 501, "estado": "Contestada", "esValida": true, "errorMensaje": null } ],
  "filasValidas": [ ]
}
```

Errores comunes: `"ID {id} no existe."`, `"ID {id} pertenece al canal '{canalReal}', no a '{canal}'."`, `"Estado '{x}' no válido para {canal}. Válidos: ..."`.

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Validado |
| `400 Bad Request` | Canal inválido, archivo ausente/vacío, o extensión distinta de `.xlsx` |

---

## POST /api/virtualidad/historico/resultado/confirmar

Aplica `Estado` + `FechaResultado = DateTime.Now` por `ID` sobre las filas válidas. Si un `ID` se repite en el archivo, gana la última fila.

**Roles:** Admin, AuxiliarSistemas

### Request body

```json
{ "filasValidas": [ ] }
```

### Response body — 200 OK

```json
{ "actualizados": 38, "mensaje": "Se actualizaron 38 resultados correctamente." }
```

### Códigos HTTP

| Código | Descripción |
|--------|-------------|
| `200 OK` | Actualización exitosa |
| `400 Bad Request` | `filasValidas` vacío |

---

## Exploración interactiva (solo Development)

- OpenAPI JSON: `http://localhost:5153/openapi/v1.json`
- Scalar UI: `http://localhost:5153/scalar/v1`
