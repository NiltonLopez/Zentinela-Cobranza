namespace Zentinela.Application.DTOs.Virtualidad.IVR;

public class InformeIVRDto
{
    public int                     TotalSolicitados  { get; set; }
    public int                     TotalContestaron  { get; set; }
    public int                     TotalNoContestaron{ get; set; }
    public double                  PctContesto       { get; set; }
    public double                  PctNoContesto     { get; set; }
    public List<IVRPorSemana>      PorSemana         { get; set; } = [];
    public List<AlertaIVRDto>      Alertas           { get; set; } = [];
    public List<IVRVsGestionDto>   VsGestion         { get; set; } = [];
}

public class IVRPorSemana
{
    public int Semana          { get; set; }
    public int Anio            { get; set; }
    public int Total           { get; set; }
    public int Contestaron     { get; set; }
    public int NoContestaron   { get; set; }
}

public class AlertaIVRDto
{
    public string Celular      { get; set; } = string.Empty;
    public int?   CarteraID    { get; set; }
    public string NombreCartera{ get; set; } = string.Empty;
    public int    TotalIVR     { get; set; }
}

public class IVRVsGestionDto
{
    public long    TitularID          { get; set; }
    public string  Celular            { get; set; } = string.Empty;
    public string? Resultado          { get; set; }
    public string? UltimoPerfil       { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa     { get; set; }
    public DateOnly  FechaEnvioIVR    { get; set; }
}
