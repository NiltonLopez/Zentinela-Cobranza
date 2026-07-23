using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.API.Tests;

// Cobertura de la categoría de ranking (Diamante/Oro/Plata/Bronce), calculada en vivo
// en CategoriaRankingService.ObtenerCategoriasAsync — compartida entre el dashboard del
// asesor (DashboardAsesorService) y la vista de admin/director. Ver también CONFIG_NIVEL
// en DashboardAsesorPage.tsx (frontend) para la regla completa.
public class CategoriaRankingServiceTests
{
    private const int CarteraInterna = 1;
    private const int CarteraExterna = 2;
    private const int LiderInterno   = 1;
    private const int LiderExterno   = 2;

    private static DbContextOptions<CallCenterDbContext> OpcionesCallCenter() =>
        new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static DbContextOptions<RecursosHumanosDbContext> OpcionesRrhh() =>
        new DbContextOptionsBuilder<RecursosHumanosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static void SeedLiderCarteras(CallCenterDbContext ctx)
    {
        ctx.LiderCarteras.AddRange(
            new LiderCartera { LiderID = LiderInterno, CarteraID = CarteraInterna, EsExterna = false },
            new LiderCartera { LiderID = LiderExterno, CarteraID = CarteraExterna, EsExterna = true }
        );
    }

    private static Pago Pago(long agenteID, int año, int mes, decimal honorarios, int liderID = LiderInterno, int carteraID = CarteraInterna) =>
        new()
        {
            CedulaTitular   = 1,
            NombreCliente   = "Cliente",
            ValorPagado     = honorarios * 2,
            FechaPago       = new DateOnly(año, mes, 15),
            Honorarios      = honorarios,
            LiderID         = liderID,
            CarteraID       = carteraID,
            UsuarioRegistro = "test",
            AgenteControlID = agenteID,
            FechaRegistro   = DateTime.UtcNow,
        };

    // 20 agentes de relleno con honorarios altos y estables en los meses indicados, para que
    // dominen el Top 20 y el agente bajo prueba quede sujeto a la clasificación por rango.
    private static void SeedFillers(CallCenterDbContext ctx, params (int Año, int Mes)[] meses)
    {
        foreach (var (año, mes) in meses)
            for (long i = 100; i < 120; i++)
                ctx.Pagos.Add(Pago(agenteID: i, año, mes, 20_000_000m));
    }

    private static async Task<string?> Calcular(CallCenterDbContext callCenter, long agenteID, DateOnly refDate)
    {
        using var rrhh = new RecursosHumanosDbContext(OpcionesRrhh());
        var cache = new CategoriaRankingCache(new MemoryCache(new MemoryCacheOptions()));
        var service = new CategoriaRankingService(callCenter, rrhh, cache);
        var categorias = await service.ObtenerCategoriasAsync(refDate.Year, refDate.Month);
        return categorias.TryGetValue(agenteID, out var dto) ? dto.Categoria : null;
    }

    [Fact]
    public void CategoriaPorRango_AplicaLosUmbralesDefinidos()
    {
        Assert.Equal("Oro",    CategoriaRankingService.CategoriaPorRango(8_000_000m));
        Assert.Equal("Oro",    CategoriaRankingService.CategoriaPorRango(6_000_000m));
        Assert.Equal("Plata",  CategoriaRankingService.CategoriaPorRango(5_999_999m));
        Assert.Equal("Plata",  CategoriaRankingService.CategoriaPorRango(4_000_000m));
        Assert.Equal("Bronce", CategoriaRankingService.CategoriaPorRango(3_999_999m));
    }

    [Fact]
    public async Task SinHonorariosEnElAnio_NoApareceEnElResultado()
    {
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 999, new DateOnly(2026, 7, 1));

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Top20PorPromedioAnual_EsDiamante_AunSinLlegarAlRangoOro()
    {
        // Agente 1 mantiene un promedio bajo (3M) pero es el único con honorarios → top 20 trivial
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 3_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Equal("Diamante", resultado);
    }

    [Fact]
    public async Task FueraDelTop20_SeClasificaPorRangoDePromedio()
    {
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);

        // 21 agentes con honorarios en junio: el agente 21 es el de menor promedio → queda fuera del Top 20
        for (long i = 1; i <= 20; i++)
            ctx.Pagos.Add(Pago(agenteID: i, 2026, 6, 10_000_000m + i));
        ctx.Pagos.Add(Pago(agenteID: 21, 2026, 6, 5_000_000m)); // Plata (4M-<6M)
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 21, new DateOnly(2026, 6, 15));

        Assert.Equal("Plata", resultado);
    }

    [Fact]
    public async Task BajoRendimientoVsMesAnterior_RecalculaConHonorarioDelMesActual()
    {
        // Promedio anual (may+jun) da Oro, pero junio (2M) bajó vs mayo (9M) → se reclasifica con junio → Bronce
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        SeedFillers(ctx, (2026, 5), (2026, 6));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 5, 9_000_000m));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 2_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Equal("Bronce", resultado);
    }

    [Fact]
    public async Task SinBajaDeRendimiento_UsaLaCategoriaPorPromedioAnual()
    {
        // Mayo 5M, junio 7M (mejoró) → no aplica el ajuste, se usa el promedio (6M) → Oro
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        SeedFillers(ctx, (2026, 5), (2026, 6));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 5, 5_000_000m));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 7_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Equal("Oro", resultado);
    }

    [Fact]
    public async Task SinCargueDelMesActual_NoConfundeConBajoRendimiento()
    {
        // Mayo tuvo 7M (Oro). Junio aún no tiene NINGÚN pago cargado (no es que haya
        // ganado $0, es que Valentina no ha subido el cargue) → no debe reclasificar a Bronce.
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        SeedFillers(ctx, (2026, 5));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 5, 7_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Equal("Oro", resultado);
    }

    [Fact]
    public async Task SinMesAnteriorConDatos_NoAplicaElAjuste()
    {
        // Primer mes del asesor: no hay honorario de mayo con qué comparar → se usa el promedio (junio) directo
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        SeedFillers(ctx, (2026, 6));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 7_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Equal("Oro", resultado);
    }

    [Fact]
    public async Task ExcluyePagosDeCombinacionesExternas()
    {
        // El agente solo tiene pagos bajo una combinación Líder-Cartera marcada EsExterna → no cuenta
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 9_000_000m, liderID: LiderExterno, carteraID: CarteraExterna));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 6, 15));

        Assert.Null(resultado);
    }

    [Fact]
    public async Task MesAnteriorEnAnioDistinto_SeConsideraParaLaComparacion()
    {
        // Enero 2026 vs diciembre 2025 (año distinto) — la comparación cruza el límite de año
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        SeedFillers(ctx, (2025, 12), (2026, 1));
        ctx.Pagos.Add(Pago(agenteID: 1, 2025, 12, 9_000_000m));
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 1,  2_000_000m));
        await ctx.SaveChangesAsync();

        var resultado = await Calcular(ctx, agenteID: 1, new DateOnly(2026, 1, 15));

        // Bajó vs. diciembre → se reclasifica con el valor de enero (2M) → Bronce
        Assert.Equal("Bronce", resultado);
    }

    [Fact]
    public async Task ResultadoSeCachea_SegundaLlamadaNoRecalcula()
    {
        var opts = OpcionesCallCenter();
        using var ctx = new CallCenterDbContext(opts);
        SeedLiderCarteras(ctx);
        ctx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 3_000_000m));
        await ctx.SaveChangesAsync();

        using var rrhh = new RecursosHumanosDbContext(OpcionesRrhh());
        var cache = new CategoriaRankingCache(new MemoryCache(new MemoryCacheOptions()));
        var service = new CategoriaRankingService(ctx, rrhh, cache);

        var primero  = await service.ObtenerCategoriasAsync(2026, 6);
        // Se agrega un pago nuevo SIN pasar por la invalidación del caché — si la segunda
        // llamada reflejara este cambio, significaría que no se está usando el caché.
        ctx.Pagos.Add(Pago(agenteID: 2, 2026, 6, 50_000_000m));
        await ctx.SaveChangesAsync();
        var segundo = await service.ObtenerCategoriasAsync(2026, 6);

        Assert.Equal(primero.Count, segundo.Count);

        // Tras invalidar, sí debe reflejar el pago nuevo
        cache.InvalidarTodo();
        var tercero = await service.ObtenerCategoriasAsync(2026, 6);
        Assert.True(tercero.ContainsKey(2));
    }

    [Fact]
    public async Task LlamadasConcurrentes_ConCacheFrio_NoDuplicanElCalculo()
    {
        // Simula varias requests pidiendo el mismo mes justo después de una invalidación:
        // cada una con su propio DbContext (como en producción, donde el servicio es Scoped),
        // pero compartiendo el mismo caché Singleton — igual que en el DI real.
        var dbName = Guid.NewGuid().ToString();
        var optsCallCenter = new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var seedCtx = new CallCenterDbContext(optsCallCenter))
        {
            SeedLiderCarteras(seedCtx);
            seedCtx.Pagos.Add(Pago(agenteID: 1, 2026, 6, 3_000_000m));
            await seedCtx.SaveChangesAsync();
        }

        var cache = new CategoriaRankingCache(new MemoryCache(new MemoryCacheOptions()));

        var tareas = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var ctx  = new CallCenterDbContext(optsCallCenter);
            using var rrhh = new RecursosHumanosDbContext(OpcionesRrhh());
            var service = new CategoriaRankingService(ctx, rrhh, cache);
            return await service.ObtenerCategoriasAsync(2026, 6);
        });

        var resultados = await Task.WhenAll(tareas);

        // Sin el candado, cada llamada calcularía su propio diccionario (referencias
        // distintas). Con el candado, solo una calcula y las demás esperan y leen el
        // mismo resultado cacheado — por eso deben ser la MISMA instancia.
        Assert.All(resultados, r => Assert.Same(resultados[0], r));
    }
}
