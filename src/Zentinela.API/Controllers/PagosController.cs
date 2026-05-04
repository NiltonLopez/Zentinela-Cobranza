using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Pagos;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Endpoints para el cargue de pagos confirmados desde CSV por cartera.
/// Solo accesible para administradores.
/// El LiderID de cada pago se resuelve automáticamente desde TitularCartera — no requiere selección manual.
/// </summary>
[ApiController]
[Route("api/pagos")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class PagosController : ControllerBase
{
    private readonly ICarguePagosService _service;
    private readonly IGestionCarterasService _gestionCarteras;

    public PagosController(ICarguePagosService service, IGestionCarterasService gestionCarteras)
    {
        _service         = service;
        _gestionCarteras = gestionCarteras;
    }

    /// <summary>Fecha del último pago registrado (FechaRegistro) para la cartera.</summary>
    [HttpGet("ultima-actualizacion/{carteraID:int}")]
    public async Task<IActionResult> ObtenerUltimaActualizacion(int carteraID)
    {
        var fecha = await _service.ObtenerUltimaActualizacionAsync(carteraID);
        return Ok(new { fechaActualizacion = fecha });
    }

    /// <summary>Descarga la plantilla XLSX con las columnas requeridas para el cargue de pagos.</summary>
    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla()
    {
        var bytes = _service.GenerarPlantilla();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla_cargue_pagos.xlsx");
    }

    /// <summary>
    /// Lista las carteras disponibles para el selector de la UI.
    /// Reutiliza la lista de relaciones de GestionCarterasService.
    /// </summary>
    [HttpGet("carteras")]
    public async Task<IActionResult> ListarCarteras()
    {
        var relaciones = await _gestionCarteras.ListarRelacionesAsync();
        var carteras = relaciones
            .DistinctBy(r => r.CarteraID)
            .OrderBy(r => r.NombreCartera)
            .Select(r => new { id = r.CarteraID, nombre = r.NombreCartera });
        return Ok(carteras);
    }

    /// <summary>
    /// Parsea y valida el CSV de pagos sin persistir nada.
    /// Por cada fila, resuelve el LiderID desde TitularCartera usando (CedulaTitular, CarteraID).
    /// Devuelve preview con primeras 10 filas, conteos y todas las filas válidas para confirmar.
    /// El CSV debe tener 5 columnas: Cedula, Nombre, Valor, Fecha, Honorarios.
    /// </summary>
    [HttpPost("validar")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Validar([FromForm] IFormFile archivo, [FromForm] int carteraID)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo no puede estar vacío." });

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (ext != ".csv" && ext != ".xlsx")
            return BadRequest(new { mensaje = "El archivo debe ser .csv o .xlsx." });

        if (carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });

        try
        {
            using var stream = archivo.OpenReadStream();
            var resultado = await _service.ValidarArchivoAsync(stream, carteraID, ext);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensaje = $"Error al procesar el archivo: {ex.Message}" });
        }
    }

    /// <summary>
    /// Inserta en bloque las filas válidas en la tabla Pago.
    /// El LiderID viene resuelto por fila desde la validación previa.
    /// El UsuarioControlID se obtiene del token JWT del usuario autenticado.
    /// </summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarPagosRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var usuarioIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(usuarioIdStr, out var usuarioControlID))
            return Unauthorized(new { mensaje = "No se pudo identificar al usuario." });

        var insertados = await _service.ConfirmarCargueAsync(request, usuarioControlID);
        return Ok(new { insertados, mensaje = $"Se cargaron {insertados} pagos correctamente." });
    }
}
