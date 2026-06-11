using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Metas;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class AsignacionMetasService : IAsignacionMetasService
{
    private readonly CallCenterDbContext      _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;

    public AsignacionMetasService(CallCenterDbContext callCenter, RecursosHumanosDbContext rrhh)
    {
        _callCenter = callCenter;
        _rrhh       = rrhh;
    }

    // ── Estado del mes actual ─────────────────────────────────────────────────

    public async Task<EstadoMetasMesDto> ObtenerEstadoMesActualAsync()
    {
        var hoy = DateTime.Today;
        var año = (short)hoy.Year;
        var mes = (byte)hoy.Month;

        var liderCarteras = await ObtenerFilasLiderCarteraAsync(año, mes);
        var agentes       = await ObtenerFilasAgenteAsync(año, mes);

        return new EstadoMetasMesDto { Año = año, Mes = mes, LiderCarteras = liderCarteras, Agentes = agentes };
    }

    private async Task<List<FilaMetaLiderCarteraDto>> ObtenerFilasLiderCarteraAsync(short año, byte mes)
    {
        // Metas administrativas: excluye relaciones externas (Lider_Cartera.EsExterna = 1)
        // e inactivadas (Lider_Cartera.Activo = 0, ADR-022).
        var combinaciones = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .Where(lc => lc.Lider.Activo && lc.Cartera.Activa && !lc.EsExterna && lc.Activo)
            .AsNoTracking()
            .ToListAsync();

        var metas = await _callCenter.MetaLiderCarteraMes
            .Where(m => m.Año == año && m.Mes == mes)
            .AsNoTracking()
            .ToListAsync();

        var asesores = await _callCenter.CarteraAgentes
            .GroupBy(ca => new { ca.LiderID, ca.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Total = g.Select(x => x.AgenteID).Distinct().Count() })
            .AsNoTracking()
            .ToListAsync();

        var asignaciones = await _callCenter.TitularCarteras
            .GroupBy(tc => new { tc.LiderID, tc.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Total = g.Count() })
            .AsNoTracking()
            .ToListAsync();

        return combinaciones
            .OrderBy(lc => lc.Cartera.NombreCartera)
            .ThenBy(lc => lc.Lider.Nombre)
            .Select(lc =>
            {
                var meta = metas.FirstOrDefault(m => m.LiderID == lc.LiderID && m.CarteraID == lc.CarteraID);
                return new FilaMetaLiderCarteraDto
                {
                    LiderID              = lc.LiderID,
                    CarteraID            = lc.CarteraID,
                    NombreLider          = lc.Lider.Nombre,
                    NombreCartera        = lc.Cartera.NombreCartera,
                    CapacidadActual      = asesores.FirstOrDefault(a => a.LiderID == lc.LiderID && a.CarteraID == lc.CarteraID)?.Total ?? 0,
                    CantidadAsignaciones = asignaciones.FirstOrDefault(a => a.LiderID == lc.LiderID && a.CarteraID == lc.CarteraID)?.Total ?? 0,
                    TipoMeta             = meta?.TipoMeta,
                    ValorMeta            = meta?.ValorMeta,
                    TieneMeta            = meta is not null,
                };
            })
            .ToList();
    }

    // Join en memoria por diseño: _callCenter y _rrhh son DbContexts distintos
    // (DBCallCenter y DBRecursosHumanos). EF Core no puede generar un JOIN SQL
    // entre ellos. NO mover a un .Where(... Contains ...) con join al query —
    // no funciona entre DbContexts y degrada a múltiples roundtrips. El patrón
    // correcto es: cargar IDs en batch desde cada contexto y cruzar en LINQ-to-Objects.
    private async Task<List<FilaMetaAgenteDto>> ObtenerFilasAgenteAsync(short año, byte mes)
    {
        // Agentes con asignaciones cuya relación (Líder, Cartera) NO es externa:
        // los asesores que trabajan exclusivamente bajo líderes externos no reciben
        // meta administrativa.
        var asignaciones = await _callCenter.CarteraAgentes
            .Where(ca => !_callCenter.LiderCarteras.Any(lc =>
                lc.LiderID == ca.LiderID && lc.CarteraID == ca.CarteraID && lc.EsExterna))
            .GroupBy(ca => ca.AgenteID)
            .Select(g => new
            {
                AgenteID  = g.Key,
                LiderID   = g.Select(x => x.LiderID).FirstOrDefault(),
                CarteraID = g.Select(x => x.CarteraID).FirstOrDefault(),
            })
            .AsNoTracking()
            .ToListAsync();

        var agenteIds = asignaciones.Select(a => (long)a.AgenteID).Distinct().ToList();

        var metas = await _callCenter.MetaAgentesMes
            .Where(m => m.Año == año && m.Mes == mes && agenteIds.Contains(m.AgenteID))
            .AsNoTracking()
            .ToListAsync();

        // Nombres desde RRHH, filtrando empleados retirados (EstadoEmpleado = false).
        // Si el agente no aparece en este diccionario, queda fuera del resultado:
        // el sistema no debe ofrecer asignar metas a personal retirado.
        var nombres = await _rrhh.Empleados
            .Where(e => e.EstadoEmpleado && agenteIds.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        var liderIds   = asignaciones.Select(a => a.LiderID).Distinct().ToList();
        var carteraIds = asignaciones.Select(a => a.CarteraID).Distinct().ToList();

        var lideres   = await _callCenter.LiderCarteras.Include(lc => lc.Lider).Include(lc => lc.Cartera)
            .Where(lc => liderIds.Contains(lc.LiderID) && carteraIds.Contains(lc.CarteraID))
            .AsNoTracking().ToListAsync();

        return asignaciones
            .Where(a => nombres.ContainsKey(a.AgenteID))
            .Select(a =>
            {
                var meta = metas.FirstOrDefault(m => m.AgenteID == a.AgenteID);
                var lc   = lideres.FirstOrDefault(l => l.LiderID == a.LiderID && l.CarteraID == a.CarteraID);
                return new FilaMetaAgenteDto
                {
                    AgenteID      = a.AgenteID,
                    NombreAgente  = nombres[a.AgenteID],
                    NombreCartera = lc?.Cartera.NombreCartera,
                    NombreLider   = lc?.Lider.Nombre,
                    TipoMeta      = meta?.TipoMeta,
                    ValorMeta     = meta?.ValorMeta,
                    TieneMeta     = meta is not null,
                };
            })
            .OrderBy(a => a.NombreAgente)
            .ToList();
    }

    // ── UPSERT por fila ───────────────────────────────────────────────────────

    public async Task<ResultadoMetaDto> GuardarMetaLiderAsync(GuardarMetaLiderRequest request)
    {
        var existing = await _callCenter.MetaLiderCarteraMes
            .FirstOrDefaultAsync(m => m.LiderID == request.LiderID && m.CarteraID == request.CarteraID
                                   && m.Año == request.Año && m.Mes == request.Mes);

        bool actualizado;
        if (existing is not null)
        {
            existing.TipoMeta  = request.TipoMeta;
            existing.ValorMeta = request.Valor;
            actualizado = true;
        }
        else
        {
            _callCenter.MetaLiderCarteraMes.Add(new MetaLiderCarteraMes
            {
                LiderID   = request.LiderID,
                CarteraID = request.CarteraID,
                Año       = request.Año,
                Mes       = request.Mes,
                TipoMeta  = request.TipoMeta,
                ValorMeta = request.Valor,
            });
            actualizado = false;
        }

        await _callCenter.SaveChangesAsync();

        return new ResultadoMetaDto
        {
            Actualizado = actualizado,
            Mensaje     = actualizado ? "Meta actualizada." : "Meta creada.",
        };
    }

    public async Task<ResultadoMetaDto> GuardarMetaAgenteAsync(GuardarMetaAgenteRequest request)
    {
        var existing = await _callCenter.MetaAgentesMes
            .FirstOrDefaultAsync(m => m.AgenteID == request.AgenteID && m.Año == request.Año && m.Mes == request.Mes);

        bool actualizado;
        if (existing is not null)
        {
            existing.TipoMeta  = request.TipoMeta;
            existing.ValorMeta = request.Valor;
            actualizado = true;
        }
        else
        {
            _callCenter.MetaAgentesMes.Add(new MetaAgenteMes
            {
                AgenteID  = request.AgenteID,
                Año       = request.Año,
                Mes       = request.Mes,
                TipoMeta  = request.TipoMeta,
                ValorMeta = request.Valor,
            });
            actualizado = false;
        }

        await _callCenter.SaveChangesAsync();

        return new ResultadoMetaDto
        {
            Actualizado = actualizado,
            Mensaje     = actualizado ? "Meta actualizada." : "Meta creada.",
        };
    }

}
