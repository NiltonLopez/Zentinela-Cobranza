using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>Lo que el recálculo programado lee o hace en la BD (separado para probar el horario sin BD).</summary>
internal interface IPasosRecalculoProgramado
{
    /// <summary>¿Existen las tablas?, último corte OK y si el ETL de gestión está corriendo.</summary>
    Task<EstadoCorteProgramado> LeerEstadoAsync(CancellationToken ct);

    /// <summary>Puesta al día del vínculo lote ↔ cargue de retorno (tolera que falten las tablas).</summary>
    Task VincularPendientesAsync(CancellationToken ct);
}

/// <summary>Un intento del día y lo que se decidió (para el log y las pruebas).</summary>
internal sealed record IntentoProgramado(DateTime Hora, DecisionCorteProgramado Decision, ResultadoRondaCorte? Resultado);

/// <summary>
/// Recálculo diario del corte de localización (ADR-040, etapa 4). Un <see cref="BackgroundService"/> que cada día,
/// a <see cref="RecalculoProgramadoLocalizacionSettings.Hora"/> (13:05) en la hora del negocio
/// (<see cref="IRelojLocalizacion"/>: zona <c>InformesLocalizacion:ZonaHoraria</c>, Colombia; NO el reloj del equipo
/// ni el de la BD, que en desarrollo difieren 7 h), pide un corte al coordinador y espera a que termine:
/// <list type="bullet">
/// <item><b>Sin duplicar</b>: si ya hay un corte OK posterior a las 13:00 de hoy (un cargue o una lectura se
///   adelantaron) no hace nada; si hay uno corriendo, espera ese (single-flight del coordinador).</item>
/// <item><b>ETL de gestión</b>: si <c>vw_EtlEstadoTitularEstado</c> dice EN CURSO (y no parece colgado), espera y
///   reintenta: el corte lee el saldo de UltimoEstadoTitular.</item>
/// <item><b>Reintentos</b>: corte fallido, ETL en curso u otro proceso calculando → otro intento cada
///   <see cref="RecalculoProgramadoLocalizacionSettings.MinutosEntreReintentos"/> minutos, hasta
///   <see cref="RecalculoProgramadoLocalizacionSettings.MaxReintentos"/>; después, hasta mañana (el cálculo
///   perezoso de las 13:00 sigue de respaldo).</item>
/// <item><b>Sin tablas</b>: aviso en el log y hasta mañana. Nunca tumba la API: todo error queda en el log.</item>
/// <item>Después de cada día, la puesta al día del vínculo lote ↔ cargue de retorno.</item>
/// </list>
/// En IIS el proceso tiene que estar vivo a las 13:05: el AppPool con <c>AlwaysRunning</c> y sin reciclaje por
/// inactividad (docs/despliegue.md). Si el proceso arranca después de la hora, el corte del día lo cubre el cálculo
/// al arrancar o el primer acceso.
/// </summary>
internal sealed class LocalizacionRecalculoProgramado : BackgroundService
{
    private readonly ILocalizacionResumenCoordinador _coordinador;
    private readonly IPasosRecalculoProgramado _pasos;
    private readonly IRelojLocalizacion _reloj;
    private readonly RecalculoProgramadoLocalizacionSettings _opciones;
    private readonly ILogger<LocalizacionRecalculoProgramado> _log;
    private readonly TimeSpan _hora;

    public LocalizacionRecalculoProgramado(
        ILocalizacionResumenCoordinador coordinador, IServiceScopeFactory scopes, IRelojLocalizacion reloj,
        IOptions<InformesLocalizacionSettings> opciones, ILogger<LocalizacionRecalculoProgramado> log)
        : this(coordinador, new PasosRecalculoProgramadoSql(scopes, opciones.Value), reloj, opciones.Value.RecalculoProgramado, log) { }

    internal LocalizacionRecalculoProgramado(
        ILocalizacionResumenCoordinador coordinador, IPasosRecalculoProgramado pasos, IRelojLocalizacion reloj,
        RecalculoProgramadoLocalizacionSettings opciones, ILogger<LocalizacionRecalculoProgramado> log)
    {
        _coordinador = coordinador;
        _pasos = pasos;
        _reloj = reloj;
        _opciones = opciones;
        _log = log;
        if (!ProgramaRecalculoLocalizacion.TryParseHora(opciones.Hora, out _hora))
            _log.LogWarning("InformesLocalizacion:RecalculoProgramado:Hora '{Hora}' no es HH:mm; se usa {Defecto}",
                opciones.Hora, ProgramaRecalculoLocalizacion.HoraPorDefecto);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.Activado)
        {
            _log.LogInformation("Recálculo programado de localización desactivado (InformesLocalizacion:RecalculoProgramado:Activado)");
            return Task.CompletedTask;
        }
        return BucleAsync(stoppingToken);
    }

    /// <summary>Espera la próxima hora programada, corre el día y vuelve a empezar, hasta que se detenga la API.</summary>
    internal async Task BucleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ahora = _reloj.Ahora;
                var proxima = ProgramaRecalculoLocalizacion.ProximaEjecucion(ahora, _hora);
                _log.LogInformation("Próximo corte programado de localización: {Proxima:yyyy-MM-dd HH:mm} (hora del negocio)", proxima);
                await _reloj.EsperarAsync(proxima - ahora, ct);
                await EjecutarDiaAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Nada de aquí puede tumbar la API; el siguiente día se intenta de nuevo.
                _log.LogError(ex, "Falló el recálculo programado de localización");
            }
        }
    }

    /// <summary>Los intentos de un día: decide, calcula y reintenta según <see cref="ProgramaRecalculoLocalizacion.Decidir"/>.</summary>
    internal async Task<IReadOnlyList<IntentoProgramado>> EjecutarDiaAsync(CancellationToken ct)
    {
        var intentos = new List<IntentoProgramado>();
        var maxReintentos = Math.Max(0, _opciones.MaxReintentos);
        var espera = TimeSpan.FromMinutes(Math.Max(1, _opciones.MinutosEntreReintentos));

        for (var n = 0; ; n++)
        {
            var ahora = _reloj.Ahora;
            DecisionCorteProgramado decision;
            ResultadoRondaCorte? resultado = null;
            try
            {
                decision = ProgramaRecalculoLocalizacion.Decidir(await _pasos.LeerEstadoAsync(ct), ahora);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La BD no respondió: se trata como un corte fallido (reintento).
                _log.LogWarning(ex, "Recálculo programado de localización: no se pudo leer el estado; se reintenta");
                decision = DecisionCorteProgramado.Calcular;
                resultado = ResultadoRondaCorte.Error;
            }

            if (decision == DecisionCorteProgramado.Calcular && resultado is null)
                resultado = await _coordinador.RecalcularYEsperarAsync("CORTE_13H", ct);

            intentos.Add(new IntentoProgramado(ahora, decision, resultado));
            var terminado = decision switch
            {
                DecisionCorteProgramado.SinTablas  => true,
                DecisionCorteProgramado.YaHayCorte => true,
                DecisionCorteProgramado.EsperarEtl => false,
                _ => resultado is ResultadoRondaCorte.Ok or ResultadoRondaCorte.SinTablas,
            };

            if (decision == DecisionCorteProgramado.SinTablas || resultado == ResultadoRondaCorte.SinTablas)
            {
                _log.LogWarning("Recálculo programado de localización: faltan las tablas ({Script}); se intenta mañana",
                    LocalizacionTablasFaltantesException.Script);
                return intentos;
            }
            if (terminado)
            {
                _log.LogInformation("Recálculo programado de localización: {Decision} {Resultado}", decision, resultado);
                break;
            }
            if (n >= maxReintentos)
            {
                _log.LogWarning(
                    "Recálculo programado de localización: sin corte tras {Intentos} intentos ({Decision} {Resultado}); se intenta mañana "
                  + "(el primer acceso después de las 13:00 lo pide igual)", n + 1, decision, resultado);
                break;
            }
            _log.LogInformation("Recálculo programado de localización: {Decision} {Resultado}; reintento en {Minutos} min",
                decision, resultado, espera.TotalMinutes);
            await _reloj.EsperarAsync(espera, ct);
        }

        try { await _pasos.VincularPendientesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudo poner al día el vínculo de los lotes con sus cargues de retorno");
        }
        return intentos;
    }
}

/// <summary>
/// <see cref="IPasosRecalculoProgramado"/> contra la BD, con un scope por llamada (el servicio es Singleton). El ETL
/// se considera en curso con la misma regla de las sugerencias: último intento EN CURSO de menos de
/// <see cref="SugerenciasLocalizacionSettings.MinutosEtlColgado"/> minutos (la edad se calcula en SQL: el reloj de la
/// BD no es el de la API).
/// </summary>
internal sealed class PasosRecalculoProgramadoSql(IServiceScopeFactory scopes, InformesLocalizacionSettings opciones)
    : IPasosRecalculoProgramado
{
    public async Task<EstadoCorteProgramado> LeerEstadoAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        await using var conn = new SqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);

        try
        {
            await LocalizacionResumenService.VerificarTablasAsync(conn, ct);
        }
        catch (LocalizacionTablasFaltantesException)
        {
            return new EstadoCorteProgramado(false, null, false);
        }

        DateTime? ultimoOk;
        await using (var cmd = new SqlCommand(
            "SELECT TOP (1) FechaCorte FROM dbo.LocalizacionCorte WHERE Estado = 'OK' ORDER BY CorteID DESC;", conn))
            ultimoOk = await cmd.ExecuteScalarAsync(ct) is DateTime d ? d : null;

        var etlEnCurso = false;
        try
        {
            await using var cmd = new SqlCommand("""
                SELECT TOP (1) ResultadoUltimoIntento, DATEDIFF(MINUTE, UltimoIntento, SYSDATETIME())
                  FROM dbo.vw_EtlEstadoTitularEstado;
                """, conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct) && !rd.IsDBNull(0)
                && string.Equals(rd.GetString(0), "EN CURSO", StringComparison.OrdinalIgnoreCase))
                etlEnCurso = rd.IsDBNull(1) || rd.GetInt32(1) < opciones.Sugerencias.MinutosEtlColgado;
        }
        catch (SqlException)
        {
            // Sin la vista (u otro error) no se bloquea el corte: es la misma tolerancia del corte y las sugerencias.
        }
        return new EstadoCorteProgramado(true, ultimoOk, etlEnCurso);
    }

    public async Task VincularPendientesAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILocalizacionRetornoService>().VincularPendientesAsync(ct);
    }
}
