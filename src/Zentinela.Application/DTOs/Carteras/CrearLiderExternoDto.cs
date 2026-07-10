namespace Zentinela.Application.DTOs.Carteras;

/// <summary>
/// Solicitud para crear un líder EXTERNO (no empleado): una fila en Lider (DBCallCenter)
/// y su usuario Zentinela con rol Líder Externo. El documento/NIT es opcional — las
/// agencias externas pueden no tener cédula colombiana.
/// </summary>
public class CrearLiderExternoDto
{
    public string  Nombre          { get; set; } = string.Empty;
    public long?   NumeroDocumento { get; set; }
    public string? Email           { get; set; }
}
