namespace Zentinela.Application.Config;

/// <summary>
/// Clasifica valores del campo UltimoPerfil (UltimoEstadoTitular) en las tres
/// categorías de gestión que usa el Comparador de Carteras.
///
/// MODIFICAR AQUÍ para agregar, quitar o mover perfiles entre categorías.
/// La comparación es case-insensitive.
/// </summary>
public static class PerfilClasificacion
{
    /// <summary>Titulares que ya pagaron o tienen paz y salvo.</summary>
    public static readonly HashSet<string> YaPago = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pago Total",
        "Paz y Salvo",
        "Normalizado",
    };

    /// <summary>Titulares con gestión positiva (promesa, acuerdo, contacto efectivo).</summary>
    public static readonly HashSet<string> ContactoPositivo = new(StringComparer.OrdinalIgnoreCase)
    {
        "Contacto Positivo",
        "Promesa de Pago",
        "Acuerdo",
    };

    /// <summary>
    /// Todo lo que no esté en YaPago ni ContactoPositivo se clasifica como
    /// Sin Gestión / Contacto Negativo (null, vacío, "No Contesta", "Buzón", etc.).
    /// </summary>
    public static CategoriaGestion Clasificar(string? perfil)
    {
        if (string.IsNullOrWhiteSpace(perfil)) return CategoriaGestion.SinGestion;
        if (YaPago.Contains(perfil))           return CategoriaGestion.YaPago;
        if (ContactoPositivo.Contains(perfil)) return CategoriaGestion.ContactoPositivo;
        return CategoriaGestion.SinGestion;
    }
}

public enum CategoriaGestion
{
    SinGestion,
    ContactoPositivo,
    YaPago,
}
