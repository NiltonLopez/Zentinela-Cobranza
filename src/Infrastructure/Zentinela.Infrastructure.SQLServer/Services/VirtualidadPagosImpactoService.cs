using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.PagosVsImpacto;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class VirtualidadPagosImpactoService : IVirtualidadPagosImpactoService
{
    private readonly CallCenterDbContext _db;

    public VirtualidadPagosImpactoService(CallCenterDbContext db) => _db = db;

    public async Task<PagosImpactoDto> GetInformeAsync(DateOnly desde, DateOnly hasta, int? carteraID)
    {
        var pagosQuery = _db.Pagos
            .Where(p => p.FechaPago >= desde && p.FechaPago <= hasta);

        if (carteraID.HasValue)
            pagosQuery = pagosQuery.Where(p => p.CarteraID == carteraID.Value);

        var pagos = await pagosQuery
            .Select(p => new { p.CedulaTitular, p.FechaPago, p.ValorPagado })
            .ToListAsync();

        if (pagos.Count == 0)
            return new PagosImpactoDto();

        var cedulas = pagos.Select(p => p.CedulaTitular).Distinct().ToList();
        var fechaMin = pagos.Min(p => p.FechaPago);
        var fechaMax = pagos.Max(p => p.FechaPago);

        // Mensajería/SMS enviada antes del pago (tabla unificada HistoricoVirtualidad)
        var mensajeria = await _db.HistoricoVirtualidad
            .Where(s => s.Canal == "SMS" && cedulas.Contains(s.NumeroDocumento) && s.FechaEnvio < fechaMax)
            .Select(s => new { s.NumeroDocumento, s.FechaEnvio })
            .ToListAsync();

        // IVR enviado antes del pago (tabla unificada HistoricoVirtualidad)
        var ivr = await _db.HistoricoVirtualidad
            .Where(s => s.Canal == "IVR" && cedulas.Contains(s.NumeroDocumento) && s.FechaEnvio < fechaMax)
            .Select(s => new { TitularID = s.NumeroDocumento, s.FechaEnvio })
            .ToListAsync();

        // Llamadas perdidas registradas antes del pago
        var llamadas = await _db.LlamadasPerdidas
            .Where(l => cedulas.Contains(l.TitularID) && l.FechaCargue < fechaMax.ToDateTime(TimeOnly.MinValue))
            .Select(l => new { l.TitularID, l.FechaCargue })
            .ToListAsync();

        var filas = pagos.Select(p =>
        {
            var fechaPagoDate = p.FechaPago;
            return new PagoImpactoRow
            {
                CedulaTitular        = p.CedulaTitular,
                FechaPago            = p.FechaPago,
                ValorPagado          = p.ValorPagado,
                ImpactadoMensajeria  = mensajeria.Any(m => m.NumeroDocumento == p.CedulaTitular && m.FechaEnvio < fechaPagoDate),
                ImpactadoIVR         = ivr.Any(i => i.TitularID == p.CedulaTitular && i.FechaEnvio < fechaPagoDate),
                ImpactadoLlamada     = llamadas.Any(l => l.TitularID == p.CedulaTitular
                                           && DateOnly.FromDateTime(l.FechaCargue) < fechaPagoDate),
            };
        }).ToList();

        var resumen = new ResumenImpactoDto
        {
            TotalPagos         = filas.Count,
            PagosConMensajeria = filas.Count(f => f.ImpactadoMensajeria),
            PagosConIVR        = filas.Count(f => f.ImpactadoIVR),
            PagosConLlamada    = filas.Count(f => f.ImpactadoLlamada),
            PagosSinImpacto    = filas.Count(f => !f.ImpactadoMensajeria && !f.ImpactadoIVR && !f.ImpactadoLlamada),
        };

        return new PagosImpactoDto { Filas = filas, Resumen = resumen };
    }
}
