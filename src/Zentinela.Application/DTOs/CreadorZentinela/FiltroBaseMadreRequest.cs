namespace Zentinela.Application.DTOs.CreadorZentinela;

public class FiltroBaseMadreRequest
{
    public int CarteraID { get; set; }

    /// <summary>Búsqueda parcial sobre el campo Nombre (LIKE %valor%).</summary>
    public string? Nombre { get; set; }

    /// <summary>Tipificación exacta del último perfil de gestión.</summary>
    public string? UltimoPerfil { get; set; }

    /// <summary>Tipificación exacta del mejor perfil histórico.</summary>
    public string? MejorPerfil { get; set; }

    public string? Departamento { get; set; }
    public string? Municipio    { get; set; }

    /// <summary>true → solo titulares con FechaPromesa no nula ni vacía.</summary>
    public bool? TieneFechaPromesa { get; set; }

    /// <summary>true → solo titulares con Saldo no nulo, no vacío y distinto de "0".</summary>
    public bool? TieneSaldo { get; set; }

    /// <summary>
    /// Nombres de columna (propiedad C#) que se incluirán en la respuesta/Excel.
    /// Lista vacía = todas las columnas.
    /// </summary>
    public List<string> ColumnasSeleccionadas { get; set; } = [];
}
