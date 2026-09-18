using Zentinela.Application.DTOs.Virtualidad.Historico;

namespace Zentinela.Application.DTOs.CreadorVirtualidad;

// ── Entrada común a los tres endpoints con archivo ──────────────────────────

/// <summary>
/// Lo que el asistente reenvía en cada paso. El archivo viaja siempre completo (patrón
/// ADR-031: re-subir el archivo es más barato que bajar 200k filas al navegador o
/// mantener estado en el servidor) y la huella ata cada paso al archivo validado.
/// </summary>
public sealed record EntradaSms(
    Stream               Archivo,
    string               NombreArchivo,
    int                  LiderID,
    IReadOnlyList<string> CarterasListaNegra,
    Stream?              ArchivoListaNegra,
    string?              NombreArchivoListaNegra);

/// <summary>El archivo cambió entre validar y el paso actual → 409.</summary>
public sealed class EstrategiaSmsConflictoException(string mensaje) : Exception(mensaje);

// ── Paso 1: validar ─────────────────────────────────────────────────────────

/// <summary>Fila ya limpia. Es la que viaja en la muestra y la que se escribe al XLSX.</summary>
public class FilaSmsDto
{
    public long   Cedula        { get; set; }
    public string Nombre        { get; set; } = string.Empty;
    public string FechaGestion  { get; set; } = string.Empty;
    public string UltimoPerfil  { get; set; } = string.Empty;
    public string NumeroMarcado { get; set; } = string.Empty;
}

/// <summary>
/// Resultado de la depuración. Cada fila tiene UN solo motivo de exclusión (orden fijo),
/// así que <c>TotalLeidas − TotalValidas</c> es exactamente la suma de los Excluidos*.
/// </summary>
public class ValidarArchivoSmsResponse
{
    public string  HuellaArchivo    { get; set; } = string.Empty;
    public string? HuellaListaNegra { get; set; }

    public int TotalLeidas  { get; set; }
    public int TotalValidas { get; set; }

    public int ExcluidosCedulaInvalida   { get; set; }
    public int ExcluidosTelefonoInvalido { get; set; }
    public int ExcluidosFijo             { get; set; }
    public int ExcluidosNombreVacio      { get; set; }
    public int ExcluidosPerfilArchivo    { get; set; }
    public int ExcluidosPerfilBD         { get; set; }
    public int ExcluidosListaNegraTabla  { get; set; }
    public int ExcluidosListaNegraArchivo{ get; set; }
    public int ExcluidosDuplicado        { get; set; }

    /// <summary>Cuántos números aportó el XLSX opcional de lista negra (0 si no se subió).</summary>
    public int TotalNumerosListaNegraArchivo { get; set; }

    public List<string>          ColumnasEncontradas { get; set; } = [];
    public List<string>          ColumnasFaltantes   { get; set; } = [];
    public List<FilaSmsDto>      Muestra             { get; set; } = [];
    public List<ErrorAgrupadoDto> ErroresAgrupados   { get; set; } = [];
}

// ── Paso 2: asesores ────────────────────────────────────────────────────────

public class AsesorEstrategiaDto
{
    public long    AgenteID          { get; set; }
    public string  Nombre            { get; set; } = string.Empty;
    public string? NumeroCorporativo { get; set; }
    public string? LinkWhatsapp      { get; set; }
    /// <summary>true = viene de Cartera_Agente del líder; false = agregado a mano ("prestado").</summary>
    public bool    Precargado        { get; set; }
}

/// <summary>Lo que se persiste por asesor al pasar al paso 3.</summary>
public class ContactoAsesorRequest
{
    public long   AgenteID          { get; set; }
    public string NumeroCorporativo { get; set; } = string.Empty;
    public string LinkWhatsapp      { get; set; } = string.Empty;
}

public class CarteraListaNegraDto
{
    /// <summary>Valor tal cual está en BD (con guiones bajos). Es lo que se envía de vuelta.</summary>
    public string Valor        { get; set; } = string.Empty;
    /// <summary>Solo para mostrar: guiones bajos reemplazados por espacio.</summary>
    public string Etiqueta     { get; set; } = string.Empty;
    public int    TotalNumeros { get; set; }
}

// ── Paso 3: catálogo anotado ────────────────────────────────────────────────

public enum EstadoMensajeDisponible
{
    /// <summary>Cabe para todos los titulares.</summary>
    Disponible = 0,
    /// <summary>Se pasa en 1..UmbralAdvertencia titulares: se puede usar, esos se borran.</summary>
    Advertencia = 1,
    /// <summary>Se pasa en más titulares que el umbral: no se puede elegir.</summary>
    Deshabilitado = 2,
}

public class TitularExcedidoDto
{
    public long   Cedula   { get; set; }
    public string Nombre   { get; set; } = string.Empty;
    public int    Longitud { get; set; }
}

public class MensajeDisponibleDto
{
    public int    ID     { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Texto  { get; set; } = string.Empty;

    public EstadoMensajeDisponible Estado { get; set; }
    public string? Motivo { get; set; }

    /// <summary>Longitud del texto sin placeholders.</summary>
    public int LongitudBase   { get; set; }
    /// <summary>Con qué link se calculó el peor caso (el más largo de los asesores).</summary>
    public int LinkMasLargo   { get; set; }
    /// <summary>Longitud del titular con el nombre más largo, en el peor caso.</summary>
    public int LongitudMaxima { get; set; }

    public int TotalExcedidos { get; set; }
    /// <summary>Hasta MaxExcedidosEnRespuesta; completa cuando TotalExcedidos ≤ umbral.</summary>
    public List<TitularExcedidoDto> Excedidos { get; set; } = [];
}

public class CatalogoMensajesResponse
{
    public string HuellaArchivo { get; set; } = string.Empty;
    public int    TotalTitulares { get; set; }
    public int    LinkMasLargo   { get; set; }
    public List<MensajeDisponibleDto> Mensajes { get; set; } = [];
}

// ── Paso 4: generar ─────────────────────────────────────────────────────────

public class GenerarEstrategiaSmsRequest
{
    public int MensajeAutorizadoID { get; set; }
    public List<ContactoAsesorRequest> Asesores { get; set; } = [];
}

/// <summary>Fila del XLSX que se entrega a virtualidad (8 columnas, en este orden).</summary>
public class FilaSmsGeneradaDto
{
    public long   Cedula        { get; set; }
    public string Nombre        { get; set; } = string.Empty;
    public string FechaGestion  { get; set; } = string.Empty;
    public string UltimoPerfil  { get; set; } = string.Empty;
    public string NumeroMarcado { get; set; } = string.Empty;
    public string CelularAsesor { get; set; } = string.Empty;
    public string LinkAsesor    { get; set; } = string.Empty;
    public string Mensaje       { get; set; } = string.Empty;
}

public sealed record ResultadoGeneracionSms(
    byte[] Xlsx,
    long   EstrategiaID,
    string NombreArchivo,
    int    TotalGenerados,
    int    ExcluidosLongitud);
