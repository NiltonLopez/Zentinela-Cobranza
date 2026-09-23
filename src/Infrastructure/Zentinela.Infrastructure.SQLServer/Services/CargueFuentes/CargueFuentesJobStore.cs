using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.Settings;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Un trabajo de cargue en memoria. Lo escriben dos hilos (el <c>Task.Run</c> que informa
/// avance y las peticiones HTTP que cancelan o confirman), así que todo el estado mutable
/// vive detrás de <see cref="_gate"/> y hacia afuera solo sale una foto inmutable
/// (<see cref="Snapshot"/>).
/// </summary>
internal sealed class CargueJob
{
    private readonly object _gate = new();
    private EstadoCargueJob _estado = EstadoCargueJob.Validando;
    private FaseCargueJob _fase = FaseCargueJob.Leyendo;
    private int _porcentaje;
    private string? _mensaje = "Recibiendo el archivo";
    private string? _error;
    private PreviewCargueDto? _preview;
    private ResultadoCargueDto? _resultado;
    private long? _ejecucionID;
    private DateTime _actualizadoEn;

    public CargueJob(Guid id, string fuente, string etiqueta, string usuario, string nombreArchivo,
                     long bytes, string carpeta, string rutaArchivo)
    {
        Id = id;
        Fuente = fuente;
        Etiqueta = etiqueta;
        Usuario = usuario;
        NombreArchivo = nombreArchivo;
        Bytes = bytes;
        Carpeta = carpeta;
        RutaArchivo = rutaArchivo;
        CreadoEn = _actualizadoEn = DateTime.Now;
    }

    public Guid Id { get; }
    public string Fuente { get; }
    public string Etiqueta { get; }
    public string Usuario { get; }
    public string NombreArchivo { get; }
    public long Bytes { get; }
    /// <summary>Carpeta propia del job (se borra entera al terminar o purgar).</summary>
    public string Carpeta { get; }
    public string RutaArchivo { get; }
    public DateTime CreadoEn { get; }

    /// <summary>Token del trabajo en curso. Se reemplaza al confirmar si quedó cancelado.</summary>
    public CancellationTokenSource Cts { get; private set; } = new();

    public EstadoCargueJob Estado { get { lock (_gate) return _estado; } }
    public DateTime ActualizadoEn { get { lock (_gate) return _actualizadoEn; } }
    public PreviewCargueDto? Preview { get { lock (_gate) return _preview; } }

    public long? EjecucionID
    {
        get { lock (_gate) return _ejecucionID; }
        set { lock (_gate) _ejecucionID = value; }
    }

    /// <summary>Validando o Cargando: ocupa el cupo de su fuente y el global.</summary>
    public bool EstaActivo => Estado is EstadoCargueJob.Validando or EstadoCargueJob.Cargando;

    public int Porcentaje { get { lock (_gate) return _porcentaje; } }

    public bool PuedeConfirmar
    {
        get
        {
            lock (_gate)
                return _estado == EstadoCargueJob.PreviewListo
                       && _preview is { Candidatas: > 0, ColumnasFaltantes.Count: 0 };
        }
    }

    // ── Transiciones ────────────────────────────────────────────────────────
    // Validando → PreviewListo | Error | Cancelado
    // PreviewListo → Cargando | Cancelado
    // Cargando → Completado | Error | Cancelado

    /// <summary>Avance del motor. Un reporte tardío (tras cancelar/fallar) se ignora.</summary>
    public void Progreso(FaseCargueJob fase, int porcentaje, string mensaje)
    {
        lock (_gate)
        {
            if (_estado is not (EstadoCargueJob.Validando or EstadoCargueJob.Cargando)) return;
            _fase = fase;
            // La barra nunca retrocede dentro de un mismo estado (el conteo de filas del CSV es estimado).
            _porcentaje = Math.Clamp(Math.Max(_porcentaje, porcentaje), 0, 100);
            _mensaje = mensaje;
            _actualizadoEn = DateTime.Now;
        }
    }

    public void MarcarPreviewListo(PreviewCargueDto preview) => Transicion(
        EstadoCargueJob.PreviewListo, [EstadoCargueJob.Validando], j =>
        {
            j._preview = preview;
            j._fase = FaseCargueJob.Preview;
            j._porcentaje = 100;
            j._mensaje = "Preview listo: revisa y confirma";
        });

    /// <summary>PreviewListo → Cargando. false si el job no estaba en PreviewListo.</summary>
    public bool TryIniciarCarga() => Transicion(
        EstadoCargueJob.Cargando, [EstadoCargueJob.PreviewListo], j =>
        {
            j._fase = FaseCargueJob.Leyendo;
            j._porcentaje = 0;
            j._mensaje = "Registrando la ejecución";
        });

    /// <summary>Cargando → PreviewListo cuando no se pudo ni registrar la ejecución (nada se escribió).</summary>
    public void VolverAPreview(string mensaje) => Transicion(
        EstadoCargueJob.PreviewListo, [EstadoCargueJob.Cargando], j =>
        {
            j._fase = FaseCargueJob.Preview;
            j._porcentaje = 100;
            j._mensaje = mensaje;
        });

    public void MarcarCompletado(ResultadoCargueDto resultado) => Transicion(
        EstadoCargueJob.Completado, [EstadoCargueJob.Cargando], j =>
        {
            j._resultado = resultado;
            j._fase = FaseCargueJob.Terminado;
            j._porcentaje = 100;
            j._mensaje = resultado.Mensaje;
        });

    public void MarcarFallo(string error, PreviewCargueDto? preview = null) => Transicion(
        EstadoCargueJob.Error, [EstadoCargueJob.Validando, EstadoCargueJob.Cargando], j =>
        {
            j._error = error;
            if (preview is not null) j._preview = preview;
            j._mensaje = j._estado == EstadoCargueJob.Cargando
                ? "La carga falló y se revirtió: no se escribió nada"
                : "La validación falló";
        });

    public void MarcarCancelado() => Transicion(
        EstadoCargueJob.Cancelado,
        [EstadoCargueJob.Validando, EstadoCargueJob.Cargando, EstadoCargueJob.PreviewListo], j =>
        {
            j._mensaje = j._estado == EstadoCargueJob.Cargando
                ? "Carga cancelada: la transacción se revirtió, no se escribió nada"
                : "Cancelado";
        });

    /// <summary>Pide cancelar el trabajo en curso; el estado cambia cuando el motor lo observa.</summary>
    public void SolicitarCancelacion()
    {
        lock (_gate)
        {
            if (_estado is not (EstadoCargueJob.Validando or EstadoCargueJob.Cargando)) return;
            _mensaje = "Cancelando…";
            _actualizadoEn = DateTime.Now;
        }
        Cts.Cancel();
    }

    /// <summary>
    /// Confirmar tras un intento de cancelar en PreviewListo no debe heredar un token ya
    /// cancelado: el trabajo moriría al primer <c>ThrowIfCancellationRequested</c>.
    /// </summary>
    public void ReiniciarCtsSiCancelado()
    {
        lock (_gate)
        {
            if (!Cts.IsCancellationRequested) return;
            Cts.Dispose();
            Cts = new CancellationTokenSource();
        }
    }

    private bool Transicion(EstadoCargueJob destino, EstadoCargueJob[] desde, Action<CargueJob> aplicar)
    {
        lock (_gate)
        {
            if (!desde.Contains(_estado)) return false;
            aplicar(this);   // ve el estado ANTERIOR (algunos mensajes dependen de él)
            _estado = destino;
            _actualizadoEn = DateTime.Now;
            return true;
        }
    }

    // ── Fotos ───────────────────────────────────────────────────────────────

    public CargueJobDto Snapshot()
    {
        lock (_gate)
            return new CargueJobDto
            {
                JobId          = Id,
                Fuente         = Fuente,
                Etiqueta       = Etiqueta,
                Usuario        = Usuario,
                NombreArchivo  = NombreArchivo,
                TamanoKB       = (Bytes + 1023) / 1024,
                CreadoEn       = CreadoEn,
                ActualizadoEn  = _actualizadoEn,
                Estado         = _estado,
                Fase           = _fase,
                Porcentaje     = _porcentaje,
                Mensaje        = _mensaje,
                Preview        = _preview,
                Resultado      = _resultado,
                Error          = _error,
                PuedeCancelar  = _estado is EstadoCargueJob.Validando or EstadoCargueJob.Cargando or EstadoCargueJob.PreviewListo,
                PuedeConfirmar = _estado == EstadoCargueJob.PreviewListo
                                 && _preview is { Candidatas: > 0, ColumnasFaltantes.Count: 0 },
            };
    }

    public CargueJobResumenDto Resumen()
    {
        lock (_gate)
            return new CargueJobResumenDto
            {
                JobId         = Id,
                EjecucionID   = _ejecucionID,
                Fuente        = Fuente,
                Usuario       = Usuario,
                NombreArchivo = NombreArchivo,
                CreadoEn      = CreadoEn,
                Estado        = _estado.ToString(),
                Porcentaje    = _porcentaje,
                Insertadas    = _resultado?.Insertadas,
                Refrescadas   = _resultado?.Refrescadas,
                EnMemoria     = true,
            };
    }
}

/// <summary>
/// Almacén en memoria (Singleton) de los trabajos de cargue, modelo
/// <see cref="AuditoriaReportesStore"/>. NO persiste: un reinicio del proceso pierde los
/// jobs (la auditoría en BD sí queda, y la lista la muestra como interrumpida). Purga cada
/// hora los jobs terminados y sus carpetas pasadas las horas de retención.
/// </summary>
internal sealed class CargueFuentesJobStore : IDisposable
{
    private readonly ConcurrentDictionary<Guid, CargueJob> _jobs = new();
    private readonly Timer _purga;
    private readonly ILogger<CargueFuentesJobStore> _log;
    private readonly TimeSpan _retencion;

    public CargueFuentesJobStore(IOptions<CargueFuentesSettings> opciones, ILogger<CargueFuentesJobStore> log)
    {
        _log = log;
        _retencion = TimeSpan.FromHours(Math.Max(1, opciones.Value.RetencionHoras));
        CarpetaBase = opciones.Value.ResolverCarpetaTemporal();
        PurgarHuerfanos();
        _purga = new Timer(_ => Purgar(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public string CarpetaBase { get; }

    /// <summary>
    /// Cerrojo para operaciones compuestas "verificar cupo + crear/transicionar": sin él,
    /// dos peticiones simultáneas de la misma fuente pasarían las dos el chequeo del 409.
    /// </summary>
    public object Cerrojo { get; } = new();

    public void Agregar(CargueJob job) => _jobs[job.Id] = job;

    public CargueJob? Obtener(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public IReadOnlyList<CargueJob> Listar() => _jobs.Values.OrderByDescending(j => j.CreadoEn).ToList();

    /// <summary>Job de la fuente que está validando o cargando, si hay.</summary>
    public CargueJob? ActivoDe(string fuente) =>
        _jobs.Values.FirstOrDefault(j => j.EstaActivo && j.Fuente.Equals(fuente, StringComparison.OrdinalIgnoreCase));

    public int Activos() => _jobs.Values.Count(j => j.EstaActivo);

    public bool Quitar(Guid id)
    {
        if (!_jobs.TryRemove(id, out var job)) return false;
        BorrarCarpeta(job);
        job.Cts.Dispose();
        return true;
    }

    public void BorrarCarpeta(CargueJob job)
    {
        try
        {
            if (Directory.Exists(job.Carpeta)) Directory.Delete(job.Carpeta, recursive: true);
        }
        catch (Exception ex)
        {
            // Un antivirus o un handle abierto no debe tumbar nada: la purga lo reintenta.
            _log.LogWarning(ex, "No se pudo borrar la carpeta del cargue {JobId}", job.Id);
        }
    }

    private void Purgar()
    {
        try
        {
            var limite = DateTime.Now - _retencion;
            foreach (var job in _jobs.Values)
                if (!job.EstaActivo && job.ActualizadoEn < limite) Quitar(job.Id);
            PurgarHuerfanos();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falló la purga de cargues de fuentes");
        }
    }

    /// <summary>
    /// Carpetas sin job en memoria (quedan tras un reinicio) y más viejas que la retención.
    /// No se borran las recientes: en un reciclaje solapado de IIS el proceso viejo puede
    /// seguir usando la suya unos segundos.
    /// </summary>
    private void PurgarHuerfanos()
    {
        try
        {
            if (!Directory.Exists(CarpetaBase)) return;
            var limite = DateTime.Now - _retencion;
            foreach (var dir in Directory.EnumerateDirectories(CarpetaBase))
            {
                if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id) || _jobs.ContainsKey(id)) continue;
                if (Directory.GetLastWriteTime(dir) < limite) Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudieron purgar carpetas huérfanas de cargues en {Carpeta}", CarpetaBase);
        }
    }

    public void Dispose() => _purga.Dispose();
}
