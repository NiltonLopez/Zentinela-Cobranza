namespace Zentinela.Application.Config;

/// <summary>
/// Clasifica valores del campo UltimoPerfil (UltimoEstadoTitular) en las tres
/// categorías de gestión que usa el Comparador de Carteras.
///
/// MODIFICAR AQUÍ para agregar, quitar o mover perfiles entre categorías.
/// La comparación es case-insensitive.
///
/// Las listas incluyen tanto los códigos vigentes del sistema de tipificaciones
/// (ver <see cref="Zentinela.Application.Tipificaciones"/>) como strings legacy
/// que pueden aparecer en datos históricos.
/// </summary>
public static class PerfilClasificacion
{
    /// <summary>Titulares ya cancelados.</summary>
    public static readonly HashSet<string> PazYSalvo = new(StringComparer.OrdinalIgnoreCase)
    {
        "Paz y salvo",
        "Consignación por aplicar",
        // legacy
        "Pago Total",
        "Normalizado",
    };

    /// <summary>Titulares con gestión positiva (promesa, acuerdo, contacto efectivo).</summary>
    public static readonly HashSet<string> ContactoPositivo = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acuerdo de pago abono",
        "Acuerdo de pago total",
        "Realizo abono",
        "Recordar",
        "Incumplido",
        "No hubo acuerdo",
        "Potencial acuerdo",
        "Renuente",
        "No gestionar",
        // legacy
        "Contacto Positivo",
        "Promesa de Pago",
        "Acuerdo",
    };

    /// <summary>
    /// Todo lo que no esté en PazYSalvo ni ContactoPositivo se clasifica como
    /// Contacto Negativo (null, vacío, "No contacto", "Sin gestion", "PQR",
    /// "Fraude o suplantacion", "Mensaje en buzón", etc.).
    /// </summary>
    public static CategoriaGestion Clasificar(string? perfil)
    {
        if (string.IsNullOrWhiteSpace(perfil)) return CategoriaGestion.ContactoNegativo;
        if (PazYSalvo.Contains(perfil))        return CategoriaGestion.PazYSalvo;
        if (ContactoPositivo.Contains(perfil)) return CategoriaGestion.ContactoPositivo;
        return CategoriaGestion.ContactoNegativo;
    }

    /// <summary>
    /// Peso ordinal: PazYSalvo (2) &gt; ContactoPositivo (1) &gt; ContactoNegativo (0).
    /// El Comparador muestra un titular cuando su peso en otra cartera es estrictamente
    /// mayor al peso en la cartera seleccionada.
    /// </summary>
    public static int Peso(CategoriaGestion c) => c switch
    {
        CategoriaGestion.PazYSalvo        => 2,
        CategoriaGestion.ContactoPositivo => 1,
        _                                 => 0,
    };

    public static int Peso(string? perfil) => Peso(Clasificar(perfil));
}

public enum CategoriaGestion
{
    ContactoNegativo,
    ContactoPositivo,
    PazYSalvo,
}
