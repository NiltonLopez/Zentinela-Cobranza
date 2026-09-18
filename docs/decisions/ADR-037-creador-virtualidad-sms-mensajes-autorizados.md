# ADR-037 — Creador de Virtualidad (SMS): catálogo cerrado de mensajes y depuración en servidor

**Estado:** Aceptado
**Fecha:** 2026-09-18
**Módulos afectados:** Cartera → Creador de Virtualidad (nuevo) · Administración → Mensajes Autorizados (nuevo)

---

## Contexto

Los líderes redactaban a mano los SMS que envía el área de virtualidad. Salieron mensajes indebidos y
la empresa quedó expuesta a demandas y derechos de petición. El problema no es técnico sino de
control: mientras el texto lo escriba el líder, no hay forma de garantizar qué sale.

Además, la base que se enviaba no pasaba por ningún filtro: iban titulares con Paz y Salvo, con PQR
abierta, fallecidos, números en lista negra, y el mismo celular repetido varias veces.

Hallazgos de la BD que fijaron el diseño:
- **La lista negra ya existía** (`ListaNegraTelefonos`, 660k filas / 103.250 números distintos, por
  cartera) pero nada en el código la consumía. Medido sobre 200k marcaciones reales: **10,3 % de los
  números están en ella**.
- `Agente` no tiene teléfono; el de RRHH es el personal. No había dónde guardar un corporativo.
- `UltimoPerfil` en BD mezcla `No gestionar` / `NO GESTIONAR`, `Paz y Salvo` / `PAZ Y SALVO` y tildes
  rotas: cualquier comparación tiene que ser sin mayúsculas ni acentos.

## Decisiones

### 1. El líder elige, no redacta
Tabla `MensajeAutorizado` con textos que escriben abogados o encargados (rol Admin/Director) desde
*Administración → Mensajes Autorizados*. El Creador solo deja **elegir uno**. Los mensajes no se
borran (los referencia la auditoría); se desactivan.

El texto lleva placeholders `{NOMBRE}`, `{CARTERA}`, `{EMPRESA}`, `{CELULAR_ASESOR}` y `{LINK_ASESOR}`.
`{CARTERA}` y `{EMPRESA}` (pedidos 2026-09-18) dependen de dos columnas **opcionales** del archivo: si
faltan, los mensajes que las usan aparecen deshabilitados con ese motivo — no se adivina ni se deja el
hueco vacío. Sus valores se limpian a GSM-7 conservando mayúsculas/minúsculas y `.,&()/-`. Los dos del asesor son
**obligatorios** (validación en API + `CHECK` en BD): sin ellos el titular no tiene a quién responder.
Se eligió placeholders y no un sufijo fijo del sistema para que los abogados controlen también la
coletilla ("CONCILIE …").

### 2. Solo GSM-7, y se rechaza al guardar
Una sola tilde o ñ convierte el SMS a UCS-2 y el límite real cae de 160 a 70. El formulario marca en
rojo cada carácter no permitido y no deja guardar. Se descartó "advertir pero permitir" (el límite
de 160 dejaría de ser verdad) y "normalizar automáticamente" (cambiar el texto de un abogado sin que
lo vea es peor que rechazarlo). Los nombres de titular sí se normalizan (Ñ→N, sin tildes) porque no
los escribe nadie.

### 3. La depuración vive en el servidor, en un solo pipeline
`CreadorVirtualidadSmsService.DepurarAsync` alimenta los tres pasos con archivo (validar, anotar el
catálogo, generar). Cada fila recibe **un solo motivo de exclusión**, en orden fijo:
cédula inválida → teléfono inválido → fijo → nombre vacío → perfil (archivo) → perfil (histórico BD)
→ lista negra (tabla, carteras elegidas) → lista negra (archivo) → duplicado por número.
Así `leídas − válidas` es exactamente la suma de los contadores y lo que el líder vio en el preview
es lo que sale en el archivo.

- **Perfil en BD**: se excluye el titular si *cualquier* registro suyo en `HistoricoEstadoTitular`
  tiene uno de: No gestionar, PQR, Paz y salvo, Ya pago, Reclamacion o derecho de peticion, Fallecido,
  Cliente fallecido. Consulta con `#Titulares` + hash join + `COLLATE Latin1_General_CI_AI`.
- **Lista negra**: el usuario elige qué carteras de `ListaNegraTelefonos` cruzar (la tabla las guarda
  con `_`; la UI las muestra con espacio y envía el valor crudo). Más un XLSX opcional con números
  extra para esa estrategia.
- **Duplicados**: se conserva la primera fila por número. No se envían dos SMS al mismo celular.
- **Fijos**: fuera. SMS solo a celulares.

### 4. Los 160 se calculan con el peor caso, en el backend
Para cada mensaje: `len(texto sin placeholders) + len(nombre) + len(cartera) + len(empresa) + 10 + len(link más largo)`. El asesor
se sortea después, así que usar el link más largo garantiza que **ningún** mensaje final supere 160 y
que el "se pasa en N titulares" del paso 3 sea exactamente el N borrado en el paso 4 —
independiente del sorteo. Vive en el backend porque el front solo tiene 10 filas de muestra.

Estados: 0 excedidos → disponible; 1..10 → "Puede utilizarlo pero se pasa en N titulares" (esos se
excluyen); >10 → visible pero deshabilitado con el motivo. El umbral 10 es del negocio.

### 5. El archivo se re-sube en cada paso
Igual que ADR-031: la huella SHA-256 ata cada paso al archivo validado; si cambia, 409 y el
asistente vuelve al paso 1. Un XLSX de 200k filas pesa ~6 MB; subirlo tres veces es más barato que
bajar 200k nombres al navegador o mantener estado en el servidor. **La huella garantiza el archivo,
no la BD**: si entre validar y generar llegan gestiones, los conteos del paso 4 pueden diferir; los
auditados son los del paso 4.

### 6. Reparto de asesores: equitativo aleatorio
Barajar titulares y repartir en ronda. Ningún asesor recibe más de un titular que otro. Un sorteo
puro desbalancea con pocas filas.

### 7. Link de WhatsApp: `wa.me` generado, sobrescribible
Zentinela es on-premise sin dominio público: no puede servir redirecciones cortas. El sistema propone
`wa.me/57<número>` (18 caracteres, enlace oficial) desde el número corporativo; el líder puede pegar
encima un `wa.link` más corto. Ambos se persisten **en columnas de `Agente`** (`NumeroCorporativo`,
`LinkWhatsapp`, `ContactoActualizadoEn/Por`). Primero se creó una tabla 1:1 aparte por prudencia; el
mismo día se verificó en el servidor que nadie más escribe en `Agente` (sin sp/fn/vistas/triggers/jobs
que la inserten, sin referencias desde otras BDs; el único escritor es `RegistroService` por EF) y se
movieron a `Agente`, como ya se hizo con `Lider_Cartera.EsExterna/Activo`
(`2026-09-18_contacto_virtualidad_en_agente.sql`).

### 8. Alcance por rol
Líder genera solo para sí (`TryResolverLiderID`, ADR-036: el `liderID` de la petición se ignora).
Director y Admin eligen líder. **Líder Externo queda fuera**: sus asesores no están en
`Cartera_Agente` ni en RRHH.

### 9. Lo que NO se hace
- Correo e IVR: tabs visibles y deshabilitadas. Sin formato definido, no se inventa.
- No se registra en `HistoricoVirtualidad`: generar un archivo no es enviarlo. La auditoría propia
  (`EstrategiaSms`) guarda quién, cuándo, qué mensaje y los conteos.

## Costo medido (200.000 filas reales, 19 carteras de lista negra)

| Paso | Tiempo |
|---|---|
| Validar | 38 s |
| Catálogo anotado | 34 s |
| Generar | ~35 s + XLSX |

Cada paso repite el pipeline (parseo ClosedXML + scan de `HistoricoEstadoTitular`, 3M filas sin
índice por `TitularID`, + cruce de lista negra). Con 25 filas el costo fijo es ~7 s: es el scan, no
el archivo. Si se vuelve molesto, el primer candidato es un índice
`IX_HistoricoEstadoTitular_TitularID (TitularID) INCLUDE (UltimoPerfil)` — DBCallCenter no es
nuestra (ADR-030), así que queda documentado y no creado.

## Verificación
- 36 casos de lógica pura: normalización de nombres (incluido mojibake `MARÃA` → `MARIA`), alfabeto
  GSM-7 con llaves solo dentro de placeholders, 160 exacto vs 161, perfiles con tildes y doble
  espacio, validación del catálogo. Todos pasan.
- Flujo completo contra la BD real con 25 filas que cubren cada motivo de exclusión: conteos suman
  exacto; el número de la cartera de lista negra **no** elegida pasa y el de la elegida se excluye;
  409 con huella distinta; 400 con mensaje deshabilitado; XLSX con 8 columnas, teléfonos como texto,
  `max(len(Mensaje)) ≤ 160`, sin `{`, reparto 8/7 entre dos asesores; fila de auditoría coherente.
- 23 asesores del líder 3 resueltos con nombre desde RRHH.

## Corrección del mismo día
El front esperaba `estado` como `0/1/2`, pero la API serializa enums como texto
(`JsonStringEnumConverter`, `Program.cs`). Con el catálogo vacío no había tarjetas y no se notaba; con un
mensaje, `ESTILO_ESTADO["Disponible"]` era `undefined` y React tumbaba la pantalla al pasar al paso 3.
Corregido: el tipo TS es la unión de textos y un estado desconocido cae a "No disponible" en vez de
reventar. Lección: en este proyecto **todo enum llega como string**.

## Deuda visible
- El contacto de los asesores se llena a medida que los líderes lo usan; el primer uso exige escribir
  todos los números. Una carga inicial por SQL aceleraría la adopción.
- Los tests del harness no están en `tests/` porque `Zentinela.API.Tests` no compila (12 errores
  preexistentes en Auth/Carteras). Cuando se arregle, los 36 casos de lógica pura son
  `NormalizadorNombreTests`, `CalculadoraSmsTests`, `MensajesAutorizadosValidacionTests`.
