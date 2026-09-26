using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using static Zentinela.Infrastructure.SQLServer.Services.Localizacion.GeneradorSqlEnvios;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Vínculo automático lote ↔ cargue de retorno (ADR-040, etapa 4). Dos entradas:
/// <list type="bullet">
/// <item><see cref="VincularCargueAsync"/>: la llama <c>CargueFuentesMotor</c> justo después de cerrar OK un cargue
///   (junto al pedido de recálculo). Revisa los lotes ENVIADOS sin vínculo de la fuente de ese cargue.</item>
/// <item><see cref="VincularPendientesAsync"/>: puesta al día diaria (recálculo programado de las 13:05), para
///   cargues que terminaron con la API caída o cuando el enganche falló.</item>
/// </list>
/// En los dos casos cada lote se vincula al PRIMER cargue OK de su fuente, posterior al envío, que trae alguno de sus
/// titulares (<see cref="GeneradorSqlEnvios.CandidatosVinculo"/>); C# decide y, si
/// <see cref="VinculoRetornoLocalizacionSettings.CerrarAlCubrirPct"/> está configurado y la cobertura lo alcanza, lo
/// cierra. Nunca lanza por falta de tablas (el script puede no estar aplicado): devuelve
/// <see cref="ResultadoVinculoRetorno.SinTablas"/>. Nunca debe tumbar un cargue: el motor lo envuelve en try/catch.
/// </summary>
internal sealed class LocalizacionRetornoService : ILocalizacionRetornoService
{
    private const int TimeoutSegundos = 120;

    /// <summary>Quien figura en <c>CerradoPor</c> cuando el lote se cierra solo.</summary>
    internal const string UsuarioSistema = "sistema (retorno automático)";

    private readonly CallCenterDbContext _db;
    private readonly InformesLocalizacionSettings _opciones;
    private readonly ILogger<LocalizacionRetornoService> _log;
    private readonly TablasEnvio _tablas;
    private readonly SqlConnection? _conexionCompartida;

    public LocalizacionRetornoService(
        CallCenterDbContext db, IOptions<InformesLocalizacionSettings> opciones, ILogger<LocalizacionRetornoService> log)
        : this(db, opciones.Value, log, Produccion, null) { }

    /// <summary>Para pruebas: tablas <c>#LocalizacionEnvio…</c> y la conexión (abierta) donde viven.</summary>
    internal LocalizacionRetornoService(
        CallCenterDbContext db, InformesLocalizacionSettings opciones, ILogger<LocalizacionRetornoService> log,
        TablasEnvio tablas, SqlConnection? conexionCompartida)
    {
        _db = db;
        _opciones = opciones;
        _log = log;
        _tablas = tablas;
        _conexionCompartida = conexionCompartida;
    }

    public async Task<ResultadoVinculoRetorno> VincularCargueAsync(long ejecucionID, CancellationToken ct = default)
    {
        await using var c = await ConexionAsync(ct);
        string? fuente;
        await using (var cmd = new SqlCommand(
            "SELECT Fuente FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @EjecucionID AND Estado = 'OK';", c.Conn))
        {
            cmd.Parameters.Add("@EjecucionID", SqlDbType.BigInt).Value = ejecucionID;
            fuente = await cmd.ExecuteScalarAsync(ct) as string;
        }
        if (fuente is null) return new ResultadoVinculoRetorno([], false);
        // Todos los pendientes de la fuente (no solo contra esta ejecución): así cada lote queda con el PRIMER cargue
        // que lo devolvió aunque uno anterior se hubiera escapado del enganche.
        return await VincularAsync(c.Conn, fuente, ct);
    }

    public async Task<ResultadoVinculoRetorno> VincularPendientesAsync(CancellationToken ct = default)
    {
        await using var c = await ConexionAsync(ct);
        return await VincularAsync(c.Conn, null, ct);
    }

    private async Task<ResultadoVinculoRetorno> VincularAsync(SqlConnection conn, string? fuente, CancellationToken ct)
    {
        // 1. Candidatos (SQL) → 2. decisión (C#, VinculoRetorno.Decidir) → 3. escritura con concurrencia optimista.
        var candidatos = new List<CandidatoVinculo>();
        try
        {
            await using var cmd = new SqlCommand(CandidatosVinculo(_tablas), conn) { CommandTimeout = TimeoutSegundos };
            cmd.Parameters.Add("@Fuente", SqlDbType.VarChar, 30).Value = (object?)fuente ?? DBNull.Value;
            cmd.Parameters.Add("@PatronHarness", SqlDbType.VarChar, 100).Value = UsuariosPruebaCargue.Prefijo + "%";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                candidatos.Add(new CandidatoVinculo(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetDateTime(3), rd.GetInt32(4)));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // Sin LocalizacionEnvio* (script sin aplicar): no hay lotes que vincular.
            return ResultadoVinculoRetorno.SinTablas;
        }

        var decisiones = candidatos
            .Select(x => VinculoRetorno.Decidir(x, _opciones.VinculoRetorno.CerrarAlCubrirPct))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();
        if (decisiones.Count == 0) return new ResultadoVinculoRetorno([], false);

        var json = JsonSerializer.Serialize(decisiones.Select(d => new
        {
            e = d.EnvioID,
            x = d.EjecucionID,
            f = d.FechaRetorno.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            c = d.Cerrar,
        }));

        var estados = new Dictionary<int, string>();
        await using (var cmd = new SqlCommand(AplicarVinculos(_tablas), conn) { CommandTimeout = TimeoutSegundos })
        {
            cmd.Parameters.Add("@Vinculos", SqlDbType.NVarChar, -1).Value = json;
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = UsuarioSistema;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) estados[rd.GetInt32(0)] = rd.GetString(1);
        }

        var porLote = candidatos.ToDictionary(x => x.EnvioID);
        var vinculados = decisiones
            .Where(d => estados.ContainsKey(d.EnvioID))
            .Select(d => new LoteVinculado(d.EnvioID, d.EjecucionID, d.FechaRetorno, porLote[d.EnvioID].Titulares, d.EnCargue,
                                           estados[d.EnvioID] == "CERRADO"))
            .ToList();

        foreach (var v in vinculados)
            _log.LogInformation(
                "Lote de localización {EnvioID} vinculado al cargue {EjecucionID}: {EnCargue} de {Titulares} titulares{Cierre}",
                v.EnvioID, v.EjecucionID, v.EnCargue, v.Titulares, v.Cerrado ? " (cerrado automáticamente)" : "");
        return new ResultadoVinculoRetorno(vinculados, false);
    }

    // ── Conexión (mismo patrón que LocalizacionEnviosService) ─────────────────

    private sealed class Conexion(SqlConnection conn, bool propia) : IAsyncDisposable
    {
        public SqlConnection Conn { get; } = conn;
        public ValueTask DisposeAsync() => propia ? Conn.DisposeAsync() : ValueTask.CompletedTask;
    }

    private async Task<Conexion> ConexionAsync(CancellationToken ct)
    {
        if (_conexionCompartida is not null) return new Conexion(_conexionCompartida, propia: false);
        var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        return new Conexion(conn, propia: true);
    }
}
