# Respaldo legacy: flujo `<Fuente>Auxiliar` + `sp_Procesar<Fuente>Auxiliar`

Respaldo de los 12 objetos de **DBCallCenter** que se eliminan al cerrar la
Fase 1 del módulo **Cargue de Fuentes de Datos** (ADR-039). Tomado el
**2026-09-23** directamente de la BD (10.0.0.89,1433), sin modificarla.

## Por qué existen estos archivos

Antes del módulo, cada fuente externa se cargaba a mano en dos pasos:

1. Se volcaba el archivo en la tabla staging `<Fuente>Auxiliar`.
2. Se ejecutaba `sp_Procesar<Fuente>Auxiliar`, que movía el staging a la
   tabla final (`Sura`, `Cajacopi`, `MutualSer`, `SIMIT`, `DatosClaro`,
   `NuevaEps`), limpiaba datos y quitaba duplicados.

ADR-039 reemplaza ese flujo por un motor genérico en la API: lee el archivo,
lo vuelca a tablas `#temp`, muestra un preview con los conteos y aplica todo
en una sola transacción, con auditoría en `CargueFuenteEjecucion` y reversión
en `CargueFuenteReversion` (ver `docs/sql/migrations/2026-09-24_cargue_fuentes.sql`).
Las tablas Auxiliar y sus SP quedan sin uso. Se guardan aquí para poder leer
la lógica que tenían (reglas de limpieza, deduplicación) y para recrearlos si
hiciera falta volver atrás.

## Contenido

| Archivo | Objeto | Origen |
|---|---|---|
| `sp_ProcesarSuraAuxiliar.sql` | SP | `OBJECT_DEFINITION`, texto íntegro |
| `sp_ProcesarCajacopiAuxiliar.sql` | SP | ídem |
| `sp_ProcesarMutualSerAuxiliar.sql` | SP | ídem |
| `sp_ProcesarSIMITAuxiliar.sql` | SP | ídem |
| `sp_ProcesarDatosClaroAuxiliar.sql` | SP | ídem |
| `sp_ProcesarNuevaEpsAuxiliar.sql` | SP | ídem |
| `SuraAuxiliar_CREATE.sql` | Tabla | generado desde `sys.columns`, `sys.types`, `sys.default_constraints`, `sys.indexes` |
| `CajacopiAuxiliar_CREATE.sql` | Tabla | ídem |
| `MutualSerAuxiliar_CREATE.sql` | Tabla | ídem |
| `SIMITAuxiliar_CREATE.sql` | Tabla | ídem |
| `DatosClaroAuxiliar_CREATE.sql` | Tabla | ídem |
| `NuevaEpsAuxiliar_CREATE.sql` | Tabla | ídem |

Todos en UTF-8 con BOM, como las migraciones, para que SSMS no rompa los acentos.

Los archivos de SP contienen **exactamente** el texto guardado en la BD: se
verificó carácter por carácter contra `OBJECT_DEFINITION` (sin cabecera
añadida, para que al recrearlos la definición quede idéntica). Los seis se
crearon con `ANSI_NULLS ON` y `QUOTED_IDENTIFIER ON`.

Las seis tablas son **heaps**: sin PK, sin índices, sin FKs, sin CHECKs, sin
triggers, filegroup PRIMARY, sin compresión, collation de columnas igual a la
de la BD (`Modern_Spanish_CI_AS`). El único default es
`DF_CajacopiAuxiliar_FechaConsulta = (getdate())`.

## Estado al momento del respaldo

| Tabla | Filas (`COUNT(*)`) | Creada en la BD | SP asociado | SP creado |
|---|---:|---|---|---|
| `SuraAuxiliar` | 0 | 2026-09-02 | `sp_ProcesarSuraAuxiliar` | 2026-09-02 |
| `CajacopiAuxiliar` | 0 | 2025-06-09 | `sp_ProcesarCajacopiAuxiliar` | 2026-04-13 |
| `MutualSerAuxiliar` | 0 | 2026-06-03 | `sp_ProcesarMutualSerAuxiliar` | 2026-04-29 |
| `SIMITAuxiliar` | 0 | 2025-07-26 | `sp_ProcesarSIMITAuxiliar` | 2026-05-26 |
| `DatosClaroAuxiliar` | 0 | 2026-03-19 | `sp_ProcesarDatosClaroAuxiliar` | 2026-03-25 |
| `NuevaEpsAuxiliar` | 0 | 2026-06-02 | `sp_ProcesarNuevaEpsAuxiliar` | 2026-06-03 |

Las seis estaban vacías: no se perdió ningún dato al respaldar solo la estructura.

Dependencias comprobadas el mismo día:

- `sys.dm_sql_referencing_entities` sobre cada tabla: el único objeto que la
  referencia es su propio SP. Ningún objeto referencia a los seis SP.
- Búsqueda de texto en `sys.sql_modules`: sin otras referencias
  (`sp_ProcesarArlSuraAuxiliar` aparece al buscar `SuraAuxiliar` solo porque
  contiene `ARLSuraAuxiliar`; no toca `SuraAuxiliar`).
- Sin sinónimos ni pasos de SQL Agent (`msdb.dbo.sysjobsteps`) que los usen.
- `src/` no referencia ninguno de los 12 objetos.

**Fuera de alcance:** `ARLSuraAuxiliar` / `sp_ProcesarArlSuraAuxiliar` y el
resto de tablas Auxiliar (Coosalud, Positiva, RUES, Titular, etc.) no forman
parte de este respaldo ni de la eliminación de la Fase 1.

## El DROP

`docs/sql/migrations/2026-09-25_drop_auxiliares_fase1.sql` — **a mano, después de validar el cargue en
producción**. Antes de borrar comprueba que `HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp))` sea igual al
SHA-256 del texto de estos archivos (sin BOM, en UTF-16LE) y que la firma de columnas de cada tabla
(nombre, tipo, largo, nulabilidad) sea la tomada de la BD el 2026-09-24, que coincide con su
`_CREATE.sql` (ninguna tabla se modificó después del respaldo): si alguien modificó un objeto después de este respaldo, el DROP aborta y hay que
respaldar de nuevo. Verificado igual el 2026-09-24 para los seis SP.

## Cómo restaurar

En SSMS o `sqlcmd`, sobre `DBCallCenter`, **primero las tablas y después los SP**
(los SP resuelven nombres al ejecutarse, pero así queda coherente):

```bash
cd docs/sql/legacy
for t in SuraAuxiliar CajacopiAuxiliar MutualSerAuxiliar SIMITAuxiliar DatosClaroAuxiliar NuevaEpsAuxiliar; do
  sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i "${t}_CREATE.sql"
done
for f in sp_Procesar*Auxiliar.sql; do
  sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -I -i "$f"
done
```

- Los `_CREATE.sql` son idempotentes (`IF OBJECT_ID(...) IS NULL`).
- Los archivos de SP son un `CREATE PROCEDURE` sin `GO` ni `DROP`: fallan si el
  SP ya existe. Para reemplazar uno existente, cambiar `CREATE` por
  `CREATE OR ALTER` antes de ejecutarlo.
- `-I` activa `QUOTED_IDENTIFIER ON`, que es como estaban creados.
- La restauración recupera la estructura, no datos (las tablas estaban vacías).
- Tras restaurar, volver a documentar los objetos en `docs/schema.md`.
