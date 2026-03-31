using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Zentinela.API.Domain.Entities;
using Zentinela.API.Infrastructure.Data;
using Zentinela.API.Infrastructure.Repositories;

namespace Zentinela.API.Tests;

public class CarteraRepositoryTests
{
    // ── Constantes ───────────────────────────────────────────────────────────
    private const int LiderUno = 1;
    private const int LiderDos = 2;

    // ── Helper ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea opciones para una base de datos InMemory con nombre único por test,
    /// evitando contaminación de datos entre pruebas paralelas.
    /// </summary>
    private static DbContextOptions<ZentinelaDbContext> CrearOpcionesInMemory() =>
        new DbContextOptionsBuilder<ZentinelaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorLiderAsync_DevuelveSoloCarterasDelLider()
    {
        // Arrange — 2 carteras del líder 1 y 1 cartera del líder 2
        var options = CrearOpcionesInMemory();

        using (var context = new ZentinelaDbContext(options))
        {
            context.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Cartera A", LiderID = LiderUno },
                new Cartera { ID = 2, NombreCartera = "Cartera B", LiderID = LiderUno },
                new Cartera { ID = 3, NombreCartera = "Cartera C", LiderID = LiderDos }
            );
            await context.SaveChangesAsync();
        }

        // Act
        using var contextLectura = new ZentinelaDbContext(options);
        var repo      = new CarteraRepository(contextLectura);
        var resultado = await repo.ObtenerPorLiderAsync(LiderUno);

        // Assert — solo deben retornarse las 2 del líder 1
        Assert.Equal(2, resultado.Count());
        Assert.All(resultado, c => Assert.Equal(LiderUno, c.LiderID));
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_CuandoNoHayCarteras_DevuelveListaVacia()
    {
        // Arrange — base de datos vacía
        var options = CrearOpcionesInMemory();

        // Act
        using var context = new ZentinelaDbContext(options);
        var repo      = new CarteraRepository(context);
        var resultado = await repo.ObtenerPorLiderAsync(LiderUno);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ObtenerTodasAsync_DevuelveTodasLasCarteras()
    {
        // Arrange — 3 carteras de diferentes líderes
        var options = CrearOpcionesInMemory();

        using (var context = new ZentinelaDbContext(options))
        {
            context.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Cartera X", LiderID = LiderUno },
                new Cartera { ID = 2, NombreCartera = "Cartera Y", LiderID = LiderDos },
                new Cartera { ID = 3, NombreCartera = "Cartera Z", LiderID = null }
            );
            await context.SaveChangesAsync();
        }

        // Act
        using var contextLectura = new ZentinelaDbContext(options);
        var repo      = new CarteraRepository(contextLectura);
        var resultado = await repo.ObtenerTodasAsync();

        // Assert — las 3 carteras deben estar presentes
        Assert.Equal(3, resultado.Count());
    }

    [Fact]
    public async Task ObtenerPorLiderAsync_ResultadoOrdenadoPorNombre()
    {
        // Arrange — carteras insertadas en orden desordenado
        var options = CrearOpcionesInMemory();

        using (var context = new ZentinelaDbContext(options))
        {
            context.Carteras.AddRange(
                new Cartera { ID = 1, NombreCartera = "Zeta",  LiderID = LiderUno },
                new Cartera { ID = 2, NombreCartera = "Alfa",  LiderID = LiderUno },
                new Cartera { ID = 3, NombreCartera = "Medio", LiderID = LiderUno }
            );
            await context.SaveChangesAsync();
        }

        // Act
        using var contextLectura = new ZentinelaDbContext(options);
        var repo      = new CarteraRepository(contextLectura);
        var resultado = (await repo.ObtenerPorLiderAsync(LiderUno)).ToList();

        // Assert — orden ascendente: Alfa → Medio → Zeta
        Assert.Equal(3, resultado.Count);
        Assert.Equal("Alfa",  resultado[0].NombreCartera);
        Assert.Equal("Medio", resultado[1].NombreCartera);
        Assert.Equal("Zeta",  resultado[2].NombreCartera);
    }
}
