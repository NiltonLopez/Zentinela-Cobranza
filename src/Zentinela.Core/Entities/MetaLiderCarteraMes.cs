namespace Zentinela.Core.Entities;

/// <summary>
/// Meta mensual de recaudo para la combinación líder-cartera.
/// Permite calcular % de cumplimiento, proyección de cierre y "debería llevar hoy".
/// </summary>
public class MetaLiderCarteraMes
{
    public int     MetaID    { get; set; }
    public int     LiderID   { get; set; }
    public int     CarteraID { get; set; }
    public short   Año       { get; set; }
    public byte    Mes       { get; set; }
    public byte    TipoMeta  { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal ValorMeta { get; set; }
}
