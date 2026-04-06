using Zentinela.Core.Entities;

namespace Zentinela.Core.Interfaces;

public interface IBaseMadreRepository
{
    /// <summary>
    /// Retorna titulares de la vista vw_BaseMadreBaseGeneral filtrados por cartera.
    /// liderID null = Admin/Director (sin restricción por líder).
    /// limite null = todos los registros (para exportar).
    /// </summary>
    Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int    carteraID,
        int?   liderID,
        string? nombre,
        string? ultimoPerfil,
        string? mejorPerfil,
        string? departamento,
        string? municipio,
        bool?  tieneFechaPromesa,
        bool?  tieneSaldo,
        int?   limite);
}
