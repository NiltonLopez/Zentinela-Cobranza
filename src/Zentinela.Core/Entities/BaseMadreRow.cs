namespace Zentinela.Core.Entities;

/// <summary>
/// Fila de la vista vw_BaseMadre.
/// Fuente: Titular_Cartera + datos de múltiples fuentes externas.
/// Sin PK — entidad keyless mapeada a la vista.
/// </summary>
public class BaseMadreRow
{
    public int     CarteraID                 { get; set; }
    public int     LiderID                   { get; set; }
    public string  NumeroDocumento           { get; set; } = "";
    public string? Nombre                   { get; set; }
    public string? Saldo                    { get; set; }
    public string? UltimaFechaGestion       { get; set; }
    public string? FechaPromesa             { get; set; }
    public string? ValorPromesa             { get; set; }
    public string? MejorPerfil              { get; set; }
    public string? UltimoPerfil             { get; set; }
    public string? Rotacion                 { get; set; }
    public string? UltimoNumeroMarcado      { get; set; }
    public string? EstadoEnFosyga           { get; set; }
    public string? EPS                      { get; set; }
    public string? Regimen                  { get; set; }
    public string? TipoAfiliado             { get; set; }
    public string? Departamento             { get; set; }
    public string? Municipio                { get; set; }
    public DateOnly? UltimaConsultaFosyga   { get; set; }
    public string? EPSEnCompensados         { get; set; }
    public DateOnly? UltimoPeriodoCompensado    { get; set; }
    public string? TipoAfiliadoCompensados      { get; set; }
    public DateOnly? UltimaConsultaCompensados  { get; set; }
    public int?    EdadRegistraduria        { get; set; }
    public int?    EdadCajaCopi             { get; set; }
    public int?    EdadPositiva             { get; set; }
    public string? CelularCajacopi          { get; set; }
    public string? EmailCajacopi            { get; set; }
    public string? CelularCamara            { get; set; }
    public string? EmailCamara              { get; set; }
    public string? CelularSaludTotal        { get; set; }
    public string? EmailSaludTotal          { get; set; }
    public string? CelularPositiva          { get; set; }
    public string? EmailPositiva            { get; set; }
    public string? CelularTigo              { get; set; }
    public string? EmailTigo                { get; set; }
    public string? CelularSOS               { get; set; }
    public string? CelularClaro             { get; set; }
    public string? EmailClaro               { get; set; }
    public DateOnly? UltimaInsercionClaro   { get; set; }
    public string? CelularMiVacuna          { get; set; }
    public string? EmailMiVacuna            { get; set; }
    public DateOnly? FechaExpedicionMiVacuna { get; set; }
    public string? EstadoMiVacuna           { get; set; }
    public string? CelularMutualSer         { get; set; }
    public string? Celular2MutualSer        { get; set; }
    public string? TelefonoFijoMutualSer    { get; set; }
    public string? EmailMutualSer           { get; set; }
    public string? RegimenMutualSer         { get; set; }
    public string? TipoAfiliadoMutualSer    { get; set; }
    public string? DepartamentoMutualSer    { get; set; }
    public string? MunicipioMutualSer       { get; set; }
    public string? EstadoMutualSer          { get; set; }
    public DateOnly? UltimaConsultaMutualSer { get; set; }
    public long?   NITSura                  { get; set; }
    public string? EmpresaSura              { get; set; }
    public long?   NITPositiva              { get; set; }
    public string? EmpresaPositiva          { get; set; }
    public long?   NITSaludTotal            { get; set; }
    public string? EmpresaSaludTotal        { get; set; }
    public long?   NitSOS                   { get; set; }
    public string? EmpresaSOS               { get; set; }
    public string? EmpresaRUES              { get; set; }
    public string? PlacaSIMIT               { get; set; }
    public string? Marca                    { get; set; }
    public int?    Modelo                   { get; set; }
    public string? Placa                    { get; set; }
}
