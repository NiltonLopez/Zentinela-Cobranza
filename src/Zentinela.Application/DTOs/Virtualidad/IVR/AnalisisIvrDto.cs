namespace Zentinela.Application.DTOs.Virtualidad.IVR;

/// <summary>
/// Análisis del canal IVR sobre los resultados cargados (ADR-031).
///
/// Todos los conteos son de CLIENTES ÚNICOS por cartera: un cliente marcado varias veces
/// cuenta una sola vez, con su MEJOR desenlace según la jerarquía
/// (marcó 1 &gt; escuchó sin marcar &gt; sin marcación válida &gt; solo audio &gt; colgó).
///
/// No incluye "Solicitadas"/"Enviadas" (no existen en el modelo de carga única) ni
/// "Atendidos"/"% Atención", que salen del CDR y aún no se cargan en Zentinela.
/// </summary>
public class AnalisisIvrDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    /// <summary>Filas del período (cada reintento cuenta). Contexto, no base de los porcentajes.</summary>
    public int TotalMarcaciones { get; set; }
    /// <summary>Clientes únicos por cartera en el período.</summary>
    public int ClientesMarcados { get; set; }

    /// <summary>
    /// Clientes cuyas marcaciones no tienen resultado cargado (Estado NULL) — es el caso de
    /// las filas del flujo anterior de dos fases. Se EXCLUYEN de la base de los porcentajes:
    /// "no sabemos qué pasó" no es lo mismo que "no contestó", y meterlos juntos haría ver
    /// como contactabilidad pésima lo que en realidad es información faltante.
    /// </summary>
    public int SinResultado { get; set; }

    /// <summary>Base real de los porcentajes: ClientesMarcados − SinResultado.</summary>
    public int ClientesConResultado { get; set; }

    public int Contactados      { get; set; }
    public int EscucharonMenu   { get; set; }
    public int MarcaronOpcion1  { get; set; }

    /// <summary>Contactados / ClientesMarcados.</summary>
    public decimal Contactabilidad   { get; set; }
    /// <summary>MarcaronOpcion1 / Contactados.</summary>
    public decimal ConversionOpcion1 { get; set; }

    public List<EmbudoPasoIvrDto>  Embudo     { get; set; } = [];
    public List<DesenlaceIvrDto>   Desenlaces { get; set; } = [];
    public List<AnalisisIvrLiderDto> PorLider { get; set; } = [];

    public int TotalLideres  { get; set; }
    public int TotalCarteras { get; set; }

    /// <summary>Lectura en prosa del resultado global, generada a partir de las cifras.</summary>
    public string Apreciacion { get; set; } = string.Empty;
}

public class EmbudoPasoIvrDto
{
    public string  Etiqueta   { get; set; } = string.Empty;
    public int     Clientes   { get; set; }
    public decimal Porcentaje { get; set; }
    /// <summary>Sobre qué se calculó el porcentaje ("de marcados", "de contactó").</summary>
    public string  Base       { get; set; } = string.Empty;
}

public class DesenlaceIvrDto
{
    public int     Rango      { get; set; }
    public string  Desenlace  { get; set; } = string.Empty;
    public int     Clientes   { get; set; }
    /// <summary>Sobre contactados.</summary>
    public decimal Porcentaje { get; set; }
}

public class AnalisisIvrLiderDto
{
    public int    LiderID     { get; set; }
    public string NombreLider { get; set; } = string.Empty;

    public int TotalCarteras        { get; set; }
    public int ClientesMarcados     { get; set; }
    public int SinResultado         { get; set; }
    public int ClientesConResultado { get; set; }
    public int Contactados          { get; set; }
    public int EscucharonMenu       { get; set; }
    public int MarcaronOpcion1      { get; set; }

    public decimal Contactabilidad   { get; set; }
    public decimal ConversionOpcion1 { get; set; }

    public List<EmbudoPasoIvrDto>     Embudo     { get; set; } = [];
    public List<DesenlaceIvrDto>      Desenlaces { get; set; } = [];
    public List<AnalisisIvrCarteraDto> PorCartera { get; set; } = [];

    public string Apreciacion { get; set; } = string.Empty;
}

public class AnalisisIvrCarteraDto
{
    public int     CarteraID            { get; set; }
    public string  NombreCartera        { get; set; } = string.Empty;
    public int     ClientesMarcados     { get; set; }
    public int     SinResultado         { get; set; }
    public int     ClientesConResultado { get; set; }
    public int     Contactados          { get; set; }
    public int     MarcaronOpcion1      { get; set; }
    public decimal Contactabilidad      { get; set; }
}
