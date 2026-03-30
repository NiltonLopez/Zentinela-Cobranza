using Microsoft.EntityFrameworkCore;
using Zentinela.API.Application.Interfaces;
using Zentinela.API.Domain.Entities;
using Zentinela.API.Infrastructure.Data;

namespace Zentinela.API.Infrastructure.Repositories;

public class CarteraRepository : ICarteraRepository
{
    private readonly ZentinelaDbContext _context;

    public CarteraRepository(ZentinelaDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Cartera>> ObtenerPorLiderAsync(int liderID)
    {
        return await _context.Carteras
            .Where(c => c.LiderID == liderID)
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<Cartera>> ObtenerTodasAsync()
    {
        return await _context.Carteras
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }
}
