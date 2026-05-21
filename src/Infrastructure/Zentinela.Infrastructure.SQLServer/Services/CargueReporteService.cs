using System.Data;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Zentinela.Application.DTOs.Reportes;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Cargue masivo de reportes de estado (UltimoEstadoTitular + HistoricoEstadoTitular).
/// Flujo: ValidarArchivoAsync (parse + validación en memoria, sin persistir)
///      → ConfirmarCargueAsync (SqlBulkCopy a tabla temporal → MERGE + INSERT).
///
/// Soporta archivos CSV y XLSX. Las columnas pueden ir en cualquier orden;
/// se localizan por nombre normalizado (sin tildes, sin espacios, minúsculas).
///
/// Columnas requeridas y alias aceptados:
///   TitularID      → titularid, cedula, numerodocumento, documento, identificacion
///   UsuarioControl → usuariocontrol, usuario, usuariocontrolnext, control, agente
///
/// Todas las demás columnas son opcionales y se mapean si están presentes.
/// </summary>
public class CargueReporteService : ICargueReporteService
{
    private readonly CallCenterDbContext _db;
    private readonly ILogger<CargueReporteService> _logger;
    private readonly ICalificadorCarteraCache _calificadorCache;

    public CargueReporteService(
        CallCenterDbContext db,
        ILogger<CargueReporteService> logger,
        ICalificadorCarteraCache calificadorCache)
    {
        _db               = db;
        _logger           = logger;
        _calificadorCache = calificadorCache;
    }

    // ── Última actualización ──────────────────────────────────────────────────

    public async Task<DateTime?> ObtenerUltimaActualizacionAsync(int carteraID) =>
        await _db.UltimosEstadosTitular
            .Where(u => u.CarteraID == carteraID)
            .MaxAsync(u => (DateTime?)u.FechaActualizacion);

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte");

        var columnas = new[]
        {
            "TitularID", "Nombre", "UsuarioControl",
            "UltimoPerfil", "MejorPerfil",
            "SaldoTotal", "NumeroMarcado", "Rotacion",
            "FechaUltimaGestion", "FechaPromesa", "ValorPromesa",
            "MotivoNoPago", "FechaActualizacion",
        };

        for (int i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var ej = new object?[]
        {
            1020304050L, "PEREZ JUAN", "jperez",
            "Compromiso de pago", "Compromiso de pago",
            5000000, "3001234567", 3,
            "2026-04-21", "2026-04-30", 500000,
            null, "2026-04-21 08:00:00",
        };
        for (int i = 0; i < ej.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = ej[i]?.ToString() ?? "";
            c.Style.Font.FontColor = XLColor.Gray;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<PreviewReporteDto> ValidarArchivoAsync(
        Stream archivoStream, int carteraID, string extension)
    {
        // 1. Extraer filas como string[][]
        var todasLasFilas = extension == ".xlsx"
            ? ExtraerXlsx(archivoStream)
            : await ExtraerCsvAsync(archivoStream);

        if (todasLasFilas.Count < 2)
            return new PreviewReporteDto();

        // 2. Resolver índices de columnas
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        var encabezado = todasLasFilas[0];
        for (int i = 0; i < encabezado.Length; i++)
        {
            var clave = Normalizar(encabezado[i]);
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = i;
        }

        var (idx, errorCol) = ResolverIndices(headers);
        if (idx is null)
        {
            return new PreviewReporteDto
            {
                FilasConError  = [new FilaValidacionReporteDto
                    { TitularID = "—", UsuarioControl = "—",
                      Estado = EstadoFilaReporte.AgenteNoEncontrado, Razon = errorCol }],
                TotalInvalidos = 1,
            };
        }

        // 3. Parsear filas e inyectar carteraID
        var filasRaw = new List<FilaReporteRaw>(todasLasFilas.Count - 1);
        for (int i = 1; i < todasLasFilas.Count; i++)
        {
            var partes = todasLasFilas[i];
            if (partes.All(string.IsNullOrWhiteSpace)) continue;
            var fila = ParsearPartes(partes, i + 1, idx);
            fila.CarteraID = carteraID;
            filasRaw.Add(fila);
        }

        if (filasRaw.Count == 0) return new PreviewReporteDto();

        // 4. Batch lookup: TitularIDs existentes
        var titularIDs = filasRaw
            .Where(f => f.TitularID.HasValue)
            .Select(f => f.TitularID!.Value)
            .Distinct()
            .ToList();

        var titularesExistentes = await _db.Titulares
            .Where(t => titularIDs.Contains(t.NumeroDocumento))
            .Select(t => t.NumeroDocumento)
            .ToHashSetAsync();

        // 5. Batch lookup: AgenteID desde Agente.UsuarioControlNext
        var usuarios = filasRaw
            .Where(f => !string.IsNullOrWhiteSpace(f.UsuarioControl))
            .Select(f => f.UsuarioControl!.Trim())
            .Distinct()
            .ToList();

        var agentePorUsuario = await _db.Agentes
            .Where(a => a.UsuarioControlNext != null && usuarios.Contains(a.UsuarioControlNext))
            .AsNoTracking()
            .ToDictionaryAsync(
                a => a.UsuarioControlNext!,
                a => a.AgenteID,
                StringComparer.OrdinalIgnoreCase);

        // 6. Resolver AgenteID en cada fila
        foreach (var f in filasRaw)
        {
            if (EsSinGestion(f.UltimoPerfil))
            {
                f.AgenteID = 0; // centinela para Sin Gestión
                continue;
            }
            if (!string.IsNullOrWhiteSpace(f.UsuarioControl) &&
                agentePorUsuario.TryGetValue(f.UsuarioControl.Trim(), out var aid))
                f.AgenteID = aid;
        }

        // 7. Clasificar
        var todasValidacion = new List<FilaValidacionReporteDto>(filasRaw.Count);
        var filasValidas     = new List<FilaReporteRaw>();
        var totalNuevos      = 0;

        foreach (var fila in filasRaw)
        {
            var estado = ClasificarFila(fila, titularesExistentes, out var razon);
            todasValidacion.Add(new FilaValidacionReporteDto
            {
                TitularID      = fila.TitularID?.ToString() ?? "—",
                Nombre         = fila.Nombre,
                UsuarioControl = fila.UsuarioControl ?? "—",
                Estado         = estado,
                Razon          = razon,
                CamposMapeados = ResolverCamposMapeados(fila),
            });

            if (estado is EstadoFilaReporte.Ok or EstadoFilaReporte.NuevoTitular)
            {
                filasValidas.Add(fila);
                if (estado == EstadoFilaReporte.NuevoTitular) totalNuevos++;
            }
        }

        var invalidas = todasValidacion
            .Where(f => f.Estado != EstadoFilaReporte.Ok && f.Estado != EstadoFilaReporte.NuevoTitular)
            .ToList();

        var totalUnicos     = filasValidas.GroupBy(f => (f.TitularID, f.CarteraID)).Count();
        var totalDuplicados = filasValidas.Count - totalUnicos;

        return new PreviewReporteDto
        {
            Filas               = todasValidacion.Where(f => f.Estado == EstadoFilaReporte.Ok || f.Estado == EstadoFilaReporte.NuevoTitular).Take(10).ToList(),
            FilasConError       = invalidas,
            TotalValidos        = filasValidas.Count,
            TotalNuevos         = totalNuevos,
            TotalInvalidos      = invalidas.Count,
            FilasValidasRaw     = filasValidas,
            ColumnasEncontradas = DeriviarColumnasEncontradas(idx),
            TotalUnicos         = totalUnicos,
            TotalDuplicados     = totalDuplicados,
        };
    }

    // ── Confirmación ─────────────────────────────────────────────────────────

    public async Task<ResultadoReporteDto> ConfirmarCargueAsync(List<FilaReporteRaw> filasValidas)
    {
        if (filasValidas.Count == 0)
            return new ResultadoReporteDto { Procesados = 0, Mensaje = "No hay filas válidas para procesar." };

        var ahora = DateTime.Now;

        if (_db.Database.GetDbConnection().State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync();

        await using var tx     = await _db.Database.BeginTransactionAsync();
        var sqlConn            = (SqlConnection)_db.Database.GetDbConnection();
        var sqlTx              = (SqlTransaction)tx.GetDbTransaction();
        var duplicadosEliminados = 0;
        var titularesInsertados  = 0;

        try
        {
            // 1. Crear Titulares nuevos (los que tienen Nombre y no existen en BD)
            var conNombre = filasValidas
                .Where(f => f.TitularID.HasValue && !string.IsNullOrEmpty(f.Nombre))
                .GroupBy(f => f.TitularID!.Value)
                .Select(g => g.First())
                .ToList();

            if (conNombre.Count > 0)
            {
                var docsCandidatos = conNombre.Select(f => f.TitularID!.Value).ToList();
                var yaExisten      = await _db.Titulares
                    .Where(t => docsCandidatos.Contains(t.NumeroDocumento))
                    .Select(t => t.NumeroDocumento)
                    .ToHashSetAsync();

                var porInsertar = conNombre
                    .Where(f => !yaExisten.Contains(f.TitularID!.Value))
                    .Select(f => new Titular
                    {
                        NumeroDocumento = f.TitularID!.Value,
                        Nombre          = f.Nombre!,
                    })
                    .ToList();

                if (porInsertar.Count > 0)
                {
                    _db.Titulares.AddRange(porInsertar);
                    await _db.SaveChangesAsync();
                }
                titularesInsertados = porInsertar.Count;
            }

            // 2. Crear tabla temporal con el mismo esquema que los destinos
            await using var cmdCreate = new SqlCommand(SqlCrearTablaTemp, sqlConn, sqlTx);
            await cmdCreate.ExecuteNonQueryAsync();

            // 3. BulkCopy → #TempReporte
            var tabla = ConstruirDataTable(filasValidas, ahora);
            using var bulk = new SqlBulkCopy(sqlConn, SqlBulkCopyOptions.Default, sqlTx)
            {
                DestinationTableName = "#TempReporte",
                BatchSize            = 10_000,
                BulkCopyTimeout      = 600,
            };
            foreach (DataColumn col in tabla.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulk.WriteToServerAsync(tabla);

            // 4. Deduplicar: un cliente puede tener varias obligaciones en el archivo;
            //    conservar solo el registro con FechaUltimaGestion más reciente por (TitularID, CarteraID).
            await using var cmdDedup = new SqlCommand(SqlDeduplicar, sqlConn, sqlTx)
                { CommandTimeout = 300 };
            duplicadosEliminados = await cmdDedup.ExecuteNonQueryAsync();

            // 5. UPSERT → UltimoEstadoTitular
            await using var cmdMerge = new SqlCommand(SqlMergeUltimo, sqlConn, sqlTx)
                { CommandTimeout = 600 };
            await cmdMerge.ExecuteNonQueryAsync();

            // 6. MERGE → HistoricoEstadoTitular:
            //    Rotacion nueva → INSERT | Rotacion ya existe → UPDATE FechaActualizacion
            await using var cmdHist = new SqlCommand(SqlMergeHistorico, sqlConn, sqlTx)
                { CommandTimeout = 600 };
            await cmdHist.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falló confirmación de cargue de reportes ({TotalFilas} filas, {TitularesNuevos} titular(es) nuevo(s) hasta el fallo)",
                filasValidas.Count, titularesInsertados);
            await tx.RollbackAsync();
            throw;
        }

        // Commit exitoso — el cargue muta UltimoEstadoTitular/HistoricoEstadoTitular,
        // que alimentan las métricas del Calificador. Invalidamos toda la caché.
        _calificadorCache.InvalidarTodo();

        var unicos = filasValidas.Count - duplicadosEliminados;
        return new ResultadoReporteDto
        {
            Procesados      = unicos,
            TitularesNuevos = titularesInsertados,
            Duplicados      = duplicadosEliminados,
            Mensaje         = titularesInsertados > 0
                ? $"Se procesaron {unicos:N0} registros únicos. {titularesInsertados} titular(es) nuevo(s) creados en Titular."
                : $"Se procesaron {unicos:N0} registros únicos: UltimoEstadoTitular actualizado e HistoricoEstadoTitular enriquecido.",
        };
    }

    // ── SQL ───────────────────────────────────────────────────────────────────

    private const string SqlCrearTablaTemp = """
        CREATE TABLE #TempReporte (
            TitularID          BIGINT          NOT NULL,
            CarteraID          INT             NOT NULL,
            AgenteID           BIGINT          NOT NULL,
            UltimoPerfil       NVARCHAR(50)        NULL,
            MejorPerfil        NVARCHAR(50)        NULL,
            SaldoTotal         MONEY               NULL,
            NumeroMarcado      NVARCHAR(10)        NULL,
            Rotacion           INT                 NULL,
            FechaUltimaGestion DATE                NULL,
            FechaPromesa       DATE                NULL,
            ValorPromesa       MONEY               NULL,
            MotivoNoPago       NVARCHAR(100)       NULL,
            FechaActualizacion DATETIME        NOT NULL
        )
        """;

    // Elimina duplicados de la misma clave (TitularID, CarteraID) conservando
    // el registro con FechaUltimaGestion más reciente. NULLs van al final.
    private const string SqlDeduplicar = """
        WITH Dedup AS (
            SELECT *, ROW_NUMBER() OVER (
                PARTITION BY TitularID, CarteraID
                ORDER BY ISNULL(FechaUltimaGestion, '1900-01-01') DESC,
                         FechaActualizacion DESC
            ) AS rn
            FROM #TempReporte
        )
        DELETE FROM Dedup WHERE rn > 1;
        """;

    // PK real de UltimoEstadoTitular es (TitularID, CarteraID); AgenteID se actualiza como campo.
    private const string SqlMergeUltimo = """
        MERGE UltimoEstadoTitular WITH (HOLDLOCK) AS tgt
        USING #TempReporte AS src
        ON  tgt.TitularID = src.TitularID
        AND tgt.CarteraID = src.CarteraID
        WHEN MATCHED THEN UPDATE SET
            tgt.AgenteID           = src.AgenteID,
            tgt.UltimoPerfil       = src.UltimoPerfil,
            tgt.MejorPerfil        = src.MejorPerfil,
            tgt.SaldoTotal         = src.SaldoTotal,
            tgt.NumeroMarcado      = src.NumeroMarcado,
            tgt.Rotacion           = src.Rotacion,
            tgt.FechaUltimaGestion = src.FechaUltimaGestion,
            tgt.FechaPromesa       = src.FechaPromesa,
            tgt.ValorPromesa       = src.ValorPromesa,
            tgt.MotivoNoPago       = src.MotivoNoPago,
            tgt.FechaActualizacion = src.FechaActualizacion
        WHEN NOT MATCHED THEN INSERT (
            TitularID, CarteraID, AgenteID,
            UltimoPerfil, MejorPerfil,
            SaldoTotal, NumeroMarcado, Rotacion,
            FechaUltimaGestion, FechaPromesa, ValorPromesa,
            MotivoNoPago, FechaActualizacion)
        VALUES (
            src.TitularID, src.CarteraID, src.AgenteID,
            src.UltimoPerfil, src.MejorPerfil,
            src.SaldoTotal, src.NumeroMarcado, src.Rotacion,
            src.FechaUltimaGestion, src.FechaPromesa, src.ValorPromesa,
            src.MotivoNoPago, src.FechaActualizacion);
        """;

    // Clave del historico: (TitularID, CarteraID, AgenteID, Rotacion).
    // - Rotacion nueva (no existe ese número)  → INSERT: nueva gestión real.
    // - Rotacion ya existe (misma o menor)     → UPDATE FechaActualizacion solo:
    //   el estado ya fue registrado; solo actualizamos cuándo se re-procesó.
    private const string SqlMergeHistorico = """
        MERGE HistoricoEstadoTitular WITH (HOLDLOCK) AS tgt
        USING #TempReporte AS src
        ON  tgt.TitularID = src.TitularID
        AND tgt.CarteraID = src.CarteraID
        AND tgt.AgenteID  = src.AgenteID
        AND ISNULL(tgt.Rotacion, -1) = ISNULL(src.Rotacion, -1)
        WHEN MATCHED THEN
            UPDATE SET tgt.FechaActualizacion = src.FechaActualizacion
        WHEN NOT MATCHED THEN
            INSERT (TitularID, CarteraID, AgenteID,
                    UltimoPerfil, MejorPerfil,
                    SaldoTotal, NumeroMarcado, Rotacion,
                    FechaUltimaGestion, FechaPromesa, ValorPromesa,
                    MotivoNoPago, FechaActualizacion)
            VALUES (src.TitularID, src.CarteraID, src.AgenteID,
                    src.UltimoPerfil, src.MejorPerfil,
                    src.SaldoTotal, src.NumeroMarcado, src.Rotacion,
                    src.FechaUltimaGestion, src.FechaPromesa, src.ValorPromesa,
                    src.MotivoNoPago, src.FechaActualizacion);
        """;

    // ── DataTable ─────────────────────────────────────────────────────────────

    private static DataTable ConstruirDataTable(List<FilaReporteRaw> filas, DateTime ahora)
    {
        var dt = new DataTable();
        dt.Columns.Add("TitularID",          typeof(long));
        dt.Columns.Add("CarteraID",          typeof(int));
        dt.Columns.Add("AgenteID",           typeof(long));
        dt.Columns.Add("UltimoPerfil",       typeof(string));
        dt.Columns.Add("MejorPerfil",        typeof(string));
        dt.Columns.Add("SaldoTotal",         typeof(decimal));
        dt.Columns.Add("NumeroMarcado",      typeof(string));
        dt.Columns.Add("Rotacion",           typeof(int));
        dt.Columns.Add("FechaUltimaGestion", typeof(DateTime));
        dt.Columns.Add("FechaPromesa",       typeof(DateTime));
        dt.Columns.Add("ValorPromesa",       typeof(decimal));
        dt.Columns.Add("MotivoNoPago",       typeof(string));
        dt.Columns.Add("FechaActualizacion", typeof(DateTime));

        foreach (var f in filas)
        {
            dt.Rows.Add(
                f.TitularID!.Value,
                f.CarteraID!.Value,
                f.AgenteID ?? 0L,
                N(f.UltimoPerfil),
                N(f.MejorPerfil),
                N(f.SaldoTotal),
                N(f.NumeroMarcado),
                N(f.Rotacion),
                D(f.FechaUltimaGestion),
                D(f.FechaPromesa),
                N(f.ValorPromesa),
                N(f.MotivoNoPago),
                ahora
            );
        }
        return dt;

        static object N(object? v)   => v ?? DBNull.Value;
        static object D(DateOnly? d) => d.HasValue
            ? new DateTime(d.Value.Year, d.Value.Month, d.Value.Day)
            : DBNull.Value;
    }

    // ── Clasificación ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> PerfilesSinGestion =
        new(StringComparer.OrdinalIgnoreCase) { "Sin Gestion", "Sin Gestión" };

    private static bool EsSinGestion(string? perfil) =>
        !string.IsNullOrWhiteSpace(perfil) && PerfilesSinGestion.Contains(perfil.Trim());

    /// <summary>
    /// Devuelve las claves de los campos opcionales cuya columna existía en el encabezado del archivo.
    /// Se calcula una sola vez por cargue; el frontend lo muestra en los headers de la tabla.
    /// </summary>
    private static string[] DeriviarColumnasEncontradas(IndicesColumnas idx)
    {
        var cols = new List<string>(12);
        if (idx.Nombre.HasValue)             cols.Add("nombre");
        if (idx.UltimoPerfil.HasValue)       cols.Add("ultimoPerfil");
        if (idx.MejorPerfil.HasValue)        cols.Add("mejorPerfil");
        if (idx.SaldoTotal.HasValue)         cols.Add("saldoTotal");
        if (idx.NumeroMarcado.HasValue)      cols.Add("numeroMarcado");
        if (idx.Rotacion.HasValue)           cols.Add("rotacion");
        if (idx.FechaUltimaGestion.HasValue) cols.Add("fechaUltimaGestion");
        if (idx.FechaPromesa.HasValue)       cols.Add("fechaPromesa");
        if (idx.ValorPromesa.HasValue)       cols.Add("valorPromesa");
        if (idx.MotivoNoPago.HasValue)       cols.Add("motivoNoPago");
        if (idx.FechaActualizacion.HasValue) cols.Add("fechaActualizacion");
        return [.. cols];
    }

    private static string[] ResolverCamposMapeados(FilaReporteRaw f)
    {
        var campos = new List<string>(12);
        if (!string.IsNullOrEmpty(f.Nombre))        campos.Add("nombre");
        if (!string.IsNullOrEmpty(f.UltimoPerfil))  campos.Add("ultimoPerfil");
        if (!string.IsNullOrEmpty(f.MejorPerfil))   campos.Add("mejorPerfil");
        if (f.SaldoTotal.HasValue)                   campos.Add("saldoTotal");
        if (!string.IsNullOrEmpty(f.NumeroMarcado)) campos.Add("numeroMarcado");
        if (f.Rotacion.HasValue)                     campos.Add("rotacion");
        if (f.FechaUltimaGestion.HasValue)           campos.Add("fechaUltimaGestion");
        if (f.FechaPromesa.HasValue)                 campos.Add("fechaPromesa");
        if (f.ValorPromesa.HasValue)                 campos.Add("valorPromesa");
        if (!string.IsNullOrEmpty(f.MotivoNoPago))  campos.Add("motivoNoPago");
        if (f.FechaActualizacion.HasValue)           campos.Add("fechaActualizacion");
        return [.. campos];
    }

    private static EstadoFilaReporte ClasificarFila(
        FilaReporteRaw fila, HashSet<long> existentes, out string? razon)
    {
        if (!EsSinGestion(fila.UltimoPerfil) && !fila.AgenteID.HasValue)
        {
            razon = $"Usuario Control '{fila.UsuarioControl}' no encontrado en el sistema";
            return EstadoFilaReporte.AgenteNoEncontrado;
        }

        if (!fila.TitularID.HasValue || !existentes.Contains(fila.TitularID.Value))
        {
            if (!string.IsNullOrEmpty(fila.Nombre))
            {
                razon = "Titular nuevo — se creará en la base de datos";
                return EstadoFilaReporte.NuevoTitular;
            }
            razon = "TitularID no existe y no se proporcionó Nombre para crearlo";
            return EstadoFilaReporte.SinNombreParaCrear;
        }

        razon = null;
        return EstadoFilaReporte.Ok;
    }

    // ── Parser self-contained ─────────────────────────────────────────────────

    private static string Normalizar(string s)
    {
        var sinTildes = s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "").Replace("_", "").Replace("-", "")
            .Replace("*", "").Replace("(", "").Replace(")", "")
            .Trim();
    }

    private record IndicesColumnas(
        int  TitularID,
        int  UsuarioControl,
        int? Nombre,
        int? UltimoPerfil,
        int? MejorPerfil,
        int? SaldoTotal,
        int? NumeroMarcado,
        int? Rotacion,
        int? FechaUltimaGestion,
        int? FechaPromesa,
        int? ValorPromesa,
        int? MotivoNoPago,
        int? FechaActualizacion
    );

    private static readonly Dictionary<string, string> Alias = new(StringComparer.Ordinal)
    {
        // TitularID
        ["titularid"]          = "titularid",
        ["titular"]            = "titularid",
        ["numerodocumento"]    = "titularid",
        ["documento"]          = "titularid",
        ["cedula"]             = "titularid",
        ["identificacion"]     = "titularid",
        // UsuarioControl — la columna llega como "AgenteID" en los archivos reales
        ["agenteid"]           = "usuariocontrol",
        ["agente"]             = "usuariocontrol",
        ["ultusugestion"]      = "usuariocontrol",
        ["usuariocontrol"]     = "usuariocontrol",
        ["usuariocontrolnext"] = "usuariocontrol",
        ["control"]            = "usuariocontrol",
        // Nombre
        ["nombre"]             = "nombre",
        ["nombrecliente"]      = "nombre",
        ["nombrecompleto"]     = "nombre",
        ["cliente"]            = "nombre",
        // UltimoPerfil
        ["ultimoperfil"]       = "ultimoperfil",
        ["perfil"]             = "ultimoperfil",
        // MejorPerfil
        ["mejorperfil"]        = "mejorperfil",
        // SaldoTotal
        ["saldototal"]         = "saldototal",
        ["saldo"]              = "saldototal",
        ["totalapagar"]        = "saldototal",
        ["inicobro365"]        = "saldototal",
        ["saldocapital"]       = "saldototal",
        ["valortotalcredito"]  = "saldototal",
        ["vlrsaldototal"]      = "saldototal",
        ["pagototal"]          = "saldototal",
        // NumeroMarcado
        ["numeromarcado"]      = "numeromarcado",
        ["marcado"]            = "numeromarcado",
        // Rotacion
        ["rotacion"]           = "rotacion",
        // FechaUltimaGestion
        ["fechaultimagestion"] = "fechaultimagestion",
        ["ultimagestion"]      = "fechaultimagestion",
        ["fechagestion"]       = "fechaultimagestion",
        // FechaPromesa
        ["fechapromesa"]       = "fechapromesa",
        ["promesa"]            = "fechapromesa",
        // ValorPromesa
        ["valorpromesa"]       = "valorpromesa",
        // MotivoNoPago
        ["motivonopago"]       = "motivonopago",
        ["motivo"]             = "motivonopago",
        ["motvnopago"]         = "motivonopago",
        // FechaActualizacion
        ["fechaactualizacion"] = "fechaactualizacion",
        ["actualizacion"]      = "fechaactualizacion",
        ["fechaact"]           = "fechaactualizacion",
    };

    private static (IndicesColumnas? idx, string error) ResolverIndices(
        Dictionary<string, int> headers)
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (clave, indice) in headers)
            if (Alias.TryGetValue(clave, out var campo) && !m.ContainsKey(campo))
                m[campo] = indice;

        var requeridos = new[] { "titularid", "usuariocontrol" };
        var faltantes  = requeridos.Where(r => !m.ContainsKey(r)).ToList();
        if (faltantes.Count > 0)
            return (null, $"Columnas no encontradas: {string.Join(", ", faltantes)}. " +
                          $"Revisa que el archivo tenga las columnas TitularID (o Cedula) y AgenteID (o UsuarioControl).");

        static int? Opt(Dictionary<string, int> d, string k) => d.TryGetValue(k, out var v) ? v : null;

        return (new IndicesColumnas(
            TitularID:          m["titularid"],
            UsuarioControl:     m["usuariocontrol"],
            Nombre:             Opt(m, "nombre"),
            UltimoPerfil:       Opt(m, "ultimoperfil"),
            MejorPerfil:        Opt(m, "mejorperfil"),
            SaldoTotal:         Opt(m, "saldototal"),
            NumeroMarcado:      Opt(m, "numeromarcado"),
            Rotacion:           Opt(m, "rotacion"),
            FechaUltimaGestion: Opt(m, "fechaultimagestion"),
            FechaPromesa:       Opt(m, "fechapromesa"),
            ValorPromesa:       Opt(m, "valorpromesa"),
            MotivoNoPago:       Opt(m, "motivonopago"),
            FechaActualizacion: Opt(m, "fechaactualizacion")
        ), string.Empty);
    }

    /// <summary>
    /// Número de celular colombiano: exactamente 10 dígitos y debe iniciar con 3.
    /// Cualquier valor que no cumpla → null (no falla la fila, solo limpia el campo).
    /// </summary>
    private static string? ValidarNumeroMarcado(string? s)
    {
        if (s is null) return null;
        var soloDigitos = new string(s.Where(char.IsDigit).ToArray());
        if (soloDigitos.Length != 10 || soloDigitos[0] != '3') return null;
        return soloDigitos;
    }

    private static FilaReporteRaw ParsearPartes(string[] p, int linea, IndicesColumnas idx)
    {
        static string? Get(string[] arr, int? i) =>
            i.HasValue && i.Value < arr.Length ? arr[i.Value].Trim().NullIfEmpty() : null;

        static decimal? Dec(string? s)
        {
            if (s is null) return null;
            return decimal.TryParse(s.Replace(".", "").Replace(",", "."),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        static DateOnly? Fecha(string? s)
        {
            if (s is null) return null;
            if (DateOnly.TryParse(s, out var d)) return d;
            // Formatos colombianos comunes que TryParse no resuelve solo
            ReadOnlySpan<string> formatos = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"];
            foreach (var fmt in formatos)
                if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d2)) return d2;
            return null;
        }

        static DateTime? FechaHora(string? s)
        {
            if (s is null) return null;
            return DateTime.TryParse(s, out var d) ? d : null;
        }

        if (!long.TryParse(Get(p, idx.TitularID), out var titularID))
            return new FilaReporteRaw
            {
                UsuarioControl = Get(p, idx.UsuarioControl),
                UltimoPerfil   = Get(p, idx.UltimoPerfil),
            };

        return new FilaReporteRaw
        {
            TitularID          = titularID,
            Nombre             = Get(p, idx.Nombre),
            UsuarioControl     = Get(p, idx.UsuarioControl),
            UltimoPerfil       = Get(p, idx.UltimoPerfil),
            MejorPerfil        = Get(p, idx.MejorPerfil),
            SaldoTotal         = Dec(Get(p, idx.SaldoTotal)),
            NumeroMarcado      = ValidarNumeroMarcado(Get(p, idx.NumeroMarcado)),
            Rotacion           = int.TryParse(Get(p, idx.Rotacion), out var rot) ? rot : null,
            FechaUltimaGestion = Fecha(Get(p, idx.FechaUltimaGestion)),
            FechaPromesa       = Fecha(Get(p, idx.FechaPromesa)),
            ValorPromesa       = Dec(Get(p, idx.ValorPromesa)),
            MotivoNoPago       = Get(p, idx.MotivoNoPago),
            FechaActualizacion = FechaHora(Get(p, idx.FechaActualizacion)),
        };
    }

    // ── Extractores ───────────────────────────────────────────────────────────

    private static List<string[]> ExtraerXlsx(Stream stream)
    {
        var filas = new List<string[]>();
        using var wb  = new XLWorkbook(stream);
        var rango = wb.Worksheets.First().RangeUsed();
        if (rango is null) return filas;
        foreach (var fila in rango.Rows())
            filas.Add(fila.Cells().Select(CeldaAString).ToArray());
        return filas;
    }

    /// <summary>
    /// Convierte una celda a string de forma segura.
    /// Las celdas de fecha en Excel se almacenan como DateTime internamente;
    /// GetString() devuelve vacío o el serial numérico para ellas.
    /// Las normalizamos a ISO (yyyy-MM-dd) para que el parser de fechas las lea correctamente.
    /// </summary>
    private static string CeldaAString(IXLCell celda)
    {
        if (celda.DataType == XLDataType.DateTime)
            return celda.GetDateTime().ToString("yyyy-MM-dd");
        return celda.GetString();
    }

    private static async Task<List<string[]>> ExtraerCsvAsync(Stream stream)
    {
        var filas = new List<string[]>();
        using var reader = new StreamReader(stream);
        var primera = await reader.ReadLineAsync();
        if (primera is null) return filas;
        var sep = primera.Contains(';') ? ';' : ',';
        filas.Add(primera.Split(sep));
        while (!reader.EndOfStream)
        {
            var linea = await reader.ReadLineAsync();
            if (linea is null) continue;
            filas.Add(linea.Split(sep));
        }
        return filas;
    }
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
