namespace Zentinela.Application.DTOs.Carteras;

public class CarteraDropdownDto
{
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = "";
}

/// <summary>
/// Datos de un titular que en la cartera seleccionada tiene Sin Gestión/Contacto Negativo
/// pero en otra cartera tiene Ya Pagó o Contacto Positivo.
/// </summary>
public class TitularComparativoDto
{
    public long      NumeroDocumento    { get; set; }
    public string    Nombre             { get; set; } = "";

    /// <summary>Estado actual del titular en la cartera seleccionada.</summary>
    public string?   PerfilSeleccionada { get; set; }

    /// <summary>Estado del titular en la otra cartera (positivo o pago).</summary>
    public string?   PerfilOtraCartera  { get; set; }

    /// <summary>Nombre de la cartera donde el titular tiene el estado positivo/pago.</summary>
    public string    NombreOtraCartera  { get; set; } = "";

    /// <summary>Teléfono marcado en la cartera seleccionada (Sin Gestión).</summary>
    public string?   NumeroMarcadoSeleccionada { get; set; }

    /// <summary>Teléfono marcado en la cartera comparada (con estado positivo/pago).</summary>
    public string?   NumeroMarcadoOtra         { get; set; }

    public decimal?  SaldoTotal         { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public decimal?  ValorPromesa       { get; set; }
}

/// <summary>Sección comparativa de la cartera seleccionada vs una cartera específica.</summary>
public class ComparadorVsCarteraDto
{
    public int    CarteraID          { get; set; }
    public string NombreCartera      { get; set; } = "";

    /// <summary>Total de titulares con estado positivo/pago en esta cartera que son Sin Gestión en la seleccionada.</summary>
    public int    TotalCoincidencias { get; set; }

    /// <summary>Vista previa de hasta 5 titulares.</summary>
    public List<TitularComparativoDto> Preview { get; set; } = [];
}

/// <summary>Resultado completo del comparador: una sección por cada cartera con coincidencias.</summary>
public class ComparadorCarterasDto
{
    public int    CarteraID                 { get; set; }
    public string NombreCarteraSeleccionada { get; set; } = "";

    /// <summary>Total de titulares Sin Gestión en la cartera seleccionada (base del comparativo).</summary>
    public int    TotalSinGestion           { get; set; }

    /// <summary>Carteras con al menos una coincidencia, ordenadas por TotalCoincidencias desc.</summary>
    public List<ComparadorVsCarteraDto> Comparaciones { get; set; } = [];
}
