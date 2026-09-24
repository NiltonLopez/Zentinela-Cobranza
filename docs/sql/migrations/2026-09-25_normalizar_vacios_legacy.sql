-- ============================================================================
-- Normalizar los vacíos legacy de las fuentes antes de su primera carga
-- — DBCallCenter (ADR-039, hallazgo S1 de docs/sql/legacy/PARIDAD-salud.md)
--
--   POR QUÉ
--   Los SP legacy (sp_Procesar<Fuente>Auxiliar) guardaban centinelas donde no
--   había dato: '' , '0', 'NULL', 'N/A', solo espacios, NIT 0, fechas
--   1900-01-01; y copiaban los textos con espacios y saltos de línea de sobra.
--   El Cargue de Fuentes (CargueFuentesMotor) guarda NULL en esos casos y deja
--   los textos recortados y con los blancos colapsados. Para decidir entre
--   "refrescar la fecha" e "insertar fila nueva" compara el contenido con
--   igualdad NULL-safe (EXISTS (SELECT … INTERSECT SELECT …)), y ahí '' no es
--   NULL, ni 'A  B' es 'A B'. Resultado sin este script: la PRIMERA recarga de
--   cada titular con centinelas no refresca su fila, inserta otra con el mismo
--   dato (una fila de historial de más por titular: ~170 mil solo en Positiva).
--
--   CRITERIO (el MISMO del motor; si uno cambia, cambiar el otro)
--   - Texto (varchar: textos, teléfonos, correos, placas) = LimpiarTexto:
--       blancos (los que char.IsWhiteSpace reconoce en el code page 1252 de las
--       columnas: CHAR(9) a CHAR(13), CHAR(32) y CHAR(160)) -> un espacio;
--       espacios repetidos -> uno; recorte a los dos lados; y si lo que queda es
--       '', '0', 'NULL' o 'N/A' (sin distinguir mayúsculas) -> NULL.
--       Se compara en Latin1_General_BIN y con DATALENGTH: en Modern_Spanish_CI_AS
--       '=' ignora los espacios finales y las mayúsculas.
--   - Entero (bigint/int: NIT, NitEmpresa, NumeroMatricula, Modelo): 0 -> NULL.
--   - Fecha (date): año <= 1900 -> NULL ("1900-01-01 es el vacío de Excel").
--   - Flag (EstadoEn<Fuente>; SaviaSalud 'Estado', ARLSura y CamaraEmpresa con
--     nombre prestado): solo en las filas donde alguna columna que entra al flag
--     pasa a NULL, se recalcula como CalcularFlag: 1 si queda alguna columna con
--     EntraAlFlag no nula, 0 si no. El resto de las filas conserva su flag.
--     Excepción: Compensados. Su flag también sale del Estado del archivo
--     (FlagDesdeArchivo, commit 36aa398: "encontrado aunque sin EPS ni período"),
--     así que NO se recalcula desde las columnas de dato: sus 2.315 filas con
--     EstadoEnCompensados = 1 y sin EPS ni período son válidas y se quedan como
--     están. Hoy Compensados no tiene nada que normalizar y no tiene bloque.
--   Columnas y EntraAlFlag: las de CatalogoCargueFuentes (src/Zentinela.Application/
--   Constants/CatalogoCargueFuentes.cs) al 2026-09-24.
--
--   CIFRAS (medidas el 2026-09-24; por columna: a NULL / con blancos de sobra;
--   * = entra al flag)
--     Positiva         2.213.891 filas   170.455 a tocar
--         NumeroTelefonoCelular*  30.056 ('0') / 0     Email* 140.758 ('') / 0
--         NumeroTelefonoFijo*          2 / 0           FechaNacimiento 65 (<= 1900) / 0
--     PositivaEmpresas 2.013.318 filas   102.661 a tocar
--         Empresa* 0 / 102.651 (94.096 con saltos de línea: texto de PDF)
--         EstadoAfiliacion 0 / 42      NIT* 1 (0) / 0
--     Coosalud         1.037.887 filas   101.539 a tocar
--         Email* 32.698 ('') / 1       DepartamentoOrigen 31.754 / 68.729 (espacio inicial)
--         MunicipioOrigen, Regimen, TipoAfiliado, EstadoAfiliacion 31.754 ('') c/u
--         NivelSisben 31.952 ('')      (31.754 filas legacy con '' en las seis)
--     SaludTotal       1.586.180 filas    37.297 a tocar
--         Ciudad 0 / 32.742   Empresa 0 / 4.291   Cargo 564 ('')   IngresoBase 101 ('0')
--         UltimaFechaPago 17 (<= 1900)   Email* 2 / 75   NumeroTelefono* 14 ('0') / 0
--     SaviaSalud         719.743 filas    31.804 a tocar
--         TipoAfiliado, EstadoDeAfiliacion, Regimen 31.804 ('') c/u
--     TigoUne            123.783 filas    27.300 a tocar    Email* 27.166 ('') / 134
--     ARLSura          1.565.455 filas    18.043 a tocar
--         Empresa* 10 ('NULL' 7, '0' 2, ' ' 1) / 18.033 (dobles espacios, tabuladores)
--     MutualSer          864.703 filas     8.078 a tocar
--         Email* 8.077 ('') / 0   NumeroTelefonoCelular* 1 / 0   NumeroTelefonoCelular2* 0 / 1
--     Fosyga           2.140.272 filas     1.681 a tocar
--         Municipio 136 / 1.465   Departamento 136 / 0   EPS* 0 / 76   Estado* 0 / 70
--     MiVacuna            36.985 filas     1.118 a tocar    Email* 1.118 ('' y ' ') / 0
--     Cajacopi         1.277.608 filas       204 a tocar    Departamento 45 / 157   Email* 2 / 0
--     SOS                226.946 filas       129 a tocar    RazonSocial* 0 / 123   Telefono* 6 / 0
--     Camara           1.860.429 filas       103 a tocar
--         NumeroTelefono* 91 ('0') / 0   Email* 0 / 8   Municipio 0 / 3   Departamento 1 / 0
--     Tigo             1.026.333 filas        63 a tocar    Email* 0 / 63 (salto de línea inicial)
--     CamaraEmpresa       71.424 filas        40 a tocar    NumeroTelefono* 37 / 0   Email* 1 / 2
--     DatosClaro       3.528.387 filas        27 a tocar    Email 0 / 27 (tabulador inicial)
--     RUES               873.188 filas        24 a tocar    NombreEmpresa 9 / 5   ActividadEconomica* 0 / 10
--     NuevaEps            62.834 filas        18 a tocar    NumeroTelefono* 18 ('0') / 0
--     RGM              1.663.543 filas         7 a tocar    Placa* 1 ('0') / 6
--   Total: 500.591 filas en 19 tablas.
--   Nada que hacer: Sura (3.749 filas) y Compensados (366.072; ni centinelas ni
--   blancos de sobra en EPS, UltimoPeriodoCompensado ni TipoAfiliado; su flag
--   no se toca, ver CRITERIO).
--   Fuera de este script: SIMIT (9 placas '0'/'N/A'/'NULL'), porque
--   2026-09-24_simit_placas_legacy.sql ya las pasa a "sin placa" con la política
--   de vacías de SIMIT (que además borra filas; este script no borra).
--
--   FLAG 1 -> 0 (filas / titulares que se quedan sin NINGUNA fila con flag 1)
--     Positiva 29.882 / 28.683   MiVacuna 269 / 269   Camara 69 / 57
--     CamaraEmpresa 13 / 6       ARLSura 1 / 1        MutualSer 1 / 1
--     Las demás: 0. Flag 0 -> 1: ninguno. Eran filas cuyo único "dato" era el
--     centinela (Positiva: celular '0' con email ''), que el SP contaba como dato.
--   Qué cambia con eso:
--     - La columna [Estado <Fuente>] de la BaseMadre (fn_BaseMadre*, vw_BaseMadre:
--       toman la fila más reciente) y las vistas vw_Datos<Fuente> /
--       vw_SinDatos<Fuente>: esos titulares pasan de "con datos" a "sin datos".
--     - El próximo cargue de la fuente trata esas filas como vacías: las purga si
--       el titular llega con datos (DELETE flag = 0), como a cualquier vacía.
--     - El panel de localización del Calificador NO usa el flag: evalúa los
--       valores con NULLIF(NULLIF(x, ''), '0'), que ya trata '', '0' y los
--       blancos como vacíos. Solo cambia donde el centinela era 'NULL', 'N/A' o
--       una fecha <= 1900: 16 titulares de Positiva (FechaNacimiento <= 1900 ->
--       [Edad Positiva] deja de valer ~126) pasan de encontrado a no encontrado.
--
--   QUÉ NO HACE
--     - No replica validaciones de formato: celulares/fijos inválidos, correos
--       mal formados o en lista negra, placas que no son placa, NIT-DV, etc. se
--       quedan como están (el motor los anula al cargar; esos titulares SÍ
--       tendrán una fila nueva en la primera recarga).
--     - No replica reglas propias de fuente: 'NO TIENE' y fijos '11111' de
--       Cajacopi, 'NO SE ENCUENTRA' de Compensados, textos de relleno en
--       Departamento de Cámara, fijos concatenados de Positiva, listas de
--       SaviaSalud, prefijos de fijos de 7 dígitos.
--     - No cambia mayúsculas (la comparación del cargue es CI: no hace falta).
--     - No borra filas ni deduplica: una fila que queda toda en NULL (p. ej. las
--       31.754 de Coosalud) sigue ahí con flag 0; el cargue la purga cuando llegue
--       ese titular con datos.
--     - No toca SIMIT, Sura, Compensados (ni su flag), las tablas Auxiliar, los SP
--       ni las vistas.
--
--   CUÁNDO Y CÓMO SE CORRE
--     Antes de la PRIMERA carga de cada fuente con el Cargue de Fuentes, fuera
--     de horario: cada tabla se bloquea en exclusiva (TABLOCKX) mientras se
--     normaliza, y la BaseMadre, el Calificador y las vistas que la leen esperan.
--     Duración medida con ROLLBACK el 2026-09-24: Positiva 52 s, Coosalud 37 s,
--     ARLSura 32 s, MiVacuna 1 s; las 19 tablas, ~10-15 min (estimado), más ~7 min del
--     bloque 0 si se corre para todas.
--     1. Editar @Solo en el primer lote: la fuente ('Positiva') o NULL = todas.
--     2. Bloque 0 (solo lectura): comparar con las cifras de arriba.
--     3. El resto: una transacción por tabla (XACT_ABORT). Cada tabla aborta sin
--        cambios si hay un cargue de esa fuente EN CURSO, si hay muchas más filas
--        que las medidas, o si al final quedan centinelas, cambia el número de
--        filas o el flag no quedó como se calculó. Con @Solo = NULL, una tabla ya
--        normalizada (tiene respaldo) se salta; con @Solo = esa fuente, THROW.
--   Aplicar con: sqlcmd -b -f 65001 -i <este archivo>   (o abrirlo en SSMS).
--   (-b: parar en el primer error; sin -b sigue con la tabla siguiente.)
--
--   CÓMO REVERTIR
--     Cada tabla guarda sus filas tocadas, tal como estaban, en
--     dbo.<Tabla>_RespaldoVacios_20260925 (con el RID de la fila). Script al
--     final (comentado), por tabla. OJO: si después corrió un cargue de esa
--     fuente, revertir devuelve los centinelas a filas que ese cargue ya refrescó.
--
--   Fecha: 2026-09-24 (cifras medidas ese día; se aplica desde el 2026-09-25).
-- ============================================================================

USE DBCallCenter;
GO

-- ============================================================================
-- PARÁMETRO. Fuente a normalizar (la clave de CatalogoCargueFuentes: 'Positiva',
-- 'Coosalud'...) o NULL = todas. Viaja en SESSION_CONTEXT para que lo lean
-- todos los lotes (GO) del script en la misma conexión.
-- ============================================================================
SET NOCOUNT ON;
DECLARE @Solo varchar(30) = NULL;          -- <<< EDITAR: p. ej. 'Positiva'

-- Primero "ninguna": si una validación de abajo falla y se sigue sin -b, los
-- lotes siguientes no hacen nada (en vez de leer un NULL = todas).
EXEC sys.sp_set_session_context @key = N'NormalizarVacios.Solo', @value = '(ninguna)';

IF @Solo = 'SIMIT'
    THROW 50001, 'SIMIT no se normaliza aquí: usar 2026-09-24_simit_placas_legacy.sql (cubre también sus 9 placas centinela).', 1;
IF @Solo IS NOT NULL AND @Solo NOT IN ('Cajacopi', 'MutualSer', 'NuevaEps', 'DatosClaro', 'Positiva', 'SaludTotal', 'SOS', 'Coosalud', 'SaviaSalud', 'MiVacuna', 'Fosyga', 'RGM', 'TigoUne', 'Tigo', 'RUES', 'Camara', 'CamaraEmpresa', 'PositivaEmpresas', 'ARLSura', 'Sura', 'Compensados')
    THROW 50002, 'Fuente desconocida en @Solo: usar la clave de CatalogoCargueFuentes (p. ej. ''Positiva'').', 1;
IF @Solo IN ('Sura', 'Compensados')
    PRINT CONCAT(@Solo, ': no tiene nada que normalizar (medido 2026-09-24).');

EXEC sys.sp_set_session_context @key = N'NormalizarVacios.Solo', @value = @Solo;
PRINT CONCAT('Normalizar vacíos legacy: ', ISNULL(@Solo, 'todas las fuentes'));
GO

-- ============================================================================
-- BLOQUE 0. Diagnóstico (solo lectura). Una fila por columna con algo que
-- normalizar, al lado de lo medido el 2026-09-24. Recorre las tablas enteras:
-- todas juntas tardan ~7 min; con @Solo, solo esa fuente.
-- ============================================================================
SET NOCOUNT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));

IF @Solo IS NULL OR @Solo = 'Cajacopi'
SELECT 'Cajacopi' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 204 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                         OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
                         OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnCajacopi] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL AND (s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[NumeroTelefonoFijo] IS NOT NULL AND (s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e3,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL THEN 1 ELSE 0 END) AS c5,
               0 AS e5
          FROM dbo.[Cajacopi] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                      CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
                      CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[FechaNacimiento] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnCajacopi] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefonoCelular', a.c0, a.e0, 0, 0),
              ('NumeroTelefonoFijo', a.c1, a.e1, 0, 0),
              ('Email', a.c2, a.e2, 2, 0),
              ('Departamento', a.c3, a.e3, 45, 157),
              ('Municipio', a.c4, a.e4, 0, 0),
              ('FechaNacimiento', a.c5, a.e5, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'MutualSer'
SELECT 'MutualSer' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 8078 AS FilasAfectadasMedido, a.Flag1a0, 1 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                         OR (s.[NumeroTelefonoCelular2] IS NOT NULL AND (v.[NumeroTelefonoCelular2] IS NULL OR s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular2] OR DATALENGTH(s.[NumeroTelefonoCelular2]) <> DATALENGTH(v.[NumeroTelefonoCelular2])))
                         OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnMutualSer] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL AND (s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[NumeroTelefonoCelular2] IS NOT NULL AND v.[NumeroTelefonoCelular2] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[NumeroTelefonoCelular2] IS NOT NULL AND (s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular2] OR DATALENGTH(s.[NumeroTelefonoCelular2]) <> DATALENGTH(v.[NumeroTelefonoCelular2])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[NumeroTelefonoFijo] IS NOT NULL AND (s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e3,
               SUM(CASE WHEN s.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[Regimen] IS NOT NULL AND (s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e5,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c6,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e6,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c7,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e7
          FROM dbo.[MutualSer] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular2],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                      CASE WHEN UPPER(n.[NumeroTelefonoCelular2]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular2] END AS [NumeroTelefonoCelular2],
                      CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (s.[NumeroTelefonoCelular2] IS NOT NULL AND v.[NumeroTelefonoCelular2] IS NULL) OR (s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoCelular2] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnMutualSer] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefonoCelular', a.c0, a.e0, 1, 0),
              ('NumeroTelefonoCelular2', a.c1, a.e1, 0, 1),
              ('NumeroTelefonoFijo', a.c2, a.e2, 0, 0),
              ('Email', a.c3, a.e3, 8077, 0),
              ('Regimen', a.c4, a.e4, 0, 0),
              ('TipoAfiliado', a.c5, a.e5, 0, 0),
              ('Departamento', a.c6, a.e6, 0, 0),
              ('Municipio', a.c7, a.e7, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'NuevaEps'
SELECT 'NuevaEps' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 18 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnNuevaEps] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[EstadoAfiliacion] IS NOT NULL AND (s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL THEN 1 ELSE 0 END) AS c3,
               0 AS e3,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e5
          FROM dbo.[NuevaEps] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL) OR (s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[EstadoAfiliacion] IS NOT NULL OR v.[TipoAfiliado] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnNuevaEps] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 18, 0),
              ('EstadoAfiliacion', a.c1, a.e1, 0, 0),
              ('TipoAfiliado', a.c2, a.e2, 0, 0),
              ('FechaNacimiento', a.c3, a.e3, 0, 0),
              ('Departamento', a.c4, a.e4, 0, 0),
              ('Municipio', a.c5, a.e5, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'DatosClaro'
SELECT 'DatosClaro' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 27 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               0 AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1
          FROM dbo.[DatosClaro] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 0, 0),
              ('Email', a.c1, a.e1, 0, 27)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'Positiva'
SELECT 'Positiva' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 170455 AS FilasAfectadasMedido, a.Flag1a0, 29882 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                         OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnPositiva] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL AND (s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[NumeroTelefonoFijo] IS NOT NULL AND (s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL THEN 1 ELSE 0 END) AS c3,
               0 AS e3,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e5
          FROM dbo.[Positiva] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                      CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (s.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnPositiva] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefonoCelular', a.c0, a.e0, 30056, 0),
              ('NumeroTelefonoFijo', a.c1, a.e1, 2, 0),
              ('Email', a.c2, a.e2, 140758, 0),
              ('FechaNacimiento', a.c3, a.e3, 65, 0),
              ('Departamento', a.c4, a.e4, 0, 0),
              ('Municipio', a.c5, a.e5, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'SaludTotal'
SELECT 'SaludTotal' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 37297 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                         OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[Ciudad] IS NOT NULL AND (v.[Ciudad] IS NULL OR s.[Ciudad] COLLATE Latin1_General_BIN <> v.[Ciudad] OR DATALENGTH(s.[Ciudad]) <> DATALENGTH(v.[Ciudad])))
                         OR (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                         OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
                         OR (s.[Cargo] IS NOT NULL AND (v.[Cargo] IS NULL OR s.[Cargo] COLLATE Latin1_General_BIN <> v.[Cargo] OR DATALENGTH(s.[Cargo]) <> DATALENGTH(v.[Cargo])))
                         OR (s.[UltimaFechaPago] IS NOT NULL AND v.[UltimaFechaPago] IS NULL)
                         OR (s.[IngresoBase] IS NOT NULL AND (v.[IngresoBase] IS NULL OR s.[IngresoBase] COLLATE Latin1_General_BIN <> v.[IngresoBase] OR DATALENGTH(s.[IngresoBase]) <> DATALENGTH(v.[IngresoBase]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnSaludTotal] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL THEN 1 ELSE 0 END) AS c2,
               0 AS e2,
               SUM(CASE WHEN s.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[EstadoAfiliacion] IS NOT NULL AND (s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])) THEN 1 ELSE 0 END) AS e3,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[Ciudad] IS NOT NULL AND v.[Ciudad] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[Ciudad] IS NOT NULL AND (s.[Ciudad] COLLATE Latin1_General_BIN <> v.[Ciudad] OR DATALENGTH(s.[Ciudad]) <> DATALENGTH(v.[Ciudad])) THEN 1 ELSE 0 END) AS e5,
               SUM(CASE WHEN s.[NIT] IS NOT NULL AND v.[NIT] IS NULL THEN 1 ELSE 0 END) AS c6,
               0 AS e6,
               SUM(CASE WHEN s.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL THEN 1 ELSE 0 END) AS c7,
               SUM(CASE WHEN v.[Empresa] IS NOT NULL AND (s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])) THEN 1 ELSE 0 END) AS e7,
               SUM(CASE WHEN s.[Cargo] IS NOT NULL AND v.[Cargo] IS NULL THEN 1 ELSE 0 END) AS c8,
               SUM(CASE WHEN v.[Cargo] IS NOT NULL AND (s.[Cargo] COLLATE Latin1_General_BIN <> v.[Cargo] OR DATALENGTH(s.[Cargo]) <> DATALENGTH(v.[Cargo])) THEN 1 ELSE 0 END) AS e8,
               SUM(CASE WHEN s.[UltimaFechaPago] IS NOT NULL AND v.[UltimaFechaPago] IS NULL THEN 1 ELSE 0 END) AS c9,
               0 AS e9,
               SUM(CASE WHEN s.[IngresoBase] IS NOT NULL AND v.[IngresoBase] IS NULL THEN 1 ELSE 0 END) AS c10,
               SUM(CASE WHEN v.[IngresoBase] IS NOT NULL AND (s.[IngresoBase] COLLATE Latin1_General_BIN <> v.[IngresoBase] OR DATALENGTH(s.[IngresoBase]) <> DATALENGTH(v.[IngresoBase])) THEN 1 ELSE 0 END) AS e10
          FROM dbo.[SaludTotal] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Ciudad] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Ciudad],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Cargo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Cargo],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[IngresoBase] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [IngresoBase]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
                      CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN UPPER(n.[Ciudad]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Ciudad] END AS [Ciudad],
                      NULLIF(s.[NIT], 0) AS [NIT],
                      CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
                      CASE WHEN UPPER(n.[Cargo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Cargo] END AS [Cargo],
                      CASE WHEN s.[UltimaFechaPago] < '19010101' THEN NULL ELSE s.[UltimaFechaPago] END AS [UltimaFechaPago],
                      CASE WHEN UPPER(n.[IngresoBase]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[IngresoBase] END AS [IngresoBase]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[NIT] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnSaludTotal] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 14, 0),
              ('Email', a.c1, a.e1, 2, 75),
              ('FechaNacimiento', a.c2, a.e2, 0, 0),
              ('EstadoAfiliacion', a.c3, a.e3, 0, 0),
              ('TipoAfiliado', a.c4, a.e4, 0, 0),
              ('Ciudad', a.c5, a.e5, 0, 32742),
              ('NIT', a.c6, a.e6, 0, 0),
              ('Empresa', a.c7, a.e7, 0, 4291),
              ('Cargo', a.c8, a.e8, 564, 0),
              ('UltimaFechaPago', a.c9, a.e9, 17, 0),
              ('IngresoBase', a.c10, a.e10, 101, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'SOS'
SELECT 'SOS' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 129 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL)
                         OR (s.[RazonSocial] IS NOT NULL AND (v.[RazonSocial] IS NULL OR s.[RazonSocial] COLLATE Latin1_General_BIN <> v.[RazonSocial] OR DATALENGTH(s.[RazonSocial]) <> DATALENGTH(v.[RazonSocial])))
                         OR (s.[Telefono] IS NOT NULL AND (v.[Telefono] IS NULL OR s.[Telefono] COLLATE Latin1_General_BIN <> v.[Telefono] OR DATALENGTH(s.[Telefono]) <> DATALENGTH(v.[Telefono])))
                         OR (s.[Direccion] IS NOT NULL AND (v.[Direccion] IS NULL OR s.[Direccion] COLLATE Latin1_General_BIN <> v.[Direccion] OR DATALENGTH(s.[Direccion]) <> DATALENGTH(v.[Direccion]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnSOS] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[RazonSocial] IS NOT NULL AND v.[RazonSocial] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[RazonSocial] IS NOT NULL AND (s.[RazonSocial] COLLATE Latin1_General_BIN <> v.[RazonSocial] OR DATALENGTH(s.[RazonSocial]) <> DATALENGTH(v.[RazonSocial])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Telefono] IS NOT NULL AND v.[Telefono] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Telefono] IS NOT NULL AND (s.[Telefono] COLLATE Latin1_General_BIN <> v.[Telefono] OR DATALENGTH(s.[Telefono]) <> DATALENGTH(v.[Telefono])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[Direccion] IS NOT NULL AND v.[Direccion] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[Direccion] IS NOT NULL AND (s.[Direccion] COLLATE Latin1_General_BIN <> v.[Direccion] OR DATALENGTH(s.[Direccion]) <> DATALENGTH(v.[Direccion])) THEN 1 ELSE 0 END) AS e3
          FROM dbo.[SOS] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[RazonSocial] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [RazonSocial],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Telefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Telefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Direccion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Direccion]) n
         CROSS APPLY (SELECT NULLIF(s.[NitEmpresa], 0) AS [NitEmpresa],
                      CASE WHEN UPPER(n.[RazonSocial]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[RazonSocial] END AS [RazonSocial],
                      CASE WHEN UPPER(n.[Telefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Telefono] END AS [Telefono],
                      CASE WHEN UPPER(n.[Direccion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Direccion] END AS [Direccion]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL) OR (s.[RazonSocial] IS NOT NULL AND v.[RazonSocial] IS NULL) OR (s.[Telefono] IS NOT NULL AND v.[Telefono] IS NULL) OR (s.[Direccion] IS NOT NULL AND v.[Direccion] IS NULL)
                              THEN CASE WHEN v.[NitEmpresa] IS NOT NULL OR v.[RazonSocial] IS NOT NULL OR v.[Telefono] IS NOT NULL OR v.[Direccion] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnSOS] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NitEmpresa', a.c0, a.e0, 0, 0),
              ('RazonSocial', a.c1, a.e1, 0, 123),
              ('Telefono', a.c2, a.e2, 6, 0),
              ('Direccion', a.c3, a.e3, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'Coosalud'
SELECT 'Coosalud' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 101539 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[MunicipioOrigen] IS NOT NULL AND (v.[MunicipioOrigen] IS NULL OR s.[MunicipioOrigen] COLLATE Latin1_General_BIN <> v.[MunicipioOrigen] OR DATALENGTH(s.[MunicipioOrigen]) <> DATALENGTH(v.[MunicipioOrigen])))
                         OR (s.[DepartamentoOrigen] IS NOT NULL AND (v.[DepartamentoOrigen] IS NULL OR s.[DepartamentoOrigen] COLLATE Latin1_General_BIN <> v.[DepartamentoOrigen] OR DATALENGTH(s.[DepartamentoOrigen]) <> DATALENGTH(v.[DepartamentoOrigen])))
                         OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                         OR (s.[NivelSisben] IS NOT NULL AND (v.[NivelSisben] IS NULL OR s.[NivelSisben] COLLATE Latin1_General_BIN <> v.[NivelSisben] OR DATALENGTH(s.[NivelSisben]) <> DATALENGTH(v.[NivelSisben])))
                         OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnCoosalud] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[MunicipioOrigen] IS NOT NULL AND v.[MunicipioOrigen] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[MunicipioOrigen] IS NOT NULL AND (s.[MunicipioOrigen] COLLATE Latin1_General_BIN <> v.[MunicipioOrigen] OR DATALENGTH(s.[MunicipioOrigen]) <> DATALENGTH(v.[MunicipioOrigen])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[DepartamentoOrigen] IS NOT NULL AND v.[DepartamentoOrigen] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[DepartamentoOrigen] IS NOT NULL AND (s.[DepartamentoOrigen] COLLATE Latin1_General_BIN <> v.[DepartamentoOrigen] OR DATALENGTH(s.[DepartamentoOrigen]) <> DATALENGTH(v.[DepartamentoOrigen])) THEN 1 ELSE 0 END) AS e3,
               SUM(CASE WHEN s.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[Regimen] IS NOT NULL AND (s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e5,
               SUM(CASE WHEN s.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c6,
               SUM(CASE WHEN v.[EstadoAfiliacion] IS NOT NULL AND (s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])) THEN 1 ELSE 0 END) AS e6,
               SUM(CASE WHEN s.[NivelSisben] IS NOT NULL AND v.[NivelSisben] IS NULL THEN 1 ELSE 0 END) AS c7,
               SUM(CASE WHEN v.[NivelSisben] IS NOT NULL AND (s.[NivelSisben] COLLATE Latin1_General_BIN <> v.[NivelSisben] OR DATALENGTH(s.[NivelSisben]) <> DATALENGTH(v.[NivelSisben])) THEN 1 ELSE 0 END) AS e7,
               SUM(CASE WHEN s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL THEN 1 ELSE 0 END) AS c8,
               0 AS e8
          FROM dbo.[Coosalud] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[MunicipioOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [MunicipioOrigen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[DepartamentoOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [DepartamentoOrigen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NivelSisben] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NivelSisben]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN UPPER(n.[MunicipioOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[MunicipioOrigen] END AS [MunicipioOrigen],
                      CASE WHEN UPPER(n.[DepartamentoOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[DepartamentoOrigen] END AS [DepartamentoOrigen],
                      CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                      CASE WHEN UPPER(n.[NivelSisben]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NivelSisben] END AS [NivelSisben],
                      CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnCoosalud] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 0, 0),
              ('Email', a.c1, a.e1, 32698, 1),
              ('MunicipioOrigen', a.c2, a.e2, 31754, 0),
              ('DepartamentoOrigen', a.c3, a.e3, 31754, 68729),
              ('Regimen', a.c4, a.e4, 31754, 0),
              ('TipoAfiliado', a.c5, a.e5, 31754, 0),
              ('EstadoAfiliacion', a.c6, a.e6, 31754, 0),
              ('NivelSisben', a.c7, a.e7, 31952, 0),
              ('FechaNacimiento', a.c8, a.e8, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'SaviaSalud'
SELECT 'SaviaSalud' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 31804 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[FechaDeNacimiento] IS NOT NULL AND v.[FechaDeNacimiento] IS NULL)
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[EstadoDeAfiliacion] IS NOT NULL AND (v.[EstadoDeAfiliacion] IS NULL OR s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoDeAfiliacion] OR DATALENGTH(s.[EstadoDeAfiliacion]) <> DATALENGTH(v.[EstadoDeAfiliacion])))
                         OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                         OR (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[NumeroCelular] IS NOT NULL AND (v.[NumeroCelular] IS NULL OR s.[NumeroCelular] COLLATE Latin1_General_BIN <> v.[NumeroCelular] OR DATALENGTH(s.[NumeroCelular]) <> DATALENGTH(v.[NumeroCelular]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[Estado] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[FechaDeNacimiento] IS NOT NULL AND v.[FechaDeNacimiento] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[EstadoDeAfiliacion] IS NOT NULL AND v.[EstadoDeAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[EstadoDeAfiliacion] IS NOT NULL AND (s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoDeAfiliacion] OR DATALENGTH(s.[EstadoDeAfiliacion]) <> DATALENGTH(v.[EstadoDeAfiliacion])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[Regimen] IS NOT NULL AND (s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])) THEN 1 ELSE 0 END) AS e3,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c4,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e4,
               SUM(CASE WHEN s.[NumeroCelular] IS NOT NULL AND v.[NumeroCelular] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[NumeroCelular] IS NOT NULL AND (s.[NumeroCelular] COLLATE Latin1_General_BIN <> v.[NumeroCelular] OR DATALENGTH(s.[NumeroCelular]) <> DATALENGTH(v.[NumeroCelular])) THEN 1 ELSE 0 END) AS e5
          FROM dbo.[SaviaSalud] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoDeAfiliacion],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroCelular]) n
         CROSS APPLY (SELECT CASE WHEN s.[FechaDeNacimiento] < '19010101' THEN NULL ELSE s.[FechaDeNacimiento] END AS [FechaDeNacimiento],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN UPPER(n.[EstadoDeAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoDeAfiliacion] END AS [EstadoDeAfiliacion],
                      CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                      CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[NumeroCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroCelular] END AS [NumeroCelular]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[NumeroCelular] IS NOT NULL AND v.[NumeroCelular] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[NumeroCelular] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[Estado] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('FechaDeNacimiento', a.c0, a.e0, 0, 0),
              ('TipoAfiliado', a.c1, a.e1, 31804, 0),
              ('EstadoDeAfiliacion', a.c2, a.e2, 31804, 0),
              ('Regimen', a.c3, a.e3, 31804, 0),
              ('NumeroTelefono', a.c4, a.e4, 0, 0),
              ('NumeroCelular', a.c5, a.e5, 0, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'MiVacuna'
SELECT 'MiVacuna' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 1118 AS FilasAfectadasMedido, a.Flag1a0, 269 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[FechaExpedicion] IS NOT NULL AND v.[FechaExpedicion] IS NULL)
                         OR (s.[Celular] IS NOT NULL AND (v.[Celular] IS NULL OR s.[Celular] COLLATE Latin1_General_BIN <> v.[Celular] OR DATALENGTH(s.[Celular]) <> DATALENGTH(v.[Celular])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnMiVacuna] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[FechaExpedicion] IS NOT NULL AND v.[FechaExpedicion] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[Celular] IS NOT NULL AND v.[Celular] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Celular] IS NOT NULL AND (s.[Celular] COLLATE Latin1_General_BIN <> v.[Celular] OR DATALENGTH(s.[Celular]) <> DATALENGTH(v.[Celular])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e2
          FROM dbo.[MiVacuna] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Celular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Celular],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
         CROSS APPLY (SELECT CASE WHEN s.[FechaExpedicion] < '19010101' THEN NULL ELSE s.[FechaExpedicion] END AS [FechaExpedicion],
                      CASE WHEN UPPER(n.[Celular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Celular] END AS [Celular],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
         CROSS APPLY (SELECT CASE WHEN (s.[Celular] IS NOT NULL AND v.[Celular] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[Celular] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnMiVacuna] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('FechaExpedicion', a.c0, a.e0, 0, 0),
              ('Celular', a.c1, a.e1, 0, 0),
              ('Email', a.c2, a.e2, 1118, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'Fosyga'
SELECT 'Fosyga' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 1681 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[Estado] IS NOT NULL AND (v.[Estado] IS NULL OR s.[Estado] COLLATE Latin1_General_BIN <> v.[Estado] OR DATALENGTH(s.[Estado]) <> DATALENGTH(v.[Estado])))
                         OR (s.[EPS] IS NOT NULL AND (v.[EPS] IS NULL OR s.[EPS] COLLATE Latin1_General_BIN <> v.[EPS] OR DATALENGTH(s.[EPS]) <> DATALENGTH(v.[EPS])))
                         OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                         OR (s.[FechaAfiliacionEfectiva] IS NOT NULL AND v.[FechaAfiliacionEfectiva] IS NULL)
                         OR (s.[FechaFinalizacionAfiliacion] IS NOT NULL AND v.[FechaFinalizacionAfiliacion] IS NULL)
                         OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnFosyga] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[Estado] IS NOT NULL AND v.[Estado] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[Estado] IS NOT NULL AND (s.[Estado] COLLATE Latin1_General_BIN <> v.[Estado] OR DATALENGTH(s.[Estado]) <> DATALENGTH(v.[Estado])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[EPS] IS NOT NULL AND v.[EPS] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[EPS] IS NOT NULL AND (s.[EPS] COLLATE Latin1_General_BIN <> v.[EPS] OR DATALENGTH(s.[EPS]) <> DATALENGTH(v.[EPS])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Regimen] IS NOT NULL AND (s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[FechaAfiliacionEfectiva] IS NOT NULL AND v.[FechaAfiliacionEfectiva] IS NULL THEN 1 ELSE 0 END) AS c3,
               0 AS e3,
               SUM(CASE WHEN s.[FechaFinalizacionAfiliacion] IS NOT NULL AND v.[FechaFinalizacionAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c4,
               0 AS e4,
               SUM(CASE WHEN s.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL THEN 1 ELSE 0 END) AS c5,
               SUM(CASE WHEN v.[TipoAfiliado] IS NOT NULL AND (s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])) THEN 1 ELSE 0 END) AS e5,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c6,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e6,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c7,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e7
          FROM dbo.[Fosyga] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Estado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Estado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EPS] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EPS],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[Estado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Estado] END AS [Estado],
                      CASE WHEN UPPER(n.[EPS]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EPS] END AS [EPS],
                      CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                      CASE WHEN s.[FechaAfiliacionEfectiva] < '19010101' THEN NULL ELSE s.[FechaAfiliacionEfectiva] END AS [FechaAfiliacionEfectiva],
                      CASE WHEN s.[FechaFinalizacionAfiliacion] < '19010101' THEN NULL ELSE s.[FechaFinalizacionAfiliacion] END AS [FechaFinalizacionAfiliacion],
                      CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
         CROSS APPLY (SELECT CASE WHEN (s.[Estado] IS NOT NULL AND v.[Estado] IS NULL) OR (s.[EPS] IS NOT NULL AND v.[EPS] IS NULL) OR (s.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL)
                              THEN CASE WHEN v.[Estado] IS NOT NULL OR v.[EPS] IS NOT NULL OR v.[Regimen] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnFosyga] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('Estado', a.c0, a.e0, 0, 70),
              ('EPS', a.c1, a.e1, 0, 76),
              ('Regimen', a.c2, a.e2, 0, 0),
              ('FechaAfiliacionEfectiva', a.c3, a.e3, 0, 0),
              ('FechaFinalizacionAfiliacion', a.c4, a.e4, 0, 0),
              ('TipoAfiliado', a.c5, a.e5, 0, 0),
              ('Departamento', a.c6, a.e6, 136, 0),
              ('Municipio', a.c7, a.e7, 136, 1465)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'RGM'
SELECT 'RGM' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 7 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[Marca] IS NOT NULL AND (v.[Marca] IS NULL OR s.[Marca] COLLATE Latin1_General_BIN <> v.[Marca] OR DATALENGTH(s.[Marca]) <> DATALENGTH(v.[Marca])))
                         OR (s.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL)
                         OR (s.[Placa] IS NOT NULL AND (v.[Placa] IS NULL OR s.[Placa] COLLATE Latin1_General_BIN <> v.[Placa] OR DATALENGTH(s.[Placa]) <> DATALENGTH(v.[Placa]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnRGM] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[Marca] IS NOT NULL AND v.[Marca] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[Marca] IS NOT NULL AND (s.[Marca] COLLATE Latin1_General_BIN <> v.[Marca] OR DATALENGTH(s.[Marca]) <> DATALENGTH(v.[Marca])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL THEN 1 ELSE 0 END) AS c1,
               0 AS e1,
               SUM(CASE WHEN s.[Placa] IS NOT NULL AND v.[Placa] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Placa] IS NOT NULL AND (s.[Placa] COLLATE Latin1_General_BIN <> v.[Placa] OR DATALENGTH(s.[Placa]) <> DATALENGTH(v.[Placa])) THEN 1 ELSE 0 END) AS e2
          FROM dbo.[RGM] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Marca] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Marca],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Placa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Placa]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[Marca]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Marca] END AS [Marca],
                      NULLIF(s.[Modelo], 0) AS [Modelo],
                      CASE WHEN UPPER(n.[Placa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Placa] END AS [Placa]) v
         CROSS APPLY (SELECT CASE WHEN (s.[Marca] IS NOT NULL AND v.[Marca] IS NULL) OR (s.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL) OR (s.[Placa] IS NOT NULL AND v.[Placa] IS NULL)
                              THEN CASE WHEN v.[Marca] IS NOT NULL OR v.[Modelo] IS NOT NULL OR v.[Placa] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnRGM] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('Marca', a.c0, a.e0, 0, 0),
              ('Modelo', a.c1, a.e1, 0, 0),
              ('Placa', a.c2, a.e2, 1, 6)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'TigoUne'
SELECT 'TigoUne' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 27300 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnTigoUne] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1
          FROM dbo.[TigoUne] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnTigoUne] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 0, 0),
              ('Email', a.c1, a.e1, 27166, 134)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'Tigo'
SELECT 'Tigo' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 63 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnTigo] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1
          FROM dbo.[Tigo] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnTigo] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 0, 0),
              ('Email', a.c1, a.e1, 0, 63)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'RUES'
SELECT 'RUES' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 24 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL)
                         OR (s.[EstadoEmpresa] IS NOT NULL AND (v.[EstadoEmpresa] IS NULL OR s.[EstadoEmpresa] COLLATE Latin1_General_BIN <> v.[EstadoEmpresa] OR DATALENGTH(s.[EstadoEmpresa]) <> DATALENGTH(v.[EstadoEmpresa])))
                         OR (s.[ActividadEconomica] IS NOT NULL AND (v.[ActividadEconomica] IS NULL OR s.[ActividadEconomica] COLLATE Latin1_General_BIN <> v.[ActividadEconomica] OR DATALENGTH(s.[ActividadEconomica]) <> DATALENGTH(v.[ActividadEconomica])))
                         OR (s.[NombreEmpresa] IS NOT NULL AND (v.[NombreEmpresa] IS NULL OR s.[NombreEmpresa] COLLATE Latin1_General_BIN <> v.[NombreEmpresa] OR DATALENGTH(s.[NombreEmpresa]) <> DATALENGTH(v.[NombreEmpresa]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnRUES] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[EstadoEmpresa] IS NOT NULL AND v.[EstadoEmpresa] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[EstadoEmpresa] IS NOT NULL AND (s.[EstadoEmpresa] COLLATE Latin1_General_BIN <> v.[EstadoEmpresa] OR DATALENGTH(s.[EstadoEmpresa]) <> DATALENGTH(v.[EstadoEmpresa])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[ActividadEconomica] IS NOT NULL AND v.[ActividadEconomica] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[ActividadEconomica] IS NOT NULL AND (s.[ActividadEconomica] COLLATE Latin1_General_BIN <> v.[ActividadEconomica] OR DATALENGTH(s.[ActividadEconomica]) <> DATALENGTH(v.[ActividadEconomica])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[NombreEmpresa] IS NOT NULL AND v.[NombreEmpresa] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[NombreEmpresa] IS NOT NULL AND (s.[NombreEmpresa] COLLATE Latin1_General_BIN <> v.[NombreEmpresa] OR DATALENGTH(s.[NombreEmpresa]) <> DATALENGTH(v.[NombreEmpresa])) THEN 1 ELSE 0 END) AS e3
          FROM dbo.[RUES] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoEmpresa],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[ActividadEconomica] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [ActividadEconomica],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NombreEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NombreEmpresa]) n
         CROSS APPLY (SELECT NULLIF(s.[NumeroMatricula], 0) AS [NumeroMatricula],
                      CASE WHEN UPPER(n.[EstadoEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoEmpresa] END AS [EstadoEmpresa],
                      CASE WHEN UPPER(n.[ActividadEconomica]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[ActividadEconomica] END AS [ActividadEconomica],
                      CASE WHEN UPPER(n.[NombreEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NombreEmpresa] END AS [NombreEmpresa]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL) OR (s.[EstadoEmpresa] IS NOT NULL AND v.[EstadoEmpresa] IS NULL) OR (s.[ActividadEconomica] IS NOT NULL AND v.[ActividadEconomica] IS NULL)
                              THEN CASE WHEN v.[NumeroMatricula] IS NOT NULL OR v.[EstadoEmpresa] IS NOT NULL OR v.[ActividadEconomica] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnRUES] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroMatricula', a.c0, a.e0, 0, 0),
              ('EstadoEmpresa', a.c1, a.e1, 0, 0),
              ('ActividadEconomica', a.c2, a.e2, 0, 10),
              ('NombreEmpresa', a.c3, a.e3, 9, 5)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'Camara'
SELECT 'Camara' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 103 AS FilasAfectadasMedido, a.Flag1a0, 69 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                         OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
                         OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnCamara] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[Municipio] IS NOT NULL AND v.[Municipio] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[Municipio] IS NOT NULL AND (s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])) THEN 1 ELSE 0 END) AS e2,
               SUM(CASE WHEN s.[Departamento] IS NOT NULL AND v.[Departamento] IS NULL THEN 1 ELSE 0 END) AS c3,
               SUM(CASE WHEN v.[Departamento] IS NOT NULL AND (s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])) THEN 1 ELSE 0 END) AS e3
          FROM dbo.[Camara] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                      CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
                      CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnCamara] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 91, 0),
              ('Email', a.c1, a.e1, 0, 8),
              ('Municipio', a.c2, a.e2, 0, 3),
              ('Departamento', a.c3, a.e3, 1, 0)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'CamaraEmpresa'
SELECT 'CamaraEmpresa' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 40 AS FilasAfectadasMedido, a.Flag1a0, 13 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                         OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnCamara] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL THEN 1 ELSE 0 END) AS c0,
               SUM(CASE WHEN v.[NumeroTelefono] IS NOT NULL AND (s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])) THEN 1 ELSE 0 END) AS e0,
               SUM(CASE WHEN s.[Email] IS NOT NULL AND v.[Email] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Email] IS NOT NULL AND (s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])) THEN 1 ELSE 0 END) AS e1
          FROM dbo.[CamaraEmpresa] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
         CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                      CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (s.[Email] IS NOT NULL AND v.[Email] IS NULL)
                              THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnCamara] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NumeroTelefono', a.c0, a.e0, 37, 0),
              ('Email', a.c1, a.e1, 1, 2)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'PositivaEmpresas'
SELECT 'PositivaEmpresas' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 102661 AS FilasAfectadasMedido, a.Flag1a0, 0 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                         OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
                         OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnPositivaEmpresa] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NIT] IS NOT NULL AND v.[NIT] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Empresa] IS NOT NULL AND (s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])) THEN 1 ELSE 0 END) AS e1,
               SUM(CASE WHEN s.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL THEN 1 ELSE 0 END) AS c2,
               SUM(CASE WHEN v.[EstadoAfiliacion] IS NOT NULL AND (s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])) THEN 1 ELSE 0 END) AS e2
          FROM dbo.[PositivaEmpresas] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
                      TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion]) n
         CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
                      CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
                      CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (s.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                              THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnPositivaEmpresa] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NIT', a.c0, a.e0, 1, 0),
              ('Empresa', a.c1, a.e1, 0, 102651),
              ('EstadoAfiliacion', a.c2, a.e2, 0, 42)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

IF @Solo IS NULL OR @Solo = 'ARLSura'
SELECT 'ARLSura' AS Fuente, x.Columna, x.Centinelas, x.Espacios, x.CentinelasMedido, x.EspaciosMedido,
       a.Filas, a.FilasAfectadas, 18043 AS FilasAfectadasMedido, a.Flag1a0, 1 AS Flag1a0Medido
  FROM (SELECT COUNT(*) AS Filas,
               SUM(CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                         OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa]))) THEN 1 ELSE 0 END) AS FilasAfectadas,
               SUM(CASE WHEN s.[EstadoEnSura] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END) AS Flag1a0,
               SUM(CASE WHEN s.[NIT] IS NOT NULL AND v.[NIT] IS NULL THEN 1 ELSE 0 END) AS c0,
               0 AS e0,
               SUM(CASE WHEN s.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL THEN 1 ELSE 0 END) AS c1,
               SUM(CASE WHEN v.[Empresa] IS NOT NULL AND (s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])) THEN 1 ELSE 0 END) AS e1
          FROM dbo.[ARLSura] s
         CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa]) n
         CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
                      CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa]) v
         CROSS APPLY (SELECT CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (s.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                              THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                              ELSE s.[EstadoEnSura] END AS Nuevo) f) a
 CROSS APPLY (VALUES ('NIT', a.c0, a.e0, 0, 0),
              ('Empresa', a.c1, a.e1, 10, 18033)) x (Columna, Centinelas, Espacios, CentinelasMedido, EspaciosMedido)
 WHERE x.Centinelas + x.Espacios + x.CentinelasMedido + x.EspaciosMedido > 0;

GO

-- ============================================================================
--  1. Cajacopi  (dbo.Cajacopi; flag EstadoEnCajacopi; clave TitularID)
--     Medido 2026-09-24: 1.277.608 filas, 204 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Email                            2 a NULL           0 con blancos de sobra
--     Departamento                    45 a NULL         157 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Cajacopi' RETURN;

IF OBJECT_ID('dbo.Cajacopi_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50111, 'dbo.Cajacopi_RespaldoVacios_20260925 ya existe: Cajacopi ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Cajacopi: ya normalizada (existe dbo.Cajacopi_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Cajacopi' AND Estado = 'EN CURSO')
    THROW 50112, 'Hay un cargue Cajacopi EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Cajacopi')
    PRINT 'Cajacopi: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Cajacopi] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Cajacopi_RespaldoVacios_20260925
  FROM dbo.[Cajacopi] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v
 WHERE (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
    OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
    OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL);
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Cajacopi: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 204); respaldo en dbo.Cajacopi_RespaldoVacios_20260925');

IF @Respaldo > 1204
    THROW 50113, 'Cajacopi: muchas más filas que las medidas el 2026-09-24 (204). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnCajacopi] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnCajacopi], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Cajacopi_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (r.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[FechaNacimiento] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnCajacopi] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefonoCelular] = v.[NumeroTelefonoCelular],
           s.[NumeroTelefonoFijo] = v.[NumeroTelefonoFijo],
           s.[Email] = v.[Email],
           s.[Departamento] = v.[Departamento],
           s.[Municipio] = v.[Municipio],
           s.[FechaNacimiento] = v.[FechaNacimiento],
           s.[EstadoEnCajacopi] = CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (r.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[FechaNacimiento] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnCajacopi] END
      FROM dbo.[Cajacopi] s
      JOIN dbo.Cajacopi_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                  CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
                  CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Cajacopi: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                                     OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
                                     OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Cajacopi] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Cajacopi] s
  JOIN dbo.Cajacopi_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnCajacopi] <> 0 AND s.[EstadoEnCajacopi] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Cajacopi_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnCajacopi] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Cajacopi] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnCajacopi] <> 0);

PRINT CONCAT('Cajacopi: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50114, 'Cajacopi: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50115, 'Cajacopi: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50116, 'Cajacopi: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50117, 'Cajacopi: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Cajacopi: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Cajacopi_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  2. MutualSer  (dbo.MutualSer; flag EstadoEnMutualSer; clave TitularID)
--     Medido 2026-09-24: 864.703 filas, 8.078 a tocar; flag 1 -> 0 en 1 filas / 1 titulares.
--     NumeroTelefonoCelular            1 a NULL           0 con blancos de sobra
--     NumeroTelefonoCelular2           0 a NULL           1 con blancos de sobra
--     Email                        8.077 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'MutualSer' RETURN;

IF OBJECT_ID('dbo.MutualSer_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50121, 'dbo.MutualSer_RespaldoVacios_20260925 ya existe: MutualSer ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'MutualSer: ya normalizada (existe dbo.MutualSer_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'MutualSer' AND Estado = 'EN CURSO')
    THROW 50122, 'Hay un cargue MutualSer EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'MutualSer')
    PRINT 'MutualSer: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[MutualSer] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.MutualSer_RespaldoVacios_20260925
  FROM dbo.[MutualSer] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular2],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoCelular2]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular2] END AS [NumeroTelefonoCelular2],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 WHERE (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
    OR (s.[NumeroTelefonoCelular2] IS NOT NULL AND (v.[NumeroTelefonoCelular2] IS NULL OR s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular2] OR DATALENGTH(s.[NumeroTelefonoCelular2]) <> DATALENGTH(v.[NumeroTelefonoCelular2])))
    OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('MutualSer: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 8078); respaldo en dbo.MutualSer_RespaldoVacios_20260925');

IF @Respaldo > 12117
    THROW 50123, 'MutualSer: muchas más filas que las medidas el 2026-09-24 (8078). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnMutualSer] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnMutualSer], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.MutualSer_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular2],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoCelular2]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular2] END AS [NumeroTelefonoCelular2],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoCelular2] IS NOT NULL AND v.[NumeroTelefonoCelular2] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoCelular2] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnMutualSer] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefonoCelular] = v.[NumeroTelefonoCelular],
           s.[NumeroTelefonoCelular2] = v.[NumeroTelefonoCelular2],
           s.[NumeroTelefonoFijo] = v.[NumeroTelefonoFijo],
           s.[Email] = v.[Email],
           s.[Regimen] = v.[Regimen],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[Departamento] = v.[Departamento],
           s.[Municipio] = v.[Municipio],
           s.[EstadoEnMutualSer] = CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoCelular2] IS NOT NULL AND v.[NumeroTelefonoCelular2] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoCelular2] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnMutualSer] END
      FROM dbo.[MutualSer] s
      JOIN dbo.MutualSer_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular2],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                  CASE WHEN UPPER(n.[NumeroTelefonoCelular2]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular2] END AS [NumeroTelefonoCelular2],
                  CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('MutualSer: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                                     OR (s.[NumeroTelefonoCelular2] IS NOT NULL AND (v.[NumeroTelefonoCelular2] IS NULL OR s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular2] OR DATALENGTH(s.[NumeroTelefonoCelular2]) <> DATALENGTH(v.[NumeroTelefonoCelular2])))
                                     OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[MutualSer] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular2] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular2],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoCelular2]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular2] END AS [NumeroTelefonoCelular2],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[MutualSer] s
  JOIN dbo.MutualSer_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnMutualSer] <> 0 AND s.[EstadoEnMutualSer] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.MutualSer_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnMutualSer] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[MutualSer] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnMutualSer] <> 0);

PRINT CONCAT('MutualSer: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 1 / 1); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50124, 'MutualSer: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50125, 'MutualSer: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50126, 'MutualSer: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50127, 'MutualSer: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('MutualSer: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.MutualSer_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  3. NuevaEps  (dbo.NuevaEps; flag EstadoEnNuevaEps; clave TitularID)
--     Medido 2026-09-24: 62.834 filas, 18 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     NumeroTelefono                  18 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'NuevaEps' RETURN;

IF OBJECT_ID('dbo.NuevaEps_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50131, 'dbo.NuevaEps_RespaldoVacios_20260925 ya existe: NuevaEps ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'NuevaEps: ya normalizada (existe dbo.NuevaEps_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'NuevaEps' AND Estado = 'EN CURSO')
    THROW 50132, 'Hay un cargue NuevaEps EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'NuevaEps')
    PRINT 'NuevaEps: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[NuevaEps] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.NuevaEps_RespaldoVacios_20260925
  FROM dbo.[NuevaEps] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('NuevaEps: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 18); respaldo en dbo.NuevaEps_RespaldoVacios_20260925');

IF @Respaldo > 1018
    THROW 50133, 'NuevaEps: muchas más filas que las medidas el 2026-09-24 (18). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnNuevaEps] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnNuevaEps], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.NuevaEps_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL) OR (r.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[EstadoAfiliacion] IS NOT NULL OR v.[TipoAfiliado] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnNuevaEps] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[EstadoAfiliacion] = v.[EstadoAfiliacion],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[FechaNacimiento] = v.[FechaNacimiento],
           s.[Departamento] = v.[Departamento],
           s.[Municipio] = v.[Municipio],
           s.[EstadoEnNuevaEps] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[EstadoAfiliacion] IS NOT NULL AND v.[EstadoAfiliacion] IS NULL) OR (r.[TipoAfiliado] IS NOT NULL AND v.[TipoAfiliado] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[EstadoAfiliacion] IS NOT NULL OR v.[TipoAfiliado] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnNuevaEps] END
      FROM dbo.[NuevaEps] s
      JOIN dbo.NuevaEps_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('NuevaEps: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[NuevaEps] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[NuevaEps] s
  JOIN dbo.NuevaEps_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnNuevaEps] <> 0 AND s.[EstadoEnNuevaEps] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.NuevaEps_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnNuevaEps] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[NuevaEps] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnNuevaEps] <> 0);

PRINT CONCAT('NuevaEps: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50134, 'NuevaEps: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50135, 'NuevaEps: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50136, 'NuevaEps: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50137, 'NuevaEps: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('NuevaEps: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.NuevaEps_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  4. DatosClaro  (dbo.DatosClaro; sin flag; clave TitularID)
--     Medido 2026-09-24: 3.528.387 filas, 27 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Email                            0 a NULL          27 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'DatosClaro' RETURN;

IF OBJECT_ID('dbo.DatosClaro_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50141, 'dbo.DatosClaro_RespaldoVacios_20260925 ya existe: DatosClaro ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'DatosClaro: ya normalizada (existe dbo.DatosClaro_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'DatosClaro' AND Estado = 'EN CURSO')
    THROW 50142, 'Hay un cargue DatosClaro EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'DatosClaro')
    PRINT 'DatosClaro: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[DatosClaro] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.DatosClaro_RespaldoVacios_20260925
  FROM dbo.[DatosClaro] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('DatosClaro: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 27); respaldo en dbo.DatosClaro_RespaldoVacios_20260925');

IF @Respaldo > 1027
    THROW 50143, 'DatosClaro: muchas más filas que las medidas el 2026-09-24 (27). Revisar el bloque 0 antes de seguir.', 1;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email]
      FROM dbo.[DatosClaro] s
      JOIN dbo.DatosClaro_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('DatosClaro: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[DatosClaro] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v;

PRINT CONCAT('DatosClaro: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50144, 'DatosClaro: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50145, 'DatosClaro: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50146, 'DatosClaro: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50147, 'DatosClaro: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('DatosClaro: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.DatosClaro_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  5. Positiva  (dbo.Positiva; flag EstadoEnPositiva; clave TitularID)
--     Medido 2026-09-24: 2.213.891 filas, 170.455 a tocar; flag 1 -> 0 en 29.882 filas / 28.683 titulares.
--     NumeroTelefonoCelular       30.056 a NULL           0 con blancos de sobra
--     NumeroTelefonoFijo               2 a NULL           0 con blancos de sobra
--     Email                      140.758 a NULL           0 con blancos de sobra
--     FechaNacimiento                 65 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Positiva' RETURN;

IF OBJECT_ID('dbo.Positiva_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50151, 'dbo.Positiva_RespaldoVacios_20260925 ya existe: Positiva ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Positiva: ya normalizada (existe dbo.Positiva_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Positiva' AND Estado = 'EN CURSO')
    THROW 50152, 'Hay un cargue Positiva EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Positiva')
    PRINT 'Positiva: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Positiva] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Positiva_RespaldoVacios_20260925
  FROM dbo.[Positiva] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 WHERE (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
    OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Positiva: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 170455); respaldo en dbo.Positiva_RespaldoVacios_20260925');

IF @Respaldo > 255682
    THROW 50153, 'Positiva: muchas más filas que las medidas el 2026-09-24 (170455). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnPositiva] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnPositiva], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Positiva_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnPositiva] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefonoCelular] = v.[NumeroTelefonoCelular],
           s.[NumeroTelefonoFijo] = v.[NumeroTelefonoFijo],
           s.[Email] = v.[Email],
           s.[FechaNacimiento] = v.[FechaNacimiento],
           s.[Departamento] = v.[Departamento],
           s.[Municipio] = v.[Municipio],
           s.[EstadoEnPositiva] = CASE WHEN (r.[NumeroTelefonoCelular] IS NOT NULL AND v.[NumeroTelefonoCelular] IS NULL) OR (r.[NumeroTelefonoFijo] IS NOT NULL AND v.[NumeroTelefonoFijo] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefonoCelular] IS NOT NULL OR v.[NumeroTelefonoFijo] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnPositiva] END
      FROM dbo.[Positiva] s
      JOIN dbo.Positiva_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
                  CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Positiva: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefonoCelular] IS NOT NULL AND (v.[NumeroTelefonoCelular] IS NULL OR s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoCelular] OR DATALENGTH(s.[NumeroTelefonoCelular]) <> DATALENGTH(v.[NumeroTelefonoCelular])))
                                     OR (s.[NumeroTelefonoFijo] IS NOT NULL AND (v.[NumeroTelefonoFijo] IS NULL OR s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN <> v.[NumeroTelefonoFijo] OR DATALENGTH(s.[NumeroTelefonoFijo]) <> DATALENGTH(v.[NumeroTelefonoFijo])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Positiva] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoCelular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefonoFijo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefonoFijo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefonoCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoCelular] END AS [NumeroTelefonoCelular],
              CASE WHEN UPPER(n.[NumeroTelefonoFijo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefonoFijo] END AS [NumeroTelefonoFijo],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Positiva] s
  JOIN dbo.Positiva_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnPositiva] <> 0 AND s.[EstadoEnPositiva] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Positiva_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnPositiva] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Positiva] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnPositiva] <> 0);

PRINT CONCAT('Positiva: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 29882 / 28683); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50154, 'Positiva: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50155, 'Positiva: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50156, 'Positiva: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50157, 'Positiva: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Positiva: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Positiva_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  6. SaludTotal  (dbo.SaludTotal; flag EstadoEnSaludTotal; clave TitularID)
--     Medido 2026-09-24: 1.586.180 filas, 37.297 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     NumeroTelefono                  14 a NULL           0 con blancos de sobra
--     Email                            2 a NULL          75 con blancos de sobra
--     Ciudad                           0 a NULL      32.742 con blancos de sobra
--     Empresa                          0 a NULL       4.291 con blancos de sobra
--     Cargo                          564 a NULL           0 con blancos de sobra
--     UltimaFechaPago                 17 a NULL           0 con blancos de sobra
--     IngresoBase                    101 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'SaludTotal' RETURN;

IF OBJECT_ID('dbo.SaludTotal_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50161, 'dbo.SaludTotal_RespaldoVacios_20260925 ya existe: SaludTotal ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'SaludTotal: ya normalizada (existe dbo.SaludTotal_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SaludTotal' AND Estado = 'EN CURSO')
    THROW 50162, 'Hay un cargue SaludTotal EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SaludTotal')
    PRINT 'SaludTotal: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[SaludTotal] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.SaludTotal_RespaldoVacios_20260925
  FROM dbo.[SaludTotal] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Ciudad] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Ciudad],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Cargo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Cargo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[IngresoBase] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [IngresoBase]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Ciudad]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Ciudad] END AS [Ciudad],
              NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[Cargo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Cargo] END AS [Cargo],
              CASE WHEN s.[UltimaFechaPago] < '19010101' THEN NULL ELSE s.[UltimaFechaPago] END AS [UltimaFechaPago],
              CASE WHEN UPPER(n.[IngresoBase]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[IngresoBase] END AS [IngresoBase]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
    OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[Ciudad] IS NOT NULL AND (v.[Ciudad] IS NULL OR s.[Ciudad] COLLATE Latin1_General_BIN <> v.[Ciudad] OR DATALENGTH(s.[Ciudad]) <> DATALENGTH(v.[Ciudad])))
    OR (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
    OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
    OR (s.[Cargo] IS NOT NULL AND (v.[Cargo] IS NULL OR s.[Cargo] COLLATE Latin1_General_BIN <> v.[Cargo] OR DATALENGTH(s.[Cargo]) <> DATALENGTH(v.[Cargo])))
    OR (s.[UltimaFechaPago] IS NOT NULL AND v.[UltimaFechaPago] IS NULL)
    OR (s.[IngresoBase] IS NOT NULL AND (v.[IngresoBase] IS NULL OR s.[IngresoBase] COLLATE Latin1_General_BIN <> v.[IngresoBase] OR DATALENGTH(s.[IngresoBase]) <> DATALENGTH(v.[IngresoBase])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('SaludTotal: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 37297); respaldo en dbo.SaludTotal_RespaldoVacios_20260925');

IF @Respaldo > 55945
    THROW 50163, 'SaludTotal: muchas más filas que las medidas el 2026-09-24 (37297). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnSaludTotal] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnSaludTotal], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.SaludTotal_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Ciudad] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Ciudad],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Cargo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Cargo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[IngresoBase] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [IngresoBase]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Ciudad]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Ciudad] END AS [Ciudad],
              NULLIF(r.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[Cargo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Cargo] END AS [Cargo],
              CASE WHEN r.[UltimaFechaPago] < '19010101' THEN NULL ELSE r.[UltimaFechaPago] END AS [UltimaFechaPago],
              CASE WHEN UPPER(n.[IngresoBase]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[IngresoBase] END AS [IngresoBase]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[NIT] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnSaludTotal] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[FechaNacimiento] = v.[FechaNacimiento],
           s.[EstadoAfiliacion] = v.[EstadoAfiliacion],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[Ciudad] = v.[Ciudad],
           s.[NIT] = v.[NIT],
           s.[Empresa] = v.[Empresa],
           s.[Cargo] = v.[Cargo],
           s.[UltimaFechaPago] = v.[UltimaFechaPago],
           s.[IngresoBase] = v.[IngresoBase],
           s.[EstadoEnSaludTotal] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL) OR (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL OR v.[NIT] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnSaludTotal] END
      FROM dbo.[SaludTotal] s
      JOIN dbo.SaludTotal_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Ciudad] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Ciudad],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Cargo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Cargo],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[IngresoBase] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [IngresoBase]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento],
                  CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN UPPER(n.[Ciudad]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Ciudad] END AS [Ciudad],
                  NULLIF(r.[NIT], 0) AS [NIT],
                  CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
                  CASE WHEN UPPER(n.[Cargo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Cargo] END AS [Cargo],
                  CASE WHEN r.[UltimaFechaPago] < '19010101' THEN NULL ELSE r.[UltimaFechaPago] END AS [UltimaFechaPago],
                  CASE WHEN UPPER(n.[IngresoBase]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[IngresoBase] END AS [IngresoBase]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('SaludTotal: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL)
                                     OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[Ciudad] IS NOT NULL AND (v.[Ciudad] IS NULL OR s.[Ciudad] COLLATE Latin1_General_BIN <> v.[Ciudad] OR DATALENGTH(s.[Ciudad]) <> DATALENGTH(v.[Ciudad])))
                                     OR (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                                     OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
                                     OR (s.[Cargo] IS NOT NULL AND (v.[Cargo] IS NULL OR s.[Cargo] COLLATE Latin1_General_BIN <> v.[Cargo] OR DATALENGTH(s.[Cargo]) <> DATALENGTH(v.[Cargo])))
                                     OR (s.[UltimaFechaPago] IS NOT NULL AND v.[UltimaFechaPago] IS NULL)
                                     OR (s.[IngresoBase] IS NOT NULL AND (v.[IngresoBase] IS NULL OR s.[IngresoBase] COLLATE Latin1_General_BIN <> v.[IngresoBase] OR DATALENGTH(s.[IngresoBase]) <> DATALENGTH(v.[IngresoBase]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[SaludTotal] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Ciudad] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Ciudad],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Cargo] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Cargo],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[IngresoBase] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [IngresoBase]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Ciudad]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Ciudad] END AS [Ciudad],
              NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[Cargo]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Cargo] END AS [Cargo],
              CASE WHEN s.[UltimaFechaPago] < '19010101' THEN NULL ELSE s.[UltimaFechaPago] END AS [UltimaFechaPago],
              CASE WHEN UPPER(n.[IngresoBase]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[IngresoBase] END AS [IngresoBase]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[SaludTotal] s
  JOIN dbo.SaludTotal_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnSaludTotal] <> 0 AND s.[EstadoEnSaludTotal] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.SaludTotal_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnSaludTotal] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[SaludTotal] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnSaludTotal] <> 0);

PRINT CONCAT('SaludTotal: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50164, 'SaludTotal: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50165, 'SaludTotal: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50166, 'SaludTotal: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50167, 'SaludTotal: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('SaludTotal: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.SaludTotal_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  7. SOS  (dbo.SOS; flag EstadoEnSOS; clave TitularID)
--     Medido 2026-09-24: 226.946 filas, 129 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     RazonSocial                      0 a NULL         123 con blancos de sobra
--     Telefono                         6 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'SOS' RETURN;

IF OBJECT_ID('dbo.SOS_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50171, 'dbo.SOS_RespaldoVacios_20260925 ya existe: SOS ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'SOS: ya normalizada (existe dbo.SOS_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SOS' AND Estado = 'EN CURSO')
    THROW 50172, 'Hay un cargue SOS EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SOS')
    PRINT 'SOS: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[SOS] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.SOS_RespaldoVacios_20260925
  FROM dbo.[SOS] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[RazonSocial] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [RazonSocial],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Telefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Telefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Direccion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Direccion]) n
 CROSS APPLY (SELECT NULLIF(s.[NitEmpresa], 0) AS [NitEmpresa],
              CASE WHEN UPPER(n.[RazonSocial]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[RazonSocial] END AS [RazonSocial],
              CASE WHEN UPPER(n.[Telefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Telefono] END AS [Telefono],
              CASE WHEN UPPER(n.[Direccion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Direccion] END AS [Direccion]) v
 WHERE (s.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL)
    OR (s.[RazonSocial] IS NOT NULL AND (v.[RazonSocial] IS NULL OR s.[RazonSocial] COLLATE Latin1_General_BIN <> v.[RazonSocial] OR DATALENGTH(s.[RazonSocial]) <> DATALENGTH(v.[RazonSocial])))
    OR (s.[Telefono] IS NOT NULL AND (v.[Telefono] IS NULL OR s.[Telefono] COLLATE Latin1_General_BIN <> v.[Telefono] OR DATALENGTH(s.[Telefono]) <> DATALENGTH(v.[Telefono])))
    OR (s.[Direccion] IS NOT NULL AND (v.[Direccion] IS NULL OR s.[Direccion] COLLATE Latin1_General_BIN <> v.[Direccion] OR DATALENGTH(s.[Direccion]) <> DATALENGTH(v.[Direccion])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('SOS: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 129); respaldo en dbo.SOS_RespaldoVacios_20260925');

IF @Respaldo > 1129
    THROW 50173, 'SOS: muchas más filas que las medidas el 2026-09-24 (129). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnSOS] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnSOS], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.SOS_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[RazonSocial] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [RazonSocial],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Telefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Telefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Direccion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Direccion]) n
 CROSS APPLY (SELECT NULLIF(r.[NitEmpresa], 0) AS [NitEmpresa],
              CASE WHEN UPPER(n.[RazonSocial]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[RazonSocial] END AS [RazonSocial],
              CASE WHEN UPPER(n.[Telefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Telefono] END AS [Telefono],
              CASE WHEN UPPER(n.[Direccion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Direccion] END AS [Direccion]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL) OR (r.[RazonSocial] IS NOT NULL AND v.[RazonSocial] IS NULL) OR (r.[Telefono] IS NOT NULL AND v.[Telefono] IS NULL) OR (r.[Direccion] IS NOT NULL AND v.[Direccion] IS NULL)
                     THEN CASE WHEN v.[NitEmpresa] IS NOT NULL OR v.[RazonSocial] IS NOT NULL OR v.[Telefono] IS NOT NULL OR v.[Direccion] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnSOS] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NitEmpresa] = v.[NitEmpresa],
           s.[RazonSocial] = v.[RazonSocial],
           s.[Telefono] = v.[Telefono],
           s.[Direccion] = v.[Direccion],
           s.[EstadoEnSOS] = CASE WHEN (r.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL) OR (r.[RazonSocial] IS NOT NULL AND v.[RazonSocial] IS NULL) OR (r.[Telefono] IS NOT NULL AND v.[Telefono] IS NULL) OR (r.[Direccion] IS NOT NULL AND v.[Direccion] IS NULL)
                  THEN CASE WHEN v.[NitEmpresa] IS NOT NULL OR v.[RazonSocial] IS NOT NULL OR v.[Telefono] IS NOT NULL OR v.[Direccion] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnSOS] END
      FROM dbo.[SOS] s
      JOIN dbo.SOS_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[RazonSocial] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [RazonSocial],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Telefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Telefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Direccion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Direccion]) n
     CROSS APPLY (SELECT NULLIF(r.[NitEmpresa], 0) AS [NitEmpresa],
                  CASE WHEN UPPER(n.[RazonSocial]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[RazonSocial] END AS [RazonSocial],
                  CASE WHEN UPPER(n.[Telefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Telefono] END AS [Telefono],
                  CASE WHEN UPPER(n.[Direccion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Direccion] END AS [Direccion]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('SOS: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NitEmpresa] IS NOT NULL AND v.[NitEmpresa] IS NULL)
                                     OR (s.[RazonSocial] IS NOT NULL AND (v.[RazonSocial] IS NULL OR s.[RazonSocial] COLLATE Latin1_General_BIN <> v.[RazonSocial] OR DATALENGTH(s.[RazonSocial]) <> DATALENGTH(v.[RazonSocial])))
                                     OR (s.[Telefono] IS NOT NULL AND (v.[Telefono] IS NULL OR s.[Telefono] COLLATE Latin1_General_BIN <> v.[Telefono] OR DATALENGTH(s.[Telefono]) <> DATALENGTH(v.[Telefono])))
                                     OR (s.[Direccion] IS NOT NULL AND (v.[Direccion] IS NULL OR s.[Direccion] COLLATE Latin1_General_BIN <> v.[Direccion] OR DATALENGTH(s.[Direccion]) <> DATALENGTH(v.[Direccion]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[SOS] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[RazonSocial] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [RazonSocial],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Telefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Telefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Direccion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Direccion]) n
 CROSS APPLY (SELECT NULLIF(s.[NitEmpresa], 0) AS [NitEmpresa],
              CASE WHEN UPPER(n.[RazonSocial]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[RazonSocial] END AS [RazonSocial],
              CASE WHEN UPPER(n.[Telefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Telefono] END AS [Telefono],
              CASE WHEN UPPER(n.[Direccion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Direccion] END AS [Direccion]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[SOS] s
  JOIN dbo.SOS_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnSOS] <> 0 AND s.[EstadoEnSOS] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.SOS_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnSOS] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[SOS] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnSOS] <> 0);

PRINT CONCAT('SOS: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50174, 'SOS: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50175, 'SOS: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50176, 'SOS: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50177, 'SOS: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('SOS: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.SOS_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  8. Coosalud  (dbo.Coosalud; flag EstadoEnCoosalud; clave TitularID)
--     Medido 2026-09-24: 1.037.887 filas, 101.539 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Email                       32.698 a NULL           1 con blancos de sobra
--     MunicipioOrigen             31.754 a NULL           0 con blancos de sobra
--     DepartamentoOrigen          31.754 a NULL      68.729 con blancos de sobra
--     Regimen                     31.754 a NULL           0 con blancos de sobra
--     TipoAfiliado                31.754 a NULL           0 con blancos de sobra
--     EstadoAfiliacion            31.754 a NULL           0 con blancos de sobra
--     NivelSisben                 31.952 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Coosalud' RETURN;

IF OBJECT_ID('dbo.Coosalud_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50181, 'dbo.Coosalud_RespaldoVacios_20260925 ya existe: Coosalud ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Coosalud: ya normalizada (existe dbo.Coosalud_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Coosalud' AND Estado = 'EN CURSO')
    THROW 50182, 'Hay un cargue Coosalud EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Coosalud')
    PRINT 'Coosalud: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Coosalud] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Coosalud_RespaldoVacios_20260925
  FROM dbo.[Coosalud] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[MunicipioOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [MunicipioOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[DepartamentoOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [DepartamentoOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NivelSisben] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NivelSisben]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[MunicipioOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[MunicipioOrigen] END AS [MunicipioOrigen],
              CASE WHEN UPPER(n.[DepartamentoOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[DepartamentoOrigen] END AS [DepartamentoOrigen],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[NivelSisben]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NivelSisben] END AS [NivelSisben],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[MunicipioOrigen] IS NOT NULL AND (v.[MunicipioOrigen] IS NULL OR s.[MunicipioOrigen] COLLATE Latin1_General_BIN <> v.[MunicipioOrigen] OR DATALENGTH(s.[MunicipioOrigen]) <> DATALENGTH(v.[MunicipioOrigen])))
    OR (s.[DepartamentoOrigen] IS NOT NULL AND (v.[DepartamentoOrigen] IS NULL OR s.[DepartamentoOrigen] COLLATE Latin1_General_BIN <> v.[DepartamentoOrigen] OR DATALENGTH(s.[DepartamentoOrigen]) <> DATALENGTH(v.[DepartamentoOrigen])))
    OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
    OR (s.[NivelSisben] IS NOT NULL AND (v.[NivelSisben] IS NULL OR s.[NivelSisben] COLLATE Latin1_General_BIN <> v.[NivelSisben] OR DATALENGTH(s.[NivelSisben]) <> DATALENGTH(v.[NivelSisben])))
    OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL);
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Coosalud: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 101539); respaldo en dbo.Coosalud_RespaldoVacios_20260925');

IF @Respaldo > 152308
    THROW 50183, 'Coosalud: muchas más filas que las medidas el 2026-09-24 (101539). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnCoosalud] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnCoosalud], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Coosalud_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[MunicipioOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [MunicipioOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[DepartamentoOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [DepartamentoOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NivelSisben] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NivelSisben]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[MunicipioOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[MunicipioOrigen] END AS [MunicipioOrigen],
              CASE WHEN UPPER(n.[DepartamentoOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[DepartamentoOrigen] END AS [DepartamentoOrigen],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[NivelSisben]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NivelSisben] END AS [NivelSisben],
              CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnCoosalud] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[MunicipioOrigen] = v.[MunicipioOrigen],
           s.[DepartamentoOrigen] = v.[DepartamentoOrigen],
           s.[Regimen] = v.[Regimen],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[EstadoAfiliacion] = v.[EstadoAfiliacion],
           s.[NivelSisben] = v.[NivelSisben],
           s.[FechaNacimiento] = v.[FechaNacimiento],
           s.[EstadoEnCoosalud] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnCoosalud] END
      FROM dbo.[Coosalud] s
      JOIN dbo.Coosalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[MunicipioOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [MunicipioOrigen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[DepartamentoOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [DepartamentoOrigen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NivelSisben] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NivelSisben]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN UPPER(n.[MunicipioOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[MunicipioOrigen] END AS [MunicipioOrigen],
                  CASE WHEN UPPER(n.[DepartamentoOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[DepartamentoOrigen] END AS [DepartamentoOrigen],
                  CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
                  CASE WHEN UPPER(n.[NivelSisben]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NivelSisben] END AS [NivelSisben],
                  CASE WHEN r.[FechaNacimiento] < '19010101' THEN NULL ELSE r.[FechaNacimiento] END AS [FechaNacimiento]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Coosalud: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[MunicipioOrigen] IS NOT NULL AND (v.[MunicipioOrigen] IS NULL OR s.[MunicipioOrigen] COLLATE Latin1_General_BIN <> v.[MunicipioOrigen] OR DATALENGTH(s.[MunicipioOrigen]) <> DATALENGTH(v.[MunicipioOrigen])))
                                     OR (s.[DepartamentoOrigen] IS NOT NULL AND (v.[DepartamentoOrigen] IS NULL OR s.[DepartamentoOrigen] COLLATE Latin1_General_BIN <> v.[DepartamentoOrigen] OR DATALENGTH(s.[DepartamentoOrigen]) <> DATALENGTH(v.[DepartamentoOrigen])))
                                     OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])))
                                     OR (s.[NivelSisben] IS NOT NULL AND (v.[NivelSisben] IS NULL OR s.[NivelSisben] COLLATE Latin1_General_BIN <> v.[NivelSisben] OR DATALENGTH(s.[NivelSisben]) <> DATALENGTH(v.[NivelSisben])))
                                     OR (s.[FechaNacimiento] IS NOT NULL AND v.[FechaNacimiento] IS NULL) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Coosalud] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[MunicipioOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [MunicipioOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[DepartamentoOrigen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [DepartamentoOrigen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NivelSisben] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NivelSisben]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[MunicipioOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[MunicipioOrigen] END AS [MunicipioOrigen],
              CASE WHEN UPPER(n.[DepartamentoOrigen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[DepartamentoOrigen] END AS [DepartamentoOrigen],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion],
              CASE WHEN UPPER(n.[NivelSisben]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NivelSisben] END AS [NivelSisben],
              CASE WHEN s.[FechaNacimiento] < '19010101' THEN NULL ELSE s.[FechaNacimiento] END AS [FechaNacimiento]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Coosalud] s
  JOIN dbo.Coosalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnCoosalud] <> 0 AND s.[EstadoEnCoosalud] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Coosalud_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnCoosalud] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Coosalud] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnCoosalud] <> 0);

PRINT CONCAT('Coosalud: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50184, 'Coosalud: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50185, 'Coosalud: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50186, 'Coosalud: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50187, 'Coosalud: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Coosalud: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Coosalud_RespaldoVacios_20260925.');
GO

-- ============================================================================
--  9. SaviaSalud  (dbo.SaviaSalud; flag Estado; clave TitularID)
--     Medido 2026-09-24: 719.743 filas, 31.804 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     TipoAfiliado                31.804 a NULL           0 con blancos de sobra
--     EstadoDeAfiliacion          31.804 a NULL           0 con blancos de sobra
--     Regimen                     31.804 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'SaviaSalud' RETURN;

IF OBJECT_ID('dbo.SaviaSalud_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50191, 'dbo.SaviaSalud_RespaldoVacios_20260925 ya existe: SaviaSalud ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'SaviaSalud: ya normalizada (existe dbo.SaviaSalud_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SaviaSalud' AND Estado = 'EN CURSO')
    THROW 50192, 'Hay un cargue SaviaSalud EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SaviaSalud')
    PRINT 'SaviaSalud: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[SaviaSalud] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.SaviaSalud_RespaldoVacios_20260925
  FROM dbo.[SaviaSalud] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoDeAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroCelular]) n
 CROSS APPLY (SELECT CASE WHEN s.[FechaDeNacimiento] < '19010101' THEN NULL ELSE s.[FechaDeNacimiento] END AS [FechaDeNacimiento],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoDeAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoDeAfiliacion] END AS [EstadoDeAfiliacion],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[NumeroCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroCelular] END AS [NumeroCelular]) v
 WHERE (s.[FechaDeNacimiento] IS NOT NULL AND v.[FechaDeNacimiento] IS NULL)
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[EstadoDeAfiliacion] IS NOT NULL AND (v.[EstadoDeAfiliacion] IS NULL OR s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoDeAfiliacion] OR DATALENGTH(s.[EstadoDeAfiliacion]) <> DATALENGTH(v.[EstadoDeAfiliacion])))
    OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
    OR (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[NumeroCelular] IS NOT NULL AND (v.[NumeroCelular] IS NULL OR s.[NumeroCelular] COLLATE Latin1_General_BIN <> v.[NumeroCelular] OR DATALENGTH(s.[NumeroCelular]) <> DATALENGTH(v.[NumeroCelular])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('SaviaSalud: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 31804); respaldo en dbo.SaviaSalud_RespaldoVacios_20260925');

IF @Respaldo > 47706
    THROW 50193, 'SaviaSalud: muchas más filas que las medidas el 2026-09-24 (31804). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[Estado] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[Estado], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.SaviaSalud_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoDeAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroCelular]) n
 CROSS APPLY (SELECT CASE WHEN r.[FechaDeNacimiento] < '19010101' THEN NULL ELSE r.[FechaDeNacimiento] END AS [FechaDeNacimiento],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoDeAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoDeAfiliacion] END AS [EstadoDeAfiliacion],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[NumeroCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroCelular] END AS [NumeroCelular]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[NumeroCelular] IS NOT NULL AND v.[NumeroCelular] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[NumeroCelular] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[Estado] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[FechaDeNacimiento] = v.[FechaDeNacimiento],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[EstadoDeAfiliacion] = v.[EstadoDeAfiliacion],
           s.[Regimen] = v.[Regimen],
           s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[NumeroCelular] = v.[NumeroCelular],
           s.[Estado] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[NumeroCelular] IS NOT NULL AND v.[NumeroCelular] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[NumeroCelular] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[Estado] END
      FROM dbo.[SaviaSalud] s
      JOIN dbo.SaviaSalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoDeAfiliacion],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroCelular]) n
     CROSS APPLY (SELECT CASE WHEN r.[FechaDeNacimiento] < '19010101' THEN NULL ELSE r.[FechaDeNacimiento] END AS [FechaDeNacimiento],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN UPPER(n.[EstadoDeAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoDeAfiliacion] END AS [EstadoDeAfiliacion],
                  CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                  CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[NumeroCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroCelular] END AS [NumeroCelular]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('SaviaSalud: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[FechaDeNacimiento] IS NOT NULL AND v.[FechaDeNacimiento] IS NULL)
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[EstadoDeAfiliacion] IS NOT NULL AND (v.[EstadoDeAfiliacion] IS NULL OR s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoDeAfiliacion] OR DATALENGTH(s.[EstadoDeAfiliacion]) <> DATALENGTH(v.[EstadoDeAfiliacion])))
                                     OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                                     OR (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[NumeroCelular] IS NOT NULL AND (v.[NumeroCelular] IS NULL OR s.[NumeroCelular] COLLATE Latin1_General_BIN <> v.[NumeroCelular] OR DATALENGTH(s.[NumeroCelular]) <> DATALENGTH(v.[NumeroCelular]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[SaviaSalud] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoDeAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoDeAfiliacion],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroCelular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroCelular]) n
 CROSS APPLY (SELECT CASE WHEN s.[FechaDeNacimiento] < '19010101' THEN NULL ELSE s.[FechaDeNacimiento] END AS [FechaDeNacimiento],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[EstadoDeAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoDeAfiliacion] END AS [EstadoDeAfiliacion],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[NumeroCelular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroCelular] END AS [NumeroCelular]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[SaviaSalud] s
  JOIN dbo.SaviaSalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[Estado] <> 0 AND s.[Estado] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.SaviaSalud_RespaldoVacios_20260925 r
 WHERE r.[Estado] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[SaviaSalud] s WHERE s.[TitularID] = r.[TitularID] AND s.[Estado] <> 0);

PRINT CONCAT('SaviaSalud: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50194, 'SaviaSalud: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50195, 'SaviaSalud: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50196, 'SaviaSalud: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50197, 'SaviaSalud: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('SaviaSalud: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.SaviaSalud_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 10. MiVacuna  (dbo.MiVacuna; flag EstadoEnMiVacuna; clave TitularID)
--     Medido 2026-09-24: 36.985 filas, 1.118 a tocar; flag 1 -> 0 en 269 filas / 269 titulares.
--     Email                        1.118 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'MiVacuna' RETURN;

IF OBJECT_ID('dbo.MiVacuna_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50201, 'dbo.MiVacuna_RespaldoVacios_20260925 ya existe: MiVacuna ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'MiVacuna: ya normalizada (existe dbo.MiVacuna_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'MiVacuna' AND Estado = 'EN CURSO')
    THROW 50202, 'Hay un cargue MiVacuna EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'MiVacuna')
    PRINT 'MiVacuna: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[MiVacuna] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.MiVacuna_RespaldoVacios_20260925
  FROM dbo.[MiVacuna] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Celular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Celular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN s.[FechaExpedicion] < '19010101' THEN NULL ELSE s.[FechaExpedicion] END AS [FechaExpedicion],
              CASE WHEN UPPER(n.[Celular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Celular] END AS [Celular],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 WHERE (s.[FechaExpedicion] IS NOT NULL AND v.[FechaExpedicion] IS NULL)
    OR (s.[Celular] IS NOT NULL AND (v.[Celular] IS NULL OR s.[Celular] COLLATE Latin1_General_BIN <> v.[Celular] OR DATALENGTH(s.[Celular]) <> DATALENGTH(v.[Celular])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('MiVacuna: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 1118); respaldo en dbo.MiVacuna_RespaldoVacios_20260925');

IF @Respaldo > 2118
    THROW 50203, 'MiVacuna: muchas más filas que las medidas el 2026-09-24 (1118). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnMiVacuna] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnMiVacuna], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.MiVacuna_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Celular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Celular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN r.[FechaExpedicion] < '19010101' THEN NULL ELSE r.[FechaExpedicion] END AS [FechaExpedicion],
              CASE WHEN UPPER(n.[Celular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Celular] END AS [Celular],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 CROSS APPLY (SELECT CASE WHEN (r.[Celular] IS NOT NULL AND v.[Celular] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[Celular] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnMiVacuna] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[FechaExpedicion] = v.[FechaExpedicion],
           s.[Celular] = v.[Celular],
           s.[Email] = v.[Email],
           s.[EstadoEnMiVacuna] = CASE WHEN (r.[Celular] IS NOT NULL AND v.[Celular] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[Celular] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnMiVacuna] END
      FROM dbo.[MiVacuna] s
      JOIN dbo.MiVacuna_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Celular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Celular],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
     CROSS APPLY (SELECT CASE WHEN r.[FechaExpedicion] < '19010101' THEN NULL ELSE r.[FechaExpedicion] END AS [FechaExpedicion],
                  CASE WHEN UPPER(n.[Celular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Celular] END AS [Celular],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('MiVacuna: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[FechaExpedicion] IS NOT NULL AND v.[FechaExpedicion] IS NULL)
                                     OR (s.[Celular] IS NOT NULL AND (v.[Celular] IS NULL OR s.[Celular] COLLATE Latin1_General_BIN <> v.[Celular] OR DATALENGTH(s.[Celular]) <> DATALENGTH(v.[Celular])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[MiVacuna] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Celular] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Celular],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN s.[FechaExpedicion] < '19010101' THEN NULL ELSE s.[FechaExpedicion] END AS [FechaExpedicion],
              CASE WHEN UPPER(n.[Celular]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Celular] END AS [Celular],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[MiVacuna] s
  JOIN dbo.MiVacuna_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnMiVacuna] <> 0 AND s.[EstadoEnMiVacuna] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.MiVacuna_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnMiVacuna] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[MiVacuna] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnMiVacuna] <> 0);

PRINT CONCAT('MiVacuna: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 269 / 269); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50204, 'MiVacuna: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50205, 'MiVacuna: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50206, 'MiVacuna: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50207, 'MiVacuna: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('MiVacuna: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.MiVacuna_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 11. Fosyga  (dbo.Fosyga; flag EstadoEnFosyga; clave TitularID)
--     Medido 2026-09-24: 2.140.272 filas, 1.681 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Estado                           0 a NULL          70 con blancos de sobra
--     EPS                              0 a NULL          76 con blancos de sobra
--     Departamento                   136 a NULL           0 con blancos de sobra
--     Municipio                      136 a NULL       1.465 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Fosyga' RETURN;

IF OBJECT_ID('dbo.Fosyga_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50211, 'dbo.Fosyga_RespaldoVacios_20260925 ya existe: Fosyga ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Fosyga: ya normalizada (existe dbo.Fosyga_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Fosyga' AND Estado = 'EN CURSO')
    THROW 50212, 'Hay un cargue Fosyga EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Fosyga')
    PRINT 'Fosyga: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Fosyga] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Fosyga_RespaldoVacios_20260925
  FROM dbo.[Fosyga] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Estado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Estado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EPS] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EPS],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Estado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Estado] END AS [Estado],
              CASE WHEN UPPER(n.[EPS]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EPS] END AS [EPS],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN s.[FechaAfiliacionEfectiva] < '19010101' THEN NULL ELSE s.[FechaAfiliacionEfectiva] END AS [FechaAfiliacionEfectiva],
              CASE WHEN s.[FechaFinalizacionAfiliacion] < '19010101' THEN NULL ELSE s.[FechaFinalizacionAfiliacion] END AS [FechaFinalizacionAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 WHERE (s.[Estado] IS NOT NULL AND (v.[Estado] IS NULL OR s.[Estado] COLLATE Latin1_General_BIN <> v.[Estado] OR DATALENGTH(s.[Estado]) <> DATALENGTH(v.[Estado])))
    OR (s.[EPS] IS NOT NULL AND (v.[EPS] IS NULL OR s.[EPS] COLLATE Latin1_General_BIN <> v.[EPS] OR DATALENGTH(s.[EPS]) <> DATALENGTH(v.[EPS])))
    OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
    OR (s.[FechaAfiliacionEfectiva] IS NOT NULL AND v.[FechaAfiliacionEfectiva] IS NULL)
    OR (s.[FechaFinalizacionAfiliacion] IS NOT NULL AND v.[FechaFinalizacionAfiliacion] IS NULL)
    OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Fosyga: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 1681); respaldo en dbo.Fosyga_RespaldoVacios_20260925');

IF @Respaldo > 2681
    THROW 50213, 'Fosyga: muchas más filas que las medidas el 2026-09-24 (1681). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnFosyga] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnFosyga], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Fosyga_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Estado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Estado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EPS] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EPS],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Estado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Estado] END AS [Estado],
              CASE WHEN UPPER(n.[EPS]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EPS] END AS [EPS],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN r.[FechaAfiliacionEfectiva] < '19010101' THEN NULL ELSE r.[FechaAfiliacionEfectiva] END AS [FechaAfiliacionEfectiva],
              CASE WHEN r.[FechaFinalizacionAfiliacion] < '19010101' THEN NULL ELSE r.[FechaFinalizacionAfiliacion] END AS [FechaFinalizacionAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
 CROSS APPLY (SELECT CASE WHEN (r.[Estado] IS NOT NULL AND v.[Estado] IS NULL) OR (r.[EPS] IS NOT NULL AND v.[EPS] IS NULL) OR (r.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL)
                     THEN CASE WHEN v.[Estado] IS NOT NULL OR v.[EPS] IS NOT NULL OR v.[Regimen] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnFosyga] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[Estado] = v.[Estado],
           s.[EPS] = v.[EPS],
           s.[Regimen] = v.[Regimen],
           s.[FechaAfiliacionEfectiva] = v.[FechaAfiliacionEfectiva],
           s.[FechaFinalizacionAfiliacion] = v.[FechaFinalizacionAfiliacion],
           s.[TipoAfiliado] = v.[TipoAfiliado],
           s.[Departamento] = v.[Departamento],
           s.[Municipio] = v.[Municipio],
           s.[EstadoEnFosyga] = CASE WHEN (r.[Estado] IS NOT NULL AND v.[Estado] IS NULL) OR (r.[EPS] IS NOT NULL AND v.[EPS] IS NULL) OR (r.[Regimen] IS NOT NULL AND v.[Regimen] IS NULL)
                  THEN CASE WHEN v.[Estado] IS NOT NULL OR v.[EPS] IS NOT NULL OR v.[Regimen] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnFosyga] END
      FROM dbo.[Fosyga] s
      JOIN dbo.Fosyga_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Estado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Estado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EPS] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EPS],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[Estado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Estado] END AS [Estado],
                  CASE WHEN UPPER(n.[EPS]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EPS] END AS [EPS],
                  CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
                  CASE WHEN r.[FechaAfiliacionEfectiva] < '19010101' THEN NULL ELSE r.[FechaAfiliacionEfectiva] END AS [FechaAfiliacionEfectiva],
                  CASE WHEN r.[FechaFinalizacionAfiliacion] < '19010101' THEN NULL ELSE r.[FechaFinalizacionAfiliacion] END AS [FechaFinalizacionAfiliacion],
                  CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Fosyga: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[Estado] IS NOT NULL AND (v.[Estado] IS NULL OR s.[Estado] COLLATE Latin1_General_BIN <> v.[Estado] OR DATALENGTH(s.[Estado]) <> DATALENGTH(v.[Estado])))
                                     OR (s.[EPS] IS NOT NULL AND (v.[EPS] IS NULL OR s.[EPS] COLLATE Latin1_General_BIN <> v.[EPS] OR DATALENGTH(s.[EPS]) <> DATALENGTH(v.[EPS])))
                                     OR (s.[Regimen] IS NOT NULL AND (v.[Regimen] IS NULL OR s.[Regimen] COLLATE Latin1_General_BIN <> v.[Regimen] OR DATALENGTH(s.[Regimen]) <> DATALENGTH(v.[Regimen])))
                                     OR (s.[FechaAfiliacionEfectiva] IS NOT NULL AND v.[FechaAfiliacionEfectiva] IS NULL)
                                     OR (s.[FechaFinalizacionAfiliacion] IS NOT NULL AND v.[FechaFinalizacionAfiliacion] IS NULL)
                                     OR (s.[TipoAfiliado] IS NOT NULL AND (v.[TipoAfiliado] IS NULL OR s.[TipoAfiliado] COLLATE Latin1_General_BIN <> v.[TipoAfiliado] OR DATALENGTH(s.[TipoAfiliado]) <> DATALENGTH(v.[TipoAfiliado])))
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Fosyga] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Estado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Estado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EPS] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EPS],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Regimen] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Regimen],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[TipoAfiliado] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [TipoAfiliado],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Estado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Estado] END AS [Estado],
              CASE WHEN UPPER(n.[EPS]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EPS] END AS [EPS],
              CASE WHEN UPPER(n.[Regimen]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Regimen] END AS [Regimen],
              CASE WHEN s.[FechaAfiliacionEfectiva] < '19010101' THEN NULL ELSE s.[FechaAfiliacionEfectiva] END AS [FechaAfiliacionEfectiva],
              CASE WHEN s.[FechaFinalizacionAfiliacion] < '19010101' THEN NULL ELSE s.[FechaFinalizacionAfiliacion] END AS [FechaFinalizacionAfiliacion],
              CASE WHEN UPPER(n.[TipoAfiliado]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[TipoAfiliado] END AS [TipoAfiliado],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Fosyga] s
  JOIN dbo.Fosyga_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnFosyga] <> 0 AND s.[EstadoEnFosyga] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Fosyga_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnFosyga] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Fosyga] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnFosyga] <> 0);

PRINT CONCAT('Fosyga: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50214, 'Fosyga: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50215, 'Fosyga: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50216, 'Fosyga: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50217, 'Fosyga: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Fosyga: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Fosyga_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 12. RGM  (dbo.RGM; flag EstadoEnRGM; clave TitularID)
--     Medido 2026-09-24: 1.663.543 filas, 7 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Placa                            1 a NULL           6 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'RGM' RETURN;

IF OBJECT_ID('dbo.RGM_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50221, 'dbo.RGM_RespaldoVacios_20260925 ya existe: RGM ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'RGM: ya normalizada (existe dbo.RGM_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'RGM' AND Estado = 'EN CURSO')
    THROW 50222, 'Hay un cargue RGM EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'RGM')
    PRINT 'RGM: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[RGM] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.RGM_RespaldoVacios_20260925
  FROM dbo.[RGM] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Marca] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Marca],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Placa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Placa]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Marca]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Marca] END AS [Marca],
              NULLIF(s.[Modelo], 0) AS [Modelo],
              CASE WHEN UPPER(n.[Placa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Placa] END AS [Placa]) v
 WHERE (s.[Marca] IS NOT NULL AND (v.[Marca] IS NULL OR s.[Marca] COLLATE Latin1_General_BIN <> v.[Marca] OR DATALENGTH(s.[Marca]) <> DATALENGTH(v.[Marca])))
    OR (s.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL)
    OR (s.[Placa] IS NOT NULL AND (v.[Placa] IS NULL OR s.[Placa] COLLATE Latin1_General_BIN <> v.[Placa] OR DATALENGTH(s.[Placa]) <> DATALENGTH(v.[Placa])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('RGM: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 7); respaldo en dbo.RGM_RespaldoVacios_20260925');

IF @Respaldo > 1007
    THROW 50223, 'RGM: muchas más filas que las medidas el 2026-09-24 (7). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnRGM] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnRGM], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.RGM_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Marca] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Marca],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Placa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Placa]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Marca]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Marca] END AS [Marca],
              NULLIF(r.[Modelo], 0) AS [Modelo],
              CASE WHEN UPPER(n.[Placa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Placa] END AS [Placa]) v
 CROSS APPLY (SELECT CASE WHEN (r.[Marca] IS NOT NULL AND v.[Marca] IS NULL) OR (r.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL) OR (r.[Placa] IS NOT NULL AND v.[Placa] IS NULL)
                     THEN CASE WHEN v.[Marca] IS NOT NULL OR v.[Modelo] IS NOT NULL OR v.[Placa] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnRGM] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[Marca] = v.[Marca],
           s.[Modelo] = v.[Modelo],
           s.[Placa] = v.[Placa],
           s.[EstadoEnRGM] = CASE WHEN (r.[Marca] IS NOT NULL AND v.[Marca] IS NULL) OR (r.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL) OR (r.[Placa] IS NOT NULL AND v.[Placa] IS NULL)
                  THEN CASE WHEN v.[Marca] IS NOT NULL OR v.[Modelo] IS NOT NULL OR v.[Placa] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnRGM] END
      FROM dbo.[RGM] s
      JOIN dbo.RGM_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Marca] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Marca],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Placa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Placa]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[Marca]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Marca] END AS [Marca],
                  NULLIF(r.[Modelo], 0) AS [Modelo],
                  CASE WHEN UPPER(n.[Placa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Placa] END AS [Placa]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('RGM: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[Marca] IS NOT NULL AND (v.[Marca] IS NULL OR s.[Marca] COLLATE Latin1_General_BIN <> v.[Marca] OR DATALENGTH(s.[Marca]) <> DATALENGTH(v.[Marca])))
                                     OR (s.[Modelo] IS NOT NULL AND v.[Modelo] IS NULL)
                                     OR (s.[Placa] IS NOT NULL AND (v.[Placa] IS NULL OR s.[Placa] COLLATE Latin1_General_BIN <> v.[Placa] OR DATALENGTH(s.[Placa]) <> DATALENGTH(v.[Placa]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[RGM] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Marca] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Marca],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Placa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Placa]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[Marca]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Marca] END AS [Marca],
              NULLIF(s.[Modelo], 0) AS [Modelo],
              CASE WHEN UPPER(n.[Placa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Placa] END AS [Placa]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[RGM] s
  JOIN dbo.RGM_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnRGM] <> 0 AND s.[EstadoEnRGM] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.RGM_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnRGM] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[RGM] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnRGM] <> 0);

PRINT CONCAT('RGM: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50224, 'RGM: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50225, 'RGM: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50226, 'RGM: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50227, 'RGM: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('RGM: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.RGM_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 13. TigoUne  (dbo.TigoUne; flag EstadoEnTigoUne; clave TitularID)
--     Medido 2026-09-24: 123.783 filas, 27.300 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Email                       27.166 a NULL         134 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'TigoUne' RETURN;

IF OBJECT_ID('dbo.TigoUne_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50231, 'dbo.TigoUne_RespaldoVacios_20260925 ya existe: TigoUne ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'TigoUne: ya normalizada (existe dbo.TigoUne_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'TigoUne' AND Estado = 'EN CURSO')
    THROW 50232, 'Hay un cargue TigoUne EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'TigoUne')
    PRINT 'TigoUne: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[TigoUne] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.TigoUne_RespaldoVacios_20260925
  FROM dbo.[TigoUne] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('TigoUne: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 27300); respaldo en dbo.TigoUne_RespaldoVacios_20260925');

IF @Respaldo > 40950
    THROW 50233, 'TigoUne: muchas más filas que las medidas el 2026-09-24 (27300). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnTigoUne] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnTigoUne], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.TigoUne_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnTigoUne] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[EstadoEnTigoUne] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnTigoUne] END
      FROM dbo.[TigoUne] s
      JOIN dbo.TigoUne_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('TigoUne: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[TigoUne] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[TigoUne] s
  JOIN dbo.TigoUne_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnTigoUne] <> 0 AND s.[EstadoEnTigoUne] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.TigoUne_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnTigoUne] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[TigoUne] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnTigoUne] <> 0);

PRINT CONCAT('TigoUne: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50234, 'TigoUne: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50235, 'TigoUne: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50236, 'TigoUne: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50237, 'TigoUne: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('TigoUne: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.TigoUne_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 14. Tigo  (dbo.Tigo; flag EstadoEnTigo; clave TitularID)
--     Medido 2026-09-24: 1.026.333 filas, 63 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     Email                            0 a NULL          63 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Tigo' RETURN;

IF OBJECT_ID('dbo.Tigo_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50241, 'dbo.Tigo_RespaldoVacios_20260925 ya existe: Tigo ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Tigo: ya normalizada (existe dbo.Tigo_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Tigo' AND Estado = 'EN CURSO')
    THROW 50242, 'Hay un cargue Tigo EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Tigo')
    PRINT 'Tigo: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Tigo] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Tigo_RespaldoVacios_20260925
  FROM dbo.[Tigo] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Tigo: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 63); respaldo en dbo.Tigo_RespaldoVacios_20260925');

IF @Respaldo > 1063
    THROW 50243, 'Tigo: muchas más filas que las medidas el 2026-09-24 (63). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnTigo] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnTigo], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Tigo_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnTigo] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[EstadoEnTigo] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnTigo] END
      FROM dbo.[Tigo] s
      JOIN dbo.Tigo_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Tigo: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Tigo] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Tigo] s
  JOIN dbo.Tigo_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnTigo] <> 0 AND s.[EstadoEnTigo] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Tigo_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnTigo] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Tigo] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnTigo] <> 0);

PRINT CONCAT('Tigo: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50244, 'Tigo: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50245, 'Tigo: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50246, 'Tigo: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50247, 'Tigo: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Tigo: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Tigo_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 15. RUES  (dbo.RUES; flag EstadoEnRUES; clave TitularID)
--     Medido 2026-09-24: 873.188 filas, 24 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     ActividadEconomica               0 a NULL          10 con blancos de sobra
--     NombreEmpresa                    9 a NULL           5 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'RUES' RETURN;

IF OBJECT_ID('dbo.RUES_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50251, 'dbo.RUES_RespaldoVacios_20260925 ya existe: RUES ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'RUES: ya normalizada (existe dbo.RUES_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'RUES' AND Estado = 'EN CURSO')
    THROW 50252, 'Hay un cargue RUES EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'RUES')
    PRINT 'RUES: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[RUES] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.RUES_RespaldoVacios_20260925
  FROM dbo.[RUES] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoEmpresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[ActividadEconomica] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [ActividadEconomica],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NombreEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NombreEmpresa]) n
 CROSS APPLY (SELECT NULLIF(s.[NumeroMatricula], 0) AS [NumeroMatricula],
              CASE WHEN UPPER(n.[EstadoEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoEmpresa] END AS [EstadoEmpresa],
              CASE WHEN UPPER(n.[ActividadEconomica]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[ActividadEconomica] END AS [ActividadEconomica],
              CASE WHEN UPPER(n.[NombreEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NombreEmpresa] END AS [NombreEmpresa]) v
 WHERE (s.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL)
    OR (s.[EstadoEmpresa] IS NOT NULL AND (v.[EstadoEmpresa] IS NULL OR s.[EstadoEmpresa] COLLATE Latin1_General_BIN <> v.[EstadoEmpresa] OR DATALENGTH(s.[EstadoEmpresa]) <> DATALENGTH(v.[EstadoEmpresa])))
    OR (s.[ActividadEconomica] IS NOT NULL AND (v.[ActividadEconomica] IS NULL OR s.[ActividadEconomica] COLLATE Latin1_General_BIN <> v.[ActividadEconomica] OR DATALENGTH(s.[ActividadEconomica]) <> DATALENGTH(v.[ActividadEconomica])))
    OR (s.[NombreEmpresa] IS NOT NULL AND (v.[NombreEmpresa] IS NULL OR s.[NombreEmpresa] COLLATE Latin1_General_BIN <> v.[NombreEmpresa] OR DATALENGTH(s.[NombreEmpresa]) <> DATALENGTH(v.[NombreEmpresa])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('RUES: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 24); respaldo en dbo.RUES_RespaldoVacios_20260925');

IF @Respaldo > 1024
    THROW 50253, 'RUES: muchas más filas que las medidas el 2026-09-24 (24). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnRUES] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnRUES], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.RUES_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoEmpresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[ActividadEconomica] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [ActividadEconomica],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NombreEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NombreEmpresa]) n
 CROSS APPLY (SELECT NULLIF(r.[NumeroMatricula], 0) AS [NumeroMatricula],
              CASE WHEN UPPER(n.[EstadoEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoEmpresa] END AS [EstadoEmpresa],
              CASE WHEN UPPER(n.[ActividadEconomica]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[ActividadEconomica] END AS [ActividadEconomica],
              CASE WHEN UPPER(n.[NombreEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NombreEmpresa] END AS [NombreEmpresa]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL) OR (r.[EstadoEmpresa] IS NOT NULL AND v.[EstadoEmpresa] IS NULL) OR (r.[ActividadEconomica] IS NOT NULL AND v.[ActividadEconomica] IS NULL)
                     THEN CASE WHEN v.[NumeroMatricula] IS NOT NULL OR v.[EstadoEmpresa] IS NOT NULL OR v.[ActividadEconomica] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnRUES] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroMatricula] = v.[NumeroMatricula],
           s.[EstadoEmpresa] = v.[EstadoEmpresa],
           s.[ActividadEconomica] = v.[ActividadEconomica],
           s.[NombreEmpresa] = v.[NombreEmpresa],
           s.[EstadoEnRUES] = CASE WHEN (r.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL) OR (r.[EstadoEmpresa] IS NOT NULL AND v.[EstadoEmpresa] IS NULL) OR (r.[ActividadEconomica] IS NOT NULL AND v.[ActividadEconomica] IS NULL)
                  THEN CASE WHEN v.[NumeroMatricula] IS NOT NULL OR v.[EstadoEmpresa] IS NOT NULL OR v.[ActividadEconomica] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnRUES] END
      FROM dbo.[RUES] s
      JOIN dbo.RUES_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoEmpresa],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[ActividadEconomica] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [ActividadEconomica],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NombreEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NombreEmpresa]) n
     CROSS APPLY (SELECT NULLIF(r.[NumeroMatricula], 0) AS [NumeroMatricula],
                  CASE WHEN UPPER(n.[EstadoEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoEmpresa] END AS [EstadoEmpresa],
                  CASE WHEN UPPER(n.[ActividadEconomica]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[ActividadEconomica] END AS [ActividadEconomica],
                  CASE WHEN UPPER(n.[NombreEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NombreEmpresa] END AS [NombreEmpresa]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('RUES: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroMatricula] IS NOT NULL AND v.[NumeroMatricula] IS NULL)
                                     OR (s.[EstadoEmpresa] IS NOT NULL AND (v.[EstadoEmpresa] IS NULL OR s.[EstadoEmpresa] COLLATE Latin1_General_BIN <> v.[EstadoEmpresa] OR DATALENGTH(s.[EstadoEmpresa]) <> DATALENGTH(v.[EstadoEmpresa])))
                                     OR (s.[ActividadEconomica] IS NOT NULL AND (v.[ActividadEconomica] IS NULL OR s.[ActividadEconomica] COLLATE Latin1_General_BIN <> v.[ActividadEconomica] OR DATALENGTH(s.[ActividadEconomica]) <> DATALENGTH(v.[ActividadEconomica])))
                                     OR (s.[NombreEmpresa] IS NOT NULL AND (v.[NombreEmpresa] IS NULL OR s.[NombreEmpresa] COLLATE Latin1_General_BIN <> v.[NombreEmpresa] OR DATALENGTH(s.[NombreEmpresa]) <> DATALENGTH(v.[NombreEmpresa]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[RUES] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoEmpresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[ActividadEconomica] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [ActividadEconomica],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NombreEmpresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NombreEmpresa]) n
 CROSS APPLY (SELECT NULLIF(s.[NumeroMatricula], 0) AS [NumeroMatricula],
              CASE WHEN UPPER(n.[EstadoEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoEmpresa] END AS [EstadoEmpresa],
              CASE WHEN UPPER(n.[ActividadEconomica]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[ActividadEconomica] END AS [ActividadEconomica],
              CASE WHEN UPPER(n.[NombreEmpresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NombreEmpresa] END AS [NombreEmpresa]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[RUES] s
  JOIN dbo.RUES_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnRUES] <> 0 AND s.[EstadoEnRUES] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.RUES_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnRUES] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[RUES] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnRUES] <> 0);

PRINT CONCAT('RUES: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50254, 'RUES: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50255, 'RUES: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50256, 'RUES: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50257, 'RUES: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('RUES: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.RUES_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 16. Camara  (dbo.Camara; flag EstadoEnCamara; clave TitularID)
--     Medido 2026-09-24: 1.860.429 filas, 103 a tocar; flag 1 -> 0 en 69 filas / 57 titulares.
--     NumeroTelefono                  91 a NULL           0 con blancos de sobra
--     Email                            0 a NULL           8 con blancos de sobra
--     Municipio                        0 a NULL           3 con blancos de sobra
--     Departamento                     1 a NULL           0 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'Camara' RETURN;

IF OBJECT_ID('dbo.Camara_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50261, 'dbo.Camara_RespaldoVacios_20260925 ya existe: Camara ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'Camara: ya normalizada (existe dbo.Camara_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Camara' AND Estado = 'EN CURSO')
    THROW 50262, 'Hay un cargue Camara EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'Camara')
    PRINT 'Camara: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[Camara] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.Camara_RespaldoVacios_20260925
  FROM dbo.[Camara] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
    OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
    OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Camara: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 103); respaldo en dbo.Camara_RespaldoVacios_20260925');

IF @Respaldo > 1103
    THROW 50263, 'Camara: muchas más filas que las medidas el 2026-09-24 (103). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnCamara] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnCamara], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.Camara_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnCamara] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[Municipio] = v.[Municipio],
           s.[Departamento] = v.[Departamento],
           s.[EstadoEnCamara] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnCamara] END
      FROM dbo.[Camara] s
      JOIN dbo.Camara_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
                  CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
                  CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('Camara: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])))
                                     OR (s.[Municipio] IS NOT NULL AND (v.[Municipio] IS NULL OR s.[Municipio] COLLATE Latin1_General_BIN <> v.[Municipio] OR DATALENGTH(s.[Municipio]) <> DATALENGTH(v.[Municipio])))
                                     OR (s.[Departamento] IS NOT NULL AND (v.[Departamento] IS NULL OR s.[Departamento] COLLATE Latin1_General_BIN <> v.[Departamento] OR DATALENGTH(s.[Departamento]) <> DATALENGTH(v.[Departamento]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[Camara] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Municipio] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Municipio],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Departamento] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Departamento]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email],
              CASE WHEN UPPER(n.[Municipio]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Municipio] END AS [Municipio],
              CASE WHEN UPPER(n.[Departamento]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Departamento] END AS [Departamento]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[Camara] s
  JOIN dbo.Camara_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnCamara] <> 0 AND s.[EstadoEnCamara] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.Camara_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnCamara] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[Camara] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnCamara] <> 0);

PRINT CONCAT('Camara: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 69 / 57); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50264, 'Camara: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50265, 'Camara: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50266, 'Camara: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50267, 'Camara: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('Camara: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.Camara_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 17. CamaraEmpresa  (dbo.CamaraEmpresa; flag EstadoEnCamara; clave NitEmpresa)
--     Medido 2026-09-24: 71.424 filas, 40 a tocar; flag 1 -> 0 en 13 filas / 6 titulares.
--     NumeroTelefono                  37 a NULL           0 con blancos de sobra
--     Email                            1 a NULL           2 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'CamaraEmpresa' RETURN;

IF OBJECT_ID('dbo.CamaraEmpresa_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50271, 'dbo.CamaraEmpresa_RespaldoVacios_20260925 ya existe: CamaraEmpresa ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'CamaraEmpresa: ya normalizada (existe dbo.CamaraEmpresa_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'CamaraEmpresa' AND Estado = 'EN CURSO')
    THROW 50272, 'Hay un cargue CamaraEmpresa EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'CamaraEmpresa')
    PRINT 'CamaraEmpresa: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[CamaraEmpresa] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.CamaraEmpresa_RespaldoVacios_20260925
  FROM dbo.[CamaraEmpresa] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 WHERE (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
    OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('CamaraEmpresa: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 40); respaldo en dbo.CamaraEmpresa_RespaldoVacios_20260925');

IF @Respaldo > 1040
    THROW 50273, 'CamaraEmpresa: muchas más filas que las medidas el 2026-09-24 (40). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnCamara] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnCamara], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.CamaraEmpresa_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                     THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnCamara] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NumeroTelefono] = v.[NumeroTelefono],
           s.[Email] = v.[Email],
           s.[EstadoEnCamara] = CASE WHEN (r.[NumeroTelefono] IS NOT NULL AND v.[NumeroTelefono] IS NULL) OR (r.[Email] IS NOT NULL AND v.[Email] IS NULL)
                  THEN CASE WHEN v.[NumeroTelefono] IS NOT NULL OR v.[Email] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnCamara] END
      FROM dbo.[CamaraEmpresa] s
      JOIN dbo.CamaraEmpresa_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
     CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
                  CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('CamaraEmpresa: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NumeroTelefono] IS NOT NULL AND (v.[NumeroTelefono] IS NULL OR s.[NumeroTelefono] COLLATE Latin1_General_BIN <> v.[NumeroTelefono] OR DATALENGTH(s.[NumeroTelefono]) <> DATALENGTH(v.[NumeroTelefono])))
                                     OR (s.[Email] IS NOT NULL AND (v.[Email] IS NULL OR s.[Email] COLLATE Latin1_General_BIN <> v.[Email] OR DATALENGTH(s.[Email]) <> DATALENGTH(v.[Email]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[CamaraEmpresa] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[NumeroTelefono] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [NumeroTelefono],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Email] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Email]) n
 CROSS APPLY (SELECT CASE WHEN UPPER(n.[NumeroTelefono]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[NumeroTelefono] END AS [NumeroTelefono],
              CASE WHEN UPPER(n.[Email]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Email] END AS [Email]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[CamaraEmpresa] s
  JOIN dbo.CamaraEmpresa_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnCamara] <> 0 AND s.[EstadoEnCamara] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[NitEmpresa])
  FROM dbo.CamaraEmpresa_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnCamara] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[CamaraEmpresa] s WHERE s.[NitEmpresa] = r.[NitEmpresa] AND s.[EstadoEnCamara] <> 0);

PRINT CONCAT('CamaraEmpresa: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 13 / 6); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50274, 'CamaraEmpresa: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50275, 'CamaraEmpresa: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50276, 'CamaraEmpresa: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50277, 'CamaraEmpresa: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('CamaraEmpresa: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.CamaraEmpresa_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 18. PositivaEmpresas  (dbo.PositivaEmpresas; flag EstadoEnPositivaEmpresa; clave TitularID)
--     Medido 2026-09-24: 2.013.318 filas, 102.661 a tocar; flag 1 -> 0 en 0 filas / 0 titulares.
--     NIT                              1 a NULL           0 con blancos de sobra
--     Empresa                          0 a NULL     102.651 con blancos de sobra
--     EstadoAfiliacion                 0 a NULL          42 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'PositivaEmpresas' RETURN;

IF OBJECT_ID('dbo.PositivaEmpresas_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50281, 'dbo.PositivaEmpresas_RespaldoVacios_20260925 ya existe: PositivaEmpresas ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'PositivaEmpresas: ya normalizada (existe dbo.PositivaEmpresas_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'PositivaEmpresas' AND Estado = 'EN CURSO')
    THROW 50282, 'Hay un cargue PositivaEmpresas EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'PositivaEmpresas')
    PRINT 'PositivaEmpresas: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[PositivaEmpresas] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.PositivaEmpresas_RespaldoVacios_20260925
  FROM dbo.[PositivaEmpresas] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion]) n
 CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion]) v
 WHERE (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
    OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
    OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('PositivaEmpresas: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 102661); respaldo en dbo.PositivaEmpresas_RespaldoVacios_20260925');

IF @Respaldo > 153991
    THROW 50283, 'PositivaEmpresas: muchas más filas que las medidas el 2026-09-24 (102661). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnPositivaEmpresa] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnPositivaEmpresa], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.PositivaEmpresas_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion]) n
 CROSS APPLY (SELECT NULLIF(r.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (r.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                     THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnPositivaEmpresa] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NIT] = v.[NIT],
           s.[Empresa] = v.[Empresa],
           s.[EstadoAfiliacion] = v.[EstadoAfiliacion],
           s.[EstadoEnPositivaEmpresa] = CASE WHEN (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (r.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                  THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnPositivaEmpresa] END
      FROM dbo.[PositivaEmpresas] s
      JOIN dbo.PositivaEmpresas_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
                  TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion]) n
     CROSS APPLY (SELECT NULLIF(r.[NIT], 0) AS [NIT],
                  CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
                  CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('PositivaEmpresas: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                                     OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])))
                                     OR (s.[EstadoAfiliacion] IS NOT NULL AND (v.[EstadoAfiliacion] IS NULL OR s.[EstadoAfiliacion] COLLATE Latin1_General_BIN <> v.[EstadoAfiliacion] OR DATALENGTH(s.[EstadoAfiliacion]) <> DATALENGTH(v.[EstadoAfiliacion]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[PositivaEmpresas] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa],
              TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[EstadoAfiliacion] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [EstadoAfiliacion]) n
 CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa],
              CASE WHEN UPPER(n.[EstadoAfiliacion]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[EstadoAfiliacion] END AS [EstadoAfiliacion]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[PositivaEmpresas] s
  JOIN dbo.PositivaEmpresas_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnPositivaEmpresa] <> 0 AND s.[EstadoEnPositivaEmpresa] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.PositivaEmpresas_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnPositivaEmpresa] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[PositivaEmpresas] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnPositivaEmpresa] <> 0);

PRINT CONCAT('PositivaEmpresas: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 0 / 0); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50284, 'PositivaEmpresas: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50285, 'PositivaEmpresas: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50286, 'PositivaEmpresas: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50287, 'PositivaEmpresas: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('PositivaEmpresas: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.PositivaEmpresas_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- 19. ARLSura  (dbo.ARLSura; flag EstadoEnSura; clave TitularID)
--     Medido 2026-09-24: 1.565.455 filas, 18.043 a tocar; flag 1 -> 0 en 1 filas / 1 titulares.
--     Empresa                         10 a NULL      18.033 con blancos de sobra
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Solo varchar(30) = CAST(SESSION_CONTEXT(N'NormalizarVacios.Solo') AS varchar(30));
IF @Solo IS NOT NULL AND @Solo <> 'ARLSura' RETURN;

IF OBJECT_ID('dbo.ARLSura_RespaldoVacios_20260925', 'U') IS NOT NULL
BEGIN
    -- El respaldo se crea DENTRO de la transacción: si existe, la tabla ya se normalizó y se confirmó.
    IF @Solo IS NOT NULL
        THROW 50291, 'dbo.ARLSura_RespaldoVacios_20260925 ya existe: ARLSura ya se normalizó. No se pisa el respaldo.', 1;
    PRINT 'ARLSura: ya normalizada (existe dbo.ARLSura_RespaldoVacios_20260925); se salta.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'ARLSura' AND Estado = 'EN CURSO')
    THROW 50292, 'Hay un cargue ARLSura EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;
IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'ARLSura')
    PRINT 'ARLSura: AVISO, esta fuente ya tuvo cargues nuevos (sus filas ya vienen limpias); se normalizan solo las legacy.';

DECLARE @Inicio datetime2 = SYSDATETIME(), @Lote int = 50000, @Desde int = 1, @n int,
        @FilasAntes int, @FilasDespues int, @Respaldo int, @Actualizadas int = 0, @Restantes int,
        @Flag10 int = 0, @Flag01 int = 0, @Flag10Real int = 0, @Tit10 int = 0;

BEGIN TRAN;

-- Bloqueo exclusivo de la tabla hasta el COMMIT: nadie escribe (ni lee) mientras tanto.
SELECT @FilasAntes = COUNT(*) FROM dbo.[ARLSura] WITH (TABLOCKX, HOLDLOCK);

-- Respaldo de las filas a tocar, tal como están. RID = %%physloc%%: el heap no tiene
-- clave, y es lo que usan los lotes y la reversión para volver a la misma fila.
SELECT IDENTITY(int, 1, 1) AS FilaRespaldo, s.%%physloc%% AS RID, s.*
  INTO dbo.ARLSura_RespaldoVacios_20260925
  FROM dbo.[ARLSura] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa]) n
 CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa]) v
 WHERE (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
    OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa])));
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('ARLSura: ', @Respaldo, ' filas a normalizar (medido 2026-09-24: 18043); respaldo en dbo.ARLSura_RespaldoVacios_20260925');

IF @Respaldo > 27064
    THROW 50293, 'ARLSura: muchas más filas que las medidas el 2026-09-24 (18043). Revisar el bloque 0 antes de seguir.', 1;

-- Flags que va a cambiar (calculado desde el respaldo, antes de tocar la tabla).
SELECT @Flag10 = ISNULL(SUM(CASE WHEN r.[EstadoEnSura] <> 0 AND f.Nuevo = 0 THEN 1 ELSE 0 END), 0),
       @Flag01 = ISNULL(SUM(CASE WHEN ISNULL(r.[EstadoEnSura], 0) = 0 AND f.Nuevo = 1 THEN 1 ELSE 0 END), 0)
  FROM dbo.ARLSura_RespaldoVacios_20260925 r
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa]) n
 CROSS APPLY (SELECT NULLIF(r.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa]) v
 CROSS APPLY (SELECT CASE WHEN (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (r.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                     THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                     ELSE r.[EstadoEnSura] END AS Nuevo) f;

-- UPDATE por lotes de @Lote filas del respaldo. Los valores nuevos se calculan desde
-- el respaldo (= la fila antes del cambio); el flag, con esos mismos valores.
WHILE @Desde <= @Respaldo
BEGIN
    UPDATE s
       SET s.[NIT] = v.[NIT],
           s.[Empresa] = v.[Empresa],
           s.[EstadoEnSura] = CASE WHEN (r.[NIT] IS NOT NULL AND v.[NIT] IS NULL) OR (r.[Empresa] IS NOT NULL AND v.[Empresa] IS NULL)
                  THEN CASE WHEN v.[NIT] IS NOT NULL OR v.[Empresa] IS NOT NULL THEN 1 ELSE 0 END
                  ELSE r.[EstadoEnSura] END
      FROM dbo.[ARLSura] s
      JOIN dbo.ARLSura_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
     CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(r.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa]) n
     CROSS APPLY (SELECT NULLIF(r.[NIT], 0) AS [NIT],
                  CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa]) v
     WHERE r.FilaRespaldo BETWEEN @Desde AND @Desde + @Lote - 1;
    SET @n = @@ROWCOUNT;
    SET @Actualizadas += @n;
    SET @Desde += @Lote;
    RAISERROR('ARLSura: %d de %d filas normalizadas', 0, 1, @Actualizadas, @Respaldo) WITH NOWAIT;
END

-- ── Verificación: si algo no cuadra, THROW -> rollback de esta tabla ─────────
SELECT @FilasDespues = COUNT(*),
       @Restantes = ISNULL(SUM(CASE WHEN (s.[NIT] IS NOT NULL AND v.[NIT] IS NULL)
                                     OR (s.[Empresa] IS NOT NULL AND (v.[Empresa] IS NULL OR s.[Empresa] COLLATE Latin1_General_BIN <> v.[Empresa] OR DATALENGTH(s.[Empresa]) <> DATALENGTH(v.[Empresa]))) THEN 1 ELSE 0 END), 0)
  FROM dbo.[ARLSura] s
 CROSS APPLY (SELECT TRIM(REPLACE(REPLACE(REPLACE(TRANSLATE(s.[Empresa] COLLATE Latin1_General_BIN, CHAR(9)+CHAR(10)+CHAR(11)+CHAR(12)+CHAR(13)+CHAR(160), '      '), ' ', ' '+CHAR(1)), CHAR(1)+' ', ''), CHAR(1), '')) AS [Empresa]) n
 CROSS APPLY (SELECT NULLIF(s.[NIT], 0) AS [NIT],
              CASE WHEN UPPER(n.[Empresa]) IN ('', '0', 'NULL', 'N/A') THEN NULL ELSE n.[Empresa] END AS [Empresa]) v;

SELECT @Flag10Real = COUNT(*)
  FROM dbo.[ARLSura] s
  JOIN dbo.ARLSura_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%%
 WHERE r.[EstadoEnSura] <> 0 AND s.[EstadoEnSura] = 0;

-- Titulares que se quedan sin ninguna fila con flag 1 en la tabla.
SELECT @Tit10 = COUNT(DISTINCT r.[TitularID])
  FROM dbo.ARLSura_RespaldoVacios_20260925 r
 WHERE r.[EstadoEnSura] <> 0
   AND NOT EXISTS (SELECT 1 FROM dbo.[ARLSura] s WHERE s.[TitularID] = r.[TitularID] AND s.[EstadoEnSura] <> 0);

PRINT CONCAT('ARLSura: filas ', @FilasAntes, ' -> ', @FilasDespues, '; normalizadas ', @Actualizadas,
             '; pendientes ', @Restantes, '; flag 1->0 ', @Flag10, ' filas / ', @Tit10,
             ' titulares (medido 1 / 1); flag 0->1 ', @Flag01);

IF @Actualizadas <> @Respaldo
    THROW 50294, 'ARLSura: los lotes no tocaron exactamente las filas del respaldo. Rollback.', 1;
IF @Restantes <> 0
    THROW 50295, 'ARLSura: quedan centinelas o blancos de sobra. Rollback.', 1;
IF @FilasDespues <> @FilasAntes
    THROW 50296, 'ARLSura: cambió el número de filas. Rollback.', 1;
IF @Flag10Real <> @Flag10
    THROW 50297, 'ARLSura: el flag no quedó como se calculó. Rollback.', 1;

COMMIT TRAN;
PRINT CONCAT('ARLSura: OK en ', DATEDIFF(SECOND, @Inicio, SYSDATETIME()), ' s. Respaldo en dbo.ARLSura_RespaldoVacios_20260925.');
GO

-- ============================================================================
-- RESUMEN: qué tablas ya están normalizadas (tienen respaldo).
-- ============================================================================
SELECT t.name AS Respaldo, SUM(p.rows) AS Filas, t.create_date AS Creado
  FROM sys.tables t
  JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
 WHERE t.name LIKE '%_RespaldoVacios_20260925'
 GROUP BY t.name, t.create_date
 ORDER BY t.name;
EXEC sys.sp_set_session_context @key = N'NormalizarVacios.Solo', @value = NULL;
GO

-- ============================================================================
-- REVERSIÓN (comentada). Por tabla, desde su respaldo: devuelve a cada fila
-- respaldada sus valores y su flag de antes. Se casa por RID (%%physloc%%) y
-- clave. El RID de una fila de heap no cambia con estos UPDATE (solo acortan la
-- fila, no la mueven); SÍ cambia si se reconstruye la tabla (ALTER TABLE ...
-- REBUILD). Las filas que un cargue posterior haya borrado (purga de vacías) ya
-- no están: el PRINT dice cuántas se restauraron de cuántas.
-- Antes, revisar si corrió algún cargue de esa fuente después del script:
--   SELECT * FROM dbo.CargueFuenteEjecucion
--    WHERE Fuente = '<Fuente>' AND FechaInicio >= '2026-09-25' ORDER BY EjecucionID;
-- Correr solo el bloque de la tabla a revertir.
-- ============================================================================
/*
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Cajacopi
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Cajacopi_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefonoCelular] = r.[NumeroTelefonoCelular],
       s.[NumeroTelefonoFijo] = r.[NumeroTelefonoFijo],
       s.[Email] = r.[Email],
       s.[Departamento] = r.[Departamento],
       s.[Municipio] = r.[Municipio],
       s.[FechaNacimiento] = r.[FechaNacimiento],
       s.[EstadoEnCajacopi] = r.[EstadoEnCajacopi]
  FROM dbo.[Cajacopi] s WITH (TABLOCKX)
  JOIN dbo.Cajacopi_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Cajacopi: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Cajacopi_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- MutualSer
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.MutualSer_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefonoCelular] = r.[NumeroTelefonoCelular],
       s.[NumeroTelefonoCelular2] = r.[NumeroTelefonoCelular2],
       s.[NumeroTelefonoFijo] = r.[NumeroTelefonoFijo],
       s.[Email] = r.[Email],
       s.[Regimen] = r.[Regimen],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[Departamento] = r.[Departamento],
       s.[Municipio] = r.[Municipio],
       s.[EstadoEnMutualSer] = r.[EstadoEnMutualSer]
  FROM dbo.[MutualSer] s WITH (TABLOCKX)
  JOIN dbo.MutualSer_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('MutualSer: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.MutualSer_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- NuevaEps
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.NuevaEps_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[EstadoAfiliacion] = r.[EstadoAfiliacion],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[FechaNacimiento] = r.[FechaNacimiento],
       s.[Departamento] = r.[Departamento],
       s.[Municipio] = r.[Municipio],
       s.[EstadoEnNuevaEps] = r.[EstadoEnNuevaEps]
  FROM dbo.[NuevaEps] s WITH (TABLOCKX)
  JOIN dbo.NuevaEps_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('NuevaEps: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.NuevaEps_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- DatosClaro
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.DatosClaro_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email]
  FROM dbo.[DatosClaro] s WITH (TABLOCKX)
  JOIN dbo.DatosClaro_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('DatosClaro: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.DatosClaro_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- Positiva
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Positiva_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefonoCelular] = r.[NumeroTelefonoCelular],
       s.[NumeroTelefonoFijo] = r.[NumeroTelefonoFijo],
       s.[Email] = r.[Email],
       s.[FechaNacimiento] = r.[FechaNacimiento],
       s.[Departamento] = r.[Departamento],
       s.[Municipio] = r.[Municipio],
       s.[EstadoEnPositiva] = r.[EstadoEnPositiva]
  FROM dbo.[Positiva] s WITH (TABLOCKX)
  JOIN dbo.Positiva_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Positiva: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Positiva_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- SaludTotal
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.SaludTotal_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[FechaNacimiento] = r.[FechaNacimiento],
       s.[EstadoAfiliacion] = r.[EstadoAfiliacion],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[Ciudad] = r.[Ciudad],
       s.[NIT] = r.[NIT],
       s.[Empresa] = r.[Empresa],
       s.[Cargo] = r.[Cargo],
       s.[UltimaFechaPago] = r.[UltimaFechaPago],
       s.[IngresoBase] = r.[IngresoBase],
       s.[EstadoEnSaludTotal] = r.[EstadoEnSaludTotal]
  FROM dbo.[SaludTotal] s WITH (TABLOCKX)
  JOIN dbo.SaludTotal_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('SaludTotal: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.SaludTotal_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- SOS
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.SOS_RespaldoVacios_20260925);
UPDATE s
   SET s.[NitEmpresa] = r.[NitEmpresa],
       s.[RazonSocial] = r.[RazonSocial],
       s.[Telefono] = r.[Telefono],
       s.[Direccion] = r.[Direccion],
       s.[EstadoEnSOS] = r.[EstadoEnSOS]
  FROM dbo.[SOS] s WITH (TABLOCKX)
  JOIN dbo.SOS_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('SOS: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.SOS_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- Coosalud
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Coosalud_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[MunicipioOrigen] = r.[MunicipioOrigen],
       s.[DepartamentoOrigen] = r.[DepartamentoOrigen],
       s.[Regimen] = r.[Regimen],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[EstadoAfiliacion] = r.[EstadoAfiliacion],
       s.[NivelSisben] = r.[NivelSisben],
       s.[FechaNacimiento] = r.[FechaNacimiento],
       s.[EstadoEnCoosalud] = r.[EstadoEnCoosalud]
  FROM dbo.[Coosalud] s WITH (TABLOCKX)
  JOIN dbo.Coosalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Coosalud: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Coosalud_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- SaviaSalud
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.SaviaSalud_RespaldoVacios_20260925);
UPDATE s
   SET s.[FechaDeNacimiento] = r.[FechaDeNacimiento],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[EstadoDeAfiliacion] = r.[EstadoDeAfiliacion],
       s.[Regimen] = r.[Regimen],
       s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[NumeroCelular] = r.[NumeroCelular],
       s.[Estado] = r.[Estado]
  FROM dbo.[SaviaSalud] s WITH (TABLOCKX)
  JOIN dbo.SaviaSalud_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('SaviaSalud: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.SaviaSalud_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- MiVacuna
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.MiVacuna_RespaldoVacios_20260925);
UPDATE s
   SET s.[FechaExpedicion] = r.[FechaExpedicion],
       s.[Celular] = r.[Celular],
       s.[Email] = r.[Email],
       s.[EstadoEnMiVacuna] = r.[EstadoEnMiVacuna]
  FROM dbo.[MiVacuna] s WITH (TABLOCKX)
  JOIN dbo.MiVacuna_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('MiVacuna: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.MiVacuna_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- Fosyga
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Fosyga_RespaldoVacios_20260925);
UPDATE s
   SET s.[Estado] = r.[Estado],
       s.[EPS] = r.[EPS],
       s.[Regimen] = r.[Regimen],
       s.[FechaAfiliacionEfectiva] = r.[FechaAfiliacionEfectiva],
       s.[FechaFinalizacionAfiliacion] = r.[FechaFinalizacionAfiliacion],
       s.[TipoAfiliado] = r.[TipoAfiliado],
       s.[Departamento] = r.[Departamento],
       s.[Municipio] = r.[Municipio],
       s.[EstadoEnFosyga] = r.[EstadoEnFosyga]
  FROM dbo.[Fosyga] s WITH (TABLOCKX)
  JOIN dbo.Fosyga_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Fosyga: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Fosyga_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- RGM
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.RGM_RespaldoVacios_20260925);
UPDATE s
   SET s.[Marca] = r.[Marca],
       s.[Modelo] = r.[Modelo],
       s.[Placa] = r.[Placa],
       s.[EstadoEnRGM] = r.[EstadoEnRGM]
  FROM dbo.[RGM] s WITH (TABLOCKX)
  JOIN dbo.RGM_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('RGM: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.RGM_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- TigoUne
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.TigoUne_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[EstadoEnTigoUne] = r.[EstadoEnTigoUne]
  FROM dbo.[TigoUne] s WITH (TABLOCKX)
  JOIN dbo.TigoUne_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('TigoUne: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.TigoUne_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- Tigo
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Tigo_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[EstadoEnTigo] = r.[EstadoEnTigo]
  FROM dbo.[Tigo] s WITH (TABLOCKX)
  JOIN dbo.Tigo_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Tigo: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Tigo_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- RUES
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.RUES_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroMatricula] = r.[NumeroMatricula],
       s.[EstadoEmpresa] = r.[EstadoEmpresa],
       s.[ActividadEconomica] = r.[ActividadEconomica],
       s.[NombreEmpresa] = r.[NombreEmpresa],
       s.[EstadoEnRUES] = r.[EstadoEnRUES]
  FROM dbo.[RUES] s WITH (TABLOCKX)
  JOIN dbo.RUES_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('RUES: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.RUES_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- Camara
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.Camara_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[Municipio] = r.[Municipio],
       s.[Departamento] = r.[Departamento],
       s.[EstadoEnCamara] = r.[EstadoEnCamara]
  FROM dbo.[Camara] s WITH (TABLOCKX)
  JOIN dbo.Camara_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('Camara: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.Camara_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- CamaraEmpresa
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.CamaraEmpresa_RespaldoVacios_20260925);
UPDATE s
   SET s.[NumeroTelefono] = r.[NumeroTelefono],
       s.[Email] = r.[Email],
       s.[EstadoEnCamara] = r.[EstadoEnCamara]
  FROM dbo.[CamaraEmpresa] s WITH (TABLOCKX)
  JOIN dbo.CamaraEmpresa_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[NitEmpresa] = s.[NitEmpresa];
PRINT CONCAT('CamaraEmpresa: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.CamaraEmpresa_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- PositivaEmpresas
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.PositivaEmpresas_RespaldoVacios_20260925);
UPDATE s
   SET s.[NIT] = r.[NIT],
       s.[Empresa] = r.[Empresa],
       s.[EstadoAfiliacion] = r.[EstadoAfiliacion],
       s.[EstadoEnPositivaEmpresa] = r.[EstadoEnPositivaEmpresa]
  FROM dbo.[PositivaEmpresas] s WITH (TABLOCKX)
  JOIN dbo.PositivaEmpresas_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('PositivaEmpresas: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.PositivaEmpresas_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

-- ARLSura
BEGIN TRAN;
DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.ARLSura_RespaldoVacios_20260925);
UPDATE s
   SET s.[NIT] = r.[NIT],
       s.[Empresa] = r.[Empresa],
       s.[EstadoEnSura] = r.[EstadoEnSura]
  FROM dbo.[ARLSura] s WITH (TABLOCKX)
  JOIN dbo.ARLSura_RespaldoVacios_20260925 r ON r.RID = s.%%physloc%% AND r.[TitularID] = s.[TitularID];
PRINT CONCAT('ARLSura: ', @@ROWCOUNT, ' de ', @Respaldo, ' filas restauradas');
COMMIT TRAN;
-- DROP TABLE dbo.ARLSura_RespaldoVacios_20260925;   -- cuando ya no haga falta
GO

*/
