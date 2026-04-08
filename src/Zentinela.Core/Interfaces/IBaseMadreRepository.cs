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
}
