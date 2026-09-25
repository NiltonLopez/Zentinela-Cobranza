namespace Zentinela.Core.Entities;

/// <summary>
/// Foto de un empleado en <c>DBRecursosHumanos.dbo.FotoEmpleado</c> (la sube RRHH; Zentinela solo
/// la lee). Una por empleado (UQ_FotoEmpleado_IdEmpleado). Se enlaza con líderes y asesores por
/// cédula: <c>Empleado.NumeroDocumento</c>, nunca por <c>Lider.ID</c>.
/// </summary>
public class FotoEmpleado
{
    public int ID { get; set; }

    /// <summary>FK a <c>Empleado.ID</c>.</summary>
    public int IDEmpleado { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>image/jpeg o image/png.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>La imagen original (hasta ~2 MB).</summary>
    public byte[] FileContent { get; set; } = [];

    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}
