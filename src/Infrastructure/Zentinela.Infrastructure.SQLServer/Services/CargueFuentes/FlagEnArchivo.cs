using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Lectura de una celda de columna <c>FlagDesdeArchivo</c> (Compensados: <c>Estado</c>). Catálogo
/// CERRADO en los dos sentidos: lo que no es claramente "encontrado" ni "no encontrado" no enciende
/// el flag y el motor lo avisa. El SP legacy daba flag 1 con cualquier valor no nulo, incluso 0
/// (la Auxiliar tenía <c>Estado int</c>, así que el proveedor mandaba un número); aquí 0 es "no".
/// </summary>
internal static class FlagEnArchivo
{
    public enum Lectura { NoEncontrado, Encontrado, NoReconocido }

    /// <summary>
    /// Marcas de "no encontrado" del proveedor, normalizadas (<see cref="ArchivoTabular.Normalizar"/>:
    /// sin espacios ni tildes). También las usa <c>ReglaCompensados</c> para anular 'NO SE ENCUENTRA'
    /// cuando llega como EPS o TipoAfiliado.
    /// </summary>
    public static readonly IReadOnlySet<string> MarcasNoEncontrado = new HashSet<string>(StringComparer.Ordinal)
    {
        "noseencuentra", "noencontrado", "noexiste", "noregistra", "sininformacion",
    };

    private static readonly HashSet<string> Si = new(StringComparer.Ordinal)
    {
        "1", "si", "s", "true", "verdadero", "encontrado", "compensado",
    };

    private static readonly HashSet<string> No = new(StringComparer.Ordinal)
    {
        "0", "no", "n", "false", "falso", "nocompensado", "null", "n/a", "na",
    };

    public static Lectura Leer(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return Lectura.NoEncontrado;
        // Excel/pandas: el 1 numérico puede llegar como '1.0' o '1,00'.
        var t = ArchivoTabular.Normalizar(NumeroEnTexto.QuitarDecimalNulo(bruto));
        if (Si.Contains(t)) return Lectura.Encontrado;
        if (No.Contains(t) || MarcasNoEncontrado.Contains(t)) return Lectura.NoEncontrado;
        return Lectura.NoReconocido;
    }
}
