using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Ranking;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.API.Tests;

// El cálculo de la categoría de ranking vive en CategoriaRankingService (ver
// CategoriaRankingServiceTests.cs) — aquí solo se verifica que DashboardAsesorService
// delegue correctamente: pide el diccionario del mes de referencia y lee su propia fila.
public class DashboardAsesorServiceTests
{
    private static DbContextOptions<CallCenterDbContext> OpcionesCallCenter() =>
        new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static DbContextOptions<RecursosHumanosDbContext> OpcionesRrhh() =>
        new DbContextOptionsBuilder<RecursosHumanosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    // Fake mínimo: devuelve exactamente el diccionario que se le configure,
    // capturando con qué (año, mes) fue invocado para poder aserirlo.
    private class CategoriaRankingServiceFake : ICategoriaRankingService
    {
        private readonly Dictionary<long, CategoriaAgenteDto> _resultado;
        public (int Año, int Mes)? UltimaLlamada { get; private set; }

        public CategoriaRankingServiceFake(Dictionary<long, CategoriaAgenteDto> resultado) => _resultado = resultado;

        public Task<Dictionary<long, CategoriaAgenteDto>> ObtenerCategoriasAsync(int año, int mes)
        {
            UltimaLlamada = (año, mes);
            return Task.FromResult(_resultado);
        }
    }

    [Fact]
    public async Task ObtenerDashboardAsync_TomaLaCategoriaDelAgenteDesdeElServicioCompartido()
    {
        const long numeroDocumento = 12345;
        var fake = new CategoriaRankingServiceFake(new Dictionary<long, CategoriaAgenteDto>
        {
            [numeroDocumento] = new CategoriaAgenteDto { AgenteID = numeroDocumento, Categoria = "Diamante" },
        });

        using var callCenter = new CallCenterDbContext(OpcionesCallCenter());
        using var rrhh       = new RecursosHumanosDbContext(OpcionesRrhh());
        var service = new DashboardAsesorService(callCenter, rrhh, fake);

        var resultado = await service.ObtenerDashboardAsync(numeroDocumento, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal("Diamante", resultado.CategoriaRanking);
        Assert.Equal((2026, 6), fake.UltimaLlamada);
    }

    [Fact]
    public async Task ObtenerDashboardAsync_SinFilaParaElAgente_CategoriaEsNull()
    {
        const long numeroDocumento = 12345;
        var fake = new CategoriaRankingServiceFake(new Dictionary<long, CategoriaAgenteDto>());

        using var callCenter = new CallCenterDbContext(OpcionesCallCenter());
        using var rrhh       = new RecursosHumanosDbContext(OpcionesRrhh());
        var service = new DashboardAsesorService(callCenter, rrhh, fake);

        var resultado = await service.ObtenerDashboardAsync(numeroDocumento, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Null(resultado.CategoriaRanking);
    }

    [Fact]
    public async Task ObtenerDashboardAsync_SinFiltroDeFecha_UsaElMesActual()
    {
        const long numeroDocumento = 12345;
        var fake = new CategoriaRankingServiceFake(new Dictionary<long, CategoriaAgenteDto>());

        using var callCenter = new CallCenterDbContext(OpcionesCallCenter());
        using var rrhh       = new RecursosHumanosDbContext(OpcionesRrhh());
        var service = new DashboardAsesorService(callCenter, rrhh, fake);

        await service.ObtenerDashboardAsync(numeroDocumento, fechaDesde: null, fechaHasta: null);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal((hoy.Year, hoy.Month), fake.UltimaLlamada);
    }
}
