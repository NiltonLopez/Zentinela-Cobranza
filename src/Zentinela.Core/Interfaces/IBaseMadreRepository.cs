using System.Data.Common;
using Zentinela.Core.Entities;

namespace Zentinela.Core.Interfaces;

public interface IBaseMadreRepository
{
    /// <summary>
    /// Retorna titulares de la vista vw_BaseMadreBaseGeneral filtrados por cartera.
    /// liderID null     = Admin/Director (sin restricción por líder).
    /// fuentesCelular   = AND por cada fuente: el titular debe tener celular en todas.
    /// saldoMinimo      = filtro en memoria (Saldo es string en la vista).
    /// limite null      = todos los registros (para exportar).
    /// </summary>
    Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int            carteraID,
        int?           liderID,
        string?        ultimoPerfil,
        string?        mejorPerfil,
        bool?          promesasVigentes,
        int?           rotacionMaxima,
        decimal?       saldoMinimo,
        string?        tipoAfiliado,
        string?        estadoFosyga,
        List<string>   fuentesCelular,
        List<string>   coincidenciasCelular,
        List<string>   fuentesEmail,
        List<string>   coincidenciasEmail,
        int?           limite);

    /// <summary>
    /// Streaming row-by-row. Proyecta en SQL solo las propiedades pedidas (ahorra
    /// payload + materialización), abre un DbDataReader y entrega cada fila al
    /// callback. Pensado para exportar (Excel/CSV) sin materializar la cartera
    /// completa en memoria. Las columnas se entregan al reader en el mismo orden
    /// que <paramref name="propiedadesSeleccionadas"/>.
    /// </summary>
    Task StreamFilasAsync(
        int                    carteraID,
        int?                   liderID,
        string?                ultimoPerfil,
        string?                mejorPerfil,
        bool?                  promesasVigentes,
        int?                   rotacionMaxima,
        decimal?               saldoMinimo,
        string?                tipoAfiliado,
        string?                estadoFosyga,
        List<string>           fuentesCelular,
        List<string>           coincidenciasCelular,
        List<string>           fuentesEmail,
        List<string>           coincidenciasEmail,
        IReadOnlyList<string>  propiedadesSeleccionadas,
        Func<DbDataReader, ValueTask> procesarFila,
        CancellationToken      ct = default);
}
