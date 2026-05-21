namespace Zentinela.Application.DTOs.Carteras;

public class CarteraLiderDropdownDto
{
    public int    CarteraID { get; set; }
    /// <summary>
    /// LiderID concreto = combinación líder-cartera específica.
    /// null = "Cartera Completa" — agrega todos los líderes de esa cartera
    /// (igual que en el Creador Zentinela). Visible solo a Admin/Director.
    /// </summary>
    public int?   LiderID   { get; set; }
    public string Etiqueta  { get; set; } = ""; // "NombreCartera — NombreLider" o "NombreCartera Completo"
}

public class CalificadorCarteraDto
{
    // Bloque 1: volumen
    public int TotalTitulares { get; set; }

    // Bloque 2: gestión (puede estar vacío si UltimoEstadoTitular no tiene datos)
    public bool    GestionDisponible         { get; set; }
    public int     TitularesGestionados      { get; set; }
    public decimal SaldoTotal                { get; set; }
    public decimal SaldoPromedio             { get; set; }
    public int     TitularesConAcuerdoVigente { get; set; }
    public decimal ValorTotalPromesas         { get; set; }

    // Bloque 3: rotación
    public decimal?          RotacionPromedio  { get; set; }
    public List<DesglosePar> DesgloseRotacion  { get; set; } = [];

    // Bloque 4: tipificación último perfil
    public List<DesglosePar> DesglosePerfil { get; set; } = [];

    // Bloque 4: Fosyga
    public int               TitularesConFosyga    { get; set; }
    public List<DesglosePar> DesgloseTipoAfiliado  { get; set; } = [];

    // Bloque 5: régimen
    public List<DesglosePar> DesgloseRegimen { get; set; } = [];

    // Bloque 6: EPS (top 10 + Otras)
    public List<DesglosePar> DesgloseEPS { get; set; } = [];

    // Bloque 7: departamento
    public List<DesglosePar> DesgloseDepartamento { get; set; } = [];

    // Bloque 8: contactabilidad
    public int               ConCelular            { get; set; }
    public int               ConEmail              { get; set; }
    public int               ConCelularYEmail      { get; set; }
    public int               SinContacto           { get; set; }
    public List<DesglosePar> DesgloseFuentesCelular { get; set; } = [];

    // Bloque 9: edad Registraduría (solo aparecen mayores de 33 años)
    public int      ConEdadRegistraduria      { get; set; } // titulares con edad reportada
    public int      SinEdadRegistraduria      { get; set; } // no aparecen (< 33 o sin dato)
    public decimal? EdadPromedioRegistraduria { get; set; } // promedio dentro de quienes sí aparecen

    // Bloque 10: porcentajes de localización por fuente
    public List<LocalizacionFuente> Localizacion { get; set; } = [];
}

public class DesglosePar
{
    public string  Etiqueta   { get; set; } = "";
    public int     Cantidad   { get; set; }
    public decimal Porcentaje { get; set; }
}

public class LocalizacionFuente
{
    public string Fuente          { get; set; } = "";
    public int    Encontrados     { get; set; }
    public int    NoEncontrados   { get; set; }
    public int    FaltanPorBuscar { get; set; }
}
