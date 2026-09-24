namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Una fila del archivo ya limpia (o marcada para exclusión). Los datos van en un arreglo
/// alineado con <c>Definicion.ColumnasDato</c> y no en propiedades con nombre: así el motor
/// sirve a cualquier fuente sin conocer sus columnas.
/// </summary>
internal sealed class FilaCargue
{
    /// <summary>Línea del archivo (1 = encabezado). Solo para desempates y diagnóstico.</summary>
    public int Linea { get; init; }

    public long TitularID { get; set; }

    /// <summary>Nombre limpio (<c>NormalizadorTitular</c>) o null si vino vacío.</summary>
    public string? Nombre { get; set; }
    public bool NombreReparado { get; set; }
    public bool NombreIrrecuperable { get; set; }

    /// <summary>Lo decide la fase de cruces: el nombre en BD está roto y el del archivo sirve.</summary>
    public bool ReparaNombreEnBD { get; set; }

    /// <summary>Par resuelto y validado contra Lider_Cartera activo; null = fila sin relación.</summary>
    public int? CarteraID { get; set; }
    public int? LiderID { get; set; }

    /// <summary>Por qué la fila no tiene relación (par inactivo, cartera no encontrada…); null = no traía cartera/líder o sí la tiene.</summary>
    public string? MotivoSinRelacion { get; set; }

    /// <summary>Valores por columna de dato: string | long | DateOnly | null.</summary>
    public required object?[] Datos { get; init; }

    /// <summary>Valores crudos de <c>ColumnasArchivoExtra</c> (SIMIT Placa1..15).</summary>
    public required string?[] Extra { get; init; }

    /// <summary>EstadoEn&lt;Fuente&gt;.</summary>
    public bool Flag { get; set; }

    /// <summary>Primer motivo de exclusión (orden fijo); null = la fila entra.</summary>
    public string? MotivoExclusion { get; set; }

    public int DatosNoNulos => Datos.Count(d => d is not null);

    /// <summary>Copia para <c>IReglaFuente.Expandir</c> (1 fila del archivo → N filas destino).</summary>
    public FilaCargue Clonar() => new()
    {
        Linea               = Linea,
        TitularID           = TitularID,
        Nombre              = Nombre,
        NombreReparado      = NombreReparado,
        NombreIrrecuperable = NombreIrrecuperable,
        CarteraID           = CarteraID,
        LiderID             = LiderID,
        MotivoSinRelacion   = MotivoSinRelacion,
        Datos               = (object?[])Datos.Clone(),
        Extra               = (string?[])Extra.Clone(),
        Flag                = Flag,
    };
}
