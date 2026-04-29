namespace Zentinela.Application.DTOs.Consultas;

public class RastreoLineaDto
{
    public string                       NumeroBuscado      { get; set; } = string.Empty;
    public int                          TotalApariciones   { get; set; }
    public int                          TitularesDistintos { get; set; }
    public List<RastreoTablaCard>       PorTabla           { get; set; } = [];
    public List<RastreoTitularResumen>  Titulares          { get; set; } = [];
}

public class RastreoTablaCard
{
    public string Tabla        { get; set; } = string.Empty;
    public string Etiqueta     { get; set; } = string.Empty;
    public int    Apariciones  { get; set; }
    public List<RastreoTitularEnTabla> Titulares { get; set; } = [];
}

public class RastreoTitularEnTabla
{
    public long   NumeroDocumento { get; set; }
    public string Nombre          { get; set; } = string.Empty;
    public int    Apariciones     { get; set; }
}

public class RastreoTitularResumen
{
    public long          NumeroDocumento     { get; set; }
    public string        Nombre              { get; set; } = string.Empty;
    public int           TablasDondeAparece  { get; set; }
    public int           TotalApariciones    { get; set; }
    public List<string>  Tablas              { get; set; } = [];
}
