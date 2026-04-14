namespace Zentinela.Application.DTOs.Asignaciones;

public enum EstadoValidacion
{
    Ok,
    NoExiste,
    YaAsignado,
}

/// <summary>Resultado de validación para una fila del Excel de asignaciones.</summary>
public class FilaValidacionDto
{
    public string  NumeroDocumento { get; set; } = string.Empty;
    public string? Nombre          { get; set; }
    public EstadoValidacion Estado { get; set; }
    public string? Razon           { get; set; }
}

/// <summary>
/// Vista previa devuelta tras validar el Excel.
/// Filas contiene hasta 10 registros (muestra); TitularIDsValidos contiene la lista
/// completa de IDs a confirmar.
/// </summary>
public class PreviewAsignacionDto
{
    public List<FilaValidacionDto> Filas            { get; set; } = [];
    public int                     TotalValidos      { get; set; }
    public int                     TotalInvalidos    { get; set; }
    public List<long>              TitularIDsValidos { get; set; } = [];
}

/// <summary>Body del endpoint de confirmación.</summary>
public class ConfirmarCargueRequest
{
    public int       CarteraID  { get; set; }
    public int       LiderID    { get; set; }
    public List<long> TitularIDs { get; set; } = [];
}

/// <summary>Resultado del cargue definitivo a Titular_Cartera.</summary>
public class ResultadoCargueDto
{
    public int    Insertados { get; set; }
    public string Mensaje    { get; set; } = string.Empty;
}
