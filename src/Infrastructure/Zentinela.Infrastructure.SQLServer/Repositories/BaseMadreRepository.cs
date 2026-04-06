using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Repositories;

public class BaseMadreRepository : IBaseMadreRepository
{
    private readonly CallCenterDbContext _context;

    public BaseMadreRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    public async Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int    carteraID,
        int?   liderID,
        string? nombre,
        string? ultimoPerfil,
        string? mejorPerfil,
        string? departamento,
        string? municipio,
        bool?  tieneFechaPromesa,
        bool?  tieneSaldo,
        int?   limite)
    {
        // JOIN vw_BaseMadreBaseGeneral ⋈ Titular_Cartera
        // La vista no tiene CarteraID → se filtra uniendo por NumeroDocumento = TitularID
        var joined = _context.BaseMadreRows
            .Join(_context.TitularCarteras,
                  bm => bm.NumeroDocumento,
                  tc => tc.TitularID,
                  (bm, tc) => new { bm, tc })
            .Where(x => x.tc.CarteraID == carteraID);

        // Líder: solo sus titulares. Admin/Director: liderID null → sin restricción
        if (liderID.HasValue)
            joined = joined.Where(x => x.tc.LiderID == liderID.Value);

        // Proyectar a BaseMadreRow y eliminar duplicados
        // (un mismo titular puede estar en la cartera bajo múltiples líderes)
        var query = joined.Select(x => x.bm).Distinct();

        // Filtros opcionales
        if (!string.IsNullOrWhiteSpace(nombre))
            query = query.Where(b => b.Nombre != null && b.Nombre.Contains(nombre));

        if (!string.IsNullOrWhiteSpace(ultimoPerfil))
            query = query.Where(b => b.UltimoPerfil == ultimoPerfil);

        if (!string.IsNullOrWhiteSpace(mejorPerfil))
            query = query.Where(b => b.MejorPerfil == mejorPerfil);

        if (!string.IsNullOrWhiteSpace(departamento))
            query = query.Where(b => b.Departamento == departamento);

        if (!string.IsNullOrWhiteSpace(municipio))
            query = query.Where(b => b.Municipio == municipio);

        if (tieneFechaPromesa == true)
            query = query.Where(b => b.FechaPromesa != null && b.FechaPromesa != "");

        if (tieneSaldo == true)
            query = query.Where(b => b.Saldo != null && b.Saldo != "" && b.Saldo != "0");

        var total = await query.CountAsync();

        var filas = await (limite.HasValue ? query.Take(limite.Value) : query)
            .AsNoTracking()
            .ToListAsync();

        return (filas, total);
    }
}
