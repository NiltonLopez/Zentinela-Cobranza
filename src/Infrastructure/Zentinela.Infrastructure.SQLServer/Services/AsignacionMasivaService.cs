using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Asignaciones;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestiona el cargue masivo de asignaciones Titular→Cartera+Líder:
/// parse de Excel, validación contra BD, staging en Titular_CarteraAuxiliar
/// y ejecución de sp_ProcesarAsignacionCartera.
/// </summary>
public class AsignacionMasivaService : IAsignacionMasivaService
{
    private readonly CallCenterDbContext _db;
    private readonly IExcelExporter     _excelExporter;

    // Máximo de filas en un solo INSERT para evitar límite de parámetros de SQL Server
    private const int ChunkSize = 500;

    public AsignacionMasivaService(CallCenterDbContext db, IExcelExporter excelExporter)
    {
        _db            = db;
        _excelExporter = excelExporter;
    }

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla() => _excelExporter.GenerarPlantillaAsignaciones();

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<PreviewAsignacionDto> ValidarExcelAsync(
        Stream excelStream, int carteraID, int liderID)
    {
        // Parsear Excel — fila 1 = encabezados, fila 2+ = datos
        var numerosDocumento = _excelExporter.ParsearNumerosDocumento(excelStream);

        if (numerosDocumento.Count == 0)
            return new PreviewAsignacionDto();

        // Batch: titulares que existen en BD
        var existentesMap = await _db.Titulares
            .Where(t => numerosDocumento.Contains(t.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.NumeroDocumento, t => t.Nombre);

        // Batch: titulares ya asignados a esta cartera+líder
        var candidatos   = existentesMap.Keys.ToList();
        var yaAsignados  = await _db.TitularCarteras
            .Where(tc => tc.CarteraID == carteraID
                      && tc.LiderID   == liderID
                      && candidatos.Contains(tc.TitularID))
            .Select(tc => tc.TitularID)
            .ToHashSetAsync();

        // Construir resultado fila a fila
        var todasLasFilas = new List<FilaValidacionDto>(numerosDocumento.Count);
        var validos       = new List<long>();

        foreach (var numDoc in numerosDocumento)
        {
            if (!existentesMap.TryGetValue(numDoc, out var nombre))
            {
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Estado          = EstadoValidacion.NoExiste,
                    Razon           = "No encontrado en la base de datos",
                });
                continue;
            }

            if (yaAsignados.Contains(numDoc))
            {
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Nombre          = nombre,
                    Estado          = EstadoValidacion.YaAsignado,
                    Razon           = "Ya asignado a esta cartera",
                });
                continue;
            }

            todasLasFilas.Add(new FilaValidacionDto
            {
                NumeroDocumento = numDoc.ToString(),
                Nombre          = nombre,
                Estado          = EstadoValidacion.Ok,
            });
            validos.Add(numDoc);
        }

        return new PreviewAsignacionDto
        {
            Filas             = todasLasFilas.Take(10).ToList(),
            TotalValidos      = validos.Count,
            TotalInvalidos    = todasLasFilas.Count - validos.Count,
            TitularIDsValidos = validos,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<ResultadoCargueDto> ConfirmarCargueAsync(
        int carteraID, int liderID, List<long> titularIDs)
    {
        if (titularIDs.Count == 0)
            return new ResultadoCargueDto { Insertados = 0, Mensaje = "No hay titulares válidos para asignar." };

        // 1. Limpiar staging
        await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE Titular_CarteraAuxiliar");

        // 2. Insertar en chunks (SQL Server admite ~2100 parámetros por statement).
        // Los IDs son long validados desde nuestra propia BD — no hay riesgo de inyección.
        foreach (var chunk in titularIDs.Chunk(ChunkSize))
        {
            var valores = string.Join(", ", chunk.Select(id => $"({id}, {carteraID})"));
#pragma warning disable EF1002
            await _db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO Titular_CarteraAuxiliar (TitularID, CarteraID) VALUES {valores}");
#pragma warning restore EF1002
        }

        // 3. Ejecutar SP — transfiere staging → Titular_Cartera (solo los no duplicados)
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC sp_ProcesarAsignacionCartera @p0, @p1", carteraID, liderID);

        return new ResultadoCargueDto
        {
            Insertados = titularIDs.Count,
            Mensaje    = $"Se asignaron {titularIDs.Count} titular(es) correctamente.",
        };
    }

}
