using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Repositories;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.Infrastructure.SQLServer;

public static class DependencyInjection
{
    public static IServiceCollection AddSQLServerInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Configuración tipada (IOptions<JwtSettings>) ──────────────────────
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

        // ── DbContexts ────────────────────────────────────────────────────────
        services.AddDbContext<ZentinelaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("ZentinelaConnection")));

        services.AddDbContext<CallCenterDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("CallCenterConnection")));

        services.AddDbContext<RecursosHumanosDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("RecursosHumanosConnection")));

        // ── Identity ─────────────────────────────────────────────────────────
        services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ZentinelaDbContext>()
        .AddDefaultTokenProviders();

        // ── Autenticación JWT Bearer ──────────────────────────────────────────
        // AddIdentity sobreescribe el esquema default a cookies. Se restablece
        // explícitamente a JwtBearer para que [Authorize] valide tokens JWT.
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
        var claveBytes = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(claveBytes),
                    ClockSkew = TimeSpan.Zero, // sin margen de tolerancia extra
                };
            });

        // ── Servicios de infraestructura ──────────────────────────────────────
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<ICarteraRepository, CarteraRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistroService, RegistroService>();
        services.AddScoped<IDashboardAsesorService, DashboardAsesorService>();

        return services;
    }
}
