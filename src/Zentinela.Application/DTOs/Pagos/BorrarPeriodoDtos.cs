namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Vista previa de cuántos pagos (y por cuánto dinero) se borrarían para un
/// período mensual + cartera dados. Se consulta ANTES de borrar, para que el
/// admin confirme con cifras reales en pantalla.
/// </summary>
public class ConteoPeriodoResponse
{
    public int     Cantidad         { get; set; }
    public decimal ValorPagadoTotal { get; set; }
    public decimal HonorariosTotal  { get; set; }
}

/// <summary>
/// Petición de borrado de un período mensual de pagos. CarteraID null = todas
/// las carteras. El período filtra por Pago.FechaPago (fecha del pago), no por
/// FechaRegistro (fecha en que se cargó al sistema).
/// </summary>
public class BorrarPeriodoRequest
{
    public int  Anio      { get; set; }
    public int  Mes       { get; set; }
    public int? CarteraID { get; set; }
}

/// <summary>
/// Una combinación (cartera, año, mes) para la que existe al menos un Pago.
/// Alimenta los 3 selects del tab "Eliminar pagos" (mes/año/cartera) para que
/// cada uno solo muestre valores que realmente tienen datos cargados, y para
/// que se filtren entre sí (elegir una cartera acota los meses/años disponibles
/// y viceversa) — mismo patrón que LiderCarteraVinculoDto en Dashboard.
/// </summary>
public class PeriodoPagoDto
{
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = string.Empty;
    public int    Anio          { get; set; }
    public int    Mes           { get; set; }
}
