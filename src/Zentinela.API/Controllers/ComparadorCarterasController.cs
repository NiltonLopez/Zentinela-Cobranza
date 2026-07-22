using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/comparador")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.LiderExterno}")]
public class ComparadorCarterasController : ControllerBase
{
    private readonly IComparadorCarterasService _service;
    private readonly IExcelExporter             _excel;

    public ComparadorCarterasController(
        IComparadorCarterasService service,
        IExcelExporter excel)
    {
        _service = service;
        _excel   = excel;
    }

    /// <summary>Carteras disponibles en el dropdown según el rol del usuario.</summary>
    [HttpGet("opciones")]
    public async Task<IActionResult> ObtenerOpciones()
    {
        var opciones = await _service.ObtenerOpcionesAsync(ObtenerContexto());
        return Ok(opciones);
    }

    /// <summary>
    /// Comparativo titular a titular: cartera (o líder-cartera) seleccionada vs todas las demás.
    /// Devuelve una sección por cartera con vista previa de hasta 5 titulares.
    /// </summary>
    [HttpGet("{carteraID:int}")]
    public async Task<IActionResult> Comparar(int carteraID, [FromQuery] int? liderID)
    {
        var resultado = await _service.CompararAsync(carteraID, liderID);
        return Ok(resultado);
    }

    /// <summary>
    /// Descarga Excel con todos los titulares coincidentes entre carteraID (o líder-cartera) y otraCarteraID.
    /// </summary>
    [HttpGet("{carteraID:int}/excel/{otraCarteraID:int}")]
    public async Task<IActionResult> DescargarExcel(int carteraID, int otraCarteraID, [FromQuery] int? liderID)
    {
        var nombreSeleccionada = await _service.ObtenerNombreCarteraAsync(carteraID);
        var nombreOtra         = await _service.ObtenerNombreCarteraAsync(otraCarteraID);

        var titulares = await _service.ObtenerParaExcelAsync(carteraID, liderID, otraCarteraID);
        var bytes     = _excel.GenerarComparativoCartera(titulares, nombreSeleccionada, nombreOtra);

        var nombreArchivo = $"Comparativo_{Sanitizar(nombreSeleccionada)}_vs_{Sanitizar(nombreOtra)}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombreArchivo);
    }

    /// <summary>
    /// Descarga Excel completo con TODOS los titulares coincidentes de todas las carteras,
    /// identificados por una columna "Cartera de referencia".
    /// </summary>
    [HttpGet("{carteraID:int}/excel")]
    public async Task<IActionResult> DescargarExcelCompleto(int carteraID, [FromQuery] int? liderID)
    {
        var nombreSeleccionada = await _service.ObtenerNombreCarteraAsync(carteraID);

        var titulares = await _service.ObtenerTodosParaExcelAsync(carteraID, liderID);
        var bytes     = _excel.GenerarComparativoCompleto(titulares, nombreSeleccionada);

        var nombreArchivo = $"Comparativo_Completo_{Sanitizar(nombreSeleccionada)}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombreArchivo);
    }

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);

    private static string Sanitizar(string nombre) =>
        string.Concat(nombre.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
}
