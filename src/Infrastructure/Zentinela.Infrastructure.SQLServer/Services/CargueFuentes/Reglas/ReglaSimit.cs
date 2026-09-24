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
///   llegaba con espacios de relleno. Aquí se normaliza (sin espacios, mayúsculas) antes de todo.</item>
/// <item>'ABC123' y 'abc 123' eran dos placas distintas para el DISTINCT. Aquí son la misma.</item>
/// <item>Una placa de más de 10 caracteres rompía el INSERT (varchar(10)); aquí se descarta con
///   advertencia y el resto de la fila sigue.</item>
/// </list>
/// </summary>
internal sealed class ReglaSimit : IReglaFuente
{
    private const string Placa = "Placa";

    public string Fuente => CatalogoCargueFuentes.Simit.Fuente;

    public IEnumerable<FilaCargue> Expandir(FilaCargue fila, ContextoCargue ctx)
    {
        var largo = ctx.Definicion.ColumnasDato[ctx.IndiceDe(Placa)].Largo;

        // Orden de aparición; sin repetidas (la misma placa en Placa1 y Placa3 es una).
        var placas = new List<string>();
        foreach (var bruto in fila.Extra)
        {
            var placa = PlacaVehiculo.Normalizar(CargueFuentesMotor.LimpiarTexto(bruto));
            if (placa is null) continue;
            if (placa.Length > largo)
            {
                ctx.Advertir($"{Placa}: placa de más de {largo} caracteres descartada");
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
