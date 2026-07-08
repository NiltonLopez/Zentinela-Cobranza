namespace Zentinela.Application.Utils;

public static class ProyeccionPromesasCalculator
{
    /// <summary>
    /// Descarta ValorPromesa si supera el techo absoluto configurado —
    /// protege la suma de valores corruptos de cargue (ver ADR-026).
    /// </summary>
    public static decimal ValorPromesaSano(decimal? valorPromesa, decimal techoAbsoluto)
    {
        var v = valorPromesa ?? 0m;
        if (v <= 0) return 0m;
        return v > techoAbsoluto ? 0m : v;
    }
}
