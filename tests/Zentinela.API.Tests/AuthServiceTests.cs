using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using Zentinela.Application.DTOs.Auth;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.API.Tests;

public class AuthServiceTests
{
    private const string EmailValido    = "lider@zentinela.com";
    private const string PasswordValida = "Segura123!";
    private const string NombreCompleto = "Juan Pérez";
    private const string RolLider       = "Líder";
    private const int    LiderID        = 42;

    private static Mock<UserManager<ApplicationUser>> CrearUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
    }

    private static IConfiguration CrearConfiguracion() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"]      = "ClaveSuperSecreta1234567890ABCD!",
                ["JwtSettings:Issuer"]         = "zentinela-tests",
                ["JwtSettings:Audience"]       = "zentinela-api",
                ["JwtSettings:ExpirationHours"] = "8"
            })
            .Build();

    private static ApplicationUser CrearUsuario(int? liderID = LiderID) => new()
    {
        Id             = 1,
        Email          = EmailValido,
        NombreCompleto = NombreCompleto,
        LiderID        = liderID
    };

    [Fact]
    public async Task Login_ConCredencialesValidas_RetornaToken()
    {
        var usuario     = CrearUsuario();
        var userManager = CrearUserManagerMock();
        var config      = CrearConfiguracion();

        userManager.Setup(m => m.FindByEmailAsync(EmailValido)).ReturnsAsync(usuario);
        userManager.Setup(m => m.CheckPasswordAsync(usuario, PasswordValida)).ReturnsAsync(true);
        userManager.Setup(m => m.GetRolesAsync(usuario)).ReturnsAsync(["Líder"]);

        var servicio  = new AuthService(userManager.Object, config);
        var resultado = await servicio.LoginAsync(new LoginRequest { Email = EmailValido, Password = PasswordValida });

        Assert.NotNull(resultado);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Token));
        Assert.Equal(NombreCompleto, resultado.NombreCompleto);
        Assert.Equal(RolLider,       resultado.Rol);
    }

    [Fact]
    public async Task Login_ConEmailInexistente_RetornaNull()
    {
        var userManager = CrearUserManagerMock();
        userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var servicio  = new AuthService(userManager.Object, CrearConfiguracion());
        var resultado = await servicio.LoginAsync(new LoginRequest { Email = "x@x.com", Password = PasswordValida });

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_ConPasswordIncorrecta_RetornaNull()
    {
        var usuario     = CrearUsuario();
        var userManager = CrearUserManagerMock();

        userManager.Setup(m => m.FindByEmailAsync(EmailValido)).ReturnsAsync(usuario);
        userManager.Setup(m => m.CheckPasswordAsync(usuario, It.IsAny<string>())).ReturnsAsync(false);

        var servicio  = new AuthService(userManager.Object, CrearConfiguracion());
        var resultado = await servicio.LoginAsync(new LoginRequest { Email = EmailValido, Password = "Mal!" });

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_TokenContieneLiderID()
    {
        var usuario     = CrearUsuario(LiderID);
        var userManager = CrearUserManagerMock();

        userManager.Setup(m => m.FindByEmailAsync(EmailValido)).ReturnsAsync(usuario);
        userManager.Setup(m => m.CheckPasswordAsync(usuario, PasswordValida)).ReturnsAsync(true);
        userManager.Setup(m => m.GetRolesAsync(usuario)).ReturnsAsync(["Líder"]);

        var servicio  = new AuthService(userManager.Object, CrearConfiguracion());
        var resultado = await servicio.LoginAsync(new LoginRequest { Email = EmailValido, Password = PasswordValida });

        Assert.NotNull(resultado);
        Assert.Equal(LiderID, resultado.LiderID);

        var jwt        = new JwtSecurityTokenHandler().ReadJwtToken(resultado.Token);
        var claimLider = jwt.Claims.FirstOrDefault(c => c.Type == "liderID");
        Assert.NotNull(claimLider);
        Assert.Equal(LiderID.ToString(), claimLider.Value);
    }
}
