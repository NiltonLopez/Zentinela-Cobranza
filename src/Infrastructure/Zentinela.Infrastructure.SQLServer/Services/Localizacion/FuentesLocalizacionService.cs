using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// <see cref="CatalogoFuentesLocalizacion"/> para el front, con la fecha del último cargue OK de cada
/// fuente (<c>dbo.CargueFuenteEjecucion</c>, ADR-039).
/// </summary>
internal sealed class FuentesLocalizacionService : IFuentesLocalizacionService
{
    private readonly CallCenterDbContext _db;
    private readonly ILogger<FuentesLocalizacionService> _log;

    public FuentesLocalizacionService(CallCenterDbContext db, ILogger<FuentesLocalizacionService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<FuenteLocalizacionDto>> ListarAsync(CancellationToken ct = default)
    {
        var ultimos = await LeerUltimosCarguesOkAsync(ct);

        return CatalogoFuentesLocalizacion.Todas
            .Select(f => new FuenteLocalizacionDto
            {
                Clave               = f.Clave,
                EtiquetaCalificador = f.EtiquetaCalificador,
                Slug                = f.Slug,
                Estado              = f.Estado,
                Grupo               = f.Grupo,
                Nota                = f.Nota,
                EnPanelCalificador  = f.EnPanelCalificador,
                UltimoCargueOk      = ultimos.TryGetValue(f.Clave, out var fecha) ? fecha : null,
            })
            .ToList();
    }

    /// <summary>
    /// Fin del último cargue OK por fuente (FechaFin; FechaInicio si faltara). Las corridas del
    /// harness de pruebas no cuentan. Si la tabla no responde, el catálogo se sirve igual sin
    /// fechas: el Calificador depende de él para pintar los estados.
    /// </summary>
    private async Task<Dictionary<string, DateTime>> LeerUltimosCarguesOkAsync(CancellationToken ct)
    {
        var resultado = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var conn = _db.Database.GetDbConnection();
            var estabaAbierta = conn.State == ConnectionState.Open;
            if (!estabaAbierta) await conn.OpenAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Fuente, MAX(ISNULL(FechaFin, FechaInicio)) AS UltimoOk
                      FROM dbo.CargueFuenteEjecucion
                     WHERE Estado = 'OK'
                       AND Usuario NOT LIKE @PatronHarness
                     GROUP BY Fuente;
                    """;
                cmd.Parameters.Add(new SqlParameter("@PatronHarness", SqlDbType.VarChar, 100)
                {
                    Value = UsuariosPruebaCargue.Prefijo + "%",
                });

                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                    if (!rd.IsDBNull(1))
                        resultado[rd.GetString(0)] = rd.GetDateTime(1);
            }
            finally
            {
                if (!estabaAbierta) await conn.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudo leer CargueFuenteEjecucion para el catálogo de fuentes de localización");
        }
        return resultado;
    }
}
