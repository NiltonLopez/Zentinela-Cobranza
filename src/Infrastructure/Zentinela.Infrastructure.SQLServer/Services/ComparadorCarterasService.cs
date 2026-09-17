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
/// Cada titular se clasifica en las categorías estandarizadas de PerfilClasificacion
/// (ADR-029): Ya pago > Promesa > Contacto directo > Tercero > Sin contacto >
/// Sin gestión > Otros. Se muestran como oportunidad los titulares cuya categoría
/// en otra cartera es estrictamente mejor que en la seleccionada. Los ya en
/// Ya pago (peso máximo) en la seleccionada quedan fuera de la base.
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
        // Líder sin LiderID: estado inconsistente, nunca debe ver el ramal de Admin (ver
        // el mismo guardarraíl en CalificadorCarteraService.ObtenerOpcionesAsync).
        if ((contexto.Rol is Roles.Lider or Roles.LiderExterno) && !contexto.LiderID.HasValue)
            return [];

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
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var (nombreSeleccionada, porTitular) =
            await ObtenerMejorPorTitularAsync(carteraID, liderID, hoy);

        // Desglose de toda la cartera por categoría (incluye Ya pago), de mejor a peor.
        var desglose = porTitular.Values
            .GroupBy(e => e.Categoria)
            .OrderByDescending(g => (int)g.Key)
            .Select(g => new CategoriaTotalDto
            {
                Categoria = PerfilClasificacion.Etiqueta(g.Key),
                Total     = g.Count(),
            })
            .ToList();

        var baseComparativo = FiltrarBase(porTitular);

        if (baseComparativo.Count == 0)
        {
            return new ComparadorCarterasDto
            {
                CarteraID                 = carteraID,
                NombreCarteraSeleccionada = nombreSeleccionada,
                TotalBaseComparativo      = 0,
                DesglosePorCategoria      = desglose,
                Comparaciones             = [],
            };
        }

        var coincidencias = await ObtenerCoincidenciasAsync(
            carteraID, liderID, baseComparativo, hoy, soloOtraCarteraID: null);

        var secciones = coincidencias
            .GroupBy(c => new { c.CarteraID, c.NombreCartera })
            .Select(g => new ComparadorVsCarteraDto
            {
                CarteraID          = g.Key.CarteraID,
                NombreCartera      = g.Key.NombreCartera,
                TotalCoincidencias = g.Count(),
                Preview            = g.Take(5).Select(c => c.Dto).ToList(),
            })
            .OrderByDescending(s => s.TotalCoincidencias)
            .ToList();

        return new ComparadorCarterasDto
        {
            CarteraID                 = carteraID,
            NombreCarteraSeleccionada = nombreSeleccionada,
            TotalBaseComparativo      = baseComparativo.Count,
            DesglosePorCategoria      = desglose,
            Comparaciones             = secciones,
        };
    }

    // ── Datos completos para Excel ────────────────────────────────────────────

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerParaExcelAsync(
        int carteraID, int? liderID, int otraCarteraID)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var (_, porTitular) = await ObtenerMejorPorTitularAsync(carteraID, liderID, hoy);

        var baseComparativo = FiltrarBase(porTitular);
        if (baseComparativo.Count == 0) return [];

        var coincidencias = await ObtenerCoincidenciasAsync(
            carteraID, liderID, baseComparativo, hoy, otraCarteraID);

        return coincidencias
            .OrderBy(c => c.Dto.Nombre)
            .Select(c => c.Dto)
            .ToList();
    }

    public async Task<IEnumerable<TitularComparativoDto>> ObtenerTodosParaExcelAsync(
        int carteraID, int? liderID)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var (_, porTitular) = await ObtenerMejorPorTitularAsync(carteraID, liderID, hoy);

        var baseComparativo = FiltrarBase(porTitular);
        if (baseComparativo.Count == 0) return [];

        var coincidencias = await ObtenerCoincidenciasAsync(
            carteraID, liderID, baseComparativo, hoy, soloOtraCarteraID: null);

        return coincidencias
            .OrderBy(c => c.NombreCartera)
            .ThenBy(c => c.Dto.Nombre)
            .Select(c => c.Dto)
            .ToList();
    }

    // ── Núcleo compartido ─────────────────────────────────────────────────────

    /// <summary>Texto mostrado cuando ninguna fila del histórico tiene teléfono útil.</summary>
    private const string SinTelefono = "No registra";

    /// <summary>Mejor estado por titular en la cartera seleccionada, ya clasificado.</summary>
    private sealed record EstadoSeleccionada(
        CategoriaPerfil Categoria, string? MejorPerfil, string NumeroMarcado);

    private sealed record Coincidencia(
        int CarteraID, string NombreCartera, TitularComparativoDto Dto);

    /// <summary>
    /// Mejor teléfono por (titular, cartera) según HistoricoEstadoTitular: clasifica
    /// cada fila histórica (no solo la del último cargue) y toma el NumeroMarcado de
    /// la de mejor categoría. Si no hay fila clasificable o el teléfono viene vacío,
    /// se devuelve <see cref="SinTelefono"/>.
    /// </summary>
    private async Task<Dictionary<(long TitularID, int CarteraID), string>> ObtenerMejoresTelefonosAsync(
        int carteraBaseID, int? liderID, DateOnly hoy,
        int? soloCarteraID = null, int? excluirCarteraID = null)
    {
        // SEMI-JOIN en vez de `ids.Contains(...)`. Antes se pasaba la lista completa de
        // titulares de la cartera (534.406 en MERCADEOS PERSONAL PROPIA), que EF Core 9
        // serializa como un parámetro JSON de ~5 MB y traduce a OPENJSON. Expresarlo
        // como subconsulta deja el filtro dentro de SQL Server, que resuelve con índice.
        var query = _db.HistoricoEstadosTitular.Where(h =>
            _db.TitularCarteras.Any(tc =>
                tc.CarteraID == carteraBaseID
                && tc.TitularID == h.TitularID
                && (liderID == null || tc.LiderID == liderID)));

        if (soloCarteraID.HasValue)
            query = query.Where(h => h.CarteraID == soloCarteraID.Value);
        if (excluirCarteraID.HasValue)
            query = query.Where(h => h.CarteraID != excluirCarteraID.Value);

        // PROYECCIÓN antes de materializar: la entidad tiene 14 columnas y aquí se usan
        // 8. Sin esto, EF traía las 14 de ~981.000 filas y el cuello pasaba a ser el
        // cliente consumiendo el stream (SQL esperaba en ASYNC_NETWORK_IO).
        var filas = await query
            .Select(h => new
            {
                h.TitularID,
                h.CarteraID,
                h.NumeroMarcado,
                h.MejorPerfil,
                h.UltimoPerfil,
                h.FechaPromesa,
                h.Rotacion,
                h.FechaUltimaGestion,
            })
            .AsNoTracking()
            .ToListAsync();

        return filas
            .Select(h => new
            {
                h.TitularID,
                h.CarteraID,
                h.NumeroMarcado,
                h.FechaUltimaGestion,
                Categoria = PerfilClasificacion.Clasificar(
                    h.MejorPerfil, h.UltimoPerfil, h.FechaPromesa, h.Rotacion, h.FechaUltimaGestion, hoy),
            })
            .GroupBy(h => (h.TitularID, h.CarteraID))
            .ToDictionary(
                g => g.Key,
                // Empate en categoría → gana la de FechaUltimaGestion más reciente.
                g => g.MaxBy(h => (h.Categoria, h.FechaUltimaGestion))!.NumeroMarcado is { Length: > 0 } tel
                    ? tel
                    : SinTelefono);
    }

    /// <summary>
    /// Titulares de la cartera seleccionada con reporte cargado, clasificados y
    /// deduplicados: un titular puede tener varias filas (una por agente) y se
    /// conserva la de mejor categoría. liderID acota a las asignaciones de ese
    /// líder (entrada "Cartera — Líder" del selector); null = cartera completa.
    /// </summary>
    private async Task<(string NombreCartera, Dictionary<long, EstadoSeleccionada> PorTitular)>
        ObtenerMejorPorTitularAsync(int carteraID, int? liderID, DateOnly hoy)
    {
        var query =
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID == carteraID
            select new
            {
                tc.TitularID,
                tc.LiderID,
                ue.MejorPerfil,
                ue.UltimoPerfil,
                ue.FechaPromesa,
                ue.Rotacion,
                ue.FechaUltimaGestion,
                NombreCartera = c.NombreCartera,
            };

        if (liderID.HasValue)
            query = query.Where(x => x.LiderID == liderID.Value);

        var filas = await query.AsNoTracking().ToListAsync();

        var nombreCartera = filas.FirstOrDefault()?.NombreCartera ?? "";

        var telefonos = await ObtenerMejoresTelefonosAsync(
            carteraID, liderID, hoy, soloCarteraID: carteraID);

        var porTitular = filas
            .Select(x => new
            {
                x.TitularID,
                x.MejorPerfil,
                Categoria = PerfilClasificacion.Clasificar(
                    x.MejorPerfil, x.UltimoPerfil, x.FechaPromesa, x.Rotacion, x.FechaUltimaGestion, hoy),
            })
            .GroupBy(x => x.TitularID)
            .Select(g => g.MaxBy(x => (int)x.Categoria)!)
            .ToDictionary(
                x => x.TitularID,
                x => new EstadoSeleccionada(
                    x.Categoria, x.MejorPerfil,
                    telefonos.GetValueOrDefault((x.TitularID, carteraID), SinTelefono)));

        return (nombreCartera, porTitular);
    }

    /// <summary>Base del comparativo: todo lo que no está en Ya pago (peso máximo).</summary>
    private static Dictionary<long, EstadoSeleccionada> FiltrarBase(
        Dictionary<long, EstadoSeleccionada> porTitular) =>
        porTitular
            .Where(kv => kv.Value.Categoria < CategoriaPerfil.YaPago)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Busca los titulares de la base en otras carteras, deduplica por
    /// (Titular, Cartera) con la mejor categoría y conserva solo los que allá
    /// tienen categoría estrictamente mejor que en la seleccionada.
    /// </summary>
    private async Task<List<Coincidencia>> ObtenerCoincidenciasAsync(
        int carteraID,
        int? liderIDBase,
        Dictionary<long, EstadoSeleccionada> baseComparativo,
        DateOnly hoy,
        int? soloOtraCarteraID)
    {
        // Mismo criterio que ObtenerMejoresTelefonosAsync: semi-join en SQL en lugar de
        // mandar las ~534k claves de baseComparativo como parámetro. Trae un superconjunto
        // (incluye titulares que la clasificación descartó por "Ya pago"); el filtro exacto
        // contra baseComparativo se aplica abajo con ContainsKey.
        var query =
            from tc in _db.TitularCarteras
            join ue in _db.UltimosEstadosTitular
                on new { tc.TitularID, tc.CarteraID } equals new { ue.TitularID, ue.CarteraID }
            join c in _db.Carteras on tc.CarteraID equals c.ID
            where tc.CarteraID != carteraID
                  && _db.TitularCarteras.Any(b =>
                         b.CarteraID == carteraID
                         && b.TitularID == tc.TitularID
                         && (liderIDBase == null || b.LiderID == liderIDBase))
            select new
            {
                tc.TitularID,
                tc.CarteraID,
                NombreCartera = c.NombreCartera,
                ue.MejorPerfil,
                ue.UltimoPerfil,
                ue.SaldoTotal,
                ue.FechaUltimaGestion,
                ue.FechaPromesa,
                ue.ValorPromesa,
                ue.Rotacion,
                tc.Titular.Nombre,
            };

        if (soloOtraCarteraID.HasValue)
            query = query.Where(x => x.CarteraID == soloOtraCarteraID.Value);

        var filas = await query.AsNoTracking().ToListAsync();

        var telefonos = soloOtraCarteraID.HasValue
            ? await ObtenerMejoresTelefonosAsync(carteraID, liderIDBase, hoy, soloCarteraID: soloOtraCarteraID.Value)
            : await ObtenerMejoresTelefonosAsync(carteraID, liderIDBase, hoy, excluirCarteraID: carteraID);

        return filas
            .Select(x => new
            {
                Fila = x,
                Categoria = PerfilClasificacion.Clasificar(
                    x.MejorPerfil, x.UltimoPerfil, x.FechaPromesa, x.Rotacion, x.FechaUltimaGestion, hoy),
            })
            .GroupBy(y => new { y.Fila.TitularID, y.Fila.CarteraID })
            .Select(g => g.MaxBy(y => (int)y.Categoria)!)
            // ContainsKey ANTES del indexador: el semi-join de arriba devuelve un
            // superconjunto de baseComparativo (incluye los "Ya pago" que FiltrarBase
            // descartó), y `baseComparativo[...]` sobre una clave ausente lanzaría
            // KeyNotFoundException. Este filtro restituye la semántica exacta.
            .Where(y => baseComparativo.ContainsKey(y.Fila.TitularID))
            .Where(y => y.Categoria > baseComparativo[y.Fila.TitularID].Categoria)
            .Select(y =>
            {
                var sel = baseComparativo[y.Fila.TitularID];
                return new Coincidencia(y.Fila.CarteraID, y.Fila.NombreCartera,
                    new TitularComparativoDto
                    {
                        NumeroDocumento           = y.Fila.TitularID,
                        Nombre                    = y.Fila.Nombre,
                        CategoriaSeleccionada     = PerfilClasificacion.Etiqueta(sel.Categoria),
                        PerfilSeleccionada        = sel.MejorPerfil,
                        CategoriaOtraCartera      = PerfilClasificacion.Etiqueta(y.Categoria),
                        PerfilOtraCartera         = y.Fila.MejorPerfil,
                        NombreOtraCartera         = y.Fila.NombreCartera,
                        NumeroMarcadoSeleccionada = sel.NumeroMarcado,
                        NumeroMarcadoOtra         = telefonos.GetValueOrDefault(
                            (y.Fila.TitularID, y.Fila.CarteraID), SinTelefono),
                        SaldoTotal                = y.Fila.SaldoTotal,
                        FechaUltimaGestion        = y.Fila.FechaUltimaGestion,
                        FechaPromesa              = y.Fila.FechaPromesa,
                        ValorPromesa              = y.Fila.ValorPromesa,
                    });
            })
            .ToList();
    }
}
