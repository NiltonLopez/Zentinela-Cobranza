using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Calcula las métricas del Calificador de Cartera a partir de vw_BaseMadre.
/// La mayor parte del cómputo se hace en memoria tras un único ToListAsync().
/// </summary>
public class CalificadorCarteraService : ICalificadorCarteraService
{
    private static readonly string[] FuentesCelular =
        ["Cajacopi", "Camara", "SaludTotal", "DatosExternos", "Positiva", "Tigo", "SOS"];

    private readonly CallCenterDbContext _db;

    public CalificadorCarteraService(CallCenterDbContext db)
    {
        _db = db;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.Rol == Roles.Lider && contexto.LiderID.HasValue)
        {
            // Líder: solo sus combinaciones expuestas en vw_BaseMadre
            var lid = contexto.LiderID.Value;
            return await _db.BaseMadreRows
                .Where(b => b.LiderID == lid)
                .Select(b => new { b.CarteraID, b.LiderID })
                .Distinct()
                .Join(_db.Carteras,
                    b => b.CarteraID,
                    c => c.ID,
                    (b, c) => new { b.CarteraID, b.LiderID, c.NombreCartera })
                .Join(_db.Lideres,
                    x => x.LiderID,
                    l => l.ID,
                    (x, l) => new CarteraLiderDropdownDto
                    {
                        CarteraID = x.CarteraID,
                        LiderID   = x.LiderID,
                        Etiqueta  = x.NombreCartera + " — " + l.Nombre,
                    })
                .OrderBy(o => o.Etiqueta)
                .AsNoTracking()
                .ToListAsync();
        }

        // Director / Admin: todas las asignaciones activas de Lider_Cartera
        return await _db.LiderCarteras
            .Select(lc => new CarteraLiderDropdownDto
            {
                CarteraID = lc.CartID,
                LiderID   = lc.LiderID,
                Etiqueta  = lc.Cartera.NombreCartera + " — " + lc.Lider.Nombre,
            })
            .OrderBy(o => o.Etiqueta)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Métricas ──────────────────────────────────────────────────────────────

    public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID)
    {
        var filas = await _db.BaseMadreRows
            .Where(b => b.CarteraID == carteraID && b.LiderID == liderID)
            .AsNoTracking()
            .ToListAsync();

        var total = filas.Count;
        if (total == 0)
            return new CalificadorCarteraDto { TotalTitulares = 0 };

        // ── Bloque 2: gestión ─────────────────────────────────────────────────
        var gestionDisponible = filas.Any(b =>
            !string.IsNullOrEmpty(b.Saldo) && b.Saldo != "0");

        var saldos = filas
            .Where(b => decimal.TryParse(b.Saldo, NumberStyles.Any,
                CultureInfo.InvariantCulture, out _))
            .Select(b => decimal.Parse(b.Saldo!, NumberStyles.Any, CultureInfo.InvariantCulture))
            .ToList();

        var saldoTotal    = saldos.Sum();
        var saldoPromedio = saldos.Count > 0 ? saldos.Average() : 0m;

        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var titularesGestionados = filas.Count(b =>
            !string.IsNullOrEmpty(b.UltimaFechaGestion) && b.UltimaFechaGestion != "0");

        var titularesConAcuerdoVigente = filas.Count(b =>
            DateOnly.TryParseExact(b.FechaPromesa, "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fp) && fp >= hoy);

        var valorTotalPromesas = filas
            .Where(b => !string.IsNullOrEmpty(b.ValorPromesa) && b.ValorPromesa != "0"
                && decimal.TryParse(b.ValorPromesa, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out _))
            .Sum(b => decimal.Parse(b.ValorPromesa!, NumberStyles.Any, CultureInfo.InvariantCulture));

        // ── Bloque 3: rotación ───────────────────────────────────────────────
        var rotaciones = filas
            .Where(b => int.TryParse(b.Rotacion, out var r) && r > 0)
            .Select(b => int.Parse(b.Rotacion!))
            .ToList();

        var rotacionPromedio = rotaciones.Count > 0
            ? Math.Round((decimal)rotaciones.Average(), 1)
            : (decimal?)null;

        var desgloseRotacion = rotaciones
            .GroupBy(r => r)
            .Select(g => new DesglosePar
            {
                Etiqueta   = g.Key.ToString(),
                Cantidad   = g.Count(),
                Porcentaje = total > 0
                    ? Math.Round((decimal)g.Count() / total * 100, 1) : 0,
            })
            .OrderBy(d => int.Parse(d.Etiqueta))
            .ToList();

        // ── Bloque 4: tipificación último perfil ──────────────────────────────
        var desglosePerfil = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.UltimoPerfil) && b.UltimoPerfil != "0")
                 .Select(b => b.UltimoPerfil!),
            total);

        // ── Bloque 4: Fosyga ──────────────────────────────────────────────────
        var titularesConFosyga = filas.Count(b =>
            !string.IsNullOrEmpty(b.EstadoEnFosyga) && b.EstadoEnFosyga != "0");

        var desgloseTipoAfiliado = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.TipoAfiliado) && b.TipoAfiliado != "0")
                 .Select(b => b.TipoAfiliado!),
            total);

        // ── Bloque 5: régimen ─────────────────────────────────────────────────
        var desgloseRegimen = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.Regimen) && b.Regimen != "0")
                 .Select(b => b.Regimen!),
            total);

        // ── Bloque 6: EPS (top 10 + Otras) ───────────────────────────────────
        var desgloseEPS = DesgloseConOtras(
            filas.Where(b => !string.IsNullOrEmpty(b.EPS) && b.EPS != "0")
                 .Select(b => b.EPS!),
            total, top: 10);

        // ── Bloque 7: departamento ────────────────────────────────────────────
        var desgloseDepartamento = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.Departamento) && b.Departamento != "0")
                 .Select(b => b.Departamento!),
            total);

        // ── Bloque 8: contactabilidad ─────────────────────────────────────────
        var conCelular       = filas.Count(TieneCelular);
        var conEmail         = filas.Count(TieneEmail);
        var conCelularYEmail = filas.Count(b => TieneCelular(b) && TieneEmail(b));
        var sinContacto      = filas.Count(b => !TieneCelular(b) && !TieneEmail(b));

        var desgloseFuentesCelular = FuentesCelular
            .Select(f =>
            {
                var cantidad = filas.Count(b => TieneCelularFuente(b, f));
                return new DesglosePar
                {
                    Etiqueta   = f,
                    Cantidad   = cantidad,
                    Porcentaje = total > 0 ? Math.Round((decimal)cantidad / total * 100, 1) : 0,
                };
            })
            .Where(d => d.Cantidad > 0)
            .OrderByDescending(d => d.Cantidad)
            .ToList();

        // ── Bloque 9: edad Registraduría ─────────────────────────────────────
        // Solo aparecen personas mayores de 33 años; null o 0 = no aparece en la fuente.
        var conEdad = filas
            .Where(b => b.EdadRegistraduria.HasValue && b.EdadRegistraduria.Value > 0)
            .ToList();

        var conEdadRegistraduria      = conEdad.Count;
        var sinEdadRegistraduria      = total - conEdadRegistraduria;
        var edadPromedioRegistraduria = conEdad.Count > 0
            ? Math.Round((decimal)conEdad.Average(b => b.EdadRegistraduria!.Value), 1)
            : (decimal?)null;

        // ── Bloque 10: localización por fuente ───────────────────────────────
        // Para cada fuente: null = falta por buscar, "0"/"" = no encontró, valor = encontrado.
        var localizacion = new List<LocalizacionFuente>
        {
            Localizar("Cajacopi (cel)",    filas.Select(b => b.CelularCajacopi)),
            Localizar("Cámara",            filas.Select(b => b.CelularCamara)),
            Localizar("Salud Total",       filas.Select(b => b.CelularSaludTotal)),
            Localizar("Datos Externos",    filas.Select(b => b.CelularDatosExternos)),
            Localizar("Positiva (cel)",    filas.Select(b => b.CelularPositiva)),
            Localizar("Tigo",              filas.Select(b => b.CelularTigo)),
            Localizar("SOS",               filas.Select(b => b.CelularSOS)),
            Localizar("Fosyga",            filas.Select(b => b.EstadoEnFosyga)),
            Localizar("Compensados",       filas.Select(b => b.EPSEnCompensados)),
            Localizar("Sura (emp)",        filas.Select(b => b.EmpresaSura)),
            Localizar("Positiva (emp)",    filas.Select(b => b.EmpresaPositiva)),
            Localizar("RUES",              filas.Select(b => b.EmpresaRUES)),
            Localizar("SIMIT",             filas.Select(b => b.PlacaSIMIT)),
        };

        return new CalificadorCarteraDto
        {
            TotalTitulares             = total,
            GestionDisponible          = gestionDisponible,
            TitularesGestionados       = titularesGestionados,
            SaldoTotal                 = saldoTotal,
            SaldoPromedio              = saldoPromedio,
            TitularesConAcuerdoVigente = titularesConAcuerdoVigente,
            ValorTotalPromesas         = valorTotalPromesas,
            RotacionPromedio           = rotacionPromedio,
            DesgloseRotacion           = desgloseRotacion,
            DesglosePerfil             = desglosePerfil,
            TitularesConFosyga         = titularesConFosyga,
            DesgloseTipoAfiliado       = desgloseTipoAfiliado,
            DesgloseRegimen            = desgloseRegimen,
            DesgloseEPS                = desgloseEPS,
            DesgloseDepartamento       = desgloseDepartamento,
            ConCelular                 = conCelular,
            ConEmail                   = conEmail,
            ConCelularYEmail           = conCelularYEmail,
            SinContacto                = sinContacto,
            DesgloseFuentesCelular     = desgloseFuentesCelular,
            ConEdadRegistraduria       = conEdadRegistraduria,
            SinEdadRegistraduria       = sinEdadRegistraduria,
            EdadPromedioRegistraduria  = edadPromedioRegistraduria,
            Localizacion               = localizacion,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TieneCelular(BaseMadreRow b) =>
        TieneCelularFuente(b, "Cajacopi")   ||
        TieneCelularFuente(b, "Camara")     ||
        TieneCelularFuente(b, "SaludTotal") ||
        TieneCelularFuente(b, "DatosExternos") ||
        TieneCelularFuente(b, "Positiva")   ||
        TieneCelularFuente(b, "Tigo")       ||
        TieneCelularFuente(b, "SOS");

    private static bool TieneCelularFuente(BaseMadreRow b, string fuente) => fuente switch
    {
        "Cajacopi"      => !string.IsNullOrEmpty(b.CelularCajacopi)      && b.CelularCajacopi      != "0",
        "Camara"        => !string.IsNullOrEmpty(b.CelularCamara)        && b.CelularCamara        != "0",
        "SaludTotal"    => !string.IsNullOrEmpty(b.CelularSaludTotal)    && b.CelularSaludTotal    != "0",
        "DatosExternos" => !string.IsNullOrEmpty(b.CelularDatosExternos) && b.CelularDatosExternos != "0",
        "Positiva"      => !string.IsNullOrEmpty(b.CelularPositiva)      && b.CelularPositiva      != "0",
        "Tigo"          => !string.IsNullOrEmpty(b.CelularTigo)          && b.CelularTigo          != "0",
        "SOS"           => !string.IsNullOrEmpty(b.CelularSOS)           && b.CelularSOS           != "0",
        _               => false,
    };

    private static bool TieneEmail(BaseMadreRow b) =>
        (!string.IsNullOrEmpty(b.EmailCajacopi)      && b.EmailCajacopi      != "0") ||
        (!string.IsNullOrEmpty(b.EmailCamara)        && b.EmailCamara        != "0") ||
        (!string.IsNullOrEmpty(b.EmailSaludTotal)    && b.EmailSaludTotal    != "0") ||
        (!string.IsNullOrEmpty(b.EmailDatosExternos) && b.EmailDatosExternos != "0") ||
        (!string.IsNullOrEmpty(b.EmailPositiva)      && b.EmailPositiva      != "0") ||
        (!string.IsNullOrEmpty(b.EmailTigo)          && b.EmailTigo          != "0");

    /// <summary>Agrupa por etiqueta, calcula cantidad y % sobre total.</summary>
    private static List<DesglosePar> Desglosar(
        IEnumerable<string> valores, int total) =>
        valores
            .GroupBy(v => v)
            .Select(g => new DesglosePar
            {
                Etiqueta   = g.Key,
                Cantidad   = g.Count(),
                Porcentaje = total > 0
                    ? Math.Round((decimal)g.Count() / total * 100, 1) : 0,
            })
            .OrderByDescending(d => d.Cantidad)
            .ToList();

    /// <summary>
    /// Clasifica los valores de una fuente en 3 cubetas:
    /// null = falta por buscar, "0"/"" = no encontró, otro = encontrado.
    /// </summary>
    private static LocalizacionFuente Localizar(string fuente, IEnumerable<string?> valores)
    {
        var encontrados = 0;
        var noEncontrados = 0;
        var faltanPorBuscar = 0;

        foreach (var v in valores)
        {
            if (v is null)
                faltanPorBuscar++;
            else if (v == "" || v == "0")
                noEncontrados++;
            else
                encontrados++;
        }

        return new LocalizacionFuente
        {
            Fuente          = fuente,
            Encontrados     = encontrados,
            NoEncontrados   = noEncontrados,
            FaltanPorBuscar = faltanPorBuscar,
        };
    }

    /// <summary>
    /// Como Desglosar, pero limita a los top N y agrupa el resto en "(Otras)".
    /// </summary>
    private static List<DesglosePar> DesgloseConOtras(
        IEnumerable<string> valores, int total, int top)
    {
        var todos = Desglosar(valores, total);
        if (todos.Count <= top)
            return todos;

        var resultado = todos.Take(top).ToList();
        var otrasCount = todos.Skip(top).Sum(d => d.Cantidad);
        resultado.Add(new DesglosePar
        {
            Etiqueta   = "(Otras)",
            Cantidad   = otrasCount,
            Porcentaje = total > 0 ? Math.Round((decimal)otrasCount / total * 100, 1) : 0,
        });
        return resultado;
    }
}
