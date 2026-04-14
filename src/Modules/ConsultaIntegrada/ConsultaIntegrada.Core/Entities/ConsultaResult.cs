namespace ConsultaIntegrada.Core.Entities;

public class ConsultaResult
{
    public string Cedula { get; set; } = string.Empty;
    public DateTime FechaConsulta { get; set; }
    public TitularInfo? Titular { get; set; }
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Datos { get; set; } = new();
    public bool Encontrado { get; set; }
}

public class TitularInfo
{
    public string Nombre { get; set; } = string.Empty;
    public string NumeroDocumento { get; set; } = string.Empty;
    public DateOnly? FechaNacimiento { get; set; }
    public DateOnly? FechaExpedicionDocumento { get; set; }
    public List<CarteraInfo> Carteras { get; set; } = new();
}

public class CarteraInfo
{
    public string NombreCartera { get; set; } = string.Empty;
    public string NombreLider { get; set; } = string.Empty;
}
