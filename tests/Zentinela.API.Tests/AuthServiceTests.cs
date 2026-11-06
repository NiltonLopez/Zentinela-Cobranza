using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Zentinela.Application.DTOs.Auth;
using Zentinela.Application.Settings;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.API.Tests;

/// <summary>
/// Login por nombre de usuario contra Identity; el token lo arma el JwtTokenGenerator real
/// para verificar los claims que el resto del sistema usa (liderID, rol).
/// </summary>
public class AuthServiceTests
{
    private const string UsuarioValido  = "lider.prueba";
    private const string EmailValido    = "lider@zentinela.com";
    private const string PasswordValida = "Segura123!";
    private const string NombreCompleto = "Juan Pérez";
    private const string RolLider       = "Líder";
    private const int    LiderID        = 42;

    private static Mock<UserManager<ApplicationUser>> CrearUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static JwtTokenGenerator CrearGenerador() =>
        new(Options.Create(new JwtSettings
        {
            SecretKey       = "ClaveSuperSecretaSoloParaPruebas1234567890",
            Issuer          = "zentinela-tests",
            Audience        = "zentinela-api",
            ExpirationHours = 8
        }));

    private static ApplicationUser CrearUsuario(int? liderID = LiderID) => new()
    {
        Id              = 1,
        UserName        = UsuarioValido,
        Email           = EmailValido,
        NombreCompleto  = NombreCompleto,
        LiderID         = liderID,
        NumeroDocumento = 1234567890
    };

    private static Mock<UserManager<ApplicationUser>> UserManagerConUsuario(ApplicationUser usuario, bool claveCorrecta = true)
    {
        var userManager = CrearUserManagerMock();
        userManager.Setup(m => m.FindByNameAsync(UsuarioValido)).ReturnsAsync(usuario);
        userManager.Setup(m => m.CheckPasswordAsync(usuario, It.IsAny<string>())).ReturnsAsync(claveCorrecta);
        userManager.Setup(m => m.GetRolesAsync(usuario)).ReturnsAsync([RolLider]);
        return userManager;
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_RetornaToken()
    {
        var servicio  = new AuthService(UserManagerConUsuario(CrearUsuario()).Object, CrearGenerador());
        var resultado = await servicio.LoginAsync(new LoginRequest { Usuario = UsuarioValido, Password = PasswordValida });

        Assert.NotNull(resultado);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Token));
        Assert.Equal(NombreCompleto, resultado.NombreCompleto);
        Assert.Equal(RolLider,       resultado.Rol);
    }

    [Fact]
    public async Task Login_ConUsuarioInexistente_RetornaNull()
    {
        var userManager = CrearUserManagerMock();
        userManager.Setup(m => m.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var servicio  = new AuthService(userManager.Object, CrearGenerador());
        var resultado = await servicio.LoginAsync(new LoginRequest { Usuario = "no.existe", Password = PasswordValida });

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_ConPasswordIncorrecta_RetornaNull()
    {
        var servicio  = new AuthService(UserManagerConUsuario(CrearUsuario(), claveCorrecta: false).Object, CrearGenerador());
        var resultado = await servicio.LoginAsync(new LoginRequest { Usuario = UsuarioValido, Password = "Mal!" });

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_TokenContieneLiderIDYRol()
    {
        var servicio  = new AuthService(UserManagerConUsuario(CrearUsuario(LiderID)).Object, CrearGenerador());
        var resultado = await servicio.LoginAsync(new LoginRequest { Usuario = UsuarioValido, Password = PasswordValida });

        Assert.NotNull(resultado);
        Assert.Equal(LiderID, resultado.LiderID);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(resultado.Token);
        Assert.Equal(LiderID.ToString(), jwt.Claims.Single(c => c.Type == "liderID").Value);
        Assert.Contains(jwt.Claims, c => c.Value == RolLider);
    }
}
