namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Cuentas con que el harness de pruebas (<c>tools/CargueHarness</c>) registra sus ejecuciones:
/// <c>harness-cargue</c> y las de cada grupo (<c>harness-cargue-salud</c>, <c>-empresas</c>,
/// <c>-otras</c>, <c>-desglose</c>, …). El harness corre contra la BD de PRODUCCIÓN y deja sus
/// filas en <c>CargueFuenteEjecucion</c> mientras dura la corrida (las borra al limpiar), así
/// que sin este filtro la lista "Cargues en curso y recientes" las mostraba como si alguien las
/// hubiera subido. Además, una ejecución del harness EN CURSO no tiene job en memoria de la API:
/// sin el filtro, <see cref="CargueFuentesOrquestador.ListarAsync"/> la tomaría por huérfana y
/// la cerraría como ERROR en plena corrida.
///
/// Los cargues reales guardan la cuenta del token (claim <c>userName</c>), que nunca empieza así.
/// Un grupo nuevo del harness debe usar este mismo prefijo para quedar oculto.
/// </summary>
internal static class UsuariosPruebaCargue
{
    /// <summary>Prefijo común de las cuentas del harness.</summary>
    public const string Prefijo = "harness-cargue";

    /// <summary>¿La ejecución la registró el harness de pruebas? (sin distinguir mayúsculas).</summary>
    public static bool EsDePrueba(string? usuario) =>
        usuario is not null && usuario.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase);
}
