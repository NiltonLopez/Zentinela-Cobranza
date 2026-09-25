using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Dispara y serializa el precálculo de los Informes de Localización (ADR-040). Singleton.
///
/// <list type="bullet">
/// <item><b>Single-flight</b>: un <see cref="SemaphoreSlim"/>(1) garantiza un cálculo a la vez. Un cargue
///   que termina mientras corre no se pierde ni se encola N veces: deja UNA solicitud pendiente que corre
///   al terminar la actual (el corte en curso pudo leer la fuente antes del commit). Los demás pedidos
///   (13:00, arranque, botón) los cubre el corte en curso.</item>
/// <item><b>Nunca bloquea</b>: <see cref="SolicitarRecalculo"/> devuelve de inmediato; el cálculo corre en
///   segundo plano con un scope propio (<see cref="IServiceScopeFactory"/>), como los trabajos del Cargue
///   de Fuentes.</item>
/// <item><b>Disparos</b>: tras cada cargue OK (<c>CargueFuentesMotor</c>), al primer acceso después de las
///   13:00 (<see cref="ReglaCorteLocalizacion"/>), al arrancar si no hay corte del día y con el botón de
///   Admin. Tras un fallo, los disparos automáticos de lectura esperan
///   <see cref="InformesLocalizacionSettings.MinutosReintentoTrasError"/> para no martillar la BD.</item>
/// </list>
/// Si faltan las tablas (el usuario aún no aplicó el script) solo se registra un aviso: el arranque de
/// la API no falla por eso.
/// </summary>
internal sealed class LocalizacionResumenCoordinador : ILocalizacionResumenCoordinador
{
    private sealed record Solicitud(string Motivo, string? Usuario);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LocalizacionResumenCoordinador> _log;
    private readonly InformesLocalizacionSettings _opciones;
    private readonly TimeProvider _reloj;

    private readonly SemaphoreSlim _unoALaVez = new(1, 1);
    private readonly object _lock = new();
    private Solicitud? _pendiente;
    private Solicitud? _enCurso;
    private DateTime? _inicioEnCurso;
    private DateTime? _ultimoFallo;
    private long _version;

    public LocalizacionResumenCoordinador(
        IServiceScopeFactory scopes,
        ILogger<LocalizacionResumenCoordinador> log,
        IOptions<InformesLocalizacionSettings> opciones,
        TimeProvider? reloj = null)
    {
        _scopes = scopes;
        _log = log;
        _opciones = opciones.Value;
        _reloj = reloj ?? TimeProvider.System;
    }

    public bool Calculando { get { lock (_lock) return _enCurso is not null; } }
    public string? MotivoEnCurso { get { lock (_lock) return _enCurso?.Motivo; } }
    public DateTime? InicioEnCurso { get { lock (_lock) return _inicioEnCurso; } }
    public long Version => Interlocked.Read(ref _version);

    private DateTime Ahora => _reloj.GetLocalNow().DateTime;

    public void SolicitarRecalculo(string motivo, string? usuario = null)
    {
        var solicitud = new Solicitud(motivo, usuario);
        lock (_lock)
        {
            // Ya hay uno corriendo. Tras un cargue queda como pendiente (la última gana) y corre al
            // terminar: el que corre pudo leer la fuente antes del commit del cargue. Cualquier otro
            // motivo (13:00, arranque, botón) ya lo cubre el corte en curso.
            if (!_unoALaVez.Wait(0))
            {
                if (motivo == "CARGUE_FUENTE") _pendiente = solicitud;
                return;
            }
            _enCurso = solicitud;
            _inicioEnCurso = Ahora;
        }
        _ = Task.Run(() => BucleAsync(solicitud));
    }

    public void VerificarVigencia(DateTime? ultimoCorteOk)
    {
        if (!ReglaCorteLocalizacion.DebeRecalcular(ultimoCorteOk, Ahora, alArrancar: false)) return;
        lock (_lock)
        {
            if (_enCurso is not null || _pendiente is not null) return;
            if (EnEsperaTrasFallo()) return;
        }
        SolicitarRecalculo(ultimoCorteOk is null ? "ARRANQUE" : "CORTE_13H");
    }

    public void VerificarAlArrancar()
    {
        if (!_opciones.CalcularAlArrancar) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _opciones.SegundosEsperaArranque)));
                using var scope = _scopes.CreateScope();
                var servicio = scope.ServiceProvider.GetRequiredService<LocalizacionResumenService>();
                var ultimo = await servicio.ObtenerUltimoCorteOkAsync(CancellationToken.None);
                if (ReglaCorteLocalizacion.DebeRecalcular(ultimo, Ahora, alArrancar: true))
                    SolicitarRecalculo("ARRANQUE");
            }
            catch (LocalizacionTablasFaltantesException ex)
            {
                _log.LogWarning("Informes de Localización sin precálculo al arrancar: {Mensaje}", ex.Message);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "No se pudo verificar el corte de localización al arrancar");
            }
        });
    }

    // ── Ejecución ─────────────────────────────────────────────────────────────

    private async Task BucleAsync(Solicitud primera)
    {
        var actual = primera;
        while (true)
        {
            await EjecutarAsync(actual);
            lock (_lock)
            {
                if (_pendiente is null)
                {
                    _enCurso = null;
                    _inicioEnCurso = null;
                    _unoALaVez.Release();
                    return;
                }
                actual = _pendiente;
                _pendiente = null;
                _enCurso = actual;
                _inicioEnCurso = Ahora;
            }
        }
    }

    private async Task EjecutarAsync(Solicitud s)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var servicio = scope.ServiceProvider.GetRequiredService<LocalizacionResumenService>();
            var r = await servicio.CalcularAsync(s.Motivo, s.Usuario, Ahora, CancellationToken.None);
            if (!r.Omitido)
            {
                Interlocked.Increment(ref _version);
                lock (_lock) _ultimoFallo = null;
            }
        }
        catch (LocalizacionTablasFaltantesException ex)
        {
            _log.LogWarning("Corte de localización no calculado ({Motivo}): {Mensaje}", s.Motivo, ex.Message);
            // Sin espera tras fallo: las lecturas no pueden disparar cortes mientras falten las tablas
            // (fallan antes), y así el primer corte corre apenas el usuario aplique el script.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló el corte de localización ({Motivo})", s.Motivo);
            lock (_lock) _ultimoFallo = Ahora;
        }
    }

    private bool EnEsperaTrasFallo() =>
        _ultimoFallo is { } f && Ahora - f < TimeSpan.FromMinutes(Math.Max(0, _opciones.MinutosReintentoTrasError));
}
