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

    /// <summary>
    /// CargoID de DBRecursosHumanos que pueden desempeñarse como líder de cobranza
    /// y, por tanto, ser dados de alta en la tabla Lider (Gestión de Líderes y Carteras):
    /// 2 = Líder de Cobranza, 7 = Coordinador de Cartera, 8 = Director Operativo.
    /// </summary>
    public static readonly IReadOnlySet<int> CargosLider = new HashSet<int> { 2, 7, 8 };

    /// <summary>True si el cargo puede ser dado de alta como líder de cobranza.</summary>
    public static bool EsCargoLider(int cargoID) => CargosLider.Contains(cargoID);
}
