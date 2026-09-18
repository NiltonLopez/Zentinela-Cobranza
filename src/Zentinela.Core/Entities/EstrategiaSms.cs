namespace Zentinela.Core.Entities;

/// <summary>
/// Auditoría de cada archivo SMS generado por el Creador de Virtualidad: quién lo
/// generó, con qué mensaje, cuántos titulares entraron y cuántos se excluyeron y por qué.
/// No registra envíos — eso lo hace virtualidad en HistoricoVirtualidad.
/// </summary>
public class EstrategiaSms
{
    public long     ID                  { get; set; }
    public int      LiderID             { get; set; }
    public string   UsuarioGenero       { get; set; } = string.Empty;
    public DateTime FechaGeneracion     { get; set; }
    public int      MensajeAutorizadoID { get; set; }
    public int      TotalLeidos         { get; set; }
    public int      TotalGenerados      { get; set; }
    public int      ExcluidosPerfil     { get; set; }
    public int      ExcluidosListaNegra { get; set; }
    public int      ExcluidosLongitud   { get; set; }
    public int      ExcluidosOtros      { get; set; }
    public string   HuellaArchivo       { get; set; } = string.Empty;
    public string   NombreArchivo       { get; set; } = string.Empty;
}
