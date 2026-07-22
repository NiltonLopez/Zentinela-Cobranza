namespace Zentinela.Application.DTOs.Carteras;

/// <summary>
/// Empleado de DBRecursosHumanos elegible para darse de alta como líder de cobranza:
/// cargo de líder (ver <see cref="CargoRoles.CargosLider"/>), activo en RRHH y que
/// aún no exista como líder activo en DBCallCenter.
/// </summary>
public class EmpleadoElegibleDto
{
    public long   NumeroDocumento { get; set; }
    public string NombreCompleto  { get; set; } = string.Empty;
    public string Cargo           { get; set; } = string.Empty;
    public string? Email          { get; set; }
}
