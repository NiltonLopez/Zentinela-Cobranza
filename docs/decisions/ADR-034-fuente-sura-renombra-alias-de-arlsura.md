# ADR-034 — Integrar la fuente Sura renombra los alias de ARLSura

**Estado:** Aceptado
**Fecha:** 2026-09-02
**Módulos afectados:** Cartera → Creador de Bases · Cartera → Calificador de Cartera · Consultas → Rastreo de Líneas · Consultas → Consulta Integrada · Informes → Informes Generales (Uso de Base de Datos)

---

## Contexto

`dbo.Sura` es una fuente nueva de contactabilidad + afiliación (1:1 por titular): celular, email,
NIT, empresa, tipo de afiliado y estado de afiliación. Se integró por el procedimiento estándar de
`/agregar-fuente-datos`, que asigna a cada columna un alias con el nombre de la fuente como sufijo:
`[Celular Sura]`, `[Email Sura]`, `[NIT Sura]`, y así.

El problema apareció al escribir la migración: **`ARLSura` ya ocupaba `[NIT Sura]` y
`[Empresa Sura]`**. `ARLSura` es una fuente distinta — el producto de riesgos laborales — que se
integró mucho antes y tomó el nombre "Sura" a secas porque en ese momento no había con qué
confundirlo. Al agregar las columnas nuevas, la vista falla:

```
The column 'NIT Sura' was specified multiple times for 'vw_BaseMadre'.
```

No es un choque cosmético: los alias son la clave de mapeo de EF (`HasColumnName`) y la proyección
del export (`SqlAlias`), así que dos fuentes no pueden compartir uno.

El mismo choque se repitió en **tres lugares más**, ninguno de los cuales detecta el compilador:

| Lugar | Síntoma si se ignora |
|---|---|
| Clave `["sura"]` del diccionario `Fuentes` (`CalificadorCarteraService`) | Excepción en runtime al inicializar el diccionario con clave duplicada |
| Alias SQL `SuraEnc` / `SuraNoEnc` del result set 9 | Error de SQL Server por columna duplicada en el `SELECT` |
| `[Empresa Sura] AS EmpresaSura` en el `SELECT INTO #t` | Ídem |

## Decisión

**Se le da el nombre "Sura" a la fuente Sura, y ARLSura pasa a llamarse "ARL Sura".**

| | Antes | Ahora |
|---|---|---|
| Alias SQL | `[NIT Sura]`, `[Empresa Sura]` | `[NIT ARL Sura]`, `[Empresa ARL Sura]` |
| Propiedades C# | `NITSura`, `EmpresaSura` | `NITArlSura`, `EmpresaArlSura` |
| Etiqueta en el panel de localización | `Sura (ARL)` | `ARL Sura` |
| Slug de la fuente | `sura` | `arlsura` |
| Clave del diccionario `Fuentes` | `["sura"]` | `["arlsura"]` |
| Alias del result set 9 | `SuraEnc` / `SuraNoEnc` | `ArlSuraEnc` / `ArlSuraNoEnc` |

Los nombres `sura`, `[NIT Sura]` y `[Empresa Sura]` quedan libres y los toma la fuente Sura.

**Es la primera integración de una fuente que renombra columnas preexistentes.** La alternativa era
sufijar la fuente nueva (`[NIT Sura EPS]`, o similar) y dejar a ARLSura con el nombre corto. Se
descartó porque perpetúa el error: `[NIT Sura]` apuntando a ARLSura es engañoso hoy y lo seguiría
siendo, y el líder que ve la columna no tiene cómo saber a cuál de las dos fuentes corresponde. El
nombre correcto pesa más que la estabilidad del alias viejo.

## Consecuencias

**Costo asumido, y es real:** una **selección de columnas guardada por un líder con el nombre viejo
deja de resolver**. Quien tuviera `NIT Sura` o `Empresa Sura` en una plantilla de export verá que esa
columna ya no aparece y tendrá que volver a elegirla como `NIT ARL Sura`. No hay migración de datos
que lo arregle, porque el nombre viejo ahora es ambiguo: apuntarlo automáticamente a ARL Sura sería
adivinar, y adivinar mal produce una base de marcación con la fuente equivocada.

**La etiqueta que ve el líder cambia** en el Creador (`NIT Sura` → `NIT ARL Sura`) y en el panel de
localización del Calificador (`Sura (ARL)` → `ARL Sura`). Ambas son mejoras de claridad, pero son
cambios visibles sin aviso previo.

**Regla que queda para futuras integraciones:** si el nombre de la fuente nueva es **prefijo o sufijo
de otra ya integrada**, hacer `grep -rn "<NombreFuente>" src/` y revisar cada hit **antes de escribir
nada**. Las cuatro colisiones de este caso eran invisibles al compilador; tres solo se manifiestan al
ejecutar. Registrado como anti-patrón en `.claude/commands/agregar-fuente-datos.md`.

## Verificación

- Migración `docs/sql/migrations/2026-09-02_sura_in_basemadre.sql` aplicada a producción sobre los
  **cuatro** objetos de la base madre: `exit=0`, 8.192 ms.
- Los 8 alias (6 de Sura + 2 renombrados de ARL Sura) se le pidieron **literalmente** a cada uno de
  los cuatro objetos con un `SELECT TOP 0`: existen en todos. Una diferencia de una tilde se habría
  visto recién a media descarga, con el `200` ya enviado.
- Las cuatro listas espejo (`BaseMadreRow`, `BaseMadreRowMapping`, `ColumnasMadre`, `SqlAlias`)
  quedan en **99 columnas exactas** cada una, sin sobrantes ni faltantes en ninguna dirección.
- **Export con las 99 columnas encendidas** contra la cartera 27: el reader entrega 99 campos por
  fila en las 500 filas leídas. Es el escenario que rompió en la regresión de MiVacuna (`1cbd33f`).
- **El renombre no rompió ARLSura**: el Calificador real sobre la cartera 27 devuelve
  `ARL Sura` con 8.689 encontrados / 40.973 no encontrados, y `Sura` en ceros (tabla vacía).
- **Rastreo de Líneas y Uso de Base de Datos devuelven el mismo conjunto de fuentes** para un mismo
  número real (`Cajacopi (celular)`, `Claro`), con 14 fuentes idénticas en ambos servicios.
- La entidad `Sura` de ConsultaIntegrada emite su `SELECT` contra la tabla real sin excepción.

## Hotfix del mismo día — `[NIT Sura]` tumbó el Creador

La primera ejecución de la migración generó la columna como
`ISNULL(CAST(SU.NIT AS VARCHAR(20)), 0)`, es decir **VARCHAR**, contra una propiedad `long? NITSura`.
El preview del Creador hace `SELECT *` y materializa `BaseMadreRow` por EF, así que reventaba con
`InvalidCastException: Unable to cast object of type 'System.String' to type 'System.Int64'`
**en todas las carteras**. El módulo quedó caído desde la primera ejecución de la migración, con el
código viejo tanto como con el nuevo: el alias `[NIT Sura]` existe en ambos, solo cambió de tipo.

Corregido a `ISNULL(SU.NIT, 0)` (bigint, igual que `[NIT ARL Sura]` y `[NIT Positiva]`) en los cuatro
objetos. **Se arregló en SQL y no en C# a propósito**: al ser `CREATE OR ALTER`, re-ejecutar el
archivo repara producción de inmediato, sin desplegar nada y sin depender de qué versión de la API
esté corriendo. Re-aplicado el mismo día, exit=0, 4,3 s; preview verificado OK en las carteras
27, 22, 8, 20 y 4.

**Por qué no lo atraparon las verificaciones previas, que fueron extensas:** todas ejercitaron el
**export** (`StreamFilasAsync`), que proyecta columnas explícitamente y cuyo lector recorría los
campos con `IsDBNull` sin convertirlos al tipo C#. El **preview** (`ObtenerAsync`) es el único camino
que materializa la entidad completa y por tanto el único que compara tipos. Un export verde no dice
nada sobre el preview. Registrado como anti-patrón en `.claude/commands/agregar-fuente-datos.md`.

## Nota sobre el estado de los datos

La tabla `dbo.Sura` estaba **vacía (0 filas)** al integrarse; la carga la hace el equipo aparte.
Hasta entonces la fuente aparece en todos los módulos devolviendo vacío, sin romper nada. Está
marcada `Funcional` en `ESTADO_FUENTE` por decisión del usuario, anticipando la carga.

## Hallazgo colateral: el procedimiento estaba mal documentado en dos puntos

Ejecutar `/agregar-fuente-datos` destapó dos errores del propio comando, ya corregidos:

1. **Los objetos SQL de la base madre son cuatro, no dos.** El comando decía "vista + TVF". Existen
   además `fn_BaseMadreParaTitulares` y `fn_BaseMadreParaTitulares_CarteraChica`, ambos con
   parámetro `dbo.TitularIDList READONLY`. Actualizar solo dos de los cuatro deja el sistema
   inconsistente y EF lanza excepciones de columna faltante en runtime.
2. **`UsoBaseDatosService` es espejo obligatorio de `RastreoLineasService`.** El comando solo
   mencionaba Rastreo. Olvidar el espejo **no produce error**: el informe de Uso de Base de Datos
   simplemente no cuenta esa fuente, y el faltante solo se nota comparando totales contra Rastreo —
   exactamente el tipo de error silencioso que un informe de acreditación no puede permitirse.

Ver ADR-030 (cruce masivo de teléfonos) para el diseño de `UsoBaseDatosService`.
