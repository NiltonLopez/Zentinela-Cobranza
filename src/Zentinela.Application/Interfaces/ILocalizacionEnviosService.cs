using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Utils;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Envíos a localización (ADR-040, etapa 3): lotes (<c>LocalizacionEnvio</c> / <c>LocalizacionEnvioDetalle</c>),
/// sus transiciones, su retorno e impacto y el uso del departamento. <paramref name="liderAlcance"/> es el
/// LiderID del Líder que consulta (el controlador lo toma del token); null = Admin/Director.
/// Lanzan <see cref="LocalizacionTablasFaltantesException"/> (503) si falta el script,
/// <see cref="LocalizacionEnvioNoEncontradoException"/> (404) y <see cref="LocalizacionEnvioConflictoException"/> (409).
/// </summary>
public interface ILocalizacionEnviosService
{
    /// <summary>
    /// Crea un lote BORRADOR con las filas de <see cref="CrearEnvioLocalizacionDto.Fuente"/> del top de sugerencias
    /// recalculado en el servidor. El alcance (par del Líder) ya lo validó el controlador. <see cref="ArgumentException"/>
    /// = 400; <see cref="LocalizacionEtlEnCursoException"/> = 409.
    /// </summary>
    Task<EnvioCreadoDto> CrearAsync(CrearEnvioLocalizacionDto p, string usuario, CancellationToken ct = default);

    /// <summary>Enviar / cerrar / anular, con concurrencia optimista sobre el estado.</summary>
    Task<EnvioLocalizacionDto> CambiarEstadoAsync(
        int envioID, AccionEnvio accion, string usuario, string? motivo, int? liderAlcance, CancellationToken ct = default);

    /// <summary>Cabecera, retorno, impacto y una página del detalle (por prioridad).</summary>
    Task<DetalleEnvioLocalizacionDto> ObtenerAsync(
        int envioID, int? liderAlcance, int pagina, int tamPagina, CancellationToken ct = default);

    /// <summary>Excel con el formato del Cargue de Fuentes (el de las sugerencias) con los titulares del lote.</summary>
    Task<ArchivoEnvioLocalizacion> ExcelAsync(int envioID, int? liderAlcance, CancellationToken ct = default);

    Task<ListaEnviosLocalizacionDto> ListarAsync(FiltroEnviosLocalizacion f, CancellationToken ct = default);

    /// <summary>Por líder: lotes enviados en el período (por FechaEnvio), retorno, impacto y % de su cola sugerida.</summary>
    Task<UsoDepartamentoDto> UsoDepartamentoAsync(DateTime desde, DateTime hasta, int? liderID, CancellationToken ct = default);

    /// <summary>Lo mismo en bruto, por lote y con la cola por par, para los rankings de Uso y Retorno.</summary>
    Task<UsoEnviosPorPar> UsoPorParAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);
}

public sealed record ArchivoEnvioLocalizacion(byte[] Contenido, string NombreArchivo);

/// <summary>Lotes enviados en un período con sus métricas, y la cola aproximada de cada par del último corte.</summary>
public sealed record UsoEnviosPorPar(IReadOnlyList<UsoLote> Lotes, IReadOnlyList<ColaPar> Colas, DateTime? CorteCola);
