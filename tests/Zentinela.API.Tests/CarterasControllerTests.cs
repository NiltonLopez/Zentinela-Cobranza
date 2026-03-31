using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Zentinela.API.Application.DTOs.Carteras;
using Zentinela.API.Application.Interfaces;
using Zentinela.API.Controllers;
using Zentinela.API.Domain.Entities;

namespace Zentinela.API.Tests;

public class CarterasControllerTests
{
    // ── Constantes ───────────────────────────────────────────────────────────
    private const string RolAdmin = "Admin";
    private const string RolLider = "Líder";
    private const int    LiderID  = 5;

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Configura el ControllerContext con los claims indicados.
    /// </summary>
    private static void AsignarContexto(
        CarterasController controller,
        IEnumerable<Claim> claims)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, authenticationType: "Test"))
            }
        };
    }

    /// <summary>
    /// Genera una lista de Carteras de prueba para el LiderID configurado.
    /// </summary>
    private static List<Cartera> CarterasDelLider(int cantidad, int liderID = LiderID)
    {
        var lista = new List<Cartera>();
        for (int i = 1; i <= cantidad; i++)
            lista.Add(new Cartera { ID = i, NombreCartera = $"Cartera {i}", LiderID = liderID });
        return lista;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerCarteras_ComoAdmin_DevuelveTodasLasCarteras()
    {
        // Arrange — 3 carteras de distintos líderes
        var todasLasCarteras = new List<Cartera>
        {
            new() { ID = 1, NombreCartera = "Cartera A", LiderID = 1 },
            new() { ID = 2, NombreCartera = "Cartera B", LiderID = 2 },
            new() { ID = 3, NombreCartera = "Cartera C", LiderID = 3 }
        };

        var repoMock = new Mock<ICarteraRepository>();
        repoMock
            .Setup(r => r.ObtenerTodasAsync())
            .ReturnsAsync(todasLasCarteras);

        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller, new[]
        {
            new Claim(ClaimTypes.Role, RolAdmin)
        });

        // Act
        var resultado = await controller.ObtenerCarteras();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(resultado);
        var dtos     = Assert.IsAssignableFrom<IEnumerable<CarteraDto>>(okResult.Value);
        Assert.Equal(3, dtos.Count());
    }

    [Fact]
    public async Task ObtenerCarteras_ComoLider_DevuelveSoloSusCarteras()
    {
        // Arrange — repositorio devuelve 2 carteras del líder 5
        var carterasDelLider = CarterasDelLider(cantidad: 2, liderID: LiderID);

        var repoMock = new Mock<ICarteraRepository>();
        repoMock
            .Setup(r => r.ObtenerPorLiderAsync(LiderID))
            .ReturnsAsync(carterasDelLider);

        var controller = new CarterasController(repoMock.Object);
        AsignarContexto(controller, new[]
        {
            new Claim(ClaimTypes.Role, RolLider),
            new Claim("liderID",       LiderID.ToString())
        });

        // Act
        var resultado = await controller.ObtenerCarteras();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(resultado);
        var dtos     = Assert.IsAssignableFrom<IEnumerable<CarteraDto>>(okResult.Value);
        Assert.Equal(2, dtos.Count());
    }

    [Fact]
    public async Task ObtenerCarteras_SinLiderID_DevuelveForbid()
    {
        // Arrange — rol Líder pero sin claim liderID
        var repoMock   = new Mock<ICarteraRepository>();
        var controller = new CarterasController(repoMock.Object);

        AsignarContexto(controller, new[]
        {
            new Claim(ClaimTypes.Role, RolLider)
            // liderID ausente intencionalmente
        });

        // Act
        var resultado = await controller.ObtenerCarteras();

        // Assert
        Assert.IsType<ForbidResult>(resultado);
    }

    [Fact]
    public async Task ObtenerCarteras_ConRolNoReconocido_DevuelveForbid()
    {
        // Arrange — rol "Asesor" sin claim liderID (rol no contemplado en la lógica)
        var repoMock   = new Mock<ICarteraRepository>();
        var controller = new CarterasController(repoMock.Object);

        AsignarContexto(controller, new[]
        {
            new Claim(ClaimTypes.Role, "Asesor")
            // liderID ausente: int.TryParse fallará → Forbid
        });

        // Act
        var resultado = await controller.ObtenerCarteras();

        // Assert
        Assert.IsType<ForbidResult>(resultado);
    }
}
