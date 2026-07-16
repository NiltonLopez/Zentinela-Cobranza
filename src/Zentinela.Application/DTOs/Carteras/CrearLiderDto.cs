namespace Zentinela.Application.DTOs.Carteras;

/// <summary>
/// Solicitud para crear un líder en DBCallCenter a partir de un empleado de RRHH.
/// Solo se envía la cédula; el backend resuelve nombre, email y cargo desde Empleado.
/// </summary>
public class CrearLiderDto
{
    public long NumeroDocumento { get; set; }
}
