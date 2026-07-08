using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.IVR;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Informe de IVR (solo lectura) sobre la tabla unificada HistoricoVirtualidad
/// (Canal = 'IVR'). El alta de solicitudes y resultados vive en
/// <see cref="HistoricoVirtualidadService"/> (módulo Administración).
/// </summary>
public class VirtualidadIVRService : IVirtualidadIVRService
{
    private const string CANAL = "IVR";
    private const int UMBRAL_ALERTA = 5; // >5 IVR a la misma línea

    private readonly CallCenterDbContext _db;

    public VirtualidadIVRService(CallCenterDbContext db) => _db = db;

    public async Task<InformeIVRDto> GetInformeAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        var queryEnriquecida =
            from s in _db.HistoricoVirtualidad
            where s.Canal == CANAL
                && s.FechaEnvio >= desde && s.FechaEnvio <= hasta
                && (!liderID.HasValue   || s.LiderID   == liderID.Value)
                && (!carteraID.HasValue || s.CarteraID == carteraID.Value)
            join c in _db.Carteras on s.CarteraID equals c.ID
            join l in _db.Lideres  on s.LiderID   equals l.ID
            select new
            {
                s.LiderID,
                NombreLider   = l.Nombre,
                s.CarteraID,
                c.NombreCartera,
                NumeroTelefono = s.Destino,
            };

        var porLiderCartera = await queryEnriquecida
            .GroupBy(x => new { x.LiderID, x.NombreLider, x.CarteraID, x.NombreCartera })
            .Select(g => new IVRPorLiderCartera
            {
                LiderID       = g.Key.LiderID,
                NombreLider   = g.Key.NombreLider,
                CarteraID     = g.Key.CarteraID,
                NombreCartera = g.Key.NombreCartera,
                Total         = g.Count(),
            })
            .OrderByDescending(g => g.Total)
            .ToListAsync();

        var alertas = await queryEnriquecida
            .GroupBy(x => new { x.NumeroTelefono, x.NombreCartera })
            .Where(g => g.Count() > UMBRAL_ALERTA)
            .Select(g => new AlertaIVRDto
            {
                NumeroTelefono = g.Key.NumeroTelefono,
                NombreCartera  = g.Key.NombreCartera,
                TotalIVR       = g.Count(),
            })
            .OrderByDescending(a => a.TotalIVR)
            .ToListAsync();

        return new InformeIVRDto
        {
            TotalIVR        = porLiderCartera.Sum(p => p.Total),
            PorLiderCartera = porLiderCartera,
            Alertas         = alertas,
        };
    }
}
