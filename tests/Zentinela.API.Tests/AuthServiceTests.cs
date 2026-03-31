using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using Zentinela.API.Application.DTOs.Auth;
using Zentinela.API.Domain.Entities;
using Zentinela.API.Infrastructure.Services;

namespace Zentinela.API.Tests;

public class AuthServiceTests
{
    // ── Constantes para evitar magic strings ────────────────────────────────
    private const string EmailValido    = "lider@zentinela.com";
    private const string PasswordValida = "Segura123!";
    private const string NombreCompleto = "Juan Pérez";
    private const string RolLider       = "Líder";
    private const int    LiderID        = 42;

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Crea un Mock de UserManager con la firma de constructor requerida por ASP.NET Identity.
    /// </summary>
    private static Mock<UserManager<ApplicationUser>> CrearUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// Construye una IConfiguration con los valores mínimos de JwtSettings.
    /// La SecretKey tiene 32 caracteres para cumplir el requisito de HMAC-SHA256.
    /// </summary>
    private static IConfiguration CrearConfiguracion() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"]        = "ClaveSuperSecreta1234567890ABCD!",
                ["JwtSettings:Issuer"]            = "zentinela-tests",
                ["JwtSettings:Audience"]          = "zentinela-api",
                ["JwtSettings:ExpirationHours"]   = "8"
            })
            .Build();

    /// <summary>
    /// Construye un ApplicationUser de prueba con valores por defecto.
    /// </summary>
    private static ApplicationUser CrearUsuario(int? liderID = LiderID) => new()
    {
        Id             = 1,
        Email          = EmailValido,
        NombreCompleto = NombreCompleto,
        LiderID        = liderID
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ConCredencialesValidas_RetornaToken()
    {
        // Arrange
        var usuario      = CrearUsuario();
        var userManager  = CrearUserManagerMock();
        var config       = CrearConfiguracion();

        userManager
            .Setup(m => m.FindByEmailAsync(EmailValido))
            .ReturnsAsync(usuario);

        userManager
            .Setup(m => m.CheckPasswordAsync(usuario, PasswordValida))
            .ReturnsAsync(true);

        userManager
            .Setup(m => m.GetRolesAsync(usuario))
            .ReturnsAsync(new List<string> { RolLider });

        var servicio = new AuthService(userManager.Object, config);
        var request  = new LoginRequest { Email = EmailValido, Password = PasswordValida };

        // Act
        var resultado = await servicio.LoginAsync(request);

        // Assert
        Assert.NotNull(resultado);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Token),
            "El token no debe estar vacío");
        Assert.Equal(NombreCompleto, resultado.NombreCompleto);
        Assert.Equal(RolLider,       resultado.Rol);
    }

    [Fact]
    public async Task Login_ConEmailInexistente_RetornaNull()
    {
        // Arrange
        var userManager = CrearUserManagerMock();
        var config      = CrearConfiguracion();

        userManager
            .Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var servicio = new AuthService(userManager.Object, config);
        var request  = new LoginRequest { Email = "noexiste@zentinela.com", Password = PasswordValida };

        // Act
        var resultado = await servicio.LoginAsync(request);

        // Assert
        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_ConPasswordIncorrecta_RetornaNull()
    {
        // Arrange
        var usuario     = CrearUsuario();
        var userManager = CrearUserManagerMock();
        var config      = CrearConfiguracion();

        userManager
            .Setup(m => m.FindByEmailAsync(EmailValido))
            .ReturnsAsync(usuario);

        userManager
            .Setup(m => m.CheckPasswordAsync(usuario, It.IsAny<string>()))
            .ReturnsAsync(false);   // contraseña incorrecta

        var servicio = new AuthService(userManager.Object, config);
        var request  = new LoginRequest { Email = EmailValido, Password = "PasswordErronea!" };

        // Act
        var resultado = await servicio.LoginAsync(request);

        // Assert
        Assert.Null(resultado);
    }

    [Fact]
    public async Task Login_TokenContieneLiderID_CuandoUsuarioTieneLider()
    {
        // Arrange
        var usuario     = CrearUsuario(liderID: LiderID);
        var userManager = CrearUserManagerMock();
        var config      = CrearConfiguracion();

        userManager
            .Setup(m => m.FindByEmailAsync(EmailValido))
            .ReturnsAsync(usuario);

        userManager
            .Setup(m => m.CheckPasswordAsync(usuario, PasswordValida))
            .ReturnsAsync(true);

        userManager
            .Setup(m => m.GetRolesAsync(usuario))
            .ReturnsAsync(new List<string> { RolLider });

        var servicio = new AuthService(userManager.Object, config);
        var request  = new LoginRequest { Email = EmailValido, Password = PasswordValida };

        // Act
        var resultado = await servicio.LoginAsync(request);

        // Assert — el LiderID en la respuesta debe coincidir con el del usuario
        Assert.NotNull(resultado);
        Assert.Equal(LiderID, resultado.LiderID);

        // Verificación adicional: el claim "liderID" debe estar presente en el JWT
        var handler     = new JwtSecurityTokenHandler();
        var jwtToken    = handler.ReadJwtToken(resultado.Token);
        var claimLider  = jwtToken.Claims.FirstOrDefault(c => c.Type == "liderID");

        Assert.NotNull(claimLider);
        Assert.Equal(LiderID.ToString(), claimLider.Value);
    }
}
