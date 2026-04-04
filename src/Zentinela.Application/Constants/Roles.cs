namespace Zentinela.Application;

/// <summary>
/// Constantes de roles del sistema. Fuente única de verdad.
/// Evita magic strings dispersos en controllers, services y seeders.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Lider = "Líder";
    public const string Director = "Director";
    public const string Asesor = "Asesor";
}
