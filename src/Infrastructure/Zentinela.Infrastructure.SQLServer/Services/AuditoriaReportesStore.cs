using System.Collections.Concurrent;
using Zentinela.Application.DTOs.AuditoriaGestiones;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Almacén en memoria (Singleton) de los reportes de auditoría generados, COMPARTIDO
/// entre todos los usuarios con permiso. Cada reporte expira a las 48 h.
/// NO persiste en BD: se pierde si el proceso del backend se reinicia (aceptado para on-premise).
/// </summary>
public sealed class AuditoriaReportesStore : IDisposable
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(48);

    private sealed record Reporte(AnalizarAuditoriaResponse Datos, DateTime GeneradoUtc);

    private readonly ConcurrentDictionary<(int CarteraID, DateOnly Dia), Reporte> _reportes = new();
    private readonly Timer _barrido;

    public AuditoriaReportesStore()
    {
        // Barre cada 6 h eliminando lo vencido (> 48 h). Primer barrido a las 6 h.
        _barrido = new Timer(_ => Purgar(), null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
    }

    public void Guardar(int carteraID, DateOnly dia, AnalizarAuditoriaResponse datos) =>
        _reportes[(carteraID, dia)] = new Reporte(datos, DateTime.UtcNow);

    /// <summary>Devuelve el reporte si existe y sigue vigente (&lt; 48 h); si no, null.</summary>
    public AnalizarAuditoriaResponse? TryObtener(int carteraID, DateOnly dia)
    {
        if (_reportes.TryGetValue((carteraID, dia), out var r))
        {
            if (DateTime.UtcNow - r.GeneradoUtc <= Vigencia) return r.Datos;
            _reportes.TryRemove((carteraID, dia), out _);   // vencido
        }
        return null;
    }

    /// <summary>Lista los reportes vigentes (metadatos) para mostrarlos como disponibles.</summary>
    public IReadOnlyList<ReporteAuditoriaInfo> Listar()
    {
        Purgar();
        return _reportes
            .Select(kv => new ReporteAuditoriaInfo(kv.Key.CarteraID, kv.Key.Dia, kv.Value.GeneradoUtc))
            .OrderByDescending(x => x.GeneradoUtc)
            .ToList();
    }

    private void Purgar()
    {
        var limite = DateTime.UtcNow - Vigencia;
        foreach (var kv in _reportes)
            if (kv.Value.GeneradoUtc < limite) _reportes.TryRemove(kv.Key, out _);
    }

    public void Dispose() => _barrido.Dispose();
}
