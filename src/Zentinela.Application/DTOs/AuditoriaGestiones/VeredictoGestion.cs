namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>
/// Veredicto de la IA para una gestión: si el perfil concuerda con la gestión.
/// </summary>
public class VeredictoGestion
{
    public int    Indice        { get; set; }
    public string Resultado     { get; set; } = "";   // "Coincide" | "No coincide" | "Ambiguo"
    public string Justificacion { get; set; } = "";
}
