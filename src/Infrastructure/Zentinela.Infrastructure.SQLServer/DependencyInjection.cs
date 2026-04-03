using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zentinela.Application.Interfaces;
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
        services.AddDbContext<ZentinelaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("ZentinelaConnection")));

        services.AddDbContext<CallCenterDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("CallCenterConnection")));

        services.AddDbContext<RecursosHumanosDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("RecursosHumanosConnection")));

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

        services.AddScoped<ICarteraRepository, CarteraRepository>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
