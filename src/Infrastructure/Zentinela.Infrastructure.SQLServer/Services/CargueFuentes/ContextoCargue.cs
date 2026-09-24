using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Estado compartido de una corrida del motor: definición, datos de apoyo y contadores de
/// mensajes. Los mensajes se escriben YA agrupables (sin "Línea N": la fila va en los
/// ejemplos del desglose) y se cuentan por grupo: con 1M de filas, una lista de strings por
/// fila serían cientos de MB para terminar mostrando 30 mensajes distintos.
/// </summary>
internal sealed class ContextoCargue
{
    private readonly Dictionary<string, int> _info = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _indices;

    // Fila que el motor está procesando: las advertencias que se emitan la usan de ejemplo.
    private int _lineaEnCurso;
    private long? _titularEnCurso;

    public ContextoCargue(DefinicionFuenteCargue definicion)
    {
        Definicion = definicion;
        Desglose = new DesgloseCargue(definicion);
        _indices = definicion.ColumnasDato
            .Select((c, i) => (c.Nombre, i))
            .ToDictionary(x => x.Nombre, x => x.i, StringComparer.OrdinalIgnoreCase);
    }

    public DefinicionFuenteCargue Definicion { get; }

    /// <summary>Composición de los chips del preview / resultado, con ejemplos (ver <see cref="DesgloseCargue"/>).</summary>
    public DesgloseCargue Desglose { get; }

    /// <summary>Cargada si la fuente tiene alguna columna Email.</summary>
    public ListaNegraCorreos? ListaNegraCorreos { get; set; }

    /// <summary>Cargada si la regla lo pide (<c>NecesitaPrefijos</c>).</summary>
    public PrefijosFijo? Prefijos { get; set; }

    /// <summary>Cargados si la fuente crea relaciones y el archivo trae cartera/líder.</summary>
    public ResolucionCarteraLider.IndicesResolucion? Indices { get; set; }

    public int TotalAdvertencias => Desglose.Advertencias.Total;

    // ── Acceso a columnas de dato por nombre (para las reglas) ─────────────

    public int IndiceDe(string columna) =>
        _indices.TryGetValue(columna, out var i)
            ? i
            : throw new ArgumentException($"La fuente {Definicion.Fuente} no tiene la columna '{columna}'.");

    public object? Get(FilaCargue fila, string columna) => fila.Datos[IndiceDe(columna)];

    public string? GetTexto(FilaCargue fila, string columna) => Get(fila, columna) as string;

    public void Set(FilaCargue fila, string columna, object? valor) => fila.Datos[IndiceDe(columna)] = valor;

    // ── Mensajes ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fila en curso (línea del archivo y documento, si ya se leyó): la usan de ejemplo las
    /// advertencias siguientes. El motor la fija antes de trabajar cada fila.
    /// </summary>
    public void EnFila(int linea, long? titularId)
    {
        _lineaEnCurso = linea;
        _titularEnCurso = titularId;
    }

    /// <summary>La fila entra con un dato anulado o corregido. <paramref name="valor"/>: lo que se anuló, para el ejemplo.</summary>
    public void Advertir(string mensaje, string? valor = null) =>
        Desglose.Advertencias.Sumar(mensaje, 1, _lineaEnCurso, _titularEnCurso, valor);

    /// <summary>La fila entra sin relación titular-cartera: advertencia + motivo para el chip "sin cartera/líder".</summary>
    public void SinRelacion(FilaCargue fila, string motivo, string? valor = null)
    {
        fila.MotivoSinRelacion = motivo;
        Advertir(motivo, valor);
    }

    /// <summary>Hecho informativo (no es un problema del archivo).</summary>
    public void Informar(string mensaje, int cantidad = 1)
    {
        if (cantidad > 0) _info[mensaje] = _info.TryGetValue(mensaje, out var actual) ? actual + cantidad : cantidad;
    }

    /// <summary>
    /// Excluye la fila si aún no tenía motivo: una fila cuenta UNA vez, con el primer motivo
    /// del orden fijo, para que Excluidas cuadre con las filas leídas.
    /// </summary>
    public void Excluir(FilaCargue fila, string motivo, string? valor = null)
    {
        if (fila.MotivoExclusion is not null) return;
        fila.MotivoExclusion = motivo;
        Desglose.Excluidas.Sumar(motivo, 1, fila.Linea, fila.TitularID, valor);
    }

    /// <summary>Exclusión de una fila que no llegó a ser <see cref="FilaCargue"/> (fila corrida, vacía).</summary>
    public void ExcluirCruda(string motivo, int linea = 0, string? valor = null) =>
        Desglose.Excluidas.Sumar(motivo, 1, linea, null, valor);

    public List<ErrorAgrupadoDto> Errores()      => Desglose.Excluidas.Agrupados();
    public List<ErrorAgrupadoDto> Advertencias() => Desglose.Advertencias.Agrupados();

    public List<ErrorAgrupadoDto> Info() =>
        _info.OrderByDescending(kv => kv.Value)
             .ThenBy(kv => kv.Key, StringComparer.Ordinal)
             .Take(CatalogoCargueFuentes.MaxMensajesAgrupados)
             .Select(kv => new ErrorAgrupadoDto { Mensaje = kv.Key, Cantidad = kv.Value })
             .ToList();
}
