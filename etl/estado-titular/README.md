# Cargador de `UltimoEstadoTitular` (ADR-039)

Reemplaza el **flujo de datos** del paquete SSIS `Carga_UltimoEstadoTitular.dtsx`. La limpieza de
los CSV no cambia: se **importa** `transformar()` del `Conversor_CargaUltimoP.py` original, sin
copiarla. Si alguien cambia una regla de limpieza allá, el cargador la hereda.

| Archivo | Qué hace |
|---|---|
| `descargar.py` | Baja los 19 "Reporte Clientes" de ControlNext (reemplaza al RPA de Power Automate). Credencial cifrada por máquina: `--guardar-credencial` |
| `limpiar.py` | Lee cada CSV con los mismos parámetros del conversor, aplica `transformar()` y escribe un `.part` con separadores de control |
| `desplegar.ps1` | Lo instala en el servidor (ver Despliegue) |
| `cargar.ps1` | Orquesta: corre la limpieza, sube los `.part` con `SqlBulkCopy` y ejecuta `procesar.sql` |
| `procesar.sql` | Imita la columna derivada, el script, la división condicional y las tres búsquedas del paquete, con operaciones sobre conjuntos |

## Por qué

El paquete era lento y frágil por razones que no estaban a la vista:

- **Las tres búsquedas eran sin caché** (`CacheType = 2`): una consulta a la base por fila en cada
  una, del orden de 6 millones de consultas por carga. Acá son tres `JOIN`.
- **Convertía CSV a Excel solo para volver a leerlo**, y ese Excel obligaba al proveedor ACE, el ACE
  a 32 bits, y los 32 bits a cargar por lotes para no pasar el techo de 2 GB.
- **El ACE truncaba la gestión a 255 caracteres** en archivos enteros, según lo que hubiera en las
  primeras 8 filas de la hoja. Y como `transformar()` se queda con los *últimos* 500 caracteres, se
  perdía justo la parte más reciente.
- **Una fila mala tumbaba la carga entera** (cartera sin registrar, titular que no convierte), o
  **se perdía en silencio** (tipo de cartera inactivo).

## Uso

```powershell
# Publicar (lo que corre el Agente)
powershell -NoProfile -ExecutionPolicy Bypass -File cargar.ps1 -Modo publicar

# Comparar contra lo que hay hoy en UltimoEstadoTitular, sin escribir nada
powershell -NoProfile -ExecutionPolicy Bypass -File cargar.ps1 -Modo comparar
```

Parámetros útiles:

| Parámetro | Para qué |
|---|---|
| `-Entrada` | Carpeta de los CSV. En el servidor, `E:\BASES\SISTEMAS\Apps\ReporteClientes` (ruta local, no UNC) |
| `-Python` | Intérprete. En el servidor, el mismo que usa el conversor |
| `-Conversor` / `-Libs` | Dónde están `Conversor_CargaUltimoP.py` y su pandas |
| `-Excluir` / `-Solo` | Carteras a saltear, o procesar solo algunas (pruebas) |
| `-SinLimpiar` | Reutilizar los `.part` ya generados (iterar sobre el SQL sin releer los CSV) |
| `-PandasLocal` | Usar el pandas del intérprete en vez de `-Libs` (pruebas fuera del servidor) |
| `-CadenaConexion` | Por defecto: el `appsettings` de desarrollo si existe, si no autenticación integrada |

## Qué pasa con cada fila

| Caso | En SSIS | Ahora |
|---|---|---|
| Cartera sin registrar en `Cartera.NombreArchivo` | Se caía el paquete entero | La fila se aparta como `cartera-sin-registrar` |
| `Tipo_Cartera` de una cartera inactiva | Se perdía en silencio | Se aparta como `tipo-cartera-inactiva` |
| Identificación que no es número | Se caía el paquete entero | Se aparta como `titular-invalido` |
| Asesor que no está en `Agente` | `AgenteID` nulo | Igual |
| Titular repetido en el archivo | Ganaba una fila al azar | Gana la gestión más reciente y, a igualdad, la última del archivo |
| Gestión de más de 255 caracteres | Truncada, según el archivo | Completa (hasta los 500 que deja `transformar`) |

Los apartados quedan en `dbo.EtlEstadoTitular_Rechazo` (30 días) y resumidos en
`dbo.vw_EtlEstadoTitularRechazos`. Solo archivo, línea y motivo: nada personal.

## Cómo se validó

Contra la carga que hizo SSIS el 2026-09-24 a partir de los **mismos** CSV (el modo `comparar`):

- **Los conjuntos de titulares coinciden exactamente**, cartera por cartera.
- **Todas las columnas coinciden** salvo dos casos, ambos explicados:
  - titulares repetidos en el archivo, donde SSIS elegía al azar cuál fila quedaba;
  - gestiones que SSIS había truncado a 255 (verificado: lo de SSIS es exactamente el comienzo de
    lo nuevo, en el 100 % de los casos).

Y en `docs/sql/pruebas/etl-estado-titular/04_setup_cargador.sql` hay una base de prueba en LocalDB
para correr el modo `publicar` de punta a punta sin tocar producción.

## Despliegue

Ver ADR-039. En corto: `desplegar.ps1` copia esta carpeta al servidor, aplica las migraciones y crea
los trabajos del Agente (`CmdExec` con el proxy `ProxySSIS`, la cuenta que ya lee `E:\BASES`),
corre una comparación, y con `-Publicar` publica. El trabajo viejo queda intacto como plan B.

## Cómo corre (desde 2026-09-26)

Trabajo del Agente `ETL_UltimoEstadoTitular_Cargador`, lunes a viernes 7–17 h cada 2 h y
sábado 7, 9 y 11 h. Sin Power Automate:

1. **Descargar** (`descargar.py`) — una cartera que no baja conserva su CSV anterior.
2. **Cargador** (`cargar.ps1 -Modo publicar`).
3. **Lista Negra** (`EjecutarETL_ListaNegra`), siempre.

## ¿Alguna cartera no se está procesando bien?

```sql
SELECT * FROM DBCallCenter.dbo.vw_EtlEstadoTitularCarteras WHERE Estado <> 'OK';
```

`NO BAJO` (ControlNext no la entregó), `ERROR LIMPIEZA`, `VACIO`, `ARCHIVO VIEJO` (nadie la
actualiza) o `CAIDA DE FILAS` (menos de la mitad que la corrida anterior). Lo mismo sale al final
de `cargador\logs\publicar.log` y en el aviso de WhatsApp de la skill `etl-estado-titular`.
