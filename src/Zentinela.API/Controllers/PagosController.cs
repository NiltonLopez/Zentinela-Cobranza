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
        // FechaRegistro se almacena como DateTime.UtcNow; EF devuelve Kind=Unspecified
        // desde SQL Server. Sin SpecifyKind, System.Text.Json serializa sin "Z" y el
        // navegador interpreta la hora como local en lugar de UTC, causando un diff
        // negativo que muestra "hace menos de un minuto" incorrectamente.
        var fechaUtc = fecha.HasValue
            ? (DateTime?)DateTime.SpecifyKind(fecha.Value, DateTimeKind.Utc)
            : null;
        return Ok(new { fechaActualizacion = fechaUtc });
    }

    /// <summary>
    /// Descarga la plantilla XLSX del cargue de pagos. Con masivo=true incluye las
    /// columnas Lider y Cartera por fila (modo "Cargue Masivo").
    /// </summary>
    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla([FromQuery] bool masivo = false)
    {
        var bytes  = masivo ? _service.GenerarPlantillaMasiva() : _service.GenerarPlantilla();
        var nombre = masivo ? "plantilla_cargue_pagos_masivo.xlsx" : "plantilla_cargue_pagos.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombre);
    }

    /// <summary>
    /// Lista las carteras disponibles para el selector de la UI.
    /// Reutiliza la lista de relaciones de GestionCarterasService.
    /// </summary>
    [HttpGet("carteras")]
    public async Task<IActionResult> ListarCarteras()
    {
        // Cargue de Pagos es flujo administrativo: las carteras externas no aplican.
        var relaciones = await _gestionCarteras.ListarRelacionesAsync(incluirExternas: false);
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
    public async Task<IActionResult> Validar(
        [FromForm] IFormFile archivo, [FromForm] int carteraID, [FromForm] bool masivo = false)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo no puede estar vacío." });

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (ext != ".csv" && ext != ".xlsx")
            return BadRequest(new { mensaje = "El archivo debe ser .csv o .xlsx." });

        // En modo masivo la cartera viene por fila en el archivo — no se exige el selector.
        if (!masivo && carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });

        try
        {
            using var stream = archivo.OpenReadStream();
            var resultado = masivo
                ? await _service.ValidarArchivoMasivoAsync(stream, ext)
                : await _service.ValidarArchivoAsync(stream, carteraID, ext);
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
    /// El UsuarioRegistro (UserName) se obtiene del token JWT del usuario autenticado.
    /// </summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarPagosRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var usuarioRegistro = User.FindFirstValue("userName");
        if (string.IsNullOrWhiteSpace(usuarioRegistro))
            return Unauthorized(new { mensaje = "No se pudo identificar al usuario." });

        var insertados = await _service.ConfirmarCargueAsync(request, usuarioRegistro);
        return Ok(new { insertados, mensaje = $"Se cargaron {insertados} pagos correctamente." });
    }

    /// <summary>
    /// Registra UN pago desde el formulario de "Pago individual". Valida y persiste en un solo paso,
    /// reutilizando la resolución de líder/agente del cargue masivo. Devuelve el líder resuelto, o
    /// 400 con el motivo si la fila no es válida o está duplicada.
    /// </summary>
    [HttpPost("registrar-individual")]
    public async Task<IActionResult> RegistrarIndividual([FromBody] RegistrarPagoIndividualRequest request)
    {
        if (request.CarteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });
        if (request.CedulaTitular <= 0)
            return BadRequest(new { mensaje = "La cédula del titular es obligatoria." });
        if (string.IsNullOrWhiteSpace(request.NombreCliente))
            return BadRequest(new { mensaje = "El nombre del cliente es obligatorio." });
        if (request.ValorPagado <= 0)
            return BadRequest(new { mensaje = "El valor pagado debe ser mayor a cero." });
        if (request.Honorarios < 0)
            return BadRequest(new { mensaje = "Los honorarios no pueden ser negativos." });
        if (!request.EsBaseGeneral && string.IsNullOrWhiteSpace(request.UsuarioControl))
            return BadRequest(new { mensaje = "Selecciona el asesor o marca BASE GENERAL." });

        var usuarioRegistro = User.FindFirstValue("userName");
        if (string.IsNullOrWhiteSpace(usuarioRegistro))
            return Unauthorized(new { mensaje = "No se pudo identificar al usuario." });

        try
        {
            var resultado = await _service.RegistrarPagoIndividualAsync(request, usuarioRegistro);
            return Ok(resultado);
        }
        catch (PagoIndividualException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Asesores de una cartera para el autocomplete del pago individual (filtra por nombre).</summary>
    [HttpGet("asesores")]
    public async Task<IActionResult> BuscarAsesores([FromQuery] int carteraID, [FromQuery] string q = "")
    {
        if (carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });

        var asesores = await _service.BuscarAsesoresAsync(carteraID, q);
        return Ok(asesores);
    }

    /// <summary>Busca un titular por cédula dentro de una cartera (para autocompletar su nombre).</summary>
    [HttpGet("titular")]
    public async Task<IActionResult> BuscarTitular([FromQuery] int carteraID, [FromQuery] long cedula)
    {
        if (carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });
        if (cedula <= 0)
            return BadRequest(new { mensaje = "Cédula inválida." });

        var titular = await _service.BuscarTitularAsync(carteraID, cedula);
        return Ok(titular);
    }

    /// <summary>
    /// Combinaciones (cartera, año, mes) con al menos un Pago cargado — alimenta los
    /// selects dinámicos y mutuamente dependientes del tab "Eliminar pagos".
    /// </summary>
    [HttpGet("periodos-disponibles")]
    public async Task<IActionResult> ObtenerPeriodosDisponibles()
    {
        var periodos = await _service.ObtenerPeriodosDisponiblesAsync();
        return Ok(periodos);
    }

    /// <summary>
    /// Vista previa de un borrado de período: cuántos pagos y por cuánto dinero hay
    /// en el mes (carteraID null/0 = todas las carteras). No borra nada.
    /// </summary>
    [HttpGet("periodo/conteo")]
    public async Task<IActionResult> ContarPeriodo(
        [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? carteraID)
    {
        if (mes < 1 || mes > 12)
            return BadRequest(new { mensaje = "Mes inválido." });
        if (anio < 2000 || anio > 2100)
            return BadRequest(new { mensaje = "Año inválido." });

        var conteo = await _service.ContarPeriodoAsync(anio, mes, carteraID is > 0 ? carteraID : null);
        return Ok(conteo);
    }

    /// <summary>
    /// Borra en bloque los pagos de un período mensual (carteraID null/0 = todas las
    /// carteras). Operación irreversible — restringida a Admin (más estricta que el
    /// resto del módulo, que también permite Auxiliar de Sistemas). El frontend debe
    /// haber mostrado el conteo de <see cref="ContarPeriodo"/> y pedido confirmación
    /// explícita antes de llamar este endpoint.
    /// </summary>
    [HttpDelete("periodo")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> BorrarPeriodo([FromBody] BorrarPeriodoRequest request)
    {
        if (request.Mes < 1 || request.Mes > 12)
            return BadRequest(new { mensaje = "Mes inválido." });
        if (request.Anio < 2000 || request.Anio > 2100)
            return BadRequest(new { mensaje = "Año inválido." });

        var carteraID = request.CarteraID is > 0 ? request.CarteraID : null;
        var borrados  = await _service.BorrarPeriodoAsync(request.Anio, request.Mes, carteraID);
        return Ok(new { borrados, mensaje = $"Se borraron {borrados} pagos del período seleccionado." });
    }
}
