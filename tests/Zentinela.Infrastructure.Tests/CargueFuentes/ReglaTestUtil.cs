using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>Arma filas y contextos de cargue para probar reglas sin BD.</summary>
internal static class ReglaTestUtil
{
    public static FilaCargue Fila(ContextoCargue ctx, params (string Columna, object? Valor)[] valores) =>
        FilaConExtra(ctx, [], valores);

    public static FilaCargue FilaConExtra(ContextoCargue ctx, string?[] extra, params (string Columna, object? Valor)[] valores)
    {
        var fila = new FilaCargue
        {
            Linea     = 2,
            TitularID = 1_234_567,
            Datos     = new object?[ctx.Definicion.ColumnasDato.Count],
            Extra     = extra,
        };
        foreach (var (columna, valor) in valores) ctx.Set(fila, columna, valor);
        return fila;
    }

    /// <summary>Cantidad de advertencias cuyo mensaje contiene <paramref name="texto"/>.</summary>
    public static int Avisos(ContextoCargue ctx, string texto) =>
        ctx.Advertencias().Where(a => a.Mensaje.Contains(texto, StringComparison.Ordinal)).Sum(a => a.Cantidad);

    public static ContextoCargue Contexto(DefinicionFuenteCargue def) => new(def);
}
