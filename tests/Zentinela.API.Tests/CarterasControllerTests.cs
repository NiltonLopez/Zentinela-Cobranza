using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Services;
using Zentinela.API.Controllers;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;

namespace Zentinela.API.Tests;

/// <summary>
/// El controlador arma el contexto desde los claims del JWT y la regla de quién ve qué
/// vive en CarteraService. Se prueban juntos, con el repositorio simulado.
/// </summary>
public class CarterasControllerTests
{
    private const string RolAdmin = "Admin";
    private const string RolLider = "Líder";
    private const int    LiderID  = 5;

    private static CarterasController CrearController(Mock<ICarteraRepository> repo, IEnumerable<Claim> claims) =>
        new(new CarteraService(repo.Object))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                }
            }
        };

    private static List<Cartera> Carteras(int cantidad) =>
        Enumerable.Range(1, cantidad)
            .Select(i => new Cartera { ID = i, NombreCartera = $"Cartera {i}", Activa = true })
            .ToList();

    private static async Task<List<CarteraDto>> Ejecutar(CarterasController controller)
    {
        var ok = Assert.IsType<OkObjectResult>(await controller.ObtenerCarteras());
        return Assert.IsAssignableFrom<IEnumerable<CarteraDto>>(ok.Value).ToList();
    }

    [Fact]
    public async Task ObtenerCarteras_ComoAdmin_DevuelveTodasLasCarteras()
    {
        var repo = new Mock<ICarteraRepository>();
        repo.Setup(r => r.ObtenerTodasAsync()).ReturnsAsync(Carteras(3));

        var dtos = await Ejecutar(CrearController(repo, [new Claim(ClaimTypes.Role, RolAdmin)]));

        Assert.Equal(3, dtos.Count);
        repo.Verify(r => r.ObtenerPorLiderAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerCarteras_ComoLider_DevuelveSoloSusCarteras()
    {
        var repo = new Mock<ICarteraRepository>();
        repo.Setup(r => r.ObtenerPorLiderAsync(LiderID)).ReturnsAsync(Carteras(2));

        var dtos = await Ejecutar(CrearController(repo,
        [
            new Claim(ClaimTypes.Role, RolLider),
            new Claim("liderID", LiderID.ToString())
        ]));

        Assert.Equal(2, dtos.Count);
        repo.Verify(r => r.ObtenerTodasAsync(), Times.Never);
    }

    [Fact]
    public async Task ObtenerCarteras_LiderSinLiderID_NoDevuelveNada()
    {
        var repo = new Mock<ICarteraRepository>();

        var dtos = await Ejecutar(CrearController(repo, [new Claim(ClaimTypes.Role, RolLider)]));

        Assert.Empty(dtos);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ObtenerCarteras_LiderIDInvalidoEnElToken_NoDevuelveNada()
    {
        var repo = new Mock<ICarteraRepository>();

        var dtos = await Ejecutar(CrearController(repo,
        [
            new Claim(ClaimTypes.Role, RolLider),
            new Claim("liderID", "no-es-un-numero")
        ]));

        Assert.Empty(dtos);
        repo.VerifyNoOtherCalls();
    }
}
