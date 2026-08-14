using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesGenerales;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informes Generales — informes transversales que no pertenecen a un módulo concreto.
/// Restringido a Admin y Director: el cruce mide el consumo de la base de datos por líder,
/// así que un líder no debería auditarse a sí mismo.
/// </summary>
[ApiController]
[Route("api/informes/generales")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Director}")]
public class InformesGeneralesController : ControllerBase
{
    /// <summary>Tope de archivos por consulta — evita requests desmedidos por error.</summary>
    private const int MaxArchivos = 20;

    private readonly IUsoBaseDatosService _service;

    public InformesGeneralesController(IUsoBaseDatosService service) => _service = service;

    /// <summary>
    /// Cruza los teléfonos de N archivos CSV/XLSX contra las fuentes de localización y
    /// devuelve el uso de base de datos agregado por fuente y por líder. No persiste nada.
    /// Cada archivo debe traer una columna de teléfono identificable por su encabezado;
    /// un archivo que falle no invalida a los demás (se reporta en Archivos[].Ok = false).
    /// </summary>
    [HttpPost("uso-base-datos")]
    // 120 MB. Tres capas tienen que acompañar este número, o el upload muere antes de llegar acá:
    //   1. web.config → maxAllowedContentLength (IIS filtra ANTES del pipeline; default ~28,6 MB)
    //   2. RequestSizeLimit         → tope del cuerpo de la request
    //   3. RequestFormLimits        → tope del parseo multipart (default 128 MB, muy cerca de 120)
    [RequestSizeLimit(120 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 120 * 1024 * 1024)]
    public async Task<IActionResult> UsoBaseDatos(
        [FromForm] List<IFormFile> archivos, CancellationToken ct)
    {
        if (archivos is null || archivos.Count == 0)
            return BadRequest(new { mensaje = "Debe adjuntar al menos un archivo." });

        if (archivos.Count > MaxArchivos)
            return BadRequest(new { mensaje = $"Máximo {MaxArchivos} archivos por consulta." });

        var entradas = new List<ArchivoTelefonos>();
        try
        {
            foreach (var archivo in archivos)
            {
                if (archivo.Length == 0)
                    return BadRequest(new { mensaje = $"El archivo '{archivo.FileName}' está vacío." });

                var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
                if (ext != ".csv" && ext != ".xlsx")
                    return BadRequest(new
                    {
                        mensaje = $"'{archivo.FileName}': solo se aceptan archivos .csv o .xlsx.",
                    });

                entradas.Add(new ArchivoTelefonos(archivo.FileName, ext, archivo.OpenReadStream()));
            }

            return Ok(await _service.AnalizarAsync(entradas, ct));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensaje = $"Error al procesar los archivos: {ex.Message}" });
        }
        finally
        {
            foreach (var entrada in entradas) entrada.Contenido.Dispose();
        }
    }
}
