using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;

namespace Zentinela.Application.Interfaces;

public interface IExcelExporter
{
    byte[] GenerarBaseMadre(
        IEnumerable<Dictionary<string, object?>> filas,
        IEnumerable<ColumnaBaseMadre> columnas,
        string nombreCartera);

    byte[] GenerarPlantillaAsignaciones();

    /// <summary>Plantilla "Cartera Completo": columnas NumeroDocumento, Nombre y LiderID.</summary>
    byte[] GenerarPlantillaAsignacionesCompleto();

    /// <summary>
    /// Parsea el Excel de asignaciones y devuelve las filas como pares (NumeroDocumento, Nombre).
    /// La columna A es NumeroDocumento (obligatoria); la columna B es Nombre (opcional).
    /// </summary>
    List<(long NumeroDocumento, string Nombre)> ParsearFilasAsignacion(Stream stream);

    /// <summary>
    /// Parsea el Excel de asignaciones "Cartera Completo": columna A NumeroDocumento,
    /// B Nombre, C LiderID. LiderID es null si la celda está vacía o no es numérica
    /// (la fila se marcará como error en la validación).
    /// </summary>
    List<(long NumeroDocumento, string Nombre, long? LiderID)> ParsearFilasAsignacionCompleto(Stream stream);

    byte[] GenerarPlantillaReporte();

    List<Application.DTOs.Reportes.FilaReporteRaw> ParsearFilasReporte(Stream stream, string extension);

    /// <summary>
    /// Genera el Excel de descarga del Comparador de Carteras con todos los titulares coincidentes.
    /// </summary>
    byte[] GenerarComparativoCartera(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada,
        string nombreOtraCartera);

    /// <summary>
    /// Genera el Excel completo del Comparador de Carteras con todas las carteras combinadas.
    /// Incluye una columna "Cartera de referencia" que identifica la cartera de cada fila.
    /// </summary>
    byte[] GenerarComparativoCompleto(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada);

    /// <summary>
    /// Archivo que se entrega a virtualidad para el envío de SMS (ADR-037). Teléfonos como
    /// texto para que Excel no los muestre en notación científica.
    /// </summary>
    byte[] GenerarEstrategiaSms(IReadOnlyList<DTOs.CreadorVirtualidad.FilaSmsGeneradaDto> filas);

    /// <summary>
    /// Lista para el departamento de localización (ADR-040) con el formato EXACTO que acepta el Cargue de
    /// Fuentes para <paramref name="fuente"/>: una hoja "BASE" con los encabezados de
    /// <see cref="ColumnasEsperadasCargue"/> (clave, Nombre, CarteraID, Cartera, LiderID, Lider y las columnas
    /// de dato vacías para que las llene el proveedor). La clave y las columnas numéricas largas van como
    /// texto para que Excel no las pase a notación científica. Sin filas de ejemplo.
    /// </summary>
    byte[] GenerarListaLocalizacion(DefinicionFuenteCargue fuente, IReadOnlyList<DTOs.InformesLocalizacion.FilaListaLocalizacion> filas);
}
