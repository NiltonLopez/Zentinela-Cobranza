using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Repositories;

public class BaseMadreRepository : IBaseMadreRepository
{
    private readonly CallCenterDbContext _context;

    public BaseMadreRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    // ── Selectores de celular/email por fuente ────────────────────────────────
    // Acceden a los valores que la vista ya expone (TOP 1 más reciente).
    // Usar estos valores garantiza que la comparación sea idéntica a hacer
    // b.[Celular Camara] = b.[Celular Positiva] directamente en SQL.
    private static string? CelularDeFuente(BaseMadreRow b, string fuente) => fuente switch
    {
        "Cajacopi"      => b.CelularCajacopi,
        "Camara"        => b.CelularCamara,
        "SaludTotal"    => b.CelularSaludTotal,
        "Positiva"      => b.CelularPositiva,
        "Tigo"          => b.CelularTigo,
        "SOS"           => b.CelularSOS,
        "Claro"         => b.CelularClaro,
        _               => null,
    };

    private static string? EmailDeFuente(BaseMadreRow b, string fuente) => fuente switch
    {
        "Cajacopi"      => b.EmailCajacopi,
        "Camara"        => b.EmailCamara,
        "SaludTotal"    => b.EmailSaludTotal,
        "Positiva"      => b.EmailPositiva,
        "Tigo"          => b.EmailTigo,
        "Claro"         => b.EmailClaro,
        _               => null,
    };

    public async Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int          carteraID,
        int?         liderID,
        string?      ultimoPerfil,
        string?      mejorPerfil,
        bool?        promesasVigentes,
        int?         rotacionMaxima,
        decimal?     saldoMinimo,
        string?      tipoAfiliado,
        string?      estadoFosyga,
        List<string> fuentesCelular,
        List<string> coincidenciasCelular,
        List<string> fuentesEmail,
        List<string> coincidenciasEmail,
        int?         limite)
    {
        // vw_BaseMadre ya expone CarteraID y LiderID directamente
        var query = _context.BaseMadreRows
            .Where(bm => bm.CarteraID == carteraID);

        // Líder: solo sus titulares. Admin/Director: liderID null → sin restricción
        if (liderID.HasValue)
            query = query.Where(bm => bm.LiderID == liderID.Value);

        // ── Filtros traducibles a SQL ─────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(ultimoPerfil))
            query = query.Where(b => b.UltimoPerfil == ultimoPerfil);

        if (!string.IsNullOrWhiteSpace(mejorPerfil))
            query = query.Where(b => b.MejorPerfil == mejorPerfil);

        if (!string.IsNullOrWhiteSpace(tipoAfiliado))
            query = query.Where(b => b.TipoAfiliado == tipoAfiliado);

        if (!string.IsNullOrWhiteSpace(estadoFosyga))
            query = query.Where(b => b.EstadoEnFosyga == estadoFosyga);

        // Fuentes de celular: titular debe tener número en TODAS las fuentes elegidas
        foreach (var fuente in fuentesCelular)
        {
            query = fuente switch
            {
                "Cajacopi"      => query.Where(b => b.CelularCajacopi      != null && b.CelularCajacopi      != "" && b.CelularCajacopi      != "0"),
                "Camara"        => query.Where(b => b.CelularCamara        != null && b.CelularCamara        != "" && b.CelularCamara        != "0"),
                "SaludTotal"    => query.Where(b => b.CelularSaludTotal    != null && b.CelularSaludTotal    != "" && b.CelularSaludTotal    != "0"),
                "Positiva"      => query.Where(b => b.CelularPositiva      != null && b.CelularPositiva      != "" && b.CelularPositiva      != "0"),
                "Tigo"          => query.Where(b => b.CelularTigo          != null && b.CelularTigo          != "" && b.CelularTigo          != "0"),
                "SOS"           => query.Where(b => b.CelularSOS           != null && b.CelularSOS           != "" && b.CelularSOS           != "0"),
                _               => query,
            };
        }

        // Fuentes de email: titular debe tener email en TODAS las fuentes elegidas
        foreach (var fuente in fuentesEmail)
        {
            query = fuente switch
            {
                "Cajacopi"      => query.Where(b => b.EmailCajacopi      != null && b.EmailCajacopi      != "" && b.EmailCajacopi      != "0"),
                "Camara"        => query.Where(b => b.EmailCamara        != null && b.EmailCamara        != "" && b.EmailCamara        != "0"),
                "SaludTotal"    => query.Where(b => b.EmailSaludTotal    != null && b.EmailSaludTotal    != "" && b.EmailSaludTotal    != "0"),
                "Positiva"      => query.Where(b => b.EmailPositiva      != null && b.EmailPositiva      != "" && b.EmailPositiva      != "0"),
                "Tigo"          => query.Where(b => b.EmailTigo          != null && b.EmailTigo          != "" && b.EmailTigo          != "0"),
                _               => query,
            };
        }

        // ── Filtros en memoria ────────────────────────────────────────────────
        // FechaPromesa y Rotacion vienen como strings desde la vista.
        var todasLasFilas = await query.AsNoTracking().ToListAsync();

        if (saldoMinimo.HasValue)
            todasLasFilas = todasLasFilas
                .Where(b => decimal.TryParse(
                    b.Saldo,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var s) && s >= saldoMinimo.Value)
                .ToList();

        if (rotacionMaxima.HasValue)
            todasLasFilas = todasLasFilas
                .Where(b => int.TryParse(b.Rotacion, out var r) && r <= rotacionMaxima.Value)
                .ToList();

        if (promesasVigentes.HasValue)
        {
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            todasLasFilas = todasLasFilas
                .Where(b =>
                {
                    // FechaPromesa viene en formato dd/MM/yyyy desde la vista
                    var tieneVigente = DateOnly.TryParseExact(
                        b.FechaPromesa, "dd/MM/yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fp) && fp >= hoy;
                    return promesasVigentes.Value ? tieneVigente : !tieneVigente;
                })
                .ToList();
        }

        // Coincidencias: compara los valores que la vista expone (TOP 1 por fuente),
        // equivalente a b.[Celular X] = b.[Celular Y] AND b.[Celular X] != '0'.
        if (coincidenciasCelular.Count >= 2)
            todasLasFilas = todasLasFilas
                .Where(b =>
                {
                    var valores = coincidenciasCelular
                        .Select(f => CelularDeFuente(b, f))
                        .Where(v => !string.IsNullOrEmpty(v) && v != "0")
                        .ToList();
                    return valores.Count == coincidenciasCelular.Count
                        && valores.Distinct().Count() == 1;
                })
                .ToList();

        if (coincidenciasEmail.Count >= 2)
            todasLasFilas = todasLasFilas
                .Where(b =>
                {
                    var valores = coincidenciasEmail
                        .Select(f => EmailDeFuente(b, f))
                        .Where(v => !string.IsNullOrEmpty(v) && v != "0")
                        .ToList();
                    return valores.Count == coincidenciasEmail.Count
                        && valores.Distinct().Count() == 1;
                })
                .ToList();

        var total = todasLasFilas.Count;
        var filas = limite.HasValue ? todasLasFilas.Take(limite.Value) : todasLasFilas;

        return (filas, total);
    }

}
