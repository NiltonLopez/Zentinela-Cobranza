using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestión administrativa de relaciones Líder_Cartera en DBCallCenter.
/// Permite listar, crear y eliminar vínculos entre líderes y carteras,
/// así como crear carteras nuevas cuando aún no existen en el sistema.
/// </summary>
public class GestionCarterasService : IGestionCarterasService
{
    private readonly CallCenterDbContext _context;

    public GestionCarterasService(CallCenterDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<RelacionLiderCarteraDto>> ListarRelacionesAsync()
    {
        // Subconsulta correlacionada para contar asignaciones de Titular_Cartera por relación.
        // Se ejecuta en SQL Server (no en memoria) gracias al Select proyectado.
        return await _context.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .OrderBy(lc => lc.Cartera.NombreCartera)
            .ThenBy(lc => lc.Lider.Nombre)
            .Select(lc => new RelacionLiderCarteraDto
            {
                LiderID           = lc.LiderID,
                CarteraID         = lc.CarteraID,
                NombreLider       = lc.Lider.Nombre,
                NombreCartera     = lc.Cartera.NombreCartera,
                TotalAsignaciones = _context.TitularCarteras
                    .Count(tc => tc.LiderID == lc.LiderID && tc.CarteraID == lc.CarteraID),
                TotalAsesores     = _context.CarteraAgentes
                    .Count(ca => ca.LiderID == lc.LiderID && ca.CarteraID == lc.CarteraID),
            })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<LiderDropdownDto>> ListarLideresAsync()
    {
        return await _context.Lideres
            .Where(l => l.Activo)
            .OrderBy(l => l.Nombre)
            .Select(l => new LiderDropdownDto { ID = l.ID, Nombre = l.Nombre })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<CarteraDto>> BuscarCarterasAsync(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Enumerable.Empty<CarteraDto>();

        return await _context.Carteras
            .Where(c => c.NombreCartera.Contains(q))
            .OrderBy(c => c.NombreCartera)
            .Take(10)
            .Select(c => new CarteraDto { ID = c.ID, NombreCartera = c.NombreCartera })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<RelacionLiderCarteraDto> CrearRelacionAsync(CrearRelacionDto dto)
    {
        int carteraID;

        // Rama 1: el usuario seleccionó una cartera existente desde el autocompletado.
        // Solo se crea la relación Lider_Cartera; la cartera ya existe en BD.
        // Rama 2: el usuario escribió un nombre nuevo; se crea la Cartera primero
        // y luego la relación. Se valida unicidad de nombre para evitar duplicados.
        if (dto.CarteraID.HasValue)
        {
            // Usar cartera existente
            carteraID = dto.CarteraID.Value;
            var existe = await _context.Carteras.AnyAsync(c => c.ID == carteraID);
            if (!existe)
                throw new InvalidOperationException("La cartera seleccionada no existe.");
        }
        else if (!string.IsNullOrWhiteSpace(dto.NombreCartera))
        {
            // Crear nueva cartera — verificar que no exista ya con ese nombre
            var nombreDuplicado = await _context.Carteras
                .AnyAsync(c => c.NombreCartera == dto.NombreCartera);
            if (nombreDuplicado)
                throw new InvalidOperationException($"Ya existe una cartera con el nombre '{dto.NombreCartera}'.");

            var nextID = (await _context.Carteras.MaxAsync(c => (int?)c.ID) ?? 0) + 1;
            var nuevaCartera = new Cartera
            {
                ID            = nextID,
                NombreCartera = dto.NombreCartera.Trim(),
                Activa        = true,
            };
            _context.Carteras.Add(nuevaCartera);
            await _context.SaveChangesAsync();
            carteraID = nuevaCartera.ID;
        }
        else
        {
            throw new InvalidOperationException("Debe indicar una cartera existente o un nombre para la nueva cartera.");
        }

        // Verificar que la relación Lider_Cartera no exista ya
        var relacionExiste = await _context.LiderCarteras
            .AnyAsync(lc => lc.LiderID == dto.LiderID && lc.CarteraID == carteraID);
        if (relacionExiste)
            throw new InvalidOperationException("Esta relación ya existe.");

        var lider = await _context.Lideres.FindAsync(dto.LiderID)
            ?? throw new InvalidOperationException("El líder seleccionado no existe.");

        var cartera = await _context.Carteras.FindAsync(carteraID)
            ?? throw new InvalidOperationException("La cartera no existe.");

        _context.LiderCarteras.Add(new LiderCartera { LiderID = dto.LiderID, CarteraID = carteraID });
        await _context.SaveChangesAsync();

        return new RelacionLiderCarteraDto
        {
            LiderID           = dto.LiderID,
            CarteraID         = carteraID,
            NombreLider       = lider.Nombre,
            NombreCartera     = cartera.NombreCartera,
            TotalAsignaciones = 0,
        };
    }

    public async Task EliminarRelacionAsync(int liderID, int carteraID)
    {
        var relacion = await _context.LiderCarteras
            .FirstOrDefaultAsync(lc => lc.LiderID == liderID && lc.CarteraID == carteraID)
            ?? throw new InvalidOperationException("La relación no existe.");

        // Pre-validación: Lider_Cartera tiene FK entrantes desde varias tablas.
        // Si existe data dependiente (asesores, pagos, metas, mensajería, IVR), SQL
        // rechaza el DELETE. Aquí lo detectamos antes y devolvemos un mensaje claro
        // listando exactamente qué bloquea, en vez de un error genérico de FK.
        var asesores       = await _context.CarteraAgentes
                                .CountAsync(ca => ca.LiderID == liderID && ca.CarteraID == carteraID);
        var pagos          = await _context.Pagos
                                .CountAsync(p => p.LiderID == liderID && p.CarteraID == carteraID);
        var metas          = await _context.MetaLiderCarteraMes
                                .CountAsync(m => m.LiderID == liderID && m.CarteraID == carteraID);
        var mensajeria     = await _context.MensajeriaEnviadas
                                .CountAsync(m => m.LiderID == liderID && m.CarteraID == carteraID);
        var ivr            = await _context.IVREnviados
                                .CountAsync(i => i.LiderID == liderID && i.CarteraID == carteraID);

        var bloqueantes = new List<string>();
        if (asesores   > 0) bloqueantes.Add($"{asesores} asesor(es) asignado(s)");
        if (pagos      > 0) bloqueantes.Add($"{pagos} pago(s) registrado(s)");
        if (metas      > 0) bloqueantes.Add($"{metas} meta(s) mensual(es)");
        if (mensajeria > 0) bloqueantes.Add($"{mensajeria} envío(s) de mensajería");
        if (ivr        > 0) bloqueantes.Add($"{ivr} envío(s) de IVR");

        if (bloqueantes.Count > 0)
        {
            throw new InvalidOperationException(
                $"No se puede eliminar la relación porque tiene: {string.Join(", ", bloqueantes)}. " +
                "Reasigna o elimina esos registros primero.");
        }

        // Sin dependientes bloqueantes — eliminamos Titular_Cartera (FK no bloqueante,
        // siempre se limpia con la relación) y luego el Lider_Cartera.
        await _context.TitularCarteras
            .Where(tc => tc.LiderID == liderID && tc.CarteraID == carteraID)
            .ExecuteDeleteAsync();

        _context.LiderCarteras.Remove(relacion);
        await _context.SaveChangesAsync();
    }
}
