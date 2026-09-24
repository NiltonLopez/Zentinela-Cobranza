# Respaldo legacy: flujo `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar`

Respaldo de los objetos de **DBCallCenter** que quedan sin uso con el módulo **Cargue de Fuentes de
Datos** (ADR-039) y que se eliminan con los dos scripts de DROP: **12 de la Fase 1** (tomados el
**2026-09-23**) y **31 de la Fase 2** (15 SP + 16 tablas, tomados el **2026-09-24**). Todo directamente de
la BD (10.0.0.89,1433), sin modificarla. Aquí también están las comparaciones de paridad entre cada SP
y el motor nuevo (`PARIDAD*.md`).

## Por qué existen estos archivos

Antes del módulo, cada fuente externa se cargaba a mano en dos pasos:

1. Se volcaba el archivo en la tabla staging `<Fuente>Auxiliar`.
2. Se ejecutaba `sp_Procesar<Fuente>Auxiliar`, que movía el staging a la tabla final, limpiaba datos y
   quitaba duplicados. `Tigo` no tenía SP: se copió a mano desde `TigoAuxiliar` en 2025.

ADR-039 reemplaza ese flujo por un motor genérico en la API: lee el archivo, lo vuelca a tablas `#temp`,
muestra un preview con los conteos y aplica todo en una sola transacción, con auditoría en
`CargueFuenteEjecucion` y reversión en `CargueFuenteReversion` (ver
`docs/sql/migrations/2026-09-24_cargue_fuentes.sql`). Desde la Fase 2 las **22 fuentes de localización**
pasan por el módulo, así que las Auxiliar y sus SP quedan sin uso. Se guardan aquí para poder leer la
lógica que tenían (reglas de limpieza, deduplicación) y para recrearlos si hiciera falta volver atrás.

## Contenido

### SP (texto íntegro de `OBJECT_DEFINITION`)

| Fase | Archivo | Tabla final | SP creado en la BD |
|---|---|---|---|
| 1 | `sp_ProcesarSuraAuxiliar.sql` | Sura | 2026-09-02 |
| 1 | `sp_ProcesarCajacopiAuxiliar.sql` | Cajacopi | 2026-04-13 |
| 1 | `sp_ProcesarMutualSerAuxiliar.sql` | MutualSer | 2026-04-29 |
| 1 | `sp_ProcesarSIMITAuxiliar.sql` | SIMIT | 2026-05-26 |
| 1 | `sp_ProcesarDatosClaroAuxiliar.sql` | DatosClaro | 2026-03-25 |
| 1 | `sp_ProcesarNuevaEpsAuxiliar.sql` | NuevaEps | 2026-06-03 |
| 2 | `sp_ProcesarPositivaAuxiliar.sql` | Positiva | 2026-04-13 |
| 2 | `sp_ProcesarSaludTotalAuxiliar.sql` | SaludTotal | 2026-04-13 |
| 2 | `sp_ProcesarSOSAuxiliar.sql` | SOS | 2026-04-13 |
| 2 | `sp_ProcesarCoosaludAuxiliar.sql` | Coosalud | 2026-08-04 |
| 2 | `sp_ProcesarSaviaSaludAuxiliar.sql` | SaviaSalud | 2026-08-04 |
| 2 | `sp_ProcesarMiVacunaAuxiliar.sql` | MiVacuna | 2026-05-22 |
| 2 | `sp_ProcesarFosygaAuxiliar.sql` | Fosyga | 2026-09-22 |
| 2 | `sp_ProcesarRGMAuxiliar.sql` | RGM | 2026-04-13 |
| 2 | `sp_ProcesarTigoUneAuxiliar.sql` | TigoUne | 2026-08-04 |
| 2 | `sp_ProcesarRUESAuxiliar.sql` | RUES | 2026-04-13 |
| 2 | `sp_ProcesarCamaraAuxiliar.sql` | Camara | 2026-04-13 |
| 2 | `sp_ProcesarCamaraEmpresaAuxiliar.sql` | CamaraEmpresa | 2025-04-23 |
| 2 | `sp_ProcesarPositivaEmpresasAuxiliar.sql` | PositivaEmpresas | 2026-04-13 |
| 2 | `sp_ProcesarArlSuraAuxiliar.sql` | ARLSura | 2026-04-13 |
| 2 | `sp_ProcesarCompensadosAuxiliar.sql` | Compensados | 2026-04-13 |

(`create_date` = `modify_date` en los 21: la mayoría se recrearon en bloque el 2026-04-13.)

Los archivos de SP contienen **exactamente** el texto guardado en la BD, sin cabecera añadida (para que al
recrearlos la definición quede idéntica): `HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp))` es igual al
SHA-256 del texto del archivo (sin BOM, en UTF-16LE). Verificado para los 21 el 2026-09-24 (los de la Fase 1
también el 2026-09-23). Los 21 se crearon con `ANSI_NULLS ON` y `QUOTED_IDENTIFIER ON`.
`sp_ProcesarTigoUneAuxiliar` tiene dos letras cirílicas en su `PRINT` final (`TIGOUНЕ`) y se conservan tal
cual.

### Tablas (`<Tabla>_CREATE.sql`)

Generados desde `sys.columns`, `sys.types`, `sys.default_constraints` y `sys.indexes`. Respaldan **solo la
estructura**, no los datos.

| Fase | Tabla | Filas al respaldar | Creada en la BD | Nota |
|---|---|---:|---|---|
| 1 | `SuraAuxiliar` | 0 | 2026-09-02 | |
| 1 | `CajacopiAuxiliar` | 0 | 2025-06-09 | default `DF_CajacopiAuxiliar_FechaConsulta` |
| 1 | `MutualSerAuxiliar` | 0 | 2026-06-03 | |
| 1 | `SIMITAuxiliar` | 0 | 2025-07-26 | |
| 1 | `DatosClaroAuxiliar` | 0 | 2026-03-19 | |
| 1 | `NuevaEpsAuxiliar` | 0 | 2026-06-02 | |
| 2 | `PositivaAuxiliar` | 0 | 2025-07-14 | |
| 2 | `SaludTotalAuxiliar` | 0 | 2026-01-28 | |
| 2 | `SOSAuxiliar` | 0 | 2025-09-16 | columna `Dirección` con tilde, `varchar(150)` |
| 2 | `CoosaludAuxiliar` | 0 | 2026-08-04 | |
| 2 | `SaviaSaludAuxiliar` | 0 | 2026-08-04 | sin `FechaConsulta`; tiene `Email` (el destino no) |
| 2 | `MiVacunaAuxiliar` | 0 | 2026-05-22 | |
| 2 | `FosygaAuxiliar` | **18.598** | 2025-06-27 | carga **fallida** del 2026-09-23 (ver abajo) |
| 2 | `RGMAuxiliar` | **30.144** | 2025-07-01 | residuo del 2026-08-31, ya en `RGM` |
| 2 | `TigoUneAuxiliar` | 0 | 2026-08-04 | |
| 2 | `TigoAuxiliar` | **661.072** | 2025-03-29 | sin SP; residuo de feb-mar 2025, ya en `Tigo`; sin columna `Nombre` |
| 2 | `RUESAuxiliar` | 0 | 2025-05-07 | |
| 2 | `CamaraAuxiliar` | 0 | 2025-12-06 | |
| 2 | `CamaraEmpresaAuxiliar` | 0 | 2025-07-31 | sin `TitularID` (clave `NitEmpresa`) |
| 2 | `PositivaEmpresasAuxiliar` | 0 | 2025-10-23 | |
| 2 | `ARLSuraAuxiliar` | 0 | 2025-03-25 | |
| 2 | `CompensadosAuxiliar` | 0 | 2026-02-18 | `UltimoPeriodoCompensado varchar(50)` |

Las 22 son **heaps**: sin PK, sin índices, sin FKs, sin CHECKs, sin triggers, filegroup PRIMARY, sin
compresión, collation de columnas igual a la de la BD (`Modern_Spanish_CI_AS`). Los únicos defaults son
`FechaConsulta`/`FechaInsercion = getdate()` donde el `_CREATE.sql` lo indica. Todas las de la Fase 2 menos
`CamaraEmpresaAuxiliar` tienen `LiderID int NULL` como última columna. La firma de columnas (nombre, tipo,
largo, nulabilidad) de cada una se comparó el 2026-09-24 con su `_CREATE.sql`: coinciden.
`TigoAuxiliar_CREATE.sql` se agregó el 2026-09-24 (faltaba: no hay SP de Tigo que respaldar).

Todos los archivos en UTF-8 con BOM, como las migraciones, para que SSMS no rompa los acentos.

**Las tres Auxiliar con filas** (detalle en [`PARIDAD-otras.md`, "Auxiliar con datos"](PARIDAD-otras.md#auxiliar-con-datos)):

- `FosygaAuxiliar`: el import del 2026-09-23 11:33 y el SP corrieron, pero `Nombre`, `CarteraID` y
  `LiderID` venían en NULL y 4.124 titulares no existían: fallaron los `INSERT` a `Titular`,
  `Titular_Cartera` y (por FK) `Fosyga`. ~18.053 filas de afiliación **no están en el destino**. Alguien
  carga Fosyga a mano esa misma semana: hablar con esa persona antes de decidir.
- `RGMAuxiliar`: el 100 % de su contenido está en `RGM` con la fecha de su carga (2026-08-31).
- `TigoAuxiliar`: los 661.072 titulares están en `Tigo`; 661.005 con el mismo contenido, 67 con otro.

## Paridad con el motor nuevo (consolidado)

Cada SP se comparó con el motor usando **los mismos datos de entrada**: el cuerpo del SP corre como batch
ad-hoc con toda tabla que escribe sustituida por `#temp` (**ningún SP se ejecutó**, ni con ROLLBACK, y
ninguna Auxiliar real se tocó); el lado nuevo es el motor real con carga real en un rango reservado de
TitularIDs y limpieza verificada. Cada caso lleva una predicción escrita antes de correr. "Sin explicar" =
diferencia que no está en la leyenda ni en el comentario de la regla (sería un bug). Leyenda de categorías
(E1–E11, P1–P7, R, F, N, =) en [`PARIDAD.md`](PARIDAD.md#leyenda-de-clasificación) y en cada documento.

| Fase / grupo | Fuente | Casos | Idénticos | Diferencias esperadas | Sin explicar | Documento |
|---|---|---:|---:|---:|---:|---|
| 1 | Sura (+ Sura-B) | 25 | 9 | 16 | 0 | [`PARIDAD.md`](PARIDAD.md) |
| 1 | Cajacopi | 20 | 7 | 12 | 1 (C10: solo de documentación, D1) | [`PARIDAD.md`](PARIDAD.md) |
| 1 | MutualSer (A + B + C) | 26 | 11 | 15 | 0 | [`PARIDAD.md`](PARIDAD.md) |
| 1 | NuevaEps | 14 | 5 | 9 | 0 | [`PARIDAD.md`](PARIDAD.md) |
| 1 | SIMIT | 15 | 2 | 13 | 0 | [`PARIDAD.md`](PARIDAD.md) |
| 1 | DatosClaro (+ B) | 19 | 8 | 11 | 0 | [`PARIDAD.md`](PARIDAD.md) |
| **1** | **Total** | **119** | **42** | **76** | **1** | |
| 2 salud | Positiva | 23 | 4 | 19 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 salud | SaludTotal | 19 | 5 | 14 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 salud | SOS (+ SOS-B) | 16 | 4 | 12 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 salud | Coosalud | 16 | 4 | 12 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 salud | SaviaSalud (+ B) | 18 | 3 | 15 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 salud | MiVacuna | 14 | 4 | 10 | 0 | [`PARIDAD-salud.md`](PARIDAD-salud.md) |
| 2 otras | Fosyga (+ B, C) | 21 | 5 | 16 | 0 | [`PARIDAD-otras.md`](PARIDAD-otras.md) |
| 2 otras | RGM (+ B, C) | 22 | 5 | 17 | 0 | [`PARIDAD-otras.md`](PARIDAD-otras.md) |
| 2 otras | TigoUne (+ B) | 20 | 4 | 16 | 0 | [`PARIDAD-otras.md`](PARIDAD-otras.md) |
| 2 otras | Tigo | — | — | — | — | sin SP: no hay paridad posible |
| 2 empresas | RUES | 16 | 6 | 10 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| 2 empresas | Camara | 23 | 7 | 16 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| 2 empresas | CamaraEmpresa | 15 | 4 | 11 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| 2 empresas | PositivaEmpresas | 13 | 3 | 10 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| 2 empresas | ARLSura | 12 | 3 | 9 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| 2 empresas | Compensados (+ B) | 16 | 4 | 12 | 0 | [`PARIDAD-empresas.md`](PARIDAD-empresas.md) |
| **2** | **Total** | **264** | **65** | **199** | **0** | |

(Cajacopi: 20 casos = 7 idénticos + 12 esperadas + 1 sin explicar. El total de la Fase 1 en `PARIDAD.md`
cuenta 77 "diferencias", que incluyen esa.)

**Conclusión: ninguna pérdida ni corrupción de datos atribuible al motor nuevo en ninguna de las 21
fuentes con SP.** Las diferencias son a favor del motor: nombres reparados, lista negra de correos exacta
(no `LIKE`), validación de contacto, clave de contenido completa (historial en vez de borrar la fila vieja),
refresco de fecha del contenido idéntico, sin limpiezas sobre toda la tabla, sin relaciones con pares
inactivos, y fila a fila donde el SP perdía el archivo entero por un valor (F).

**Hallazgos** (detalle y estado en cada documento; en negrita los que siguen pendientes de una decisión):

- Fase 1 (`PARIDAD.md`): B1–B3, D1–D3, **O1** (UltimaFechaConsulta de Sura en la clave), O5 (el nombre
  limpio quita dígitos de razones sociales).
- Salud (`PARIDAD-salud.md`): **S1** `''`/`'0'` legacy vs NULL — la primera recarga crea una fila nueva en
  vez de refrescar (Positiva 140.758 correos `''` y 30.056 celulares `'0'`, Coosalud 32.698, MiVacuna
  1.118; evitable con un script de normalización); S2 datos legacy que el motor ya no produciría; **S3**
  `TelefonoColombiano.Normalizar` no tolera el `.0` de un CSV con decimales; S4 decisiones de definición
  (teléfono como celular; Coosalud/SaviaSalud/MiVacuna crean Titular sin FK).
- Otras (`PARIDAD-otras.md`): **H1** columna `Entero` → `bigint` contra destino `int`; H2 texto del aviso
  para `'555…'`; H3 `IX_Runt_TitularID_Fecha` no existe (corregido en `schema.md`); **H4** carga fallida en
  `FosygaAuxiliar` y Fosyga cargado a mano esta semana; H5 lista blanca de reversión (resuelto).
- Empresas (`PARIDAD-empresas.md`): H1 reversión de RUES y CamaraEmpresa (resuelto); H2 columna Estado
  de Compensados (resuelto: `FlagDesdeArchivo`); H3 `EstadoAfiliacion` de PositivaEmpresas con basura del PDF;
  **H4** 'NIT-DV' en columnas NIT enteras se anula; H5 `schema.md` de Compensados y RUES (resuelto); H6 la
  cifra de 67.826 correos de Cámara (corregida en ADR-039: es una medición sobre `dbo.Camara`, su SP no
  cruzaba la lista negra).

## Estado de dependencias

Comprobado (solo lectura) el 2026-09-23 para la Fase 1 y el 2026-09-24 para la Fase 2:

- `sys.dm_sql_referencing_entities` y `sys.sql_expression_dependencies` sobre cada tabla: el único objeto
  que la referencia es su propio SP. Ningún objeto referencia a los 21 SP.
- Búsqueda de texto con bordes de palabra en `sys.sql_modules`: `sp_ProcesarArlSuraAuxiliar` aparece al
  buscar `SuraAuxiliar` sin bordes (contiene `ARLSuraAuxiliar`; no toca `SuraAuxiliar`), y
  `sp_ProcesarSaludTotalAuxiliar` nombra `RUESAuxiliar` **en un comentario** (copia y pega). Por eso el DROP
  de la Fase 2 excluye de la guarda de texto a todos los SP que se borran juntos.
- Sin sinónimos ni pasos de SQL Agent (`msdb.dbo.sysjobsteps`) que los usen; ningún paquete SSIS.
- `src/` no usa ninguno de los objetos (solo los nombra en comentarios de las reglas).

**Fuera de alcance** (siguen, y por qué, en la cabecera de `2026-09-26_drop_auxiliares_fase2.sql`):
`TitularAuxiliar` + `sp_ProcesarTitularAuxiliar` (titulares y asignaciones, en uso),
`Titular_CarteraAuxiliar`, `ListaNegraTelefonosAuxiliar`, `DatosExternosAuxiliar`, `RuntAuxiliar` (Runt no
entró al cargue) y los `Staging_*` de los ETL.

## El DROP

- **Fase 1:** `docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql` (6 tablas + 6 SP).
- **Fase 2:** `docs/sql/migrations/2026-09-26_drop_auxiliares_fase2.sql` (16 tablas + 15 SP).

Los dos **a mano, después de validar el cargue en producción**, en cualquier orden. Antes de borrar
comprueban que no haya filas, que `HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp))` sea igual al SHA-256 del
texto de estos archivos y que la firma de columnas de cada tabla sea la tomada de la BD (que coincide con
su `_CREATE.sql`), y que nada fuera de la lista use los objetos: si alguien modificó un objeto después del
respaldo, el DROP aborta y hay que respaldar de nuevo. **El de la Fase 2 hoy se bloquea** por las filas de
`FosygaAuxiliar`, `RGMAuxiliar` y `TigoAuxiliar`: vaciarlas es decisión del usuario, con confirmación (ver
la cabecera del script).

## Cómo restaurar

En SSMS o `sqlcmd`, sobre `DBCallCenter`, **primero las tablas y después los SP**
(los SP resuelven nombres al ejecutarse, pero así queda coherente):

```bash
cd docs/sql/legacy
for f in *Auxiliar_CREATE.sql; do
  sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i "$f"
done
for f in sp_Procesar*Auxiliar.sql; do
  sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -I -i "$f"
done
```

- Para restaurar solo una fase, limitar los bucles a sus archivos (tabla "Contenido").
- Los `_CREATE.sql` son idempotentes (`IF OBJECT_ID(...) IS NULL`).
- Los archivos de SP son un `CREATE PROCEDURE` sin `GO` ni `DROP`: fallan si el
  SP ya existe. Para reemplazar uno existente, cambiar `CREATE` por
  `CREATE OR ALTER` antes de ejecutarlo.
- `-I` activa `QUOTED_IDENTIFIER ON`, que es como estaban creados.
- La restauración recupera la estructura, no datos (las filas de `FosygaAuxiliar`, `RGMAuxiliar` y
  `TigoAuxiliar` no se respaldaron).
- Tras restaurar, volver a documentar los objetos en `docs/schema.md`.
