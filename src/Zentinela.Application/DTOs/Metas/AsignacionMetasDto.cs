namespace Zentinela.Application.DTOs.Metas;

public enum TipoMeta { Agente, LiderCartera }

// ── DTOs para el estado actual del mes (vista inline) ─────────────────────────

public class FilaMetaLiderCarteraDto
{
    public int      LiderID              { get; set; }
    public int      CarteraID            { get; set; }
    public string   NombreLider          { get; set; } = string.Empty;
    public string   NombreCartera        { get; set; } = string.Empty;
    public int      CapacidadActual      { get; set; }   // asesores asignados
    public int      CantidadAsignaciones { get; set; }   // titulares asignados
    public byte?    TipoMeta             { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal? ValorMeta            { get; set; }
    public bool     TieneMeta            { get; set; }
}

public class FilaMetaAgenteDto
{
    public long     AgenteID      { get; set; }
    public string   NombreAgente  { get; set; } = string.Empty;
    public string?  NombreCartera { get; set; }
    public string?  NombreLider   { get; set; }
    public byte?    TipoMeta      { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal? ValorMeta     { get; set; }
    public bool     TieneMeta     { get; set; }
}

public class EstadoMetasMesDto
{
    public int                          Año           { get; set; }
    public int                          Mes           { get; set; }
    public List<FilaMetaLiderCarteraDto> LiderCarteras { get; set; } = [];
    public List<FilaMetaAgenteDto>       Agentes       { get; set; } = [];
}

// ── Body del endpoint de guardado (UPSERT de una o varias filas) ──────────────

public class GuardarMetaLiderRequest
{
    public int     LiderID   { get; set; }
    public int     CarteraID { get; set; }
    public short   Año       { get; set; }
    public byte    Mes       { get; set; }
    public byte    TipoMeta  { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal Valor     { get; set; }
}

public class GuardarMetaAgenteRequest
{
    public long    AgenteID { get; set; }
    public short   Año      { get; set; }
    public byte    Mes      { get; set; }
    public byte    TipoMeta { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal Valor    { get; set; }
}

public class ResultadoMetaDto
{
    public bool   Actualizado { get; set; }
    public string Mensaje     { get; set; } = string.Empty;
}
