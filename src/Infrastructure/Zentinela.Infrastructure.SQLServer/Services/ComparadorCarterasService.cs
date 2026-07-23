using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.Config;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Compara titular a titular la cartera seleccionada contra todas las demás carteras.
/// Identifica oportunidades de gestión: titulares cuyo perfil en otra cartera tiene
/// peso estrictamente mayor que en la seleccionada. Esto cubre dos casos:
///   1) Seleccionada = Contacto Negativo → muestra coincidencias en Contacto Positivo o Paz y Salvo.
///   2) Seleccionada = Contacto Positivo → muestra coincidencias en Paz y Salvo.
/// Los titulares ya en Paz y Salvo (peso máximo) en la seleccionada se excluyen de la base.
/// </summary>
public class ComparadorCarterasService : IComparadorCarterasService
{
    private readonly CallCenterDbContext _db;
    private readonly ICarteraRepository  _carteraRepo;

    public ComparadorCarterasService(CallCenterDbContext db, ICarteraRepository carteraRepo)
    {
        _db          = db;
        _carteraRepo = carteraRepo;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    // Misma estructura que CreadorZentinelaService.ObtenerOpcionesAsync: Líder → una
    // entrada por cartera propia. Admin/Director → combinaciones (Cartera, Líder)
    // más una entrada agregada "Cartera Completo" por cada cartera.
    public async Task<IEnumerable<OpcionCarteraDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.LiderID.HasValue
            && contexto.Rol is Roles.Lider or Roles.LiderExterno)
        {
            var carteras = await _carteraRepo.ObtenerPorLiderAsync(contexto.LiderID.Value);
            return carteras.Select(c => new OpcionCarteraDto
            {
                CarteraID = c.ID,
                LiderID   = contexto.LiderID,
                Etiqueta  = c.NombreCartera,
            }).ToList();
        }

        var combinaciones = await _carteraRepo.ObtenerCombinacionesActivasAsync();
        var resultado = new List<OpcionCarteraDto>();

        foreach (var grupo in combinaciones.GroupBy(lc => lc.CarteraID))
        {
            var nombreCartera = grupo.First().Cartera.NombreCartera;

            foreach (var lc in grupo)
            {
                resultado.Add(new OpcionCarteraDto
                {
                    CarteraID = lc.CarteraID,
                    LiderID   = lc.LiderID,
                    Etiqueta  = $"{nombreCartera} — {lc.Lider.Nombre}",
                });
            }

            resultado.Add(new OpcionCarteraDto
            {
                CarteraID = grupo.Key,
                LiderID   = null,
                Etiqueta  = $"{nombreCartera} Completo",
            });
        }

        return resultado;
    }

    public async Task<string> ObtenerNombreCarteraAsync(int carteraID) =>
        await _db.Carteras
            .Where(c => c.ID == carteraID)
            .Select(c => c.NombreCartera)
            .FirstOrDefaultAsync()
        ?? $"Cartera {carteraID}";

    // ── Comparador principal ──────────────────────────────────────────────────

    public async Task<ComparadorCarterasDto> CompararAsync(int carteraID, int? liderID)
    {
        // 1. Titulares de la cartera seleccionada que tienen reporte cargado.
        // liderID acota la base a las asignaciones de ese líder en la cartera
        // (entrada "Cartera — Líder" del selector); null = cartera completa.
        var enSeleccionadaQuery =
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID == carteraID
            select new { tc.TitularID, tc.LiderID, ue.UltimoPerfil, ue.NumeroMarcado, NombreCartera = c.NombreCartera };

        if (liderID.HasValue)
            enSeleccionadaQuery = enSeleccionadaQuery.Where(x => x.LiderID == liderID.Value);

        var enSeleccionada = await enSeleccionadaQuery.AsNoTracking().ToListAsync();

        var nombreSeleccionada = enSeleccionada.FirstOrDefault()?.NombreCartera ?? "";

        // Un titular puede tener varias filas (un agente por fila).
        // Tomamos la fila con el mejor perfil entre todos sus agentes.
        var mejorEnSeleccionada = enSeleccionada
            .GroupBy(x => x.TitularID)
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .ToList();

        var numMarcadoSeleccionadaDict = mejorEnSeleccionada.ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        // Base del comparativo: titulares cuyo mejor perfil en la seleccionada NO es
        // Paz y Salvo (peso < 2). Incluye Contacto Negativo y Contacto Positivo.
        var paraComparar = mejorEnSeleccionada
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) < 2)
            .ToDictionary(x => x.TitularID, x => x.UltimoPerfil);

        if (paraComparar.Count == 0)
        {
            return new ComparadorCarterasDto
            {
                CarteraID                 = carteraID,
                NombreCarteraSeleccionada = nombreSeleccionada,
                TotalSinGestion           = 0,
                Comparaciones             = [],
            };
        }

        var pesoEnSeleccionada = paraComparar
            .ToDictionary(kv => kv.Key, kv => PerfilClasificacion.Peso(kv.Value));

        // 2. Buscar esos mismos titulares en otras carteras
        var titularIDs = paraComparar.Keys.ToList();

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
        //    luego conservar solo los que en la otra cartera tienen peso mayor que en la seleccionada.
        var coincidencias = enOtras
            .GroupBy(x => new { x.TitularID, x.CarteraID, x.NombreCartera })
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) > pesoEnSeleccionada[x.TitularID])
            .GroupBy(x => new { x.CarteraID, x.NombreCartera })
            .Select(g => new ComparadorVsCarteraDto
            {
                CarteraID          = g.Key.CarteraID,
                NombreCartera      = g.Key.NombreCartera,
                TotalCoincidencias = g.Count(),
                Preview            = g.Take(5).Select(x => MapTitular(x.TitularID, x.Nombre,
                    paraComparar.GetValueOrDefault(x.TitularID), x.UltimoPerfil, g.Key.NombreCartera,
                    numMarcadoSeleccionadaDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                    x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa)).ToList(),
            })
            .OrderByDescending(c => c.TotalCoincidencias)
            .ToList();

        return new ComparadorCarterasDto
        {
            CarteraID                 = carteraID,
            NombreCarteraSeleccionada = nombreSeleccionada,
            TotalSinGestion           = paraComparar.Count,
            Comparaciones             = coincidencias,
        };
    }

    // ── Datos completos para Excel ────────────────────────────────────────────

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerParaExcelAsync(
        int carteraID, int? liderID, int otraCarteraID)
    {
        // Titulares de la cartera seleccionada que califican como base del comparativo
        // (peso < Paz y Salvo, es decir Negativo o Positivo).
        var enSeleccionadaQuery =
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            where tc.CarteraID == carteraID
            select new { tc.TitularID, tc.LiderID, ue.UltimoPerfil, ue.NumeroMarcado };

        if (liderID.HasValue)
            enSeleccionadaQuery = enSeleccionadaQuery.Where(x => x.LiderID == liderID.Value);

        var enSeleccionada = await enSeleccionadaQuery.AsNoTracking().ToListAsync();

        var mejorEnSel = enSeleccionada
            .GroupBy(x => x.TitularID)
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .ToList();

        var paraCompararDict = mejorEnSel
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) < 2)
            .ToDictionary(x => x.TitularID, x => x.UltimoPerfil);

        var numMarcadoSelDict = mejorEnSel
            .ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        if (paraCompararDict.Count == 0) return [];

        var pesoEnSel = paraCompararDict
            .ToDictionary(kv => kv.Key, kv => PerfilClasificacion.Peso(kv.Value));

        var titularIDs = paraCompararDict.Keys.ToList();

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
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) > pesoEnSel[x.TitularID])
            .Select(x => MapTitular(x.TitularID, x.Nombre,
                paraCompararDict.GetValueOrDefault(x.TitularID), x.UltimoPerfil, nombreOtra,
                numMarcadoSelDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa))
            .ToList();
    }

    // ── Excel completo — todas las carteras ───────────────────────────────────

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerTodosParaExcelAsync(int carteraID, int? liderID)
    {
        // Reutiliza CompararAsync que ya tiene toda la lógica de deduplicación
        var resultado = await CompararAsync(carteraID, liderID);

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

        // También necesitamos los titulares "para comparar" que no están en el preview
        // (tienen más de 5 en alguna sección). Re-consultamos sin límite.
        var enSeleccionadaCompletoQuery =
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            where tc.CarteraID == carteraID
            select new { tc.TitularID, tc.LiderID, ue.UltimoPerfil, ue.NumeroMarcado };

        if (liderID.HasValue)
            enSeleccionadaCompletoQuery = enSeleccionadaCompletoQuery.Where(x => x.LiderID == liderID.Value);

        var enSeleccionadaCompleto = await enSeleccionadaCompletoQuery.AsNoTracking().ToListAsync();

        var mejorEnSelFull = enSeleccionadaCompleto
            .GroupBy(x => x.TitularID)
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .ToList();

        var paraCompararFull = mejorEnSelFull
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) < 2)
            .ToDictionary(x => x.TitularID, x => x.UltimoPerfil);

        var pesoEnSelFull = paraCompararFull
            .ToDictionary(kv => kv.Key, kv => PerfilClasificacion.Peso(kv.Value));

        var numMarcadoSelFullDict = mejorEnSelFull
            .ToDictionary(x => x.TitularID, x => x.NumeroMarcado);

        var allIDs = paraCompararFull.Keys.ToList();

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
            .Select(g => g.MaxBy(x => PerfilClasificacion.Peso(x.UltimoPerfil))!)
            .Where(x => PerfilClasificacion.Peso(x.UltimoPerfil) > pesoEnSelFull[x.TitularID])
            .OrderBy(x => x.NombreCartera)
            .ThenBy(x => x.Nombre)
            .Select(x => MapTitular(x.TitularID, x.Nombre,
                paraCompararFull.GetValueOrDefault(x.TitularID), x.UltimoPerfil, x.NombreCartera,
                numMarcadoSelFullDict.GetValueOrDefault(x.TitularID), x.NumeroMarcado,
                x.SaldoTotal, x.FechaUltimaGestion, x.ValorPromesa))
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
