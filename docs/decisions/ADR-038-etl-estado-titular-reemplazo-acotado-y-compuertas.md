# ADR-038 — El ETL de estado de titulares reemplaza acotado, valida antes de escribir y se audita

**Estado:** Aceptado
**Fecha:** 2026-09-23
**Módulo afectado:** dependencia externa `ETL_UltimoEstadoTitular` (SSIS) → `UltimoEstadoTitular`,
`HistoricoEstadoTitular`. Impacta Comparador de Carteras, Calificador de Cartera y los dashboards.
**Reemplaza el diagnóstico de:** `docs/dependencia-etl-ultimoestadotitular.md` (corregido en el mismo commit)

---

## Qué pasó

`UltimoEstadoTitular` apareció con **cero filas**. El Comparador de Carteras quedó inservible y
nadie lo relacionó con el ETL, porque los módulos no dan error cuando la tabla está vacía: dan
resultados vacíos, que parecen una consulta sin coincidencias.

## La causa NO fue la que documenté primero

El primer diagnóstico culpó al modificador `/X86` del trabajo del Agente, que fuerza el paquete a
32 bits y lo estrella contra el techo de 2 GB de espacio de direcciones. Eso **sí** es un problema
real y sigue siéndolo: explica por qué el ETL viene fallando desde el 17 de septiembre.

Pero **no explica la pérdida de datos**, porque una corrida que revienta por falta de memoria ni
siquiera llega a tocar la tabla final.

Los datos los destruyó una corrida **exitosa**. Del detalle por tarea de la ejecución 11218
(`SSISDB.catalog.executable_statistics`, 18-sep 11:36, 64 bits, `execution_result = 0`):

| Tarea | Duración |
|---|---|
| Tarea Ejecutar SQL | 1,4 s |
| Tarea Ejecutar proceso | 2,2 s |
| Contenedor Foreach Por Cartera | **0,047 s** |
| Tarea Ejecutar SQL → Insertar en tablas principales | **19,5 s** |
| Tarea Ejecutar proceso - Eliminar Excel | 0,1 s |

El Foreach duró 47 milisegundos: iteró **cero veces**, no había ningún Excel que procesar. Y aun
así el paso siguiente corrió 19 segundos y llamó a:

```sql
CREATE PROCEDURE dbo.sp_Insertar_UltimoEstadoTitular
AS
BEGIN
    TRUNCATE TABLE UltimoEstadoTitular;   -- destruye primero
    ;WITH CTE AS (...)                    -- y después ve qué había
    INSERT INTO UltimoEstadoTitular SELECT ... FROM Staging_UltimoEstadoTitular WHERE rn = 1;
END
```

Sin transacción, sin mirar el staging. Truncó la tabla entera, insertó cero, **y el paquete reportó
éxito**. Ningún monitoreo podía detectarlo: en SSISDB esa corrida figura en verde.

> El volumen exacto que se perdió no quedó registrado en ningún lado — esa es justamente una de las
> razones de ser de `EtlEstadoTitular_Ejecucion`. El orden de magnitud es de **~2,3 millones de
> filas**, tomando como referencia el snapshot del 11-sep en el histórico (2.341.627 filas,
> 22 carteras).

> **La lección no es "el ETL falla".** Es que el modo de falla peligroso era el **éxito falso**.
> Un diseño que solo se defiende de los errores no habría evitado nada de esto.

Reproducido en laboratorio antes de escribir el arreglo (SQL Server 2019 LocalDB, tablas con los
tipos reales): con el procedimiento original y el staging vacío, la tabla pasa de **2.201 filas a
0 sin lanzar un solo error**.

## Decisión

### 1. Reemplazo acotado — la defensa estructural

Ya no se borra la tabla entera. Se borran y reinsertan **solo las carteras que vienen en el
staging**:

```sql
DELETE u FROM dbo.UltimoEstadoTitular u
WHERE EXISTS (SELECT 1 FROM #Carteras c WHERE c.CarteraID = u.CarteraID);

INSERT INTO dbo.UltimoEstadoTitular (...) SELECT ... FROM #Nuevo;
```

Esto es lo más importante del ADR, porque **hace el desastre imposible por construcción y no por
vigilancia**: staging vacío son cero carteras que reemplazar, o sea cero filas tocadas. No depende
de que ningún umbral esté bien calibrado ni de que nadie desactive una validación.

Efecto lateral deseable: el ETL deja de pisar lo que el *Cargue de Reportes* de Zentinela
(`CargueReporteService`, ADR-011) haya escrito para carteras que el Excel de esa noche no trae.
Las dos vías que escriben la tabla dejan de estorbarse.

Contrapartida aceptada: si una cartera desaparece del origen de forma legítima, sus filas quedan
hasta que alguien las retire. Es preferible a la alternativa.

### 2. Compuertas — fallar ruidoso en vez de tener éxito en falso

Antes de tocar nada, el procedimiento aborta con `THROW` si:

- el staging está vacío;
- trae menos carteras de las esperadas (referencia: la última corrida buena; tolerancia 10 %, que
  admite retirar una cartera de 22 pero no admite que lleguen 4);
- el volumen cae más del 30 % contra la última corrida buena.

`THROW`, no `RETURN`: SSIS tiene que ver el paso **en rojo**. Los tres límites son parámetros con
valor por defecto, y `@Forzar = 1` los saltea dejando constancia, para el caso legítimo en que el
universo de carteras cambia de verdad.

El paso de histórico tiene su propia compuerta: si `UltimoEstadoTitular` está vacía, aborta **antes
de purgar**. El procedimiento anterior, con la tabla en cero, igual borraba tres meses de histórico
sin insertar nada a cambio — es decir, el incidente se habría comido también el histórico si nadie
lo mira a tiempo.

### 3. Transacción

El borrado y la inserción son un solo acto. Nadie puede leer la tabla a medio reemplazar, y un
fallo en la inserción deshace el borrado.

### 4. Auditoría propia

`EtlEstadoTitular_Ejecucion` registra cada corrida: qué trajo, qué hizo, por qué abortó. Existe
porque **SSISDB no sirve para esto**: reporta éxito aunque la corrida no haya hecho nada. Es además
la referencia de las compuertas y la fuente de `vw_EtlEstadoTitularEstado`, pensada para que el
Comparador y el Calificador muestren "datos al {fecha}" en lugar de cero resultados sin explicar.

### 5. Dos correcciones de determinismo que aparecieron en el camino

- **Dedup del staging.** El `ROW_NUMBER` ordenaba solo por `FechaUltimaGestion`, que es una fecha de
  negocio sin hora y empata muchísimo: el ganador quedaba librado al plan de ejecución. Se agregó
  `FechaActualizacion DESC` como desempate.
- **"Último registro histórico".** Se resolvía con un `JOIN` contra `GROUP BY ... MAX(FechaActualizacion)`.
  Cuando dos filas del mismo `(TitularID, CarteraID)` comparten ese máximo — y pasa, porque la carga
  escribe muchas filas con el mismo timestamp — el join devolvía **las dos**: el `UPDATE` tocaba
  ambas y el `LEFT JOIN` del `INSERT` podía duplicar. `ROW_NUMBER` garantiza exactamente una.

## Por qué NO se tocó el paquete `.dtsx`

El rediseño completo vive en los dos stored procedures, que es donde estaba el daño. Los nombres y
las firmas no cambian y todos los parámetros nuevos tienen valor por defecto, así que la llamada
sin argumentos que hace el paso "Tarea Ejecutar SQL → Insertar en tablas principales" sigue siendo
válida. **No hace falta SSDT, ni redesplegar el proyecto SSIS, ni detener el trabajo del Agente.**

Tampoco cambia el esquema de las dos tablas, así que los servicios de Zentinela que las leen no se
enteran.

## Lo que este ADR NO resuelve

- **El `/X86`.** Sigue haciendo fallar las corridas por memoria. Es un cambio en el trabajo del
  Agente (infraestructura compartida, fuera de Zentinela): ver `docs/dependencia-etl-ultimoestadotitular.md`.
- **La contraseña de `sa` en texto plano** dentro de `msdb.dbo.sysjobsteps.command`. Pendiente, y
  hay que rotarla al sacarla de ahí.
- **Los datos perdidos no vuelven solos.** Los Excel de origen ya no están. Para eso está
  `docs/sql/migrations/2026-09-23_recuperar_ultimoestadotitular.sql`, que reconstruye la tabla desde
  el último estado conocido de cada titular en el histórico. Queda con la antigüedad del histórico,
  no al día; la próxima corrida buena del ETL lo pone al corriente. Entra por el mismo camino que el
  ETL — staging + `sp_Insertar_UltimoEstadoTitular` — para pasar por las mismas compuertas y quedar
  en la misma auditoría, marcada `Origen = 'Recuperacion'`.

## Verificación

Banco de pruebas en `docs/sql/pruebas/etl-estado-titular/`, sobre SQL Server 2019 LocalDB con los
tipos reales de las tres tablas. **20 casos, 20 pasan.** Cubre:

1. Staging vacío — el escenario exacto del 18-sep: aborta, **no toca la tabla**, auditoría en `ABORTADA`.
2. Carga parcial (4 carteras de 22) — lo que quedó en el staging real tras el fallo por memoria:
   aborta. Insertarlo habría dado una tabla que **parece** válida y no lo es.
3. Carga completa: reemplaza las 22 carteras y **la cartera ajena sobrevive** (reemplazo acotado).
4. Dedup determinista: gana la fila más reciente, no la que quiera el plan.
5. Rotación sin cambios: no inserta duplicados, ni siquiera con dos filas de histórico empatadas
   en `FechaActualizacion`.
6. `UltimoEstadoTitular` vacía: el paso de histórico aborta **sin purgar**.
7. La vista de frescura responde.

Y una prueba de regresión aparte, que corre el escenario 1 contra los procedimientos **originales**
y documenta el resultado: `2201 → 0 filas, error lanzado: ninguno`.

## Reversión

`docs/sql/migrations/2026-09-23_rediseno_etl_estado_titular_ROLLBACK.sql` restaura los dos
procedimientos tal como estaban, copiados de `OBJECT_DEFINITION` antes de tocar nada. Conserva la
auditoría, que es justo lo que se querría leer para entender por qué se revirtió.
