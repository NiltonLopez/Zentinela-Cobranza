namespace Zentinela.Application.DTOs.Virtualidad.IVR;

public class ValidarIVRResponse
{
    public int                 TotalFilas    { get; set; }
    public int                 TotalValidas  { get; set; }
    public int                 TotalErrores  { get; set; }
    public List<FilaIVRDto>    Preview       { get; set; } = [];
    public List<FilaIVRDto>    FilasValidas  { get; set; } = [];
    public List<AlertaIVRDto>  Alertas       { get; set; } = [];
}

public class FilaIVRDto
{
    public long     NumeroDocumento { get; set; }
    public string   NumeroTelefono  { get; set; } = string.Empty;
    public string   NombreCartera   { get; set; } = string.Empty;
    public int      LiderID         { get; set; }
    public int      CarteraID       { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    public bool     EsValida        { get; set; }
    public string?  ErrorMensaje    { get; set; }
}

public class AlertaIVRDto
{
    public string NumeroTelefono { get; set; } = string.Empty;
    public string NombreCartera  { get; set; } = string.Empty;
    public int    TotalIVR       { get; set; }
}
