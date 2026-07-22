namespace Zentinela.Application;

/// <summary>
/// Clasificaciones de UltimoPerfil confirmadas en especificación funcional 2026-04-02.
/// Fuente única de verdad para cálculo de métricas de rendimiento.
/// </summary>
public static class Tipificaciones
{
    /// <summary>
    /// Denominador de contactabilidad y dividendo de efectividad.
    /// </summary>
    public static readonly HashSet<string> ContactoPositivo = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acuerdo de pago abono", "Acuerdo de pago total", "Incumplido", "No hubo acuerdo",
        "Paz y salvo", "PQR", "Renuente", "Realizo abono", "Recordar",
        "Potencial acuerdo", "No gestionar", "Consignación por aplicar",
        "Negociación vigente", "Caso especial",
        "Compromiso de pago total", "Compromiso de abono", "Abonando",
    };

    /// <summary>
    /// Numerador de efectividad.
    /// </summary>
    public static readonly HashSet<string> AcuerdoAbono = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acuerdo de pago abono", "Acuerdo de pago total", "Realizo abono",
        "Consignación por aplicar", "Paz y salvo",
        "Compromiso de pago total", "Compromiso de abono", "Abonando",
    };

    /// <summary>
    /// Filtro para el tablero de acuerdos vigentes del Dashboard Asesor.
    /// </summary>
    public static readonly HashSet<string> AcuerdosVigentes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acuerdo de pago abono", "Acuerdo de pago total",
    };

    public static readonly HashSet<string> NoContacto = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mensaje con tercero", "Mensaje en buzón", "Fallecido", "No contacto",
        "Mensaje contestadora/buzon", "Numero equivocado", "Ilocalizado",
        "Datos de localización", "Sin gestion", "Volver a llamar",
        "Fraude o suplantacion", "Fraude o suplantacion de identidad",
    };
}
