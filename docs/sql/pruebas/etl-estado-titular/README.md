# Banco de pruebas — ETL de estado de titulares (ADR-038)

Verifica `sp_Insertar_UltimoEstadoTitular` y `sp_Insertar_HistoricoEstadoTitular` **sin tocar
DBCallCenter**: levanta una base desechable en LocalDB con los tipos reales de las tres tablas.

No reproduce el particionamiento ni el columnstore de `HistoricoEstadoTitular` — los
procedimientos no dependen de eso, y la única lectura de `sys.partitions` funciona igual sobre un
heap.

## Correr las pruebas

```bash
LDB="/c/Program Files/Microsoft SQL Server/150/Tools/Binn/SqlLocalDB.exe"
"$LDB" start MSSQLLocalDB

cd docs/sql/pruebas/etl-estado-titular
SC="/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd"

# 1. Base desechable con el estado de partida
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -b -f 65001 -i 01_setup.sql

# 2. Los procedimientos nuevos (el sed cambia la base de destino)
sed 's/USE DBCallCenter;/USE EtlHarness;/' ../../migrations/2026-09-23_rediseno_etl_estado_titular.sql > _tmp_migracion.sql
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -b -f 65001 -i _tmp_migracion.sql

# 3. Las 20 afirmaciones
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -f 65001 -W -s "|" -i 02_pruebas.sql
```

Esperado: **20 casos, 20 `PASA`**.

Dos detalles que hacen fallar esto en silencio si se cambian:

- `sqlcmd` es un ejecutable de Windows: no entiende rutas estilo `/tmp` de Git Bash, y con una
  ruta absoluta en `-i` falla con "Acceso denegado". Por eso los archivos intermedios se escriben
  en el directorio actual con nombre relativo (`_tmp_*.sql`, ignorados por git).
- El `sed` va **sin ancla `^`**: las migraciones llevan BOM UTF-8 (para que SSMS no rompa los
  acentos), así que `USE` no está al principio de la línea. Con `^USE` el reemplazo no ocurre, el
  script apunta a `DBCallCenter` y —si se está viendo la salida— falla con *Database
  'DBCallCenter' does not exist*. Si se redirige a `/dev/null`, falla sin que se note.

## Prueba de regresión

Demuestra que los procedimientos **originales** sí destruían la tabla. Sobre una base recién
creada, instalando los viejos:

```bash
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -b -f 65001 -i 01_setup.sql
sed 's/USE DBCallCenter;/USE EtlHarness;/' ../../migrations/2026-09-23_rediseno_etl_estado_titular_ROLLBACK.sql > _tmp_rollback.sql
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -b -f 65001 -i _tmp_rollback.sql
"$SC" -S "(localdb)\MSSQLLocalDB" -E -I -f 65001 -W -s "|" -i 03_regresion_procedimientos_viejos.sql
```

Esperado: `filas_antes 2201 | filas_despues 0 | error_lanzado 0`.

## Qué cubre

| # | Caso | Lo que protege |
|---|---|---|
| 1 | Staging vacío | El escenario exacto del 18-sep. Aborta y **no toca la tabla** |
| 2 | Carga parcial (4 de 22 carteras) | Lo que quedó tras el fallo por memoria. Insertarlo daría una tabla que *parece* válida |
| 3 | Carga completa | Reemplazo acotado: las 22 se actualizan y **la cartera ajena sobrevive** |
| 4 | Dedup del staging | Gana la fila más reciente, no la que elija el plan |
| 5 | Histórico, rotación sin cambios | No duplica, ni con dos filas empatadas en `FechaActualizacion` |
| 6 | `UltimoEstadoTitular` vacía | El histórico aborta **sin purgar** |
| 7 | Vista de frescura | Responde con la última carga buena |

## Estado de partida que arma `01_setup.sql`

- 22 carteras × 100 titulares cargadas por el ETL (`CarteraID` 1..22).
- **La cartera 99**, que representa lo que el *Cargue de Reportes* de Zentinela escribió por su
  cuenta. El ETL nocturno no debe pisarla — es lo que verifica el caso 3.
- El mismo contenido replicado en el histórico, para que la referencia de carteras de la primera
  corrida (cuando todavía no hay auditoría) sea 22.
