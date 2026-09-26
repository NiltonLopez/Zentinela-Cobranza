using Microsoft.Extensions.Options;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Hora del negocio para los Informes de Localización (ADR-040, etapa 4): <see cref="TimeProvider"/> convertido a
/// <see cref="InformesLocalizacionSettings.ZonaHoraria"/> (Colombia). Singleton. En producción la API corre en el
/// servidor de la BD y la zona coincide con la del sistema; en el equipo de desarrollo (7 h adelante) evita que la
/// regla de las 13:00 y el recálculo de las 13:05 se evalúen con la hora de otro país.
/// </summary>
internal sealed class RelojLocalizacion : IRelojLocalizacion
{
    private readonly TimeProvider _reloj;
    private readonly TimeZoneInfo _zona;

    public RelojLocalizacion(IOptions<InformesLocalizacionSettings> opciones, TimeProvider? reloj = null)
    {
        _reloj = reloj ?? TimeProvider.System;
        _zona = ZonaHorariaLocalizacion.Resolver(opciones.Value.ZonaHoraria);
    }

    public TimeZoneInfo Zona => _zona;

    public DateTime Ahora => ZonaHorariaLocalizacion.Ahora(_reloj, _zona);

    public Task EsperarAsync(TimeSpan espera, CancellationToken ct) =>
        espera <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(espera, _reloj, ct);
}
