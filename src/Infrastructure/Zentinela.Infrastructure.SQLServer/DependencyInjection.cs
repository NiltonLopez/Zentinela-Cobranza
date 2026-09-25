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
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;

namespace Zentinela.Infrastructure.SQLServer;

public static class DependencyInjection
{
    public static IServiceCollection AddSQLServerInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Configuración tipada (IOptions<JwtSettings>) ──────────────────────
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        services.Configure<ProyeccionPromesasSettings>(configuration.GetSection("ProyeccionPromesas"));
        services.Configure<CargueFuentesSettings>(configuration.GetSection("CargueFuentes"));
        services.Configure<InformesLocalizacionSettings>(configuration.GetSection("InformesLocalizacion"));

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
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
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

        // ── Caché in-memory (Singleton) ───────────────────────────────────────
        // Caché del Calificador de Cartera con invalidación global por CTS.
        // Debe vivir entre requests para servir de algo.
        services.AddMemoryCache();
        services.AddSingleton<ICalificadorCarteraCache, CalificadorCarteraCache>();
        services.AddSingleton<ICategoriaRankingCache, CategoriaRankingCache>();

        // ── Servicios de infraestructura ──────────────────────────────────────
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<ICarteraRepository, CarteraRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistroService, RegistroService>();
        services.AddScoped<IDashboardAsesorService, DashboardAsesorService>();
        services.AddScoped<ICategoriaRankingService, CategoriaRankingService>();
        services.AddScoped<IDashboardDirectorService, DashboardDirectorService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IBaseMadreRepository, BaseMadreRepository>();
        services.AddScoped<ICalificadorCarteraService, CalificadorCarteraService>();
        services.AddScoped<IComparadorCarterasService, ComparadorCarterasService>();
        services.AddScoped<IAsignacionMasivaService, AsignacionMasivaService>();
        services.AddScoped<ICargueReporteService, CargueReporteService>();
        services.AddScoped<IGestionCarterasService, GestionCarterasService>();
        services.AddScoped<ICarguePagosService, CarguePagosService>();
        services.AddScoped<IInformesRecaudoService, InformesRecaudoService>();
        services.AddScoped<IAsignacionMetasService, AsignacionMetasService>();
        services.AddScoped<IVirtualidadMensajeriaService, VirtualidadMensajeriaService>();
        services.AddScoped<IVirtualidadIVRService, VirtualidadIVRService>();
        services.AddScoped<IVirtualidadLlamadasService, VirtualidadLlamadasService>();
        services.AddScoped<IVirtualidadEntrantesService, VirtualidadEntrantesService>();
        services.AddScoped<IVirtualidadPagosImpactoService, VirtualidadPagosImpactoService>();
        services.AddScoped<IHistoricoVirtualidadService, HistoricoVirtualidadService>();
        services.AddScoped<ICreadorVirtualidadSmsService, CreadorVirtualidadSmsService>();
        services.AddScoped<IMensajesAutorizadosService, MensajesAutorizadosService>();
        services.AddScoped<IRastreoLineasService, RastreoLineasService>();
        services.AddScoped<IUsoBaseDatosService, UsoBaseDatosService>();

        // ── Auditoría de Gestiones — evaluador de congruencia perfil↔gestión (Gemini) ──
        services.AddScoped<IEvaluadorCongruencia, EvaluadorCongruenciaGemini>();
        services.AddScoped<IAuditoriaGestionesService, AuditoriaGestionesService>();
        // Almacén en memoria de reportes generados (compartido, expira a 48 h).
        services.AddSingleton<AuditoriaReportesStore>();

        // ── Cargue de Fuentes de Datos (ADR-039) ─────────────────────────────
        // Store y orquestador Singleton: los trabajos corren en segundo plano y sobreviven a
        // la petición que los inició. El motor es Scoped (usa CallCenterDbContext): el
        // orquestador le crea un scope propio por trabajo con IServiceScopeFactory.
        services.AddSingleton<CargueFuentesJobStore>();
        services.AddSingleton<ICargueFuentesService, CargueFuentesOrquestador>();
        services.AddScoped<CargueFuentesMotor>();

        // ── Informes de Localización (ADR-040) ───────────────────────────────
        // Catálogo de fuentes (estado + último cargue OK) y fotos de personas. El caché de fotos
        // es Singleton con MemoryCache propio (tope de 64 MB por bytes, ver FotoPersonaCache).
        services.AddScoped<IFuentesLocalizacionService, FuentesLocalizacionService>();
        services.AddSingleton<FotoPersonaCache>();
        services.AddScoped<IFotoPersonaOrigen, FotoPersonaOrigenSql>();
        services.AddScoped<IFotoPersonaService, FotoPersonaService>();
        // Precálculo por cortes: el coordinador es Singleton (un cálculo a la vez, en segundo plano, con
        // scope propio); el servicio que corre el batch y las lecturas son Scoped.
        services.AddSingleton<ILocalizacionResumenCoordinador, LocalizacionResumenCoordinador>();
        services.AddScoped<LocalizacionResumenService>();
        services.AddScoped<IInformesLocalizacionService, InformesLocalizacionService>();
        // Sugerencias (etapa 2): en línea sobre el par, no dependen del precálculo.
        services.AddScoped<ILocalizacionSugerenciasService, LocalizacionSugerenciasService>();
        // Envíos (etapa 3): lotes, retorno, impacto y uso del departamento.
        services.AddScoped<ILocalizacionEnviosService, LocalizacionEnviosService>();

        return services;
    }
}
