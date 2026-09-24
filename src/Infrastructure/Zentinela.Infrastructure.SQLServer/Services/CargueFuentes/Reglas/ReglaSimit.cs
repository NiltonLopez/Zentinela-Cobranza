using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// SIMIT: traducción de <c>sp_ProcesarSIMITAuxiliar</c> (respaldo en docs/sql/legacy/).
///
/// Lo que se tradujo: el paso 6 del SP despivotaba Placa1..Placa15 con
/// <c>OUTER APPLY (VALUES ...)</c> + DISTINCT, y un titular sin ninguna placa entraba como UNA
/// fila con Placa NULL y EstadoEnSIMIT = 0. Aquí lo hace <see cref="Expandir"/> en C#, así
/// #Carga ya llega una fila por placa y el motor aplica lo genérico (dedup exacto por
/// titular + placa, refrescar / insertar, política de vacías: la fila sin placas de un titular
/// que sí tiene placas no entra, y sus vacías viejas se purgan).
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Deduplicaba SIMITAuxiliar por titular ANTES de despivotar (ROW_NUMBER por TitularID):
///   si el titular venía en dos filas, las placas de la otra fila se perdían. Aquí se unen.</item>
/// <item>El flag contaba '' y espacios como placa (IS NULL), y Placa15 era nchar(10): la placa
///   llegaba con espacios de relleno. Aquí se normaliza (sin espacios ni guiones, mayúsculas)
///   antes de todo.</item>
/// <item>'ABC123', 'abc 123' y 'ABC-123' eran placas distintas para el DISTINCT. Aquí son la misma.</item>
/// <item>Una placa de más de 10 caracteres rompía el INSERT (varchar(10)).</item>
/// <item>Guardaba como placa cualquier texto de la columna: 'PAZ Y SALVO', 'NO CARGO', 'SIN',
///   'XXXXXX'... (en dbo.SIMIT, 5.718 'PAZ Y SALV' y ~150 marcas más con EstadoEnSIMIT = 1: el
///   titular contaba como encontrado en SIMIT sin tener vehículo). Aquí lo que no cumple
///   <see cref="PlacaVehiculo.EsValida"/> (incluido lo de más de 10) se descarta con UNA
///   advertencia agrupada y el resto de la fila sigue; si no queda ninguna placa, el titular
///   entra como "sin placa" (Placa NULL, EstadoEnSIMIT 0), igual que con 0 placas.</item>
/// </list>
/// </summary>
internal sealed class ReglaSimit : IReglaFuente
{
    private const string Placa = "Placa";

    /// <summary>
    /// Mensaje FIJO, sin el valor: las advertencias se agrupan por texto exacto, y con el valor
    /// dentro el preview mostraría un grupo por cada marca en vez de uno con el conteo total.
    /// </summary>
    internal const string AvisoNoEsPlaca =
        Placa + ": valor que no es una placa descartado (p. ej. 'PAZ Y SALVO', 'NO CARGO', 'SIN', 'XXXXXX')";

    public string Fuente => CatalogoCargueFuentes.Simit.Fuente;

    public IEnumerable<FilaCargue> Expandir(FilaCargue fila, ContextoCargue ctx)
    {
        // Orden de aparición; sin repetidas (la misma placa en Placa1 y Placa3 es una).
        var placas = new List<string>();
        foreach (var bruto in fila.Extra)
        {
            var placa = PlacaVehiculo.Normalizar(CargueFuentesMotor.LimpiarTexto(bruto));
            if (placa is null) continue;
            // La regla de formato (5..7, A-Z/0-9, letra y dígito) cubre también el largo de la
            // columna (varchar(10)): lo que la cumple nunca rompe el INSERT.
            if (!PlacaVehiculo.EsValida(placa))
            {
                ctx.Advertir(AvisoNoEsPlaca, bruto?.Trim());
                continue;
            }
            if (!placas.Contains(placa, StringComparer.Ordinal)) placas.Add(placa);
        }

        // Sin placas: una sola fila con Placa NULL (el motor le calcula EstadoEnSIMIT = 0).
        if (placas.Count == 0)
        {
            ctx.Set(fila, Placa, null);
            return [fila];
        }

        var resultado = new List<FilaCargue>(placas.Count);
        foreach (var placa in placas)
        {
            var f = resultado.Count == 0 ? fila : fila.Clonar();
            ctx.Set(f, Placa, placa);
            resultado.Add(f);
        }
        return resultado;
    }
}
