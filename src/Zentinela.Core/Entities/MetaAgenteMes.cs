namespace Zentinela.Core.Entities;

/// <summary>
/// Meta mensual de recaudo para un agente individual.
/// Permite calcular % de cumplimiento y proyecciones en el informe de agentes.
/// </summary>
public class MetaAgenteMes
{
    public int     MetaID    { get; set; }
    public long    AgenteID  { get; set; }
    public short   Año       { get; set; }
    public byte    Mes       { get; set; }
    public byte    TipoMeta  { get; set; }   // 0 = Recaudo, 1 = Honorarios
    public decimal ValorMeta { get; set; }
}
