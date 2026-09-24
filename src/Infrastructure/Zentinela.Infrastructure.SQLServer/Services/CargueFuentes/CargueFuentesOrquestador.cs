using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Cara del módulo hacia el controller (Singleton). Recibe el archivo, administra cupos y
/// estados, y corre el <see cref="CargueFuentesMotor"/> (Scoped) en segundo plano con un
/// scope propio por trabajo: el scope de la petición HTTP muere al responder el 202.
///
/// Límite conocido (hosting in-process en IIS): un reciclaje del AppPool o su idle timeout
/// matan el trabajo. En Cargando no hay escritura parcial (la transacción se revierte sola)
/// y la ejecución queda EN CURSO en BD; <see cref="ListarAsync"/> la reporta "Interrumpido".
/// </summary>
internal sealed class CargueFuentesOrquestador : ICargueFuentesService
{
    /// <summary>Texto con que se cierra en BD una ejecución que murió con el proceso.</summary>
    private const string MotivoInterrumpida = "Interrumpida (reinicio del servidor)";

    private readonly CargueFuentesJobStore _store;
    private readonly IServiceScopeFactory _scopes;
    private readonly CargueFuentesSettings _opciones;
    private readonly ILogger<CargueFuentesOrquestador> _log;

    public CargueFuentesOrquestador(
        CargueFuentesJobStore store, IServiceScopeFactory scopes,
        IOptions<CargueFuentesSettings> opciones, ILogger<CargueFuentesOrquestador> log)
    {
        _store = store;
        _scopes = scopes;
        _opciones = opciones.Value;
        _log = log;
    }

    // ── Catálogo ────────────────────────────────────────────────────────────

    public IReadOnlyList<FuenteCargueDto> ListarFuentes() =>
        CatalogoCargueFuentes.Todas.Select(d => new FuenteCargueDto
        {
            Fuente               = d.Fuente,
            Etiqueta             = d.Etiqueta,
            TablaDestino         = d.TablaDestino,
            Notas                = d.Notas,
            Habilitada           = d.Habilitada && CatalogoReglasFuente.Existe(d.Fuente),
            FaseIntegracion      = d.FaseIntegracion,
            ColumnasEsperadas    = ColumnasEsperadas(d),
            ExtensionesAceptadas = CatalogoCargueFuentes.ExtensionesAceptadas,
            LimiteFilasXlsx      = _opciones.LimiteFilasXlsx,
        }).ToList();

    private static List<ColumnaEsperadaDto> ColumnasEsperadas(DefinicionFuenteCargue d)
    {
        // Mismo filtro que aplica el motor: un alias de columna de dato no vale para las comunes.
        var aliasDeDatos = d.ColumnasDato.Concat(d.ColumnasArchivoExtra).SelectMany(c => c.Alias).ToHashSet();
        string[] Comunes(string[] alias) => alias.Where(a => !aliasDeDatos.Contains(a)).ToArray();

        var lista = new List<ColumnaEsperadaDto>
        {
            // La clave: TitularID, o la columna propia de la fuente (CamaraEmpresa: NitEmpresa).
            new() { Nombre = d.ColumnaClaveTitular, Alias = Comunes(d.AliasDeClave), Tipo = "Documento",
                    Obligatoria = true, Descripcion = d.DescripcionClaveTitular },
        };
        if (d.CreaTitular)
            lista.Add(new() { Nombre = "Nombre", Alias = Comunes(CatalogoCargueFuentes.AliasNombre), Tipo = "Texto",
                              Obligatoria = true, Descripcion = "Obligatorio para crear titulares nuevos; repara nombres rotos" });
        if (d.CreaTitularCartera)
        {
            lista.Add(new() { Nombre = "CarteraID", Alias = Comunes(CatalogoCargueFuentes.AliasCarteraId), Tipo = "Entero",
                              Descripcion = "Opcional. Con LiderID (o nombres) crea la relación titular-cartera" });
            lista.Add(new() { Nombre = "Cartera", Alias = Comunes(CatalogoCargueFuentes.AliasCartera), Tipo = "Texto",
                              Descripcion = "Opcional. Nombre de la cartera, alternativa a CarteraID" });
            lista.Add(new() { Nombre = "LiderID", Alias = Comunes(CatalogoCargueFuentes.AliasLiderId), Tipo = "Entero",
                              Descripcion = "Opcional. El par líder-cartera debe estar activo" });
            lista.Add(new() { Nombre = "Lider", Alias = Comunes(CatalogoCargueFuentes.AliasLider), Tipo = "Texto",
                              Descripcion = "Opcional. Nombre del líder, alternativa a LiderID" });
        }
        // Las columnas que produce la regla (SIMIT: Placa) no se piden en el archivo.
        lista.AddRange(d.ColumnasDato.Where(c => c.DesdeArchivo).Concat(d.ColumnasArchivoExtra).Select(c => new ColumnaEsperadaDto
        {
            Nombre      = c.Nombre,
            Alias       = c.Alias,
            Tipo        = c.Tipo.ToString(),
            Obligatoria = c.Obligatoria,
            Descripcion = c.Descripcion,
        }));
        return lista;
    }

    // ── Validar ─────────────────────────────────────────────────────────────

    public async Task<CargueJobDto> IniciarValidacionAsync(
        string fuente, Stream archivo, string nombreArchivo, long bytes, string usuario, CancellationToken ct)
    {
        var def = CatalogoCargueFuentes.Buscar(fuente)
                  ?? throw new ArgumentException($"Fuente '{fuente}' desconocida.");
        if (!def.Habilitada || !CatalogoReglasFuente.Existe(def.Fuente))
            throw new ArgumentException($"La fuente {def.Etiqueta} todavía no está habilitada para cargue.");

        // Nunca se usa el nombre original como ruta: solo para mostrar y auditar.
        var nombre = Path.GetFileName(nombreArchivo ?? string.Empty);
        var ext = Path.GetExtension(nombre).ToLowerInvariant();
        if (!CatalogoCargueFuentes.ExtensionesAceptadas.Contains(ext))
            throw new ArgumentException($"Solo se aceptan archivos {string.Join(" o ", CatalogoCargueFuentes.ExtensionesAceptadas)}.");

        var id = Guid.NewGuid();
        var carpeta = Path.Combine(_store.CarpetaBase, id.ToString("N"));
        var job = new CargueJob(id, def.Fuente, def.Etiqueta, usuario, nombre, bytes, carpeta, Path.Combine(carpeta, "archivo" + ext));

        lock (_store.Cerrojo)
        {
            VerificarCupo(def, excepto: null);
            _store.Agregar(job);   // ocupa el cupo ya, antes de copiar el archivo
        }

        try
        {
            Directory.CreateDirectory(carpeta);
            await using var destino = new FileStream(job.RutaArchivo, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                     64 * 1024, FileOptions.Asynchronous);
            await archivo.CopyToAsync(destino, ct);
        }
        catch
        {
            _store.Quitar(job.Id);
            throw;
        }

        var trabajo = new TrabajoCargue(job.Id, def, CatalogoReglasFuente.Obtener(def.Fuente), job.RutaArchivo, nombre, usuario);
        _ = Task.Run(() => EjecutarAsync(job, async (motor, progreso, token) =>
        {
            var preview = await motor.ValidarAsync(trabajo, progreso, token);
            token.ThrowIfCancellationRequested();   // si se canceló en el último instante, no queda PreviewListo
            job.MarcarPreviewListo(preview);
        }));

        return job.Snapshot();
    }

    /// <summary>Un job activo por fuente (dos cargues de la misma tabla compartirían sello y se pisarían la política de vacías) y un tope global.</summary>
    private void VerificarCupo(DefinicionFuenteCargue def, CargueJob? excepto)
    {
        if (_store.ActivoDe(def.Fuente) is { } activo && activo != excepto)
            throw new CargueFuentesConflictoException(
                $"Ya hay un cargue de {def.Etiqueta} en curso ({activo.Usuario}, {activo.Porcentaje} %). "
                + "Espera a que termine o cancélalo.");
        var activos = _store.Activos();
        if (activos >= Math.Max(1, _opciones.MaxJobsSimultaneos))
            throw new CargueFuentesConflictoException(
                $"Hay {activos} cargues en curso (máximo {_opciones.MaxJobsSimultaneos}). Espera a que alguno termine.");
    }

    // ── Consultar ───────────────────────────────────────────────────────────

    public CargueJobDto? Obtener(Guid jobId) => _store.Obtener(jobId)?.Snapshot();

    public async Task<IReadOnlyList<CargueJobResumenDto>> ListarAsync(CancellationToken ct)
    {
        var enMemoria = _store.Listar();
        var resumen = enMemoria.Select(j => j.Resumen()).ToList();
        var porJob = resumen.ToDictionary(r => r.JobId!.Value);

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            await using var conn = new SqlConnection(db.Database.GetConnectionString());
            await conn.OpenAsync(ct);

            var huerfanas = new List<long>();
            await using (var cmd = new SqlCommand("""
                SELECT TOP (200) EjecucionID, JobID, Fuente, Usuario, NombreArchivo, FechaInicio, Estado,
                       Insertadas, Refrescadas, Error
                  FROM dbo.CargueFuenteEjecucion
                 WHERE FechaInicio >= DATEADD(hour, -@Horas, SYSDATETIME())
                 ORDER BY EjecucionID DESC;
                """, conn))
            {
                cmd.Parameters.Add("@Horas", SqlDbType.Int).Value = Math.Max(1, _opciones.RetencionHoras);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var ejecucionID = rd.GetInt64(0);
                    var jobId = rd.GetGuid(1);
                    if (porJob.TryGetValue(jobId, out var vivo))
                    {
                        vivo.EjecucionID ??= ejecucionID;
                        continue;
                    }

                    var estadoBD = rd.GetString(6);
                    var error = rd.IsDBNull(9) ? null : rd.GetString(9);
                    // EN CURSO sin job en memoria = murió con el proceso (reciclaje de IIS). Se
                    // devuelve "Interrumpido" (contrato con el front) y no "Error": no es un
                    // error del archivo ni de la carga, y no escribió nada.
                    if (estadoBD == "EN CURSO") huerfanas.Add(ejecucionID);
                    var estado = estadoBD switch
                    {
                        "OK"        => nameof(EstadoCargueJob.Completado),
                        "CANCELADA" => nameof(EstadoCargueJob.Cancelado),
                        "EN CURSO"  => "Interrumpido",
                        _ when error == MotivoInterrumpida => "Interrumpido",
                        _           => nameof(EstadoCargueJob.Error),
                    };

                    resumen.Add(new CargueJobResumenDto
                    {
                        JobId         = jobId,
                        EjecucionID   = ejecucionID,
                        Fuente        = rd.GetString(2),
                        Usuario       = rd.GetString(3),
                        NombreArchivo = rd.GetString(4),
                        CreadoEn      = rd.GetDateTime(5),
                        Estado        = estado,
                        Porcentaje    = estadoBD == "OK" ? 100 : 0,
                        Insertadas    = rd.IsDBNull(7) ? null : rd.GetInt32(7),
                        Refrescadas   = rd.IsDBNull(8) ? null : rd.GetInt32(8),
                        EnMemoria     = false,
                    });
                }
            }

            if (huerfanas.Count > 0)
            {
                // Oportunista: se cierran aquí porque no hay otro momento en que alguien note
                // que el proceso murió. Los IDs son long leídos de la BD (no hay inyección posible).
                await using var cerrar = new SqlCommand($"""
                    UPDATE dbo.CargueFuenteEjecucion
                       SET Estado = 'ERROR', FechaFin = SYSDATETIME(), Error = @Motivo
                     WHERE Estado = 'EN CURSO' AND EjecucionID IN ({string.Join(",", huerfanas)});
                    """, conn);
                cerrar.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = MotivoInterrumpida;
                await cerrar.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin BD la lista igual sirve con lo que hay en memoria.
            _log.LogWarning(ex, "No se pudo leer CargueFuenteEjecucion para la lista de cargues");
        }

        return resumen.OrderByDescending(r => r.CreadoEn).ToList();
    }

    // ── Confirmar ───────────────────────────────────────────────────────────

    public async Task<CargueJobDto?> ConfirmarAsync(Guid jobId, string usuario, CancellationToken ct)
    {
        var job = _store.Obtener(jobId);
        if (job is null) return null;
        var def = CatalogoCargueFuentes.Buscar(job.Fuente)!;

        lock (_store.Cerrojo)
        {
            if (job.Estado != EstadoCargueJob.PreviewListo)
                throw new CargueFuentesConflictoException(
                    $"El cargue está en estado {job.Estado}: solo se confirma con el preview listo.");
            if (!job.PuedeConfirmar)
                throw new CargueFuentesConflictoException("El preview no tiene filas para cargar.");
            VerificarCupo(def, excepto: job);
            job.ReiniciarCtsSiCancelado();
            if (!job.TryIniciarCarga())
                throw new CargueFuentesConflictoException("El cargue cambió de estado mientras se confirmaba.");
        }

        var huella = job.Preview!.HuellaArchivo;
        var trabajo = new TrabajoCargue(job.Id, def, CatalogoReglasFuente.Obtener(def.Fuente),
                                        job.RutaArchivo, job.NombreArchivo, usuario);
        long ejecucionID;
        try
        {
            // La fila EN CURSO se inserta ANTES de lanzar el trabajo: si el proceso muere a
            // mitad, queda la evidencia (y la lista la muestra como interrumpida).
            await using var scope = _scopes.CreateAsyncScope();
            var motor = scope.ServiceProvider.GetRequiredService<CargueFuentesMotor>();
            ejecucionID = await motor.RegistrarInicioAsync(trabajo, huella, usuario, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo registrar la ejecución del cargue {JobId}", job.Id);
            job.VolverAPreview("No se pudo registrar la ejecución; no se escribió nada. Intenta de nuevo.");
            throw;
        }
        job.EjecucionID = ejecucionID;

        _ = Task.Run(() => EjecutarAsync(job, async (motor, progreso, token) =>
        {
            var resultado = await motor.ConfirmarAsync(trabajo, ejecucionID, huella, simular: false, progreso, token);
            job.MarcarCompletado(resultado);
        }));

        return job.Snapshot();
    }

    // ── Cancelar / descartar ────────────────────────────────────────────────

    public CargueJobDto? Cancelar(Guid jobId, string usuario)
    {
        var job = _store.Obtener(jobId);
        if (job is null) return null;

        switch (job.Estado)
        {
            case EstadoCargueJob.Validando or EstadoCargueJob.Cargando:
                // En Cargando el token llega a los SqlCommand → rollback → CANCELADA en auditoría.
                _log.LogInformation("Cargue {JobId} ({Fuente}) cancelado por {Usuario}", job.Id, job.Fuente, usuario);
                job.SolicitarCancelacion();
                break;
            case EstadoCargueJob.PreviewListo:
                job.MarcarCancelado();
                _store.BorrarCarpeta(job);
                break;
            default:
                throw new CargueFuentesConflictoException($"El cargue ya terminó ({job.Estado}): no hay nada que cancelar.");
        }
        return job.Snapshot();
    }

    public bool Descartar(Guid jobId)
    {
        var job = _store.Obtener(jobId);
        if (job is null) return false;
        if (job.EstaActivo)
            throw new CargueFuentesConflictoException("El cargue sigue en curso: cancélalo antes de descartarlo.");
        return _store.Quitar(jobId);
    }

    // ── Ejecución en segundo plano ──────────────────────────────────────────

    /// <summary>
    /// Envoltorio de TODO trabajo en segundo plano. Captura cualquier excepción: una excepción
    /// no observada en un <c>Task.Run</c> no tumba el proceso, pero se pierde y el job
    /// quedaría en Validando/Cargando para siempre.
    /// </summary>
    private async Task EjecutarAsync(
        CargueJob job, Func<CargueFuentesMotor, IProgress<ProgresoCargue>, CancellationToken, Task> trabajo)
    {
        var token = job.Cts.Token;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var motor = scope.ServiceProvider.GetRequiredService<CargueFuentesMotor>();
            await trabajo(motor, new ProgresoJob(job), token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Cancelar llega como OperationCanceledException o, dentro de un SqlCommand, como
            // SqlException "Operation cancelled by user": ambas son la misma cancelación.
            job.MarcarCancelado();
        }
        catch (CargueArchivoRechazadoException ex)
        {
            job.MarcarFallo(ex.Message, ex.Preview);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló el cargue {JobId} de {Fuente}", job.Id, job.Fuente);
            job.MarcarFallo(ex.Message);
        }
        finally
        {
            // El archivo solo hace falta entre validar y confirmar.
            if (job.Estado is EstadoCargueJob.Completado or EstadoCargueJob.Error or EstadoCargueJob.Cancelado)
                _store.BorrarCarpeta(job);
        }
    }

    /// <summary>IProgress síncrono: <c>Progress&lt;T&gt;</c> publica en el pool y los reportes llegarían desordenados.</summary>
    private sealed class ProgresoJob(CargueJob job) : IProgress<ProgresoCargue>
    {
        public void Report(ProgresoCargue p) => job.Progreso(p.Fase, p.Porcentaje, p.Mensaje);
    }
}
