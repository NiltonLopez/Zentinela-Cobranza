using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Metas;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class CargueMetasService : ICargueMetasService
{
    private readonly CallCenterDbContext      _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;

    public CargueMetasService(CallCenterDbContext callCenter, RecursosHumanosDbContext rrhh)
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
        var combinaciones = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .Where(lc => lc.Lider.Activo && lc.Cartera.Activa)
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
                    MetaRecaudo          = meta?.MetaRecaudo,
                    MetaHonorarios       = meta?.MetaHonorarios,
                    TieneMeta            = meta is not null,
                };
            })
            .ToList();
    }

    private async Task<List<FilaMetaAgenteDto>> ObtenerFilasAgenteAsync(short año, byte mes)
    {
        // Agentes activos: todos los que tienen al menos una asignación en Cartera_Agente
        var asignaciones = await _callCenter.CarteraAgentes
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

        // Nombres desde RRHH
        var nombres = await _rrhh.Empleados
            .Where(e => agenteIds.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        var liderIds   = asignaciones.Select(a => a.LiderID).Distinct().ToList();
        var carteraIds = asignaciones.Select(a => a.CarteraID).Distinct().ToList();

        var lideres   = await _callCenter.LiderCarteras.Include(lc => lc.Lider).Include(lc => lc.Cartera)
            .Where(lc => liderIds.Contains(lc.LiderID) && carteraIds.Contains(lc.CarteraID))
            .AsNoTracking().ToListAsync();

        return asignaciones
            .Select(a =>
            {
                var meta = metas.FirstOrDefault(m => m.AgenteID == a.AgenteID);
                var lc   = lideres.FirstOrDefault(l => l.LiderID == a.LiderID && l.CarteraID == a.CarteraID);
                return new FilaMetaAgenteDto
                {
                    AgenteID       = a.AgenteID,
                    NombreAgente   = nombres.TryGetValue(a.AgenteID, out var n) ? n : $"Agente {a.AgenteID}",
                    NombreCartera  = lc?.Cartera.NombreCartera,
                    NombreLider    = lc?.Lider.Nombre,
                    MetaRecaudo    = meta?.MetaRecaudo,
                    MetaHonorarios = meta?.MetaHonorarios,
                    TieneMeta      = meta is not null,
                };
            })
            .OrderBy(a => a.NombreAgente)
            .ToList();
    }

    // ── UPSERT por fila ───────────────────────────────────────────────────────

    public async Task<ResultadoMetaDto> GuardarMetaLiderAsync(GuardarMetaLiderRequest req)
    {
        var existing = await _callCenter.MetaLiderCarteraMes
            .FirstOrDefaultAsync(m => m.LiderID == req.LiderID && m.CarteraID == req.CarteraID
                                   && m.Año == req.Año && m.Mes == req.Mes);

        bool actualizado;
        if (existing is not null)
        {
            existing.MetaRecaudo    = req.MetaRecaudo;
            existing.MetaHonorarios = req.MetaHonorarios;
            actualizado = true;
        }
        else
        {
            _callCenter.MetaLiderCarteraMes.Add(new MetaLiderCarteraMes
            {
                LiderID        = req.LiderID,
                CarteraID      = req.CarteraID,
                Año            = req.Año,
                Mes            = req.Mes,
                MetaRecaudo    = req.MetaRecaudo,
                MetaHonorarios = req.MetaHonorarios,
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

    public async Task<ResultadoMetaDto> GuardarMetaAgenteAsync(GuardarMetaAgenteRequest req)
    {
        var existing = await _callCenter.MetaAgentesMes
            .FirstOrDefaultAsync(m => m.AgenteID == req.AgenteID && m.Año == req.Año && m.Mes == req.Mes);

        bool actualizado;
        if (existing is not null)
        {
            existing.MetaRecaudo    = req.MetaRecaudo;
            existing.MetaHonorarios = req.MetaHonorarios;
            actualizado = true;
        }
        else
        {
            _callCenter.MetaAgentesMes.Add(new MetaAgenteMes
            {
                AgenteID       = req.AgenteID,
                Año            = req.Año,
                Mes            = req.Mes,
                MetaRecaudo    = req.MetaRecaudo,
                MetaHonorarios = req.MetaHonorarios,
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
