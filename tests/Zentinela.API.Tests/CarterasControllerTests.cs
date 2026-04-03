using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.API.Controllers;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;

namespace Zentinela.API.Tests;

public class CarterasControllerTests
{
    private const string RolAdmin = "Admin";
    private const string RolLider = "Líder";
    private const int    LiderID  = 5;

    private static void AsignarContexto(CarterasController controller, IEnumerable<Claim> claims)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private static List<Cartera> CarterasDelLider(int cantidad) =>
        Enumerable.Range(1, cantidad)
            .Select(i => new Cartera { ID = i, NombreCartera = $"Cartera {i}", Activa = true })
            .ToList();

    [Fact]
    public async Task ObtenerCarteras_ComoAdmin_DevuelveTodasLasCarteras()
    {
        var todas = new List<Cartera>
        {
            new() { ID = 1, NombreCartera = "Cartera A", Activa = true },
            new() { ID = 2, NombreCartera = "Cartera B", Activa = true },
            new() { ID = 3, NombreCartera = "Cartera C", Activa = true }
        };

        var repoMock = new Mock<ICarteraRepository>();
        repoMock.Setup(r => r.ObtenerTodasAsync()).ReturnsAsync(todas);

        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller, [new Claim(ClaimTypes.Role, RolAdmin)]);

        var resultado = await controller.ObtenerCarteras();

        var ok   = Assert.IsType<OkObjectResult>(resultado);
        var dtos = Assert.IsAssignableFrom<IEnumerable<CarteraDto>>(ok.Value);
        Assert.Equal(3, dtos.Count());
    }

    [Fact]
    public async Task ObtenerCarteras_ComoLider_DevuelveSoloSusCarteras()
    {
        var carteras = CarterasDelLider(2);

        var repoMock = new Mock<ICarteraRepository>();
        repoMock.Setup(r => r.ObtenerPorLiderAsync(LiderID)).ReturnsAsync(carteras);

        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller,
        [
            new Claim(ClaimTypes.Role, RolLider),
            new Claim("liderID", LiderID.ToString())
        ]);

        var resultado = await controller.ObtenerCarteras();

        var ok   = Assert.IsType<OkObjectResult>(resultado);
        var dtos = Assert.IsAssignableFrom<IEnumerable<CarteraDto>>(ok.Value);
        Assert.Equal(2, dtos.Count());
    }

    [Fact]
    public async Task ObtenerCarteras_SinLiderID_DevuelveForbid()
    {
        var repoMock   = new Mock<ICarteraRepository>();
        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller, [new Claim(ClaimTypes.Role, RolLider)]);

        var resultado = await controller.ObtenerCarteras();

        Assert.IsType<ForbidResult>(resultado);
    }

    [Fact]
    public async Task ObtenerCarteras_ConRolNoReconocido_DevuelveForbid()
    {
        var repoMock   = new Mock<ICarteraRepository>();
        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller, [new Claim(ClaimTypes.Role, "Asesor")]);

        var resultado = await controller.ObtenerCarteras();

        Assert.IsType<ForbidResult>(resultado);
    }
}
