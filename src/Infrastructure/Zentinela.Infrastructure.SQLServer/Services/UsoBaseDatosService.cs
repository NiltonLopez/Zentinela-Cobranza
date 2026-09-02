using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zentinela.Application.DTOs.InformesGenerales;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Cruce masivo de teléfonos contra las fuentes de localización de DBCallCenter.
///
/// Es la versión masiva de <see cref="RastreoLineasService"/>: ese resuelve un número por
/// vez con <c>WHERE col = @numero</c>; aquí los números se cargan a una tabla <c>#temp</c>
/// y cada rama pasa a ser <c>JOIN #Telefonos ON col = t.Numero</c>, de modo que SQL Server
/// resuelve las 14 columnas con hash joins escaneando cada tabla una sola vez
/// (~12,2M filas en total, medido en ~1-2 s). Consultar número por número sería inviable.
///
/// El conjunto de tablas y columnas es DELIBERADAMENTE el mismo que el de
/// <see cref="RastreoLineasService"/>: ambos módulos deben dar la misma respuesta para el
/// mismo número. Si se agrega una fuente allá, hay que agregarla acá.
///
/// No persiste nada: las tres tablas <c>#temp</c> mueren al cerrar la conexión
/// (mismo patrón que <see cref="CargueReporteService"/>, ADR-011).
/// </summary>
public class UsoBaseDatosService : IUsoBaseDatosService
{
    /// <summary>
    /// Tope de números distintos aceptados en una sola consulta. Es una guarda de
    /// memoria/tiempo, no un límite del algoritmo: el cruce es un hash join, así que
    /// el costo lo domina el escaneo de las fuentes (~12,2M filas) y crece muy poco
    /// con la cantidad de números cargados.
    /// </summary>
    private const int MaxTelefonosDistintos = 1_000_000;

    private readonly CallCenterDbContext          _db;
    private readonly ILogger<UsoBaseDatosService> _logger;

    public UsoBaseDatosService(CallCenterDbContext db, ILogger<UsoBaseDatosService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Encabezados aceptados para la columna de teléfono (ya normalizados por
    /// <see cref="ArchivoTabular.Normalizar"/>). Se resuelve por nombre, no por posición (ADR-004).
    /// Si ninguno matchea, el archivo se rechaza indicando los encabezados que sí traía —
    /// deliberadamente NO se adivina la columna: en un informe de acreditación, elegir mal
    /// produciría cifras incorrectas en silencio.
    /// </summary>
    private static readonly HashSet<string> AliasColumnaTelefono = new(StringComparer.Ordinal)
    {
        "numerotelefono", "numerodetelefono", "telefono", "numero", "numerolinea", "linea",
        "celular", "numerocelular", "numerodecelular", "movil", "numeromovil",
        "numerotelefonocelular", "telefonocelular", "numeromarcado", "marcado",
        "msisdn", "destino", "numerodestino", "telefonodestino", "numerocontacto", "contacto",
    };

    public async Task<UsoBaseDatosDto> AnalizarAsync(
        IReadOnlyList<ArchivoTelefonos> archivos, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        var dto        = new UsoBaseDatosDto();

        if (archivos.Count == 0)
        {
            dto.Advertencias.Add("No se recibió ningún archivo.");
            return dto;
        }

        // ── 1. Parseo por archivo ─────────────────────────────────────────────
        // Un archivo malo NO tumba el request: se marca Ok=false con su motivo y se
        // procesan los demás. Con 15 archivos, fallar todo por uno sería inutilizable.
        var numerosGlobales = new HashSet<string>(StringComparer.Ordinal);
        var celulares       = new HashSet<string>(StringComparer.Ordinal);
        var sumaDistintosPorArchivo = 0;

        foreach (var archivo in archivos)
        {
            ct.ThrowIfCancellationRequested();
            var resumen = new ArchivoProcesadoDto { NombreArchivo = archivo.NombreArchivo };

            try
            {
                var (numerosDelArchivo, celularesDelArchivo) =
                    await ProcesarArchivoAsync(archivo, resumen);

                sumaDistintosPorArchivo += numerosDelArchivo.Count;
                numerosGlobales.UnionWith(numerosDelArchivo);
                celulares.UnionWith(celularesDelArchivo);
            }
            catch (Exception ex)
            {
                resumen.Ok      = false;
                resumen.Mensaje = $"No se pudo leer el archivo: {ex.Message}";
                _logger.LogWarning(ex, "Fallo al procesar el archivo {Archivo}", archivo.NombreArchivo);
            }

            dto.Archivos.Add(resumen);
            dto.TotalFilasLeidas        += resumen.FilasLeidas;
            dto.TotalTelefonosValidos   += resumen.TelefonosValidos;
            dto.TotalTelefonosInvalidos += resumen.TelefonosInvalidos;
        }

        dto.TelefonosDistintos        = numerosGlobales.Count;
        dto.SolapamientoEntreArchivos = Math.Max(0, sumaDistintosPorArchivo - numerosGlobales.Count);
        dto.TelefonosCelulares        = celulares.Count;
        dto.TelefonosFijos            = numerosGlobales.Count - celulares.Count;

        if (numerosGlobales.Count > MaxTelefonosDistintos)
            throw new InvalidOperationException(
                $"Se recibieron {numerosGlobales.Count:N0} números distintos y el máximo por consulta " +
                $"es {MaxTelefonosDistintos:N0}. Divide los archivos en varias cargas.");

        if (numerosGlobales.Count == 0)
        {
            dto.Advertencias.Add("Ningún archivo aportó teléfonos válidos — no se consultó la base de datos.");
            dto.DuracionMs = cronometro.ElapsedMilliseconds;
            return dto;
        }

        // ── 2. Cruce contra la BD ─────────────────────────────────────────────
        await EjecutarCruceAsync(numerosGlobales, dto, ct);

        // ── 3. Derivados ──────────────────────────────────────────────────────
        dto.NumerosNoEncontrados = dto.TelefonosDistintos - dto.NumerosEnBaseDatos;
        dto.PorcentajeUso = dto.TelefonosDistintos > 0
            ? Math.Round(100m * dto.NumerosEnBaseDatos / dto.TelefonosDistintos, 2)
            : 0m;

        foreach (var f in dto.PorFuente)
            f.Porcentaje = dto.NumerosEnBaseDatos > 0
                ? Math.Round(100m * f.NumerosDistintos / dto.NumerosEnBaseDatos, 2)
                : 0m;

        foreach (var l in dto.PorLider)
        {
            l.Porcentaje = dto.NumerosEnBaseDatos > 0
                ? Math.Round(100m * l.NumerosAcreditados / dto.NumerosEnBaseDatos, 2)
                : 0m;

            // Uso real de SU base. Se mide en titulares contra titulares: usar números en
            // el numerador daría > 100 % porque un titular puede aportar varios teléfonos.
            l.PorcentajeCobertura = l.TitularesAsignados > 0
                ? Math.Round(100m * l.TitularesDistintos / l.TitularesAsignados, 2)
                : null;
        }

        foreach (var lc in dto.PorLiderCartera)
            lc.PorcentajeCobertura = lc.TitularesAsignados > 0
                ? Math.Round(100m * lc.TitularesDistintos / lc.TitularesAsignados, 2)
                : null;

        if (dto.TitularesHuerfanos > 0)
            dto.Advertencias.Add(
                $"{dto.TitularesHuerfanos:N0} titular(es) aparecen en las fuentes pero no existen en dbo.Titular. " +
                "Sus números sí cuentan como uso de base de datos.");

        dto.DuracionMs = cronometro.ElapsedMilliseconds;

        _logger.LogInformation(
            "Uso de Base de Datos: {Archivos} archivo(s), {Distintos} número(s) distinto(s), " +
            "{Encontrados} en BD ({Porcentaje}%), {ConLider} con líder, {SinLider} sin líder, {Ms} ms",
            archivos.Count, dto.TelefonosDistintos, dto.NumerosEnBaseDatos,
            dto.PorcentajeUso, dto.NumerosConLider, dto.NumerosSinLider, dto.DuracionMs);

        return dto;
    }

    // ── Parseo ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extrae los teléfonos válidos de un archivo. Rellena <paramref name="resumen"/> con
    /// los conteos y, si algo falla, con Ok=false y el motivo.
    /// </summary>
    private static async Task<(HashSet<string> Numeros, HashSet<string> Celulares)> ProcesarArchivoAsync(
        ArchivoTelefonos archivo, ArchivoProcesadoDto resumen)
    {
        var numeros   = new HashSet<string>(StringComparer.Ordinal);
        var celulares = new HashSet<string>(StringComparer.Ordinal);

        var filas = archivo.Extension == ".xlsx"
            ? ArchivoTabular.ExtraerFilasXlsx(archivo.Contenido)
            : (await ArchivoTabular.ExtraerFilasCsvAsync(archivo.Contenido)).Filas;

        if (filas.Count == 0)
        {
            resumen.Ok      = false;
            resumen.Mensaje = "El archivo está vacío.";
            return (numeros, celulares);
        }

        // Resolver la columna de teléfono por nombre de encabezado.
        var encabezados = filas[0];
        var indice      = -1;
        for (var i = 0; i < encabezados.Length; i++)
        {
            if (!AliasColumnaTelefono.Contains(ArchivoTabular.Normalizar(encabezados[i]))) continue;
            indice = i;
            resumen.ColumnaDetectada = encabezados[i].Trim();
            break;
        }

        if (indice < 0)
        {
            var vistos = string.Join(", ", encabezados.Select(h => h.Trim()).Where(h => h.Length > 0));
            resumen.Ok      = false;
            resumen.Mensaje = $"No se encontró una columna de teléfono. Columnas en el archivo: {vistos}.";
            return (numeros, celulares);
        }

        for (var i = 1; i < filas.Count; i++)
        {
            var partes = filas[i];
            if (partes.All(string.IsNullOrWhiteSpace)) continue;

            resumen.FilasLeidas++;

            // El CSV se parte con Split plano: una fila corta no debe reventar el archivo.
            if (indice >= partes.Length)
            {
                resumen.TelefonosInvalidos++;
                continue;
            }

            var (numero, esCelular) = TelefonoColombiano.Normalizar(partes[indice]);
            if (numero is null)
            {
                resumen.TelefonosInvalidos++;
                continue;
            }

            resumen.TelefonosValidos++;
            numeros.Add(numero);
            if (esCelular) celulares.Add(numero);
        }

        resumen.TelefonosDistintos = numeros.Count;
        resumen.Ok                 = true;

        if (numeros.Count == 0)
            resumen.Mensaje = "La columna se encontró pero no aportó ningún teléfono válido.";

        return (numeros, celulares);
    }

    // ── Cruce ─────────────────────────────────────────────────────────────────

    private async Task EjecutarCruceAsync(
        HashSet<string> numeros, UsoBaseDatosDto dto, CancellationToken ct)
    {
        var conn = (SqlConnection)_db.Database.GetDbConnection();

        // Solo cerramos la conexión si la abrimos nosotros: si otro código de la misma
        // request ya la tenía abierta, cerrarla tiraría SUS tablas #temp.
        var laAbriYo = conn.State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);

        try
        {
            await using (var crear = new SqlCommand(SqlCrearTemp, conn))
                await crear.ExecuteNonQueryAsync(ct);

            var tabla = new DataTable();
            tabla.Columns.Add("Numero", typeof(string));
            foreach (var n in numeros) tabla.Rows.Add(n);

            using (var bulk = new SqlBulkCopy(conn)
                   {
                       DestinationTableName = "#Telefonos",
                       BatchSize            = 10_000,
                       BulkCopyTimeout      = 300,
                   })
            {
                bulk.ColumnMappings.Add("Numero", "Numero");
                await bulk.WriteToServerAsync(tabla, ct);
            }

            // 10 min. Medido: 1.000.000 de números con 100 % de coincidencia (el peor caso,
            // porque maximiza el tamaño de #Acreditacion) tarda ~115 s. Se deja holgura para
            // un servidor cargado; el tope real de entrada es MaxTelefonosDistintos.
            await using var cmd = new SqlCommand(SqlCruce, conn) { CommandTimeout = 600 };
            await using var rd  = await cmd.ExecuteReaderAsync(ct);

            await LeerEscalaresAsync(rd, dto, ct);
            await LeerPorFuenteAsync(rd, dto, ct);
            await LeerPorLiderAsync(rd, dto, ct);
            await LeerPorLiderCarteraAsync(rd, dto, ct);
            await LeerTopCompartidosAsync(rd, dto, ct);
            await LeerNoEncontradosAsync(rd, dto, ct);
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    private static async Task LeerEscalaresAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        if (!await rd.ReadAsync(ct)) return;
        dto.NumerosEnBaseDatos  = rd.GetInt32(rd.GetOrdinal("NumerosEnBaseDatos"));
        dto.TitularesDistintos  = rd.GetInt32(rd.GetOrdinal("TitularesDistintos"));
        dto.TitularesHuerfanos  = rd.GetInt32(rd.GetOrdinal("TitularesHuerfanos"));
        dto.NumerosConLider     = rd.GetInt32(rd.GetOrdinal("NumerosConLider"));
        dto.NumerosSinLider     = rd.GetInt32(rd.GetOrdinal("NumerosSinLider"));
        dto.NumerosCompartidos  = rd.GetInt32(rd.GetOrdinal("NumerosCompartidos"));
        dto.NumerosUnSoloLider  = rd.GetInt32(rd.GetOrdinal("NumerosUnSoloLider"));
        dto.LideresDistintos    = rd.GetInt32(rd.GetOrdinal("LideresDistintos"));
        dto.SumaAcreditaciones  = rd.GetInt32(rd.GetOrdinal("SumaAcreditaciones"));
    }

    private static async Task LeerPorFuenteAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            dto.PorFuente.Add(new UsoPorFuenteDto
            {
                Fuente             = rd.GetString(0),
                Etiqueta           = rd.GetString(1),
                NumerosDistintos   = rd.GetInt32(2),
                TitularesDistintos = rd.GetInt32(3),
                Apariciones        = rd.GetInt32(4),
            });
    }

    private static async Task LeerPorLiderAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            dto.PorLider.Add(new UsoPorLiderDto
            {
                LiderID                 = rd.IsDBNull(0) ? null : rd.GetInt32(0),
                Lider                   = rd.GetString(1),
                NumerosAcreditados      = rd.GetInt32(2),
                TitularesDistintos      = rd.GetInt32(3),
                CarterasDistintas       = rd.GetInt32(4),
                TitularesAsignados      = rd.GetInt32(5),
                RelacionInactiva        = rd.GetBoolean(6),
                SinRelacionLiderCartera = rd.GetBoolean(7),
            });
    }

    private static async Task LeerPorLiderCarteraAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            dto.PorLiderCartera.Add(new UsoPorLiderCarteraDto
            {
                LiderID                 = rd.IsDBNull(0) ? null : rd.GetInt32(0),
                Lider                   = rd.GetString(1),
                CarteraID               = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                Cartera                 = rd.GetString(3),
                NumerosAcreditados      = rd.GetInt32(4),
                TitularesDistintos      = rd.GetInt32(5),
                TitularesAsignados      = rd.GetInt32(6),
                EsExterna               = rd.GetBoolean(7),
                RelacionInactiva        = rd.GetBoolean(8),
                SinRelacionLiderCartera = rd.GetBoolean(9),
            });
    }

    private static async Task LeerTopCompartidosAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            dto.TopCompartidos.Add(new UsoNumeroCompartidoDto
            {
                Numero         = rd.GetString(0),
                Lideres        = rd.GetInt32(1),
                Titulares      = rd.GetInt32(2),
                LideresNombres = rd.IsDBNull(3) ? string.Empty : rd.GetString(3),
            });
    }

    private static async Task LeerNoEncontradosAsync(SqlDataReader rd, UsoBaseDatosDto dto, CancellationToken ct)
    {
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
            dto.MuestraNoEncontrados.Add(rd.GetString(0));
    }

    // ── SQL ───────────────────────────────────────────────────────────────────

    // varchar(10) y NO nvarchar: nvarchar tiene mayor precedencia de tipo y forzaría un
    // CONVERT_IMPLICIT sobre la COLUMNA DE ORIGEN en cada una de los ~12,2M filas,
    // anulando el hash join. COLLATE DATABASE_DEFAULT evita heredar la colación de tempdb.
    private const string SqlCrearTemp = """
        CREATE TABLE #Telefonos (
            Numero varchar(10) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY CLUSTERED
        );
        """;

    // Adaptación masiva de RastreoLineasService.SqlConsulta: cada `WHERE col = @numero`
    // pasa a ser `JOIN #Telefonos t ON col = t.Numero`.
    //
    // NO se hace INNER JOIN a dbo.Titular: un TitularID huérfano (presente en la fuente,
    // ausente de la maestra) igual acredita "el número está en nuestra base de datos".
    private const string SqlCruce = """
        SET NOCOUNT ON;

        -- ── PASO 1: coincidencias número ↔ fuente ↔ titular ────────────────────
        SELECT Numero, Fuente, Etiqueta, TitularID, SUM(Apariciones) AS Apariciones
        INTO #Coincidencias
        FROM (
            SELECT t.Numero, 'Cajacopi' AS Fuente, 'Cajacopi (celular)' AS Etiqueta,
                   x.TitularID, COUNT(*) AS Apariciones
              FROM #Telefonos t JOIN dbo.Cajacopi x ON x.NumeroTelefonoCelular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Cajacopi', 'Cajacopi (fijo)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Cajacopi x ON x.NumeroTelefonoFijo = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Camara', 'Cámara', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Camara x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Positiva', 'Positiva (celular)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Positiva x ON x.NumeroTelefonoCelular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Positiva', 'Positiva (fijo)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Positiva x ON x.NumeroTelefonoFijo = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'SaludTotal', 'Salud Total', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.SaludTotal x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'SOS', 'SOS', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.SOS x ON x.Telefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Tigo', 'Tigo', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Tigo x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'TigoUne', 'Tigo Une', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.TigoUne x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Claro', 'Claro', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.DatosClaro x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'MutualSer', 'MutualSer (celular 1)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.MutualSer x ON x.NumeroTelefonoCelular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'MutualSer', 'MutualSer (celular 2)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.MutualSer x ON x.NumeroTelefonoCelular2 = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'MutualSer', 'MutualSer (fijo)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.MutualSer x ON x.NumeroTelefonoFijo = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'MiVacuna', 'MiVacuna (celular)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.MiVacuna x ON x.Celular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'Coosalud', 'Coosalud', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Coosalud x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            -- Sura — único teléfono es NumeroCelular (no tiene columna de fijo).
            SELECT t.Numero, 'Sura', 'Sura', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.Sura x ON x.NumeroCelular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            -- SaviaSalud es la única fuente con DOS columnas de teléfono. Ambas ramas
            -- llevan Fuente = 'SaviaSalud' (no una por columna): si se separaran, un
            -- titular con dato en las dos contaría como dos fuentes distintas y
            -- distorsionaría el desglose por fuente.
            SELECT t.Numero, 'SaviaSalud', 'Savia Salud (celular)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.SaviaSalud x ON x.NumeroCelular = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            SELECT t.Numero, 'SaviaSalud', 'Savia Salud (teléfono)', x.TitularID, COUNT(*)
              FROM #Telefonos t JOIN dbo.SaviaSalud x ON x.NumeroTelefono = t.Numero
             GROUP BY t.Numero, x.TitularID
            UNION ALL
            -- NuevaEps guarda TitularID como varchar(30): se castea para unificar el tipo
            SELECT t.Numero, 'NuevaEps', 'NuevaEps (teléfono)',
                   TRY_CAST(x.TitularID AS BIGINT), COUNT(*)
              FROM #Telefonos t JOIN dbo.NuevaEps x ON x.NumeroTelefono = t.Numero
             WHERE TRY_CAST(x.TitularID AS BIGINT) IS NOT NULL
             GROUP BY t.Numero, TRY_CAST(x.TitularID AS BIGINT)
        ) q
        GROUP BY Numero, Fuente, Etiqueta, TitularID;

        CREATE CLUSTERED INDEX IX_Coincidencias ON #Coincidencias (TitularID, Numero);

        -- ── PASO 2: acreditación número ↔ titular ↔ líder ──────────────────────
        -- Titular_Cartera es la tabla de iteración, NUNCA Lider_Cartera. Iterar desde
        -- Lider_Cartera es exactamente el bug de InformesRecaudoService.GetInformeLideresAsync,
        -- donde las filas sin par correspondiente desaparecían del total en silencio.
        -- El LEFT JOIN garantiza que un titular sin asignación conserve su fila con LiderID NULL.
        SELECT DISTINCT c.Numero, c.TitularID, tc.LiderID, tc.CarteraID
        INTO #Acreditacion
        FROM #Coincidencias c
        LEFT JOIN dbo.Titular_Cartera tc ON tc.TitularID = c.TitularID;

        CREATE CLUSTERED INDEX IX_Acreditacion ON #Acreditacion (Numero, LiderID);

        -- Números encontrados en BD que no tienen NINGÚN líder: bucket residual explícito.
        SELECT Numero
        INTO #SinLider
        FROM #Acreditacion
        GROUP BY Numero
        HAVING COUNT(LiderID) = 0;

        -- ── PASO 3: tamaño de la base asignada a cada líder ────────────────────
        -- Denominador del "uso real": cuántos titulares tiene asignados el líder en
        -- Titular_Cartera. Se restringe a los líderes que aparecen en el cruce para no
        -- agregar los 2,5M de filas completos.
        SELECT LiderID
        INTO #LideresDelCruce
        FROM #Acreditacion
        WHERE LiderID IS NOT NULL
        GROUP BY LiderID;

        -- Por líder: DISTINCT porque el mismo titular puede estar en varias carteras
        -- del mismo líder y no debe contarse dos veces.
        SELECT tc.LiderID, COUNT(DISTINCT tc.TitularID) AS TitularesAsignados
        INTO #AsigLider
        FROM dbo.Titular_Cartera tc
        JOIN #LideresDelCruce l ON l.LiderID = tc.LiderID
        GROUP BY tc.LiderID;

        -- Por líder-cartera: la PK (TitularID, LiderID, CarteraID) ya garantiza unicidad,
        -- asi que COUNT(*) equivale a COUNT(DISTINCT TitularID) y es mas barato.
        SELECT tc.LiderID, tc.CarteraID, COUNT(*) AS TitularesAsignados
        INTO #AsigLiderCartera
        FROM dbo.Titular_Cartera tc
        JOIN #LideresDelCruce l ON l.LiderID = tc.LiderID
        GROUP BY tc.LiderID, tc.CarteraID;

        -- ── RESULT SET 1: escalares ────────────────────────────────────────────
        SELECT
            (SELECT COUNT(DISTINCT Numero)    FROM #Coincidencias)        AS NumerosEnBaseDatos,
            (SELECT COUNT(DISTINCT TitularID) FROM #Coincidencias)        AS TitularesDistintos,
            (SELECT COUNT(*) FROM (SELECT DISTINCT c.TitularID
                                     FROM #Coincidencias c
                                    WHERE NOT EXISTS (SELECT 1 FROM dbo.Titular t
                                                       WHERE t.NumeroDocumento = c.TitularID)) h)
                                                                          AS TitularesHuerfanos,
            (SELECT COUNT(DISTINCT Numero) FROM #Acreditacion
              WHERE LiderID IS NOT NULL)                                  AS NumerosConLider,
            (SELECT COUNT(*) FROM #SinLider)                              AS NumerosSinLider,
            (SELECT COUNT(*) FROM (SELECT Numero FROM #Acreditacion
                                    WHERE LiderID IS NOT NULL
                                    GROUP BY Numero
                                   HAVING COUNT(DISTINCT LiderID) > 1) k) AS NumerosCompartidos,
            (SELECT COUNT(*) FROM (SELECT Numero FROM #Acreditacion
                                    WHERE LiderID IS NOT NULL
                                    GROUP BY Numero
                                   HAVING COUNT(DISTINCT LiderID) = 1) u) AS NumerosUnSoloLider,
            (SELECT COUNT(DISTINCT LiderID) FROM #Acreditacion
              WHERE LiderID IS NOT NULL)                                  AS LideresDistintos,
            (SELECT COUNT(*) FROM (SELECT DISTINCT Numero, LiderID FROM #Acreditacion
                                    WHERE LiderID IS NOT NULL) s)         AS SumaAcreditaciones;

        -- ── RESULT SET 2: por fuente ───────────────────────────────────────────
        SELECT Fuente,
               Etiqueta,
               COUNT(DISTINCT Numero)    AS NumerosDistintos,
               COUNT(DISTINCT TitularID) AS TitularesDistintos,
               SUM(Apariciones)          AS Apariciones
          FROM #Coincidencias
         GROUP BY Fuente, Etiqueta
         ORDER BY NumerosDistintos DESC, Etiqueta;

        -- ── RESULT SET 3: por líder ────────────────────────────────────────────
        -- El bucket NULL se restringe a los números de #SinLider, para que
        -- {ConLider} ∪ {SinLider} particione exactamente NumerosEnBaseDatos.
        -- Lider_Cartera entra SOLO como LEFT JOIN de enriquecimiento, jamás como filtro.
        SELECT a.LiderID,
               CASE WHEN a.LiderID IS NULL THEN '(sin líder asignado)'
                    ELSE ISNULL(l.Nombre, CONCAT('(líder ', a.LiderID, ' inexistente)')) END AS Lider,
               COUNT(DISTINCT a.Numero)    AS NumerosAcreditados,
               COUNT(DISTINCT a.TitularID) AS TitularesDistintos,
               COUNT(DISTINCT a.CarteraID) AS CarterasDistintas,
               ISNULL(MAX(al.TitularesAsignados), 0) AS TitularesAsignados,
               CAST(MAX(CASE WHEN a.LiderID IS NOT NULL AND lc.LiderID IS NOT NULL AND lc.Activo = 0
                             THEN 1 ELSE 0 END) AS bit) AS RelacionInactiva,
               CAST(MAX(CASE WHEN a.LiderID IS NOT NULL AND lc.LiderID IS NULL
                             THEN 1 ELSE 0 END) AS bit) AS SinRelacionLiderCartera
          FROM #Acreditacion a
          LEFT JOIN dbo.Lider l          ON l.ID = a.LiderID
          LEFT JOIN dbo.Lider_Cartera lc ON lc.LiderID = a.LiderID AND lc.CarteraID = a.CarteraID
          LEFT JOIN #AsigLider al        ON al.LiderID = a.LiderID
         WHERE a.LiderID IS NOT NULL
            OR EXISTS (SELECT 1 FROM #SinLider s WHERE s.Numero = a.Numero)
         GROUP BY a.LiderID, l.Nombre
         ORDER BY NumerosAcreditados DESC;

        -- ── RESULT SET 4: por líder × cartera ──────────────────────────────────
        SELECT a.LiderID,
               CASE WHEN a.LiderID IS NULL THEN '(sin líder asignado)'
                    ELSE ISNULL(l.Nombre, CONCAT('(líder ', a.LiderID, ' inexistente)')) END AS Lider,
               a.CarteraID,
               CASE WHEN a.CarteraID IS NULL THEN '(sin cartera)'
                    ELSE ISNULL(ca.NombreCartera, CONCAT('(cartera ', a.CarteraID, ' inexistente)')) END AS Cartera,
               COUNT(DISTINCT a.Numero)    AS NumerosAcreditados,
               COUNT(DISTINCT a.TitularID) AS TitularesDistintos,
               ISNULL(MAX(alc.TitularesAsignados), 0) AS TitularesAsignados,
               CAST(ISNULL(MAX(CAST(lc.EsExterna AS int)), 0) AS bit) AS EsExterna,
               CAST(CASE WHEN MAX(CAST(lc.Activo AS int)) = 0 THEN 1 ELSE 0 END AS bit) AS RelacionInactiva,
               CAST(CASE WHEN a.LiderID IS NOT NULL
                          AND MAX(CASE WHEN lc.LiderID IS NULL THEN 1 ELSE 0 END) = 1
                         THEN 1 ELSE 0 END AS bit) AS SinRelacionLiderCartera
          FROM #Acreditacion a
          LEFT JOIN dbo.Lider l          ON l.ID  = a.LiderID
          LEFT JOIN dbo.Cartera ca       ON ca.ID = a.CarteraID
          LEFT JOIN dbo.Lider_Cartera lc ON lc.LiderID = a.LiderID AND lc.CarteraID = a.CarteraID
          LEFT JOIN #AsigLiderCartera alc ON alc.LiderID = a.LiderID AND alc.CarteraID = a.CarteraID
         WHERE a.LiderID IS NOT NULL
            OR EXISTS (SELECT 1 FROM #SinLider s WHERE s.Numero = a.Numero)
         GROUP BY a.LiderID, l.Nombre, a.CarteraID, ca.NombreCartera
         ORDER BY NumerosAcreditados DESC;

        -- ── RESULT SET 5: TOP 50 números compartidos ───────────────────────────
        -- Se agrega sobre pares DISTINCT (Numero, LiderID) — sin el TitularID. Si se
        -- incluyera, un líder con 2 titulares para el mismo número aportaría 2 filas y
        -- STRING_AGG repetiría su nombre (STRING_AGG no admite DISTINCT).
        WITH NumLider AS (
            SELECT DISTINCT Numero, LiderID
              FROM #Acreditacion WHERE LiderID IS NOT NULL
        ),
        NumTitular AS (
            SELECT Numero, COUNT(DISTINCT TitularID) AS Titulares
              FROM #Acreditacion WHERE LiderID IS NOT NULL
             GROUP BY Numero
        )
        SELECT TOP (50)
               nl.Numero,
               COUNT(*)          AS Lideres,      -- una fila por líder distinto
               MIN(nt.Titulares) AS Titulares,    -- constante dentro del grupo
               STRING_AGG(CAST(ISNULL(l.Nombre, CONCAT('#', nl.LiderID)) AS nvarchar(max)), ', ')
                   WITHIN GROUP (ORDER BY l.Nombre) AS LideresNombres
          FROM NumLider nl
          JOIN NumTitular nt ON nt.Numero = nl.Numero
          LEFT JOIN dbo.Lider l ON l.ID = nl.LiderID
         GROUP BY nl.Numero
        HAVING COUNT(*) > 1
         ORDER BY Lideres DESC, Titulares DESC, nl.Numero;

        -- ── RESULT SET 6: muestra de no encontrados ────────────────────────────
        SELECT TOP (100) t.Numero
          FROM #Telefonos t
         WHERE NOT EXISTS (SELECT 1 FROM #Coincidencias c WHERE c.Numero = t.Numero)
         ORDER BY t.Numero;
        """;
}
