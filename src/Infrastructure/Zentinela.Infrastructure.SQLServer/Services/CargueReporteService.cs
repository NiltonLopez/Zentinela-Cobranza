using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Reportes;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestiona el cargue masivo de reportes hacia UltimoEstadoTitular e HistoricoEstadoTitular.
/// Flujo: parse Excel → validación batch → staging ReporteEstadoAuxiliar
///        → sp_ProcesarUltimoEstado (UPSERT + titulares nuevos) + sp_ProcesarHistoricoEstado (INSERT append).
/// Compatible con SSIS: SSIS puede cargar directamente en ReporteEstadoAuxiliar
/// y luego llamar a los mismos SPs.
/// </summary>
public class CargueReporteService : ICargueReporteService
{
    private readonly CallCenterDbContext _db;
    private readonly IExcelExporter     _excelExporter;

    // Columnas de INSERT en staging — mismo orden que la tabla
    private const string ColsStaging =
        "TitularID, Nombre, CarteraID, AgenteID, UltimoPerfil, MejorPerfil, FechaMejorPerfil, " +
        "SaldoTotal, NumeroMarcado, Rotacion, FechaUltimaGestion, FechaPromesa, " +
        "ValorPromesa, MotivoNoPago, FechaActualizacion";

    // Máximo de filas por statement (15 columnas → 66 filas ≈ 990 params, dentro del límite de 1000)
    private const int ChunkSize = 66;

    public CargueReporteService(CallCenterDbContext db, IExcelExporter excelExporter)
    {
        _db            = db;
        _excelExporter = excelExporter;
    }

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla() => _excelExporter.GenerarPlantillaReporte();

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<PreviewReporteDto> ValidarExcelAsync(Stream excelStream, int carteraID)
    {
        var filasRaw = _excelExporter.ParsearFilasReporte(excelStream);

        if (filasRaw.Count == 0)
            return new PreviewReporteDto();

        // Inyectar carteraID en cada fila (viene de la UI, no del archivo)
        foreach (var f in filasRaw)
            f.CarteraID = carteraID;

        // Batch lookup: ¿cuáles TitularIDs ya existen en BD?
        var titularIDs = filasRaw
            .Where(f => f.TitularID.HasValue)
            .Select(f => f.TitularID!.Value)
            .Distinct()
            .ToList();

        var titularesExistentes = await _db.Titulares
            .Where(t => titularIDs.Contains(t.NumeroDocumento))
            .Select(t => t.NumeroDocumento)
            .ToHashSetAsync();

        // Batch lookup: AgenteIDs válidos
        var agenteIDs = filasRaw
            .Where(f => f.AgenteID.HasValue)
            .Select(f => (long)f.AgenteID!.Value)
            .Distinct()
            .ToList();

        var agentesOk = await _db.Agentes
            .Where(a => agenteIDs.Contains(a.AgenteID))
            .Select(a => (int)a.AgenteID)
            .ToHashSetAsync();

        // Clasificar filas
        var todasLasFilas = new List<FilaValidacionReporteDto>(filasRaw.Count);
        var filasValidas  = new List<FilaReporteRaw>();
        var totalNuevos   = 0;

        foreach (var fila in filasRaw)
        {
            var estado = ClasificarFila(fila, titularesExistentes, agentesOk, out var razon);

            todasLasFilas.Add(new FilaValidacionReporteDto
            {
                TitularID = fila.TitularID?.ToString() ?? "—",
                Nombre    = fila.Nombre,
                AgenteID  = fila.AgenteID?.ToString()  ?? "—",
                Estado    = estado,
                Razon     = razon,
            });

            if (estado is EstadoFilaReporte.Ok or EstadoFilaReporte.NuevoTitular)
            {
                filasValidas.Add(fila);
                if (estado == EstadoFilaReporte.NuevoTitular)
                    totalNuevos++;
            }
        }

        return new PreviewReporteDto
        {
            Filas           = todasLasFilas.Take(10).ToList(),
            TotalValidos    = filasValidas.Count,
            TotalNuevos     = totalNuevos,
            TotalInvalidos  = todasLasFilas.Count - filasValidas.Count,
            FilasValidasRaw = filasValidas,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<ResultadoReporteDto> ConfirmarCargueAsync(List<FilaReporteRaw> filasValidas)
    {
        if (filasValidas.Count == 0)
            return new ResultadoReporteDto { Procesados = 0, Mensaje = "No hay filas válidas para procesar." };

        var nuevos = filasValidas.Count(f => !string.IsNullOrEmpty(f.Nombre));

        // 1. Limpiar staging
        await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE ReporteEstadoAuxiliar");

        // 2. Insertar en chunks con valores sanitizados
        foreach (var chunk in filasValidas.Chunk(ChunkSize))
        {
            var valores = string.Join(", ", chunk.Select(FilaAValores));
#pragma warning disable EF1002 // valores numéricos validados + strings correctamente escapados
            await _db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO ReporteEstadoAuxiliar ({ColsStaging}) VALUES {valores}");
#pragma warning restore EF1002
        }

        // 3. UPSERT → UltimoEstadoTitular (también inserta titulares nuevos en Titular)
        await _db.Database.ExecuteSqlRawAsync("EXEC sp_ProcesarUltimoEstado");

        // 4. INSERT append → HistoricoEstadoTitular
        await _db.Database.ExecuteSqlRawAsync("EXEC sp_ProcesarHistoricoEstado");

        return new ResultadoReporteDto
        {
            Procesados      = filasValidas.Count,
            TitularesNuevos = nuevos,
            Mensaje         = nuevos > 0
                ? $"Se procesaron {filasValidas.Count} registro(s). {nuevos} titular(es) nuevo(s) creado(s) en Titular."
                : $"Se procesaron {filasValidas.Count} registro(s): UltimoEstadoTitular actualizado e HistoricoEstadoTitular enriquecido.",
        };
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private static EstadoFilaReporte ClasificarFila(
        FilaReporteRaw    fila,
        HashSet<long>     titularesExistentes,
        HashSet<int>      agentesOk,
        out string?       razon)
    {
        // AgenteID siempre es obligatorio
        if (!fila.AgenteID.HasValue || !agentesOk.Contains(fila.AgenteID.Value))
        { razon = "AgenteID no encontrado en la base de datos"; return EstadoFilaReporte.AgenteIDInvalido; }

        // TitularID no existe → ¿tiene Nombre para crearlo?
        if (!fila.TitularID.HasValue || !titularesExistentes.Contains(fila.TitularID.Value))
        {
            if (!string.IsNullOrEmpty(fila.Nombre))
            { razon = "Titular nuevo — se creará en la base de datos"; return EstadoFilaReporte.NuevoTitular; }

            razon = "TitularID no existe y no se proporcionó Nombre para crearlo";
            return EstadoFilaReporte.SinNombreParaCrear;
        }

        razon = null;
        return EstadoFilaReporte.Ok;
    }

    /// <summary>
    /// Convierte una fila en la expresión VALUES (...) para el INSERT en staging.
    /// Numéricos se emiten directamente; strings se escapan con comillas simples.
    /// </summary>
    private static string FilaAValores(FilaReporteRaw f)
    {
        static string Str(string? s)   => s == null ? "NULL" : $"'{s.Replace("'", "''")}'";
        static string Num(long? n)     => n.HasValue    ? n.Value.ToString()   : "NULL";
        static string Int(int? n)      => n.HasValue    ? n.Value.ToString()   : "NULL";
        static string Dec(decimal? d)  => d.HasValue    ? d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";
        static string Dat(DateOnly? d) => d.HasValue    ? $"'{d.Value:yyyy-MM-dd}'" : "NULL";
        static string Dtm(DateTime? d) => d.HasValue    ? $"'{d.Value:yyyy-MM-dd HH:mm:ss}'" : "NULL";

        return $"({Num(f.TitularID)}, {Str(f.Nombre)}, {Int(f.CarteraID)}, {Int(f.AgenteID)}, " +
               $"{Str(f.UltimoPerfil)}, {Str(f.MejorPerfil)}, {Dat(f.FechaMejorPerfil)}, " +
               $"{Dec(f.SaldoTotal)}, {Str(f.NumeroMarcado)}, {Int(f.Rotacion)}, " +
               $"{Dat(f.FechaUltimaGestion)}, {Dat(f.FechaPromesa)}, {Dec(f.ValorPromesa)}, " +
               $"{Str(f.MotivoNoPago)}, {Dtm(f.FechaActualizacion)})";
    }
}
