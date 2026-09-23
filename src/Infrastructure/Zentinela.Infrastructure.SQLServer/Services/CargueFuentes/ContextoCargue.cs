using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Estado compartido de una corrida del motor: definición, datos de apoyo y contadores de
/// mensajes. Los mensajes se escriben YA agrupables (sin "Línea N") y se cuentan en un
/// diccionario: con 1M de filas, una lista de strings por fila serían cientos de MB para
/// terminar mostrando 30 mensajes distintos.
/// </summary>
internal sealed class ContextoCargue
{
    private readonly Dictionary<string, int> _errores      = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _advertencias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _info         = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _indices;

    public ContextoCargue(DefinicionFuenteCargue definicion)
    {
        Definicion = definicion;
        _indices = definicion.ColumnasDato
            .Select((c, i) => (c.Nombre, i))
            .ToDictionary(x => x.Nombre, x => x.i, StringComparer.OrdinalIgnoreCase);
    }

    public DefinicionFuenteCargue Definicion { get; }

    /// <summary>Cargada si la fuente tiene alguna columna Email.</summary>
    public ListaNegraCorreos? ListaNegraCorreos { get; set; }

    /// <summary>Cargada si la regla lo pide (<c>NecesitaPrefijos</c>).</summary>
    public PrefijosFijo? Prefijos { get; set; }

    /// <summary>Cargados si la fuente crea relaciones y el archivo trae cartera/líder.</summary>
    public ResolucionCarteraLider.IndicesResolucion? Indices { get; set; }

    public int TotalAdvertencias => _advertencias.Values.Sum();

    // ── Acceso a columnas de dato por nombre (para las reglas) ─────────────

    public int IndiceDe(string columna) =>
        _indices.TryGetValue(columna, out var i)
            ? i
            : throw new ArgumentException($"La fuente {Definicion.Fuente} no tiene la columna '{columna}'.");

    public object? Get(FilaCargue fila, string columna) => fila.Datos[IndiceDe(columna)];

    public string? GetTexto(FilaCargue fila, string columna) => Get(fila, columna) as string;

    public void Set(FilaCargue fila, string columna, object? valor) => fila.Datos[IndiceDe(columna)] = valor;

    // ── Mensajes ──────────────────────────────────────────────────────────

    /// <summary>La fila entra con un dato anulado o corregido.</summary>
    public void Advertir(string mensaje) => Sumar(_advertencias, mensaje, 1);

    /// <summary>Hecho informativo (no es un problema del archivo).</summary>
    public void Informar(string mensaje, int cantidad = 1)
    {
        if (cantidad > 0) Sumar(_info, mensaje, cantidad);
    }

    /// <summary>
    /// Excluye la fila si aún no tenía motivo: una fila cuenta UNA vez, con el primer motivo
    /// del orden fijo, para que Excluidas cuadre con las filas leídas.
    /// </summary>
    public void Excluir(FilaCargue fila, string motivo)
    {
        if (fila.MotivoExclusion is not null) return;
        fila.MotivoExclusion = motivo;
        Sumar(_errores, motivo, 1);
    }

    /// <summary>Exclusión de una fila que no llegó a ser <see cref="FilaCargue"/> (fila corrida, vacía).</summary>
    public void ExcluirCruda(string motivo) => Sumar(_errores, motivo, 1);

    public List<ErrorAgrupadoDto> Errores()      => Agrupar(_errores);
    public List<ErrorAgrupadoDto> Advertencias() => Agrupar(_advertencias);
    public List<ErrorAgrupadoDto> Info()         => Agrupar(_info);

    private static void Sumar(Dictionary<string, int> d, string mensaje, int n) =>
        d[mensaje] = d.TryGetValue(mensaje, out var actual) ? actual + n : n;

    private static List<ErrorAgrupadoDto> Agrupar(Dictionary<string, int> d) =>
        d.OrderByDescending(kv => kv.Value)
         .ThenBy(kv => kv.Key, StringComparer.Ordinal)
         .Take(CatalogoCargueFuentes.MaxMensajesAgrupados)
         .Select(kv => new ErrorAgrupadoDto { Mensaje = kv.Key, Cantidad = kv.Value })
         .ToList();
}
