namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Fuente sin reglas propias: todo lo resuelve el motor a partir de la definición. Existe
/// como clase (y no como "regla nula") para que el catálogo de reglas sea explícito: cada
/// fuente habilitada declara qué regla usa, aunque sea esta.
/// </summary>
internal sealed class ReglaGenerica(string fuente) : IReglaFuente
{
    public string Fuente { get; } = fuente;
}
