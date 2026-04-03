namespace Zentinela.Core.Entities;

public class Titular
{
    // PK natural del negocio — no es autoincremental
    public long NumeroDocumento { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public long? NitEmpresa { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    public DateOnly? FechaExpedicionDocumento { get; set; }
}
