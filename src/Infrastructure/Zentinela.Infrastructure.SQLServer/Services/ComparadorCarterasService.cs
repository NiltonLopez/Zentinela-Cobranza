using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.Config;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Compara titular a titular la cartera seleccionada contra todas las demás carteras.
/// Identifica titulares que en la cartera seleccionada tienen Sin Gestión / Contacto Negativo
/// pero en otra cartera ya tienen Contacto Positivo o Ya Pagó — oportunidades de gestión.
/// </summary>
public class ComparadorCarterasService : IComparadorCarterasService
{
    private readonly CallCenterDbContext _db;

    public ComparadorCarterasService(CallCenterDbContext db)
    {
        _db = db;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<CarteraDropdownDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.Rol == Roles.Lider && contexto.LiderID.HasValue)
        {
            var lid = contexto.LiderID.Value;
            return await _db.LiderCarteras
                .Where(lc => lc.LiderID == lid)
                .Select(lc => new CarteraDropdownDto
                {
                    CarteraID     = lc.CarteraID,
                    NombreCartera = lc.Cartera.NombreCartera,
                })
                .Distinct()
                .OrderBy(c => c.NombreCartera)
                .AsNoTracking()
                .ToListAsync();
        }

        return await _db.LiderCarteras
            .Select(lc => new CarteraDropdownDto
            {
                CarteraID     = lc.CarteraID,
                NombreCartera = lc.Cartera.NombreCartera,
            })
            .Distinct()
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Comparador principal ──────────────────────────────────────────────────

    public async Task<ComparadorCarterasDto> CompararAsync(int carteraID)
    {
        // 1. Titulares de la cartera seleccionada que tienen reporte cargado
        var enSeleccionada = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID == carteraID
            select new { tc.TitularID, ue.UltimoPerfil, ue.NumeroMarcado, NombreCartera = c.NombreCartera }
        ).AsNoTracking().ToListAsync();

        var nombreSeleccionada = enSeleccionada.FirstOrDefault()?.NombreCartera ?? "";

        // Un titular puede tener varias filas (un agente por fila).
        // Tomamos la fila con el mejor perfil entre todos sus agentes.
        var mejorEnSeleccionada = enSeleccionada
            .GroupBy(x => x.TitularID)
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .ToList();

        var mejorPerfilPorTitular = mejorEnSeleccionada.ToDictionary(x => x.TitularID, x => x.UltimoPerfil);
        var numMarcadoSeleccionadaDict = mejorEnSeleccionada.ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        // Solo los que en su mejor perfil siguen siendo Sin Gestión
        var sinGestion = mejorPerfilPorTitular
            .Where(kv => PerfilClasificacion.Clasificar(kv.Value) == CategoriaGestion.SinGestion)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (sinGestion.Count == 0)
        {
            return new ComparadorCarterasDto
            {
                CarteraID                 = carteraID,
                NombreCarteraSeleccionada = nombreSeleccionada,
                TotalSinGestion           = 0,
                Comparaciones             = [],
            };
        }

        // 2. Buscar esos mismos titulares en otras carteras con estado positivo
        var titularIDs = sinGestion.Keys.ToList();

        var enOtras = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID != carteraID && titularIDs.Contains(tc.TitularID)
            select new
            {
                tc.TitularID,
                tc.CarteraID,
                NombreCartera      = c.NombreCartera,
                ue.UltimoPerfil,
                ue.NumeroMarcado,
                ue.SaldoTotal,
                ue.FechaUltimaGestion,
                ue.ValorPromesa,
                ue.MotivoNoPago,
                tc.Titular.Nombre,
            }
        ).AsNoTracking().ToListAsync();

        // 3. Deduplicar por (TitularID, CarteraID) tomando el mejor perfil por agente,
        //    luego filtrar solo los que tienen estado positivo en la otra cartera
        var coincidencias = enOtras
            .GroupBy(x => new { x.TitularID, x.CarteraID, x.NombreCartera })
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .Where(x => PerfilClasificacion.Clasificar(x.UltimoPerfil) != CategoriaGestion.SinGestion)
            .GroupBy(x => new { x.CarteraID, x.NombreCartera })
            .Select(g => new ComparadorVsCarteraDto
            {
                CarteraID          = g.Key.CarteraID,
                NombreCartera      = g.Key.NombreCartera,
                TotalCoincidencias = g.Count(),
                Preview            = g.Take(5).Select(x => MapTitular(x.TitularID, x.Nombre,
                    sinGestion.GetValueOrDefault(x.TitularID), x.UltimoPerfil, g.Key.NombreCartera,
                    numMarcadoSeleccionadaDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                    x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa)).ToList(),
            })
            .OrderByDescending(c => c.TotalCoincidencias)
            .ToList();

        return new ComparadorCarterasDto
        {
            CarteraID                 = carteraID,
            NombreCarteraSeleccionada = nombreSeleccionada,
            TotalSinGestion           = sinGestion.Count,
            Comparaciones             = coincidencias,
        };
    }

    // ── Datos completos para Excel ────────────────────────────────────────────

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerParaExcelAsync(
        int carteraID, int otraCarteraID)
    {
        // Titulares Sin Gestión en la cartera seleccionada
        var sinGestion = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            where tc.CarteraID == carteraID
            select new { tc.TitularID, ue.UltimoPerfil, ue.NumeroMarcado }
        ).AsNoTracking().ToListAsync();

        var mejorEnSel = sinGestion
            .GroupBy(x => x.TitularID)
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .ToList();

        var sinGestionDict = mejorEnSel
            .Where(x => PerfilClasificacion.Clasificar(x.UltimoPerfil) == CategoriaGestion.SinGestion)
            .ToDictionary(x => x.TitularID, x => x.UltimoPerfil);

        var numMarcadoSelDict = mejorEnSel
            .ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        if (sinGestionDict.Count == 0) return [];

        var titularIDs = sinGestionDict.Keys.ToList();

        var enOtra = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            where tc.CarteraID == otraCarteraID && titularIDs.Contains(tc.TitularID)
            select new
            {
                tc.TitularID,
                ue.UltimoPerfil,
                ue.NumeroMarcado,
                ue.SaldoTotal,
                ue.FechaUltimaGestion,
                ue.ValorPromesa,
                ue.MotivoNoPago,
                tc.Titular.Nombre,
            }
        ).AsNoTracking().ToListAsync();

        var nombreOtra = await _db.Carteras
            .Where(c => c.ID == otraCarteraID)
            .Select(c => c.NombreCartera)
            .FirstOrDefaultAsync() ?? $"Cartera {otraCarteraID}";

        return enOtra
            .GroupBy(x => x.TitularID)
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .Where(x => PerfilClasificacion.Clasificar(x.UltimoPerfil) != CategoriaGestion.SinGestion)
            .Select(x => MapTitular(x.TitularID, x.Nombre,
                sinGestionDict.GetValueOrDefault(x.TitularID), x.UltimoPerfil, nombreOtra,
                numMarcadoSelDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa))
            .ToList();
    }

    // ── Excel completo — todas las carteras ───────────────────────────────────

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerTodosParaExcelAsync(int carteraID)
    {
        // Reutiliza CompararAsync que ya tiene toda la lógica de deduplicación
        var resultado = await CompararAsync(carteraID);

        if (resultado.Comparaciones.Count == 0) return [];

        // Para el Excel completo necesitamos TODOS los titulares, no solo el preview de 5.
        // Repetimos la consulta sin límite agrupando por cartera.
        var sinGestionDict = resultado.Comparaciones
            .SelectMany(c => c.Preview)
            .GroupBy(t => t.NumeroDocumento)
            .ToDictionary(g => g.Key, g => g.First().PerfilSeleccionada);

        var titularIDs = sinGestionDict.Keys.ToList();

        var enOtras = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID != carteraID && titularIDs.Contains(tc.TitularID)
            select new
            {
                tc.TitularID,
                tc.CarteraID,
                NombreCartera      = c.NombreCartera,
                ue.UltimoPerfil,
                ue.NumeroMarcado,
                ue.SaldoTotal,
                ue.FechaUltimaGestion,
                ue.ValorPromesa,
                ue.MotivoNoPago,
                tc.Titular.Nombre,
            }
        ).AsNoTracking().ToListAsync();

        // También necesitamos los titulares Sin Gestión que no están en el preview
        // (tienen más de 5 en alguna sección). Re-consultamos sin límite.
        var sinGestionCompleto = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            where tc.CarteraID == carteraID
            select new { tc.TitularID, ue.UltimoPerfil, ue.NumeroMarcado }
        ).AsNoTracking().ToListAsync();

        var mejorEnSelFull = sinGestionCompleto
            .GroupBy(x => x.TitularID)
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .ToList();

        var sinGestionFull = mejorEnSelFull
            .Where(x => PerfilClasificacion.Clasificar(x.UltimoPerfil) == CategoriaGestion.SinGestion)
            .ToDictionary(x => x.TitularID, x => x.UltimoPerfil);

        var numMarcadoSelFullDict = mejorEnSelFull
            .ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        var allIDs = sinGestionFull.Keys.ToList();

        var todasEnOtras = await (
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID != carteraID && allIDs.Contains(tc.TitularID)
            select new
            {
                tc.TitularID,
                tc.CarteraID,
                NombreCartera      = c.NombreCartera,
                ue.UltimoPerfil,
                ue.NumeroMarcado,
                ue.SaldoTotal,
                ue.FechaUltimaGestion,
                ue.ValorPromesa,
                ue.MotivoNoPago,
                tc.Titular.Nombre,
            }
        ).AsNoTracking().ToListAsync();

        return todasEnOtras
            .GroupBy(x => new { x.TitularID, x.CarteraID, x.NombreCartera })
            .Select(g => g.OrderByDescending(x => PesoCategoria(PerfilClasificacion.Clasificar(x.UltimoPerfil))).First())
            .Where(x => PerfilClasificacion.Clasificar(x.UltimoPerfil) != CategoriaGestion.SinGestion)
            .OrderBy(x => x.NombreCartera)
            .ThenBy(x => x.Nombre)
            .Select(x => MapTitular(x.TitularID, x.Nombre,
                sinGestionFull.GetValueOrDefault(x.TitularID), x.UltimoPerfil, x.NombreCartera,
                numMarcadoSelFullDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa))
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int PesoCategoria(CategoriaGestion c) => c switch
    {
        CategoriaGestion.YaPago           => 2,
        CategoriaGestion.ContactoPositivo => 1,
        _                                 => 0,
    };

    private static TitularComparativoDto MapTitular(
        long titularID, string nombre,
        string? perfilSeleccionada, string? perfilOtra, string nombreOtraCartera,
        string? numeroMarcadoSeleccionada, string? numeroMarcadoOtra,
        decimal? saldoTotal, DateOnly? fechaUltimaGestion, decimal? valorPromesa)
        => new()
        {
            NumeroDocumento           = titularID,
            Nombre                    = nombre,
            PerfilSeleccionada        = perfilSeleccionada,
            PerfilOtraCartera         = perfilOtra,
            NombreOtraCartera         = nombreOtraCartera,
            NumeroMarcadoSeleccionada = numeroMarcadoSeleccionada,
            NumeroMarcadoOtra         = numeroMarcadoOtra,
            SaldoTotal                = saldoTotal,
            FechaUltimaGestion        = fechaUltimaGestion,
            ValorPromesa              = valorPromesa,
        };
}
