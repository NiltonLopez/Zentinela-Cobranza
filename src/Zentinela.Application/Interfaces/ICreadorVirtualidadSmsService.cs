using Zentinela.Application.DTOs.CreadorVirtualidad;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Asistente de 4 pasos para generar el archivo de SMS que se entrega a virtualidad
/// (ADR-037). Los tres métodos con archivo comparten el mismo pipeline de depuración,
/// así que validar, anotar el catálogo y generar producen los mismos conteos.
/// </summary>
public interface ICreadorVirtualidadSmsService
{
    /// <summary>Plantilla XLSX: hoja "BASE" con las 7 columnas de entrada (solo encabezados) y hoja "EJEMPLO" que no se procesa.</summary>
    byte[] GenerarPlantilla();

    /// <summary>Carteras presentes en ListaNegraTelefonos, para que el usuario elija cuáles cruzar.</summary>
    Task<List<CarteraListaNegraDto>> ListarCarterasListaNegraAsync(CancellationToken ct = default);

    /// <summary>Asesores del líder (Cartera_Agente) con su contacto persistido, si lo tienen.</summary>
    Task<List<AsesorEstrategiaDto>> ListarAsesoresDelLiderAsync(int liderID, CancellationToken ct = default);

    /// <summary>Todos los agentes activos, para agregar "prestados" de otro líder.</summary>
    Task<List<AsesorEstrategiaDto>> ListarAgentesActivosAsync(CancellationToken ct = default);

    /// <summary>Upsert de número corporativo + link por AgenteID. Devuelve cuántos se escribieron.</summary>
    Task<int> GuardarContactosAsync(IReadOnlyList<ContactoAsesorRequest> contactos, string usuario, CancellationToken ct = default);

    /// <summary>Paso 1: depura el archivo y devuelve conteos, muestra y huella.</summary>
    Task<ValidarArchivoSmsResponse> ValidarAsync(EntradaSms entrada, CancellationToken ct = default);

    /// <summary>
    /// Paso 3: catálogo de mensajes activos anotado con cuántos titulares exceden 160,
    /// calculado con el link más largo de <paramref name="links"/>.
    /// </summary>
    Task<CatalogoMensajesResponse> MensajesDisponiblesAsync(
        EntradaSms entrada, string huellaEsperada, IReadOnlyList<string> links, CancellationToken ct = default);

    /// <summary>Paso 4: reparte asesores, arma mensajes, excluye los que exceden, genera el XLSX y audita.</summary>
    Task<ResultadoGeneracionSms> GenerarAsync(
        EntradaSms entrada, string huellaEsperada, GenerarEstrategiaSmsRequest request, string usuario,
        CancellationToken ct = default);
}
