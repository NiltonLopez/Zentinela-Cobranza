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
///   (13:00, arranque, botón, recálculo programado) los cubre el corte en curso.</item>
/// <item><b>Rondas</b>: una ronda es el cálculo que arrancó más las corridas pendientes que se le encadenaron.
///   <see cref="RecalcularYEsperarAsync"/> (el recálculo programado de las 13:05, etapa 4) espera la ronda en
///   curso o arranca una, y recibe cómo terminó su última corrida.</item>
/// <item><b>Nunca bloquea</b>: <see cref="SolicitarRecalculo"/> devuelve de inmediato; el cálculo corre en
///   segundo plano con un scope propio (<see cref="IServiceScopeFactory"/>), como los trabajos del Cargue
///   de Fuentes.</item>
/// <item><b>Disparos</b>: tras cada cargue OK (<c>CargueFuentesMotor</c>), a las 13:05 (servicio en segundo
///   plano), al primer acceso después de las 13:00 (<see cref="ReglaCorteLocalizacion"/>, respaldo), al arrancar si
///   no hay corte del día y con el botón de Admin. Tras un fallo, los disparos automáticos de lectura esperan
///   <see cref="InformesLocalizacionSettings.MinutosReintentoTrasError"/> para no martillar la BD.</item>
/// <item><b>Hora</b>: la del negocio (<see cref="IRelojLocalizacion"/>, zona configurada), no la del equipo.</item>
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
    private readonly IRelojLocalizacion _reloj;

    private readonly SemaphoreSlim _unoALaVez = new(1, 1);
    private readonly object _lock = new();
    private Solicitud? _pendiente;
    private Solicitud? _enCurso;
    private DateTime? _inicioEnCurso;
    private DateTime? _ultimoFallo;
    private TaskCompletionSource<ResultadoRondaCorte>? _ronda;
    private long _version;

    public LocalizacionResumenCoordinador(
        IServiceScopeFactory scopes,
        ILogger<LocalizacionResumenCoordinador> log,
        IOptions<InformesLocalizacionSettings> opciones,
        IRelojLocalizacion reloj)
    {
        _scopes = scopes;
        _log = log;
        _opciones = opciones.Value;
        _reloj = reloj;
    }

    public bool Calculando { get { lock (_lock) return _enCurso is not null; } }
    public string? MotivoEnCurso { get { lock (_lock) return _enCurso?.Motivo; } }
    public DateTime? InicioEnCurso { get { lock (_lock) return _inicioEnCurso; } }
    public long Version => Interlocked.Read(ref _version);

    private DateTime Ahora => _reloj.Ahora;

    public void SolicitarRecalculo(string motivo, string? usuario = null) =>
        _ = Iniciar(new Solicitud(motivo, usuario));

    public async Task<ResultadoRondaCorte> RecalcularYEsperarAsync(string motivo, CancellationToken ct)
    {
        var ronda = Iniciar(new Solicitud(motivo, null));
        return await ronda.WaitAsync(ct);
    }

    /// <summary>
    /// Arranca una ronda o, si ya hay una, se suma a ella. Tras un cargue la solicitud queda como pendiente (la
    /// última gana) y corre al terminar la actual: la que corre pudo leer la fuente antes del commit del cargue.
    /// Cualquier otro motivo ya lo cubre el corte en curso. Devuelve la tarea de la ronda.
    /// </summary>
    private Task<ResultadoRondaCorte> Iniciar(Solicitud solicitud)
    {
        lock (_lock)
        {
            if (!_unoALaVez.Wait(0))
            {
                if (solicitud.Motivo == "CARGUE_FUENTE") _pendiente = solicitud;
                // El semáforo y _ronda se toman y sueltan juntos bajo el lock: aquí _ronda existe.
                return _ronda!.Task;
            }
            _enCurso = solicitud;
            _inicioEnCurso = Ahora;
            var ronda = new TaskCompletionSource<ResultadoRondaCorte>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ronda = ronda;
            _ = Task.Run(() => BucleAsync(solicitud, ronda));
            return ronda.Task;
        }
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

    private async Task BucleAsync(Solicitud primera, TaskCompletionSource<ResultadoRondaCorte> ronda)
    {
        var actual = primera;
        var resultado = ResultadoRondaCorte.Error;
        try
        {
            while (true)
            {
                resultado = await EjecutarAsync(actual);
                lock (_lock)
                {
                    if (_pendiente is null)
                    {
                        _enCurso = null;
                        _inicioEnCurso = null;
                        _ronda = null;
                        _unoALaVez.Release();
                        break;
                    }
                    actual = _pendiente;
                    _pendiente = null;
                    _enCurso = actual;
                    _inicioEnCurso = Ahora;
                }
            }
        }
        finally
        {
            // EjecutarAsync no lanza; el finally solo garantiza que nadie quede esperando para siempre.
            ronda.TrySetResult(resultado);
        }
    }

    private async Task<ResultadoRondaCorte> EjecutarAsync(Solicitud s)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var servicio = scope.ServiceProvider.GetRequiredService<LocalizacionResumenService>();
            var r = await servicio.CalcularAsync(s.Motivo, s.Usuario, Ahora, CancellationToken.None);
            if (r.Omitido) return ResultadoRondaCorte.Omitido;
            Interlocked.Increment(ref _version);
            lock (_lock) _ultimoFallo = null;
            return ResultadoRondaCorte.Ok;
        }
        catch (LocalizacionTablasFaltantesException ex)
        {
            _log.LogWarning("Corte de localización no calculado ({Motivo}): {Mensaje}", s.Motivo, ex.Message);
            // Sin espera tras fallo: las lecturas no pueden disparar cortes mientras falten las tablas
            // (fallan antes), y así el primer corte corre apenas el usuario aplique el script.
            return ResultadoRondaCorte.SinTablas;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló el corte de localización ({Motivo})", s.Motivo);
            lock (_lock) _ultimoFallo = Ahora;
            return ResultadoRondaCorte.Error;
        }
    }

    private bool EnEsperaTrasFallo() =>
        _ultimoFallo is { } f && Ahora - f < TimeSpan.FromMinutes(Math.Max(0, _opciones.MinutosReintentoTrasError));
}
