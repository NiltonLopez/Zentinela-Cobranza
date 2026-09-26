namespace Zentinela.Application.Interfaces;

/// <summary>
/// Reloj de los Informes de Localización (ADR-040, etapa 4): la hora del negocio en la zona configurada
/// (<c>InformesLocalizacion:ZonaHoraria</c>, Colombia) y las esperas del recálculo programado. Inyectable para
/// probar el horario sin esperar de verdad.
/// </summary>
public interface IRelojLocalizacion
{
    /// <summary>Ahora en la zona del negocio (sin zona: es la hora que se compara y se guarda).</summary>
    DateTime Ahora { get; }

    /// <summary>Espera <paramref name="espera"/> (cancelable).</summary>
    Task EsperarAsync(TimeSpan espera, CancellationToken ct);
}
