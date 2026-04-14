namespace Zentinela.Application.DTOs.Carteras;

public class CrearRelacionDto
{
    /// <summary>ID de una cartera existente. Null si se crea una cartera nueva.</summary>
    public int? CarteraID { get; set; }

    /// <summary>Nombre de la nueva cartera. Requerido si CarteraID es null.</summary>
    public string? NombreCartera { get; set; }

    public int LiderID { get; set; }
}
