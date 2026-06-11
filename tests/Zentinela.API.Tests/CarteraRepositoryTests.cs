using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Repositories;

namespace Zentinela.API.Tests;

public class CarteraRepositoryTests
{
    private const int LiderUno = 1;
    private const int LiderDos = 2;

    private static DbContextOptions<CallCenterDbContext> Opciones() =>
        new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    // Semilla base: 2 carteras activas del lider 1, 1 del lider 2
    private static async Task SeedBase(CallCenterDbContext ctx)
    {
        var cA = new Cartera { ID = 1, NombreCartera = "Cartera A", Activa = true };
        var cB = new Cartera { ID = 2, NombreCartera = "Cartera B", Activa = true };
        var cC = new Cartera { ID = 3, NombreCartera = "Cartera C", Activa = true };

        ctx.Carteras.AddRange(cA, cB, cC);

        ctx.LiderCarteras.AddRange(
            new LiderCartera { LiderID = LiderUno, CarteraID = 1 },
            new LiderCartera { LiderID = LiderUno, CarteraID = 2 },
            new LiderCartera { LiderID = LiderDos, CarteraID = 3 }
        );
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_DevuelveSoloCarterasDelLider()
    {
        var opts = Opciones();
        using (var ctx = new CallCenterDbContext(opts)) await SeedBase(ctx);

        using var ctxR = new CallCenterDbContext(opts);
        var repo      = new CarteraRepository(ctxR);
        var resultado = await repo.ObtenerPorLiderAsync(LiderUno);

        Assert.Equal(2, resultado.Count());
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_ExcluyeRelacionesInactivas()
    {
        // ADR-022: una relación retirada (Activo = false) no debe aparecer en lo operativo,
        // aunque su cartera siga activa. El líder 1 tiene 2 carteras: una de ellas inactivada.
        var opts = Opciones();
        using (var ctx = new CallCenterDbContext(opts))
        {
            ctx.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Cartera A", Activa = true },
                new Cartera { ID = 2, NombreCartera = "Cartera B", Activa = true }
            );
            ctx.LiderCarteras.AddRange(
                new LiderCartera { LiderID = LiderUno, CarteraID = 1, Activo = true },
                new LiderCartera { LiderID = LiderUno, CarteraID = 2, Activo = false }
            );
            await ctx.SaveChangesAsync();
        }

        using var ctxR = new CallCenterDbContext(opts);
        var repo       = new CarteraRepository(ctxR);
        var resultado  = (await repo.ObtenerPorLiderAsync(LiderUno)).ToList();

        Assert.Single(resultado);
        Assert.Equal("Cartera A", resultado[0].NombreCartera);
    }

    [Fact]
    public async Task ObtenerCombinacionesActivasAsync_ExcluyeRelacionesInactivas()
    {
        // La relación inactivada se excluye de las combinaciones operativas (ADR-022).
        var opts = Opciones();
        using (var ctx = new CallCenterDbContext(opts))
        {
            ctx.Lideres.AddRange(
                new Lider { ID = LiderUno, Nombre = "Líder Uno", Activo = true },
                new Lider { ID = LiderDos, Nombre = "Líder Dos", Activo = true }
            );
            ctx.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Cartera A", Activa = true },
                new Cartera { ID = 2, NombreCartera = "Cartera B", Activa = true }
            );
            ctx.LiderCarteras.AddRange(
                new LiderCartera { LiderID = LiderUno, CarteraID = 1, Activo = true },
                new LiderCartera { LiderID = LiderDos, CarteraID = 2, Activo = false }
            );
            await ctx.SaveChangesAsync();
        }

        using var ctxR = new CallCenterDbContext(opts);
        var repo       = new CarteraRepository(ctxR);
        var resultado  = (await repo.ObtenerCombinacionesActivasAsync()).ToList();

        Assert.Single(resultado);
        Assert.Equal(LiderUno, resultado[0].LiderID);
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_CuandoNoHayCarteras_DevuelveListaVacia()
    {
        var opts = Opciones();
        using var ctx = new CallCenterDbContext(opts);
        var repo      = new CarteraRepository(ctx);
        var resultado = await repo.ObtenerPorLiderAsync(LiderUno);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodasAsync_DevuelveTodasLasCarterasActivas()
    {
        var opts = Opciones();
        using (var ctx = new CallCenterDbContext(opts)) await SeedBase(ctx);

        using var ctxR = new CallCenterDbContext(opts);
        var repo      = new CarteraRepository(ctxR);
        var resultado = await repo.ObtenerTodasAsync();

        Assert.Equal(3, resultado.Count());
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_ResultadoOrdenadoPorNombre()
    {
        var opts = Opciones();
        using (var ctx = new CallCenterDbContext(opts))
        {
            ctx.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Zeta",  Activa = true },
                new Cartera { ID = 2, NombreCartera = "Alfa",  Activa = true },
                new Cartera { ID = 3, NombreCartera = "Medio", Activa = true }
            );
            ctx.LiderCarteras.AddRange(
                new LiderCartera { LiderID = LiderUno, CarteraID = 1 },
                new LiderCartera { LiderID = LiderUno, CarteraID = 2 },
                new LiderCartera { LiderID = LiderUno, CarteraID = 3 }
            );
            await ctx.SaveChangesAsync();
        }

        using var ctxR    = new CallCenterDbContext(opts);
        var repo          = new CarteraRepository(ctxR);
        var resultado     = (await repo.ObtenerPorLiderAsync(LiderUno)).ToList();

        Assert.Equal(3,      resultado.Count);
        Assert.Equal("Alfa",  resultado[0].NombreCartera);
        Assert.Equal("Medio", resultado[1].NombreCartera);
        Assert.Equal("Zeta",  resultado[2].NombreCartera);
    }
}
