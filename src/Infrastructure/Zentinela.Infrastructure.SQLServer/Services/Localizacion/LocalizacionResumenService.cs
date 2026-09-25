using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>Cómo terminó un intento de corte.</summary>
internal sealed record ResultadoCorteLocalizacion(int? CorteID, bool Omitido, int Pares, int Titulares, long DuracionMs);

/// <summary>
/// Calcula un corte del precálculo de los Informes de Localización (ADR-040). Scoped: lo usa el
/// <see cref="LocalizacionResumenCoordinador"/> desde un scope propio, nunca una petición HTTP.
///
/// Conexión propia (no la del DbContext): el batch vive ~1 minuto, llena #temp y no debe compartir
/// estado con otro código del scope. El SQL lo arma <see cref="GeneradorSqlLocalizacion"/>.
/// </summary>
internal sealed class LocalizacionResumenService
{
    // Medido el 2026-09-25: ~51 s para los 56 pares activos (1,93 M titulares). 600 s deja margen
    // para un servidor cargado sin dejar colgado un corte para siempre.
    private const int TimeoutSegundos = 600;

    // Un 'EN CURSO' más viejo que esto es de un proceso que murió (el batch no pasa de TimeoutSegundos).
    private static readonly TimeSpan EnCursoHuerfano = TimeSpan.FromMinutes(15);

    // Retención de cortes (las filas de resumen se borran en cascada).
    private const int SemanasRetencion = 26;

    private readonly CallCenterDbContext _db;
    private readonly ILogger<LocalizacionResumenService> _log;

    public LocalizacionResumenService(CallCenterDbContext db, ILogger<LocalizacionResumenService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Fecha del último corte OK (null si no hay). Lanza si faltan las tablas.</summary>
    public async Task<DateTime?> ObtenerUltimoCorteOkAsync(CancellationToken ct)
    {
        await using var conn = await AbrirAsync(ct);
        await VerificarTablasAsync(conn, ct);
        await using var cmd = new SqlCommand(
            "SELECT TOP (1) FechaCorte FROM dbo.LocalizacionCorte WHERE Estado = 'OK' ORDER BY CorteID DESC;", conn);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is DateTime d ? d : null;
    }

    /// <summary>
    /// Calcula un corte completo: registra el corte EN CURSO, corre el batch (todo o nada), lo cierra
    /// OK con duración y conteos y aplica la retención. Si falla, lo deja ERROR con el mensaje y relanza.
    /// Si otro proceso (otra instancia de la API) tiene un corte EN CURSO reciente, no hace nada.
    /// </summary>
    public async Task<ResultadoCorteLocalizacion> CalcularAsync(string motivo, string? usuario, DateTime ahora, CancellationToken ct)
    {
        await using var conn = await AbrirAsync(ct);
        await VerificarTablasAsync(conn, ct);

        if (await HayOtroEnCursoAsync(conn, ahora, ct))
        {
            _log.LogInformation("Corte de localización omitido ({Motivo}): otro proceso tiene uno en curso", motivo);
            return new ResultadoCorteLocalizacion(null, true, 0, 0, 0);
        }

        var datosGestionAl = await LeerDatosGestionAlAsync(conn, ct);
        var corteID = await RegistrarInicioAsync(conn, motivo, usuario, ahora, datosGestionAl, ct);
        var reloj = Stopwatch.StartNew();
        try
        {
            await using var cmd = new SqlCommand(GeneradorSqlLocalizacion.Precalculo(), conn) { CommandTimeout = TimeoutSegundos };
            cmd.Parameters.Add("@CorteID", SqlDbType.Int).Value = corteID;
            cmd.Parameters.Add("@Hoy", SqlDbType.Date).Value = ahora.Date;

            int pares = 0, titulares = 0;
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
            {
                // El batch devuelve un solo result set al final: (Pares, Titulares).
                do
                {
                    if (rd.FieldCount == 2 && await rd.ReadAsync(ct))
                    {
                        pares = rd.GetInt32(0);
                        titulares = rd.GetInt32(1);
                    }
                } while (await rd.NextResultAsync(ct));
            }
            reloj.Stop();

            await using (var fin = new SqlCommand("""
                UPDATE dbo.LocalizacionCorte
                   SET Estado = 'OK', DuracionMs = @DuracionMs, Pares = @Pares, Titulares = @Titulares
                 WHERE CorteID = @CorteID;
                """, conn))
            {
                fin.Parameters.Add("@DuracionMs", SqlDbType.Int).Value = (int)Math.Min(int.MaxValue, reloj.ElapsedMilliseconds);
                fin.Parameters.Add("@Pares", SqlDbType.Int).Value = pares;
                fin.Parameters.Add("@Titulares", SqlDbType.Int).Value = titulares;
                fin.Parameters.Add("@CorteID", SqlDbType.Int).Value = corteID;
                await fin.ExecuteNonQueryAsync(ct);
            }

            await AplicarRetencionAsync(conn, ahora, ct);

            _log.LogInformation(
                "Corte de localización {CorteID} OK ({Motivo}): {Pares} pares, {Titulares} titulares en {Ms} ms",
                corteID, motivo, pares, titulares, reloj.ElapsedMilliseconds);
            return new ResultadoCorteLocalizacion(corteID, false, pares, titulares, reloj.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await RegistrarErrorAsync(corteID, ex, ct.IsCancellationRequested);
            throw;
        }
    }

    // ── Pasos ─────────────────────────────────────────────────────────────────

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>
    /// Las tablas las crea el usuario con el script (el clasificador bloquea escrituras de DDL desde
    /// aquí). Mientras falten, el corte no se intenta y los informes responden 503.
    /// </summary>
    internal static async Task VerificarTablasAsync(SqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN OBJECT_ID('dbo.LocalizacionCorte', 'U') IS NOT NULL
                          AND OBJECT_ID('dbo.LocalizacionResumenFuente', 'U') IS NOT NULL
                          AND OBJECT_ID('dbo.LocalizacionResumenFuenteLider', 'U') IS NOT NULL
                          AND OBJECT_ID('dbo.LocalizacionResumenPar', 'U') IS NOT NULL
                          AND OBJECT_ID('dbo.LocalizacionResumenLider', 'U') IS NOT NULL
                        THEN 1 ELSE 0 END;
            """, conn);
        if ((int)(await cmd.ExecuteScalarAsync(ct))! == 0)
            throw new LocalizacionTablasFaltantesException();
    }

    /// <summary>Otro proceso calculando (corte EN CURSO reciente); los huérfanos se cierran como ERROR.</summary>
    private static async Task<bool> HayOtroEnCursoAsync(SqlConnection conn, DateTime ahora, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE dbo.LocalizacionCorte
               SET Estado = 'ERROR', Error = N'Interrumpido: el proceso que lo calculaba terminó antes de cerrarlo.'
             WHERE Estado = 'EN CURSO' AND FechaCorte < @Limite;
            SELECT COUNT(*) FROM dbo.LocalizacionCorte WHERE Estado = 'EN CURSO';
            """, conn);
        cmd.Parameters.Add("@Limite", SqlDbType.DateTime2).Value = ahora - EnCursoHuerfano;
        return (int)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    /// <summary>Última carga OK del ETL de gestión (UltimoEstadoTitular): "Gestión al …" del banner.</summary>
    private async Task<DateTime?> LeerDatosGestionAlAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            await using var cmd = new SqlCommand("SELECT TOP (1) UltimaCargaOk FROM dbo.vw_EtlEstadoTitularEstado;", conn);
            var r = await cmd.ExecuteScalarAsync(ct);
            return r is DateTime d ? d : null;
        }
        catch (SqlException ex)
        {
            _log.LogWarning(ex, "No se pudo leer vw_EtlEstadoTitularEstado para el corte de localización");
            return null;
        }
    }

    private static async Task<int> RegistrarInicioAsync(
        SqlConnection conn, string motivo, string? usuario, DateTime ahora, DateTime? datosGestionAl, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO dbo.LocalizacionCorte (FechaCorte, Motivo, Usuario, Estado, DatosGestionAl)
            OUTPUT INSERTED.CorteID
            VALUES (@FechaCorte, @Motivo, @Usuario, 'EN CURSO', @DatosGestionAl);
            """, conn);
        // La hora sale de la API (no de SYSDATETIME) para que la regla de las 13:00 compare relojes iguales.
        cmd.Parameters.Add("@FechaCorte", SqlDbType.DateTime2).Value = ahora;
        cmd.Parameters.Add("@Motivo", SqlDbType.VarChar, 20).Value = motivo;
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = (object?)Recortar(usuario, 100) ?? DBNull.Value;
        cmd.Parameters.Add("@DatosGestionAl", SqlDbType.DateTime2).Value = (object?)datosGestionAl ?? DBNull.Value;
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task AplicarRetencionAsync(SqlConnection conn, DateTime ahora, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "DELETE FROM dbo.LocalizacionCorte WHERE FechaCorte < @Limite;", conn) { CommandTimeout = TimeoutSegundos };
        cmd.Parameters.Add("@Limite", SqlDbType.DateTime2).Value = ahora.AddDays(-7 * SemanasRetencion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Conexión nueva: la del batch puede haber quedado inservible (timeout, cancelación).</summary>
    private async Task RegistrarErrorAsync(int corteID, Exception ex, bool cancelado)
    {
        try
        {
            await using var conn = new SqlConnection(_db.Database.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE dbo.LocalizacionCorte SET Estado = 'ERROR', Error = @Error WHERE CorteID = @CorteID;", conn);
            cmd.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value =
                Recortar(cancelado ? "Cancelado." : ex.Message, 1000) ?? "Error";
            cmd.Parameters.Add("@CorteID", SqlDbType.Int).Value = corteID;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex2)
        {
            _log.LogError(ex2, "No se pudo marcar ERROR el corte de localización {CorteID}", corteID);
        }
    }

    private static string? Recortar(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}
