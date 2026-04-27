namespace Zentinela.Application.DTOs.Virtualidad.Mensajeria;

public class ValidarMensajeriaResponse
{
    public int                      TotalFilas    { get; set; }
    public int                      TotalValidas  { get; set; }
    public int                      TotalErrores  { get; set; }
    public List<FilaMensajeriaDto>  Preview       { get; set; } = [];
    public List<FilaMensajeriaDto>  FilasValidas  { get; set; } = [];
    public List<AlertaMensajeriaDto> Alertas      { get; set; } = [];
}

public class FilaMensajeriaDto
{
    public long     NumeroDocumento { get; set; }
    public string   NumeroTelefono  { get; set; } = string.Empty;
    // NombreCartera se mantiene en el DTO de preview/UI (resuelto desde el selector
    // o por JOIN), pero NO se persiste — la BD lo obtiene siempre con JOIN a Cartera.
    public string   NombreCartera   { get; set; } = string.Empty;
    public int      LiderID         { get; set; }
    public int      CarteraID       { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    public bool     EsValida        { get; set; }
    public string?  ErrorMensaje    { get; set; }
}

public class AlertaMensajeriaDto
{
    public string NumeroTelefono  { get; set; } = string.Empty;
    public string NombreCartera   { get; set; } = string.Empty;
    public int    TotalMensajes   { get; set; }
}
