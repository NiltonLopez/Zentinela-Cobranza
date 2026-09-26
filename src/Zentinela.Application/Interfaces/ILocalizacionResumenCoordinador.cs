namespace Zentinela.Application.Interfaces;

/// <summary>
/// Coordina el precálculo de los Informes de Localización (ADR-040). Singleton: un solo cálculo a la
/// vez por proceso; un cargue que termina mientras corre deja UNA corrida posterior pendiente.
/// Nunca bloquea a quien lo llama.
/// </summary>
public interface ILocalizacionResumenCoordinador
{
    /// <summary>Pide un corte nuevo en segundo plano (ARRANQUE | CORTE_13H | CARGUE_FUENTE | MANUAL).</summary>
    void SolicitarRecalculo(string motivo, string? usuario = null);

    /// <summary>
    /// Regla de las 13:00 al leer: si el último corte OK (<paramref name="ultimoCorteOk"/>) quedó viejo,
    /// pide uno nuevo (salvo que haya fallado hace poco). Lo llaman las lecturas.
    /// </summary>
    void VerificarVigencia(DateTime? ultimoCorteOk);

    /// <summary>Al arrancar la API: si no hay corte OK del día, lo calcula (en segundo plano, con espera).</summary>
    void VerificarAlArrancar();

    /// <summary>
    /// Para el recálculo programado (etapa 4): pide un corte y espera a que termine. Si ya hay uno corriendo no
    /// lanza otro: espera ese (single-flight) y devuelve cómo terminó su ronda (incluida la corrida pendiente que
    /// haya dejado un cargue).
    /// </summary>
    Task<Utils.ResultadoRondaCorte> RecalcularYEsperarAsync(string motivo, CancellationToken ct);

    bool Calculando { get; }
    string? MotivoEnCurso { get; }
    DateTime? InicioEnCurso { get; }

    /// <summary>Sube con cada corte OK: las lecturas lo usan como clave de caché.</summary>
    long Version { get; }
}
