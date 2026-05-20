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
    public const string AuxiliarSistemas = "Auxiliar de Sistemas";

    // Roles externos: líderes/asesores que no pertenecen a la empresa.
    // Ven solo módulos operativos (Cartera para LiderExterno, Consulta Integrada
    // para AsesorExterno). Quedan excluidos de informes, metas y dashboards admin.
    public const string LiderExterno  = "Líder Externo";
    public const string AsesorExterno = "Asesor Externo";
}
