using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Repositories;

public class CarteraRepository : ICarteraRepository
{
    private readonly CallCenterDbContext _context;

    public CarteraRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Cartera>> ObtenerPorLiderAsync(int liderID)
    {
        return await _context.LiderCarteras
            .Where(lc => lc.LiderID == liderID && lc.Activo)
            .Select(lc => lc.Cartera)
            .Where(c => c.Activa)
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<Cartera>> ObtenerTodasAsync()
    {
        return await _context.Carteras
            .Where(c => c.Activa)
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<LiderCartera>> ObtenerCombinacionesActivasAsync()
    {
        return await _context.LiderCarteras
            .Include(lc => lc.Cartera)
            .Include(lc => lc.Lider)
            .Where(lc => lc.Cartera.Activa && lc.Activo)
            .OrderBy(lc => lc.Cartera.NombreCartera)
            .ThenBy(lc => lc.Lider.Nombre)
            .AsNoTracking()
            .ToListAsync();
    }
}
