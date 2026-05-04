namespace Zentinela.Application;

/// <summary>
/// Mapeo entre CargoID de DBRecursosHumanos y el rol en Zentinela.
/// Fuente: especificación funcional aprobada 2026-04-02.
/// </summary>
public static class CargoRoles
{
    private static readonly Dictionary<int, string> _mapa = new()
    {
        { 1, Roles.Asesor },
        { 2, Roles.Lider },
        { 3, Roles.Admin },
        { 8, Roles.Director },
        { 19, Roles.AuxiliarSistemas },
    };

    /// <summary>
    /// Retorna el rol Zentinela correspondiente al CargoID,
    /// o null si el cargo no tiene rol asignado en el sistema.
    /// </summary>
    public static string? ObtenerRol(int cargoID) =>
        _mapa.TryGetValue(cargoID, out var rol) ? rol : null;
}
