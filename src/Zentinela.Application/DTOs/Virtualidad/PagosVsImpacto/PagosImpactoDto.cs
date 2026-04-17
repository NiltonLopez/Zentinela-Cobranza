namespace Zentinela.Application.DTOs.Virtualidad.PagosVsImpacto;

public class PagosImpactoDto
{
    public List<PagoImpactoRow>     Filas          { get; set; } = [];
    public ResumenImpactoDto        Resumen        { get; set; } = new();
}

public class PagoImpactoRow
{
    public long    CedulaTitular        { get; set; }
    public DateOnly FechaPago           { get; set; }
    public decimal  ValorPagado         { get; set; }
    public bool     ImpactadoMensajeria { get; set; }
    public bool     ImpactadoIVR        { get; set; }
    public bool     ImpactadoLlamada    { get; set; }
}

public class ResumenImpactoDto
{
    public int TotalPagos             { get; set; }
    public int PagosConMensajeria     { get; set; }
    public int PagosConIVR            { get; set; }
    public int PagosConLlamada        { get; set; }
    public int PagosSinImpacto        { get; set; }
}
