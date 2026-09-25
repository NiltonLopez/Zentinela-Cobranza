using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Utils;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informes de Localización (ADR-040): resumen, cobertura por fuente, frescura y rankings con foto,
/// leídos del último corte del precálculo; y sugerencias de localización (etapa 2), calculadas en línea.
///
/// Alcance: Admin y Director ven todo. Un Líder ve solo lo suyo: <c>TryResolverLiderID</c> ignora el
/// <c>liderID</c> de la URL y usa el del token, y si pide una <c>carteraID</c> tiene que estar en su
/// Lider_Cartera activo. Como su liderID siempre queda fijado, un Líder nunca obtiene la cartera completa.
///
/// Errores: 503 mientras el usuario no aplique el script de las tablas; 409 si aún no hay corte. Las
/// sugerencias no usan el precálculo: 400 por parámetros, 409 mientras corre el ETL de gestión.
/// </summary>
[ApiController]
[Route("api/informes/localizacion")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
public class InformesLocalizacionController : ControllerBase
{
    private readonly IInformesLocalizacionService _service;
    private readonly ILocalizacionResumenCoordinador _coordinador;
    private readonly ILocalizacionSugerenciasService _sugerencias;
    private readonly IExcelExporter _excel;

    public InformesLocalizacionController(
        IInformesLocalizacionService service, ILocalizacionResumenCoordinador coordinador,
        ILocalizacionSugerenciasService sugerencias, IExcelExporter excel)
    {
        _service = service;
        _coordinador = coordinador;
        _sugerencias = sugerencias;
        _excel = excel;
    }

    /// <summary>KPIs, índice, gráfico por fuente y alertas del alcance.</summary>
    [HttpGet("resumen")]
    public Task<IActionResult> Resumen([FromQuery] int? liderID, [FromQuery] int? carteraID, CancellationToken ct) =>
        ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerResumenAsync(l, c, ct)));

    /// <summary>Cobertura por fuente agrupada por par, cartera o líder.</summary>
    [HttpGet("cobertura")]
    public Task<IActionResult> Cobertura(
        [FromQuery] int? liderID, [FromQuery] int? carteraID, [FromQuery] string? agrupar, CancellationToken ct) =>
        ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerCoberturaAsync(l, c, agrupar ?? "par", ct)));

    /// <summary>Antigüedad de la última consulta por fuente, en tramos, con umbral de "dato viejo" (90/180/365).</summary>
    [HttpGet("frescura")]
    public Task<IActionResult> Frescura(
        [FromQuery] int? liderID, [FromQuery] int? carteraID, [FromQuery] int umbral = 180, CancellationToken ct = default)
    {
        if (umbral is not (90 or 180 or 365))
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "El umbral debe ser 90, 180 o 365 días." }));
        return ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerFrescuraAsync(l, c, umbral, ct)));
    }

    /// <summary>
    /// Rankings de líderes (con foto) y de carteras. El Líder recibe el top más su propia fila, nunca la
    /// lista completa, y en carteras solo sus pares. <paramref name="incluirExternas"/> solo aplica a
    /// Admin/Director.
    /// </summary>
    [HttpGet("rankings")]
    public async Task<IActionResult> Rankings(
        [FromQuery] string? criterio, [FromQuery] bool incluirExternas = false, CancellationToken ct = default)
    {
        if (!CalculoLocalizacion.TryParseCriterio(criterio, out var c))
            return BadRequest(new { mensaje = "Criterio inválido: Indice, Contacto, Cobertura, Frescura o SinContactoSaldo." });

        // Sin liderID pedido: para el Líder devuelve el suyo (el del token); para el resto, null.
        if (!User.TryResolverLiderID(null, out var propio)) return Forbid();
        var esLider = EsLider;

        return await Traducir(async () =>
            Ok(await _service.ObtenerRankingsAsync(c, esLider ? false : incluirExternas, esLider ? propio : null, ct)));
    }

    /// <summary>
    /// Estado del precálculo: corte que se muestra ("Localización al …", "Gestión al …") y si hay uno
    /// calculándose. Abierto a los tres roles porque solo expone metadatos del corte: el front lo usa para
    /// el banner y para los estados "calculando" / "sin tablas".
    /// </summary>
    [HttpGet("resumen/estado")]
    public Task<IActionResult> Estado(CancellationToken ct) =>
        Traducir(async () => Ok(await _service.ObtenerEstadoAsync(ct)));

    /// <summary>Pide un corte nuevo en segundo plano (Admin). Responde 202 de inmediato.</summary>
    [HttpPost("resumen/recalcular")]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Recalcular(CancellationToken ct) =>
        Traducir(async () =>
        {
            // Si faltan las tablas, que el Admin lo vea ya (503) y no como un corte fallido en el log.
            await _service.ObtenerEstadoAsync(ct);
            _coordinador.SolicitarRecalculo("MANUAL", User.FindFirstValue("userName") ?? User.Identity?.Name);
            return Accepted(new { mensaje = "Recálculo solicitado. El corte nuevo aparece en cuanto termine (≈1 minuto)." });
        });

    // ── Sugerencias (etapa 2) ─────────────────────────────────────────────────

    /// <summary>
    /// A quién mandar a buscar y en qué fuente Funcional, con prioridad explicable (ADR-040, etapa 2). En
    /// línea sobre el par; no depende de las tablas del precálculo. La cartera es obligatoria; sin
    /// <c>liderID</c> es la cartera completa (solo Admin/Director: al Líder se le fija el suyo). 409 mientras
    /// corre el ETL de gestión; 400 ante parámetros inválidos.
    /// </summary>
    [HttpGet("sugerencias")]
    public Task<IActionResult> Sugerencias([FromQuery] ConsultaSugerenciasLocalizacion q, CancellationToken ct) =>
        ConSugerencias(q, ct, dto => Ok(dto));

    /// <summary>
    /// Las mismas sugerencias (el mismo top) en el formato del Cargue de Fuentes: un .xlsx si hay una sola
    /// fuente; un .zip con un .xlsx por fuente si hay varias (el cargue recibe un archivo por fuente).
    /// </summary>
    [HttpGet("sugerencias/excel")]
    public Task<IActionResult> SugerenciasExcel([FromQuery] ConsultaSugerenciasLocalizacion q, CancellationToken ct) =>
        ConSugerencias(q, ct, ArchivoSugerencias);

    private async Task<IActionResult> ConSugerencias(
        ConsultaSugerenciasLocalizacion q, CancellationToken ct, Func<SugerenciasLocalizacionDto, IActionResult> responder)
    {
        if (q.CarteraID is not > 0) return BadRequest(new { mensaje = "Elige una cartera." });
        if (!User.TryResolverLiderID(q.LiderID, out var efectivo)) return Forbid();
        if (EsLider && !await _service.EsCarteraDelLiderAsync(efectivo!.Value, q.CarteraID.Value, ct)) return Forbid();

        var parametros = new ParametrosSugerenciasLocalizacion
        {
            LiderID             = efectivo,
            CarteraID           = q.CarteraID.Value,
            Fuentes             = (q.Fuentes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            UmbralDias          = q.UmbralDias,
            UmbralRefrescoDias  = q.UmbralRefresco,
            SoloConSaldo        = q.SoloConSaldo,
            Top                 = q.Top,
            ExcluirEnviadosDias = q.ExcluirEnviadosDias,
            IncluirLocalizados  = q.IncluirLocalizados,
        };

        try
        {
            return responder(await _sugerencias.ObtenerAsync(parametros, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (LocalizacionEtlEnCursoException ex)
        {
            return Conflict(new { mensaje = ex.Message, etlEnCurso = true, inicio = ex.Inicio });
        }
    }

    private IActionResult ArchivoSugerencias(SugerenciasLocalizacionDto dto)
    {
        // Un archivo por fuente, en el orden del catálogo, con las filas del top de esa fuente.
        var archivos = dto.Parametros.Fuentes
            .Select(clave => (Def: CatalogoCargueFuentes.Buscar(clave),
                              Filas: dto.Filas.Where(f => f.Fuente.Equals(clave, StringComparison.OrdinalIgnoreCase))
                                              .Select(f => new FilaListaLocalizacion(f.TitularID, f.Nombre, f.CarteraID, f.Cartera, f.LiderID, f.Lider))
                                              .ToList()))
            .Where(x => x.Def is not null && x.Filas.Count > 0)
            .Select(x => (Nombre: $"Localizacion_{x.Def!.Fuente}_{NombreSeguro(dto.EtiquetaAlcance)}_{DateTime.Now:yyyyMMdd}.xlsx",
                          Bytes: _excel.GenerarListaLocalizacion(x.Def, x.Filas)))
            .ToList();

        if (archivos.Count == 0)
            return NotFound(new { mensaje = "No hay sugerencias para exportar con estos filtros." });

        const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (archivos.Count == 1)
            return File(archivos[0].Bytes, Xlsx, archivos[0].Nombre);

        // Varias fuentes: un .zip con un .xlsx por fuente (el Cargue de Fuentes recibe uno por fuente).
        // Pesa poco (tope de 5.000 filas en total): se arma en memoria, sin E/S síncrona sobre la respuesta.
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, bytes) in archivos)
            {
                var entrada = zip.CreateEntry(nombre, CompressionLevel.Fastest);
                using var s = entrada.Open();
                s.Write(bytes);
            }
        }
        return File(zipStream.ToArray(), "application/zip",
                    $"Localizacion_{NombreSeguro(dto.EtiquetaAlcance)}_{DateTime.Now:yyyyMMdd}.zip");
    }

    /// <summary>Nombre de archivo sin caracteres raros: letras, dígitos y guion bajo.</summary>
    private static string NombreSeguro(string s)
    {
        var limpio = new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        while (limpio.Contains("__")) limpio = limpio.Replace("__", "_");
        limpio = limpio.Trim('_');
        return limpio.Length > 60 ? limpio[..60] : limpio;
    }

    // ── Alcance y errores ─────────────────────────────────────────────────────

    // Mismo criterio que AlcanceLiderExtensions (claim de rol del token).
    private bool EsLider => User.FindFirstValue(ClaimTypes.Role) == Roles.Lider;

    private async Task<IActionResult> ConAlcance(
        int? liderID, int? carteraID, CancellationToken ct, Func<int?, int?, Task<IActionResult>> accion)
    {
        if (!User.TryResolverLiderID(liderID, out var efectivo)) return Forbid();

        // La cartera de un Líder tiene que ser suya (y activa); con su liderID fijado queda en el par.
        if (EsLider && carteraID is { } c
            && !await _service.EsCarteraDelLiderAsync(efectivo!.Value, c, ct))
            return Forbid();

        return await Traducir(() => accion(efectivo, carteraID));
    }

    private async Task<IActionResult> Traducir(Func<Task<IActionResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (LocalizacionTablasFaltantesException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = ex.Message, script = LocalizacionTablasFaltantesException.Script, sinTablas = true });
        }
        catch (LocalizacionSinCorteException ex)
        {
            return Conflict(new { mensaje = ex.Message, calculando = ex.Calculando, sinCorte = true });
        }
    }
}

/// <summary>
/// Query string de <c>GET sugerencias</c> y <c>GET sugerencias/excel</c>. <see cref="Fuentes"/> va separado
/// por comas ("Fosyga,Sura"); vacío = todas las Funcionales.
/// </summary>
public sealed class ConsultaSugerenciasLocalizacion
{
    public int? LiderID { get; set; }
    public int? CarteraID { get; set; }
    public string? Fuentes { get; set; }
    public int UmbralDias { get; set; } = 180;
    public int? UmbralRefresco { get; set; }
    public bool SoloConSaldo { get; set; } = true;
    public int Top { get; set; } = 500;
    public int ExcluirEnviadosDias { get; set; } = 60;
    public bool IncluirLocalizados { get; set; }
}
