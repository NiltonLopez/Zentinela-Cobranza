using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Catálogo de fuentes de localización (ADR-040): estado operativo (Funcional / Intermitente /
/// Inactivo), grupo, nota y último cargue OK de cada fuente. Lo consumen el Calificador (chips de
/// estado del panel "Porcentajes de Localización") y los Informes de Localización. Mismos roles que
/// el Calificador (incluido Auxiliar de Sistemas, que ya lee sus opciones); Director incluido.
/// </summary>
[ApiController]
[Route("api/fuentes-localizacion")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.AuxiliarSistemas},{Roles.LiderExterno}")]
public class FuentesLocalizacionController : ControllerBase
{
    private readonly IFuentesLocalizacionService _service;

    public FuentesLocalizacionController(IFuentesLocalizacionService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await _service.ListarAsync(ct));
}
