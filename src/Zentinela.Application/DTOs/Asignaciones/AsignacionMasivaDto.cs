namespace Zentinela.Application.DTOs.Asignaciones;

public enum EstadoValidacion
{
    Ok,
    Nuevo,       // no existía en BD — pasó validaciones — se creará al confirmar
    YaAsignado,  // ya está asignado a esta cartera+líder
    Error,       // falló validación (cédula corta, nombre sin apellido, etc.)
}

/// <summary>Resultado de validación para una fila del Excel de asignaciones.</summary>
public class FilaValidacionDto
{
    public string  NumeroDocumento { get; set; } = string.Empty;
    public string? Nombre          { get; set; }
    public EstadoValidacion Estado { get; set; }
    public string? Razon           { get; set; }
}

/// <summary>Titular que no existía en BD y se creará al confirmar el cargue.</summary>
public class NuevoTitularDto
{
    public long   NumeroDocumento { get; set; }
    public string Nombre          { get; set; } = string.Empty;
}

/// <summary>
/// Asignación (TitularID + LiderID) validada en modo "Cartera Completo".
/// A diferencia del modo por líder, aquí cada fila trae su propio LiderID
/// (columna del Excel), por lo que el cargue reparte titulares entre varios líderes.
/// </summary>
public class AsignacionValidaDto
{
    public long TitularID { get; set; }
    public int  LiderID   { get; set; }
}

/// <summary>
/// Vista previa devuelta tras validar el Excel.
/// Filas contiene hasta 10 registros sin error (Ok/Nuevo/YaAsignado) para vista previa.
/// FilasConError contiene TODAS las filas con error para que el usuario las inspeccione.
/// TitularIDsValidos contiene la lista completa de IDs a confirmar; NuevosTitulares los que se crearán en Titular.
/// </summary>
public class PreviewAsignacionDto
{
    public List<FilaValidacionDto> Filas             { get; set; } = [];
    public List<FilaValidacionDto> FilasConError      { get; set; } = [];
    public int                     TotalValidos       { get; set; }   // Ok — existen en BD, listos
    public int                     TotalNuevos        { get; set; }   // Nuevo — se crearán en Titular
    public int                     TotalErrores       { get; set; }   // Error — fallaron validación
    public List<long>              TitularIDsValidos  { get; set; } = [];
    // Solo se llena en modo "Cartera Completo": pares (TitularID, LiderID) a insertar.
    // En modo por líder queda vacía (el LiderID es único y viene de la selección).
    public List<AsignacionValidaDto> AsignacionesValidas { get; set; } = [];
    public List<NuevoTitularDto>   NuevosTitulares    { get; set; } = [];
}

/// <summary>Body del endpoint de confirmación.</summary>
public class ConfirmarCargueRequest
{
    public int                   CarteraID       { get; set; }
    // null = modo "Cartera Completo" (reemplaza toda la cartera con Asignaciones);
    // con valor = modo por líder (reemplaza solo ese líder con TitularIDs).
    public int?                  LiderID         { get; set; }
    public List<long>            TitularIDs      { get; set; } = [];
    public List<AsignacionValidaDto> Asignaciones { get; set; } = [];
    public List<NuevoTitularDto> NuevosTitulares { get; set; } = [];
}

/// <summary>Resultado del cargue definitivo a Titular_Cartera.</summary>
public class ResultadoCargueDto
{
    public int    Insertados  { get; set; }
    public int    Duplicados  { get; set; }
    public string Mensaje     { get; set; } = string.Empty;
}
