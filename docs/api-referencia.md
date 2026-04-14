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
| `fuentesCelular` | `string[]` | El titular debe tener celular en TODAS las fuentes listadas. Claves: `Cajacopi`, `Camara`, `SaludTotal`, `Positiva`, `Tigo`, `SOS` |
| `coincidenciasCelular` | `string[]` | El número debe ser idéntico en todas las fuentes seleccionadas (mínimo 2) |
| `fuentesEmail` | `string[]` | El titular debe tener email en TODAS las fuentes listadas. Claves: `Cajacopi`, `Camara`, `SaludTotal`, `Positiva`, `Tigo` |
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
Nombre del archivo: `BaseMadre_<CarteraID>_<timestamp>.xlsx`.

---

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
**Roles permitidos:** `Admin`

> Si el usuario no tiene registro en DBRecursosHumanos, solo se elimina de Zentinela.

### Response — 200 OK

```json
{ "mensaje": "Usuario inactivado y eliminado de Zentinela correctamente." }
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

Elimina una relación Líder_Cartera. **No elimina** los registros de `Titular_Cartera` asociados — deben reasignarse manualmente.

**Autenticación requerida:** Sí  
**Roles permitidos:** `Admin`

### Response — 200 OK

```json
{ "mensaje": "Relación eliminada correctamente." }
```

---

## Exploración interactiva (solo Development)

- OpenAPI JSON: `http://localhost:5153/openapi/v1.json`
- Scalar UI: `http://localhost:5153/scalar/v1`
