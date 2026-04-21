using ConsultaIntegrada.Infrastructure.Entities;
using ConsultaIntegrada.Initialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Zentinela.Application;
using Zentinela.Infrastructure.FileExporter;
using Zentinela.Infrastructure.SQLServer;
using Zentinela.Infrastructure.SQLServer.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddSQLServerInfrastructure(builder.Configuration);
builder.Services.AddFileExporterInfrastructure();

// Consulta Integrada — usa la misma cadena de conexión de DBCallCenter
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CallCenterConnection")));
builder.Services.AddApplicationServices();

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// CORS antes de autenticación y endpoints — sin UseRouting() explícito
// En ASP.NET Core 9 el routing es implícito; llamarlo explícito rompe CORS con endpoint routing
app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
