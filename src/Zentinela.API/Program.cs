using ConsultaIntegrada.Infrastructure.Entities;
using ConsultaIntegrada.Initialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Zentinela.API.HealthChecks;
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

    options.AddPolicy("ZentinelaPolicy", policy =>
        policy.WithOrigins(
                  "http://zentinela",
                  "http://10.0.0.89:5050")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Health check para el smoke test post-deploy (verifica app + conectividad a la BD).
builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<DatabaseHealthCheck>("db");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRouting();
app.UseCors(app.Environment.IsDevelopment() ? "FrontendPolicy" : "ZentinelaPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Endpoint anónimo /health (200 = sano, 503 = no sano) para el smoke test del deploy.
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
