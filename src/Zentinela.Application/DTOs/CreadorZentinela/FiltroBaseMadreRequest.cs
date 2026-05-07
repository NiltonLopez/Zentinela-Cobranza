namespace Zentinela.Application.DTOs.CreadorZentinela;

public class FiltroBaseMadreRequest
{
    public int CarteraID { get; set; }

    /// <summary>
    /// LiderID opcional. Solo lo respeta el backend cuando el rol es Director
    /// o Admin: en ese caso se filtra estrictamente por ese líder, o se trae
    /// la cartera completa si llega null. Para el rol Líder se usa siempre su
    /// LiderID del JWT, ignorando este campo (guard de seguridad).
    /// </summary>
    public int? LiderID { get; set; }

    /// <summary>Tipificación exacta del último perfil de gestión.</summary>
    public string? UltimoPerfil { get; set; }

    /// <summary>Tipificación exacta del mejor perfil histórico.</summary>
    public string? MejorPerfil { get; set; }

    /// <summary>
    /// Filtro de promesas de pago:
    /// true  → solo titulares con FechaPromesa >= hoy (promesa vigente).
    /// false → solo titulares SIN promesa vigente (sin fecha o fecha vencida).
    /// null  → sin filtro.
    /// </summary>
    public bool? PromesasVigentes { get; set; }

    /// <summary>Rotación máxima permitida. Filtra sobre la columna Rotacion (string) en memoria.</summary>
    public int? RotacionMaxima { get; set; }

    /// <summary>Saldo mínimo en pesos. Filtra sobre la columna Saldo (string) en memoria.</summary>
    public decimal? SaldoMinimo { get; set; }

    /// <summary>Tipo de afiliado en Fosyga (ej: Cotizante, Beneficiario, Pensionado).</summary>
    public string? TipoAfiliado { get; set; }

    /// <summary>Estado de afiliación en Fosyga (ej: Activo, Inactivo, Retirado).</summary>
    public string? EstadoFosyga { get; set; }

    /// <summary>
    /// Fuentes de datos de celular que deben tener número registrado.
    /// Claves válidas: Cajacopi, Camara, SaludTotal, Positiva, Tigo, SOS, Claro,
    /// MutualSer (cel 1), MutualSer2 (cel 2). MutualSer expone 2 celulares como
    /// columnas independientes — ver docs/para-tener-en-cuenta.md.
    /// Solo se retornan titulares con número en TODAS las fuentes seleccionadas.
    /// </summary>
    public List<string> FuentesCelular { get; set; } = [];

    /// <summary>
    /// Fuentes entre las que se exige coincidencia exacta de número (mínimo 2).
    /// Solo se retornan titulares donde el número de celular sea idéntico
    /// en todas las fuentes seleccionadas y ninguna esté vacía.
    /// Filtro aplicado en memoria.
    /// </summary>
    public List<string> CoincidenciasCelular { get; set; } = [];

    /// <summary>
    /// Fuentes de email que deben tener dirección registrada.
    /// Claves válidas: Cajacopi, Camara, SaludTotal, Positiva, Tigo, Claro, MutualSer.
    /// Solo se retornan titulares con email en TODAS las fuentes seleccionadas.
    /// </summary>
    public List<string> FuentesEmail { get; set; } = [];

    /// <summary>
    /// Fuentes entre las que se exige coincidencia exacta de email (mínimo 2).
    /// Filtro aplicado en memoria.
    /// </summary>
    public List<string> CoincidenciasEmail { get; set; } = [];

    /// <summary>
    /// Nombres de columna (propiedad C#) que se incluirán en la respuesta/Excel.
    /// Lista vacía = todas las columnas.
    /// </summary>
    public List<string> ColumnasSeleccionadas { get; set; } = [];
}
