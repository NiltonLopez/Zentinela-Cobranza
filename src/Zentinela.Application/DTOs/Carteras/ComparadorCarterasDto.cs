namespace Zentinela.Application.DTOs.Carteras;

public class CarteraDropdownDto
{
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = "";
}

/// <summary>
/// Datos de un titular que en otra cartera tiene una categoría de gestión
/// estrictamente mejor que en la cartera seleccionada (ADR-029).
/// </summary>
public class TitularComparativoDto
{
    public long      NumeroDocumento    { get; set; }
    public string    Nombre             { get; set; } = "";

    /// <summary>Categoría estandarizada en la cartera seleccionada (Ya pago, Promesa, ...).</summary>
    public string?   CategoriaSeleccionada { get; set; }

    /// <summary>Perfil crudo (MejorPerfil) en la cartera seleccionada, como detalle.</summary>
    public string?   PerfilSeleccionada { get; set; }

    /// <summary>Categoría estandarizada en la otra cartera.</summary>
    public string?   CategoriaOtraCartera { get; set; }

    /// <summary>Perfil crudo (MejorPerfil) en la otra cartera, como detalle.</summary>
    public string?   PerfilOtraCartera  { get; set; }

    /// <summary>Nombre de la cartera donde el titular tiene la mejor categoría.</summary>
    public string    NombreOtraCartera  { get; set; } = "";

    /// <summary>Teléfono marcado en la cartera seleccionada.</summary>
    public string?   NumeroMarcadoSeleccionada { get; set; }

    /// <summary>Teléfono marcado en la cartera comparada.</summary>
    public string?   NumeroMarcadoOtra         { get; set; }

    public decimal?  SaldoTotal         { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa       { get; set; }
    public decimal?  ValorPromesa       { get; set; }
}

/// <summary>Sección comparativa de la cartera seleccionada vs una cartera específica.</summary>
public class ComparadorVsCarteraDto
{
    public int    CarteraID          { get; set; }
    public string NombreCartera      { get; set; } = "";

    /// <summary>Total de titulares con mejor categoría en esta cartera que en la seleccionada.</summary>
    public int    TotalCoincidencias { get; set; }

    /// <summary>Vista previa de hasta 5 titulares.</summary>
    public List<TitularComparativoDto> Preview { get; set; } = [];
}

/// <summary>Total de titulares de la cartera seleccionada en una categoría estandarizada.</summary>
public class CategoriaTotalDto
{
    public string Categoria { get; set; } = "";
    public int    Total     { get; set; }
}

/// <summary>Resultado completo del comparador: una sección por cada cartera con coincidencias.</summary>
public class ComparadorCarterasDto
{
    public int    CarteraID                 { get; set; }
    public string NombreCarteraSeleccionada { get; set; } = "";

    /// <summary>
    /// Total de titulares de la base del comparativo: todos los que en la cartera
    /// seleccionada NO están en Ya pago.
    /// </summary>
    public int    TotalBaseComparativo      { get; set; }

    /// <summary>
    /// Desglose de TODA la cartera seleccionada por categoría estandarizada
    /// (incluye Ya pago), ordenado de mejor a peor categoría.
    /// </summary>
    public List<CategoriaTotalDto> DesglosePorCategoria { get; set; } = [];

    /// <summary>Carteras con al menos una coincidencia, ordenadas por TotalCoincidencias desc.</summary>
    public List<ComparadorVsCarteraDto> Comparaciones { get; set; } = [];
}
