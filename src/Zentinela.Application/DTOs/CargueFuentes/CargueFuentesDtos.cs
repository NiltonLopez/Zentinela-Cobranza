using Zentinela.Application.DTOs.Virtualidad.Historico;

namespace Zentinela.Application.DTOs.CargueFuentes;

// Los enums viajan como TEXTO (JsonStringEnumConverter en Program.cs): el front compara
// contra 'PreviewListo', no contra 1.

/// <summary>Estado del trabajo en segundo plano.</summary>
public enum EstadoCargueJob { Validando, PreviewListo, Cargando, Completado, Error, Cancelado }

/// <summary>Fase del pipeline en la que va el trabajo (para la etiqueta de la barra de progreso).</summary>
public enum FaseCargueJob { Leyendo, Columnas, Apoyo, Limpieza, Dedup, Cruces, Preview, Escribiendo, Auditoria, Terminado }

/// <summary>Columna que el archivo puede/debe traer, para el panel "Columnas esperadas".</summary>
public class ColumnaEsperadaDto
{
    public string   Nombre      { get; set; } = string.Empty;
    public string[] Alias       { get; set; } = [];
    public string   Tipo        { get; set; } = string.Empty;
    public bool     Obligatoria { get; set; }
    public string   Descripcion { get; set; } = string.Empty;
}

/// <summary>Fuente cargable (selector del front).</summary>
public class FuenteCargueDto
{
    public string   Fuente               { get; set; } = string.Empty;
    public string   Etiqueta             { get; set; } = string.Empty;
    public string   TablaDestino         { get; set; } = string.Empty;
    public string   Notas                { get; set; } = string.Empty;
    public bool     Habilitada           { get; set; }
    public int      FaseIntegracion      { get; set; }
    public List<ColumnaEsperadaDto> ColumnasEsperadas { get; set; } = [];
    public string[] ExtensionesAceptadas { get; set; } = [];
    public int      LimiteFilasXlsx      { get; set; }
}

/// <summary>
/// Resultado de la validación: conteos, mensajes agrupados y muestra. Solo agregados:
/// el job no retiene las filas (confirmar re-lee el archivo del disco).
/// </summary>
public class PreviewCargueDto
{
    public int TotalLeidas           { get; set; }
    public int Excluidas             { get; set; }
    public int DuplicadasEnArchivo   { get; set; }
    /// <summary>Filas destino de más que produjo la regla al expandir (SIMIT: una por placa). Leídas + estas = excluidas + duplicadas + candidatas.</summary>
    public int AgregadasPorExpansion { get; set; }
    public int Candidatas            { get; set; }
    public int Insertadas            { get; set; }
    public int Refrescadas           { get; set; }
    public int DescartadasVacias     { get; set; }
    public int VaciasQuePurgara      { get; set; }
    public int TitularesNuevos       { get; set; }
    public int NombresReparados      { get; set; }
    public int NombresIrrecuperables { get; set; }
    public int RelacionesNuevas      { get; set; }
    public int FilasSinCarteraLider  { get; set; }

    public List<string> ColumnasEncontradas         { get; set; } = [];
    public List<string> ColumnasFaltantes           { get; set; } = [];
    public List<string> ColumnasOpcionalesFaltantes { get; set; } = [];

    public List<ErrorAgrupadoDto> Errores      { get; set; } = [];
    public List<ErrorAgrupadoDto> Advertencias { get; set; } = [];
    public List<ErrorAgrupadoDto> Info         { get; set; } = [];

    public List<string>        ColumnasMuestra { get; set; } = [];
    public List<List<string?>> Muestra         { get; set; } = [];

    public string   HuellaArchivo { get; set; } = string.Empty;
    public DateOnly SelloPrevisto { get; set; }

    /// <summary>De qué se compone cada chip de conteo (grupos con hasta 10 ejemplos). Ver <see cref="DesgloseChipDto"/>.</summary>
    public List<DesgloseChipDto> Desglose { get; set; } = [];
}

/// <summary>Lo que efectivamente escribió la confirmación.</summary>
public class ResultadoCargueDto
{
    public long     EjecucionID        { get; set; }
    public DateTime SelloFechaConsulta { get; set; }
    public int      Insertadas         { get; set; }
    public int      Refrescadas        { get; set; }
    public int      DescartadasVacias  { get; set; }
    public int      VaciasPurgadas     { get; set; }
    public int      TitularesNuevos    { get; set; }
    public int      NombresReparados   { get; set; }
    /// <summary>Filas con el nombre dañado sin arreglo ('?'): entran, pero no reparan Titular.</summary>
    public int      NombresIrrecuperables { get; set; }
    public int      RelacionesNuevas   { get; set; }
    public int      Advertencias       { get; set; }
    public string   Mensaje            { get; set; } = string.Empty;
    public double   DuracionSegundos   { get; set; }

    /// <summary>De qué se compone cada métrica: conteos finales de la escritura, ejemplos de la preparación.</summary>
    public List<DesgloseChipDto> Desglose { get; set; } = [];
}

/// <summary>Chip de conteo del preview / resultado que se puede desplegar.</summary>
public enum ChipCargue
{
    Leidas, Excluidas, Duplicadas, Insertadas, Refrescadas, VaciasDescartadas, VaciasPurgadas,
    TitularesNuevos, NombresReparados, RelacionesNuevas, SinCarteraLider, Advertencias,
}

/// <summary>Tono de un grupo del desglose (los mismos de las listas agrupadas del front).</summary>
public enum TonoDesglose { Error, Aviso, Info }

/// <summary>
/// Un grupo dentro de un chip: título SIN número de fila, cantidad EXACTA (aunque los
/// ejemplos se corten) y hasta 10 ejemplos ("Fila 12 · 1234567 · valor"). Nunca es negativa,
/// salvo el grupo de ajuste del resultado (la base cambió entre preparar y escribir).
/// </summary>
public class GrupoDesgloseDto
{
    public string       Titulo   { get; set; } = string.Empty;
    public int          Cantidad { get; set; }
    public TonoDesglose Tono     { get; set; }
    public List<string> Ejemplos { get; set; } = [];
}

/// <summary>
/// Composición de un chip. Invariante (la verifica el motor): la suma de <c>Cantidad</c> de
/// sus grupos es el número que muestra el chip (ver <c>DesgloseCargue.NumeroDelChip</c>).
/// </summary>
public class DesgloseChipDto
{
    public ChipCargue             Clave  { get; set; }
    public List<GrupoDesgloseDto> Grupos { get; set; } = [];
}

/// <summary>Foto de un trabajo (lo que devuelve el polling).</summary>
public class CargueJobDto
{
    public Guid            JobId         { get; set; }
    public string          Fuente        { get; set; } = string.Empty;
    public string          Etiqueta      { get; set; } = string.Empty;
    public string          Usuario       { get; set; } = string.Empty;
    public string          NombreArchivo { get; set; } = string.Empty;
    public long            TamanoKB      { get; set; }
    public DateTime        CreadoEn      { get; set; }
    public DateTime        ActualizadoEn { get; set; }
    public EstadoCargueJob Estado        { get; set; }
    public FaseCargueJob   Fase          { get; set; }
    public int             Porcentaje    { get; set; }
    public string?         Mensaje       { get; set; }
    public PreviewCargueDto?   Preview   { get; set; }
    public ResultadoCargueDto? Resultado { get; set; }
    public string?         Error         { get; set; }
    public bool            PuedeCancelar  { get; set; }
    public bool            PuedeConfirmar { get; set; }
}

/// <summary>
/// Fila de la lista "en curso / últimas 48 h": une los jobs en memoria con las ejecuciones
/// registradas en <c>CargueFuenteEjecucion</c> (que sobreviven a un reinicio del servidor).
/// </summary>
public class CargueJobResumenDto
{
    /// <summary>Id del job; solo se puede "Retomar" si <see cref="EnMemoria"/>.</summary>
    public Guid?    JobId         { get; set; }
    public long?    EjecucionID   { get; set; }
    public string   Fuente        { get; set; } = string.Empty;
    public string   Usuario       { get; set; } = string.Empty;
    public string   NombreArchivo { get; set; } = string.Empty;
    public DateTime CreadoEn      { get; set; }
    /// <summary>Mismo vocabulario que <see cref="EstadoCargueJob"/>.</summary>
    public string   Estado        { get; set; } = string.Empty;
    public int      Porcentaje    { get; set; }
    public int?     Insertadas    { get; set; }
    public int?     Refrescadas   { get; set; }
    public bool     EnMemoria     { get; set; }
}
