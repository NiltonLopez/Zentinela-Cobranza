using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;

namespace Zentinela.Application.Services;

public class CreadorZentinelaService : ICreadorZentinelaService
{
    private const int LimitePreview = 10;

    private readonly IBaseMadreRepository _baseMadreRepo;
    private readonly ICarteraRepository   _carteraRepo;
    private readonly IExcelExporter       _excelExporter;

    public CreadorZentinelaService(
        IBaseMadreRepository baseMadreRepo,
        ICarteraRepository carteraRepo,
        IExcelExporter excelExporter)
    {
        _baseMadreRepo  = baseMadreRepo;
        _carteraRepo    = carteraRepo;
        _excelExporter  = excelExporter;
    }

    public IEnumerable<ColumnaBaseMadre> ObtenerColumnas() => ColumnasMadre.Todas;

    public async Task<IEnumerable<OpcionCarteraDto>> ObtenerOpcionesAsync(ContextoUsuario contexto)
    {
        // Líder: una entrada por cartera asignada (etiqueta = NombreCartera).
        if (contexto.Rol == Roles.Lider && contexto.LiderID.HasValue)
        {
            var carteras = await _carteraRepo.ObtenerPorLiderAsync(contexto.LiderID.Value);
            return carteras.Select(c => new OpcionCarteraDto
            {
                CarteraID = c.ID,
                LiderID   = contexto.LiderID,
                Etiqueta  = c.NombreCartera,
            }).ToList();
        }

        // Admin / Director: combinaciones individuales + entrada agregada por cartera.
        var combinaciones = await _carteraRepo.ObtenerCombinacionesActivasAsync();
        var resultado = new List<OpcionCarteraDto>();

        foreach (var grupo in combinaciones.GroupBy(lc => lc.CarteraID))
        {
            var nombreCartera = grupo.First().Cartera.NombreCartera;

            foreach (var lc in grupo)
            {
                resultado.Add(new OpcionCarteraDto
                {
                    CarteraID = lc.CarteraID,
                    LiderID   = lc.LiderID,
                    Etiqueta  = $"{nombreCartera} — {lc.Lider.Nombre}",
                });
            }

            resultado.Add(new OpcionCarteraDto
            {
                CarteraID = grupo.Key,
                LiderID   = null,
                Etiqueta  = $"{nombreCartera} Completo",
            });
        }

        return resultado;
    }

    public async Task<PreviewBaseMadreResponse> ObtenerPreviewAsync(
        ContextoUsuario contexto, FiltroBaseMadreRequest filtro)
    {
        var liderID = ResolverLiderID(contexto, filtro.LiderID);
        var (filas, total) = await _baseMadreRepo.ObtenerAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.PromesasVigentes, filtro.RotacionMaxima,
            filtro.SaldoMinimo, filtro.TipoAfiliado, filtro.EstadoFosyga,
            filtro.FuentesCelular,
            filtro.CoincidenciasCelular,
            filtro.FuentesEmail,
            filtro.CoincidenciasEmail,
            limite: LimitePreview);

        var columnas = ResolverColumnas(filtro.ColumnasSeleccionadas);
        return new PreviewBaseMadreResponse
        {
            Total = total,
            Filas = filas.Select(f => FilaADiccionario(f, columnas)).ToList(),
        };
    }

    public async Task<(byte[] Bytes, string NombreArchivo)> ExportarExcelAsync(
        ContextoUsuario contexto, FiltroBaseMadreRequest filtro)
    {
        var liderID = ResolverLiderID(contexto, filtro.LiderID);
        var (filas, _) = await _baseMadreRepo.ObtenerAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.PromesasVigentes, filtro.RotacionMaxima,
            filtro.SaldoMinimo, filtro.TipoAfiliado, filtro.EstadoFosyga,
            filtro.FuentesCelular,
            filtro.CoincidenciasCelular,
            filtro.FuentesEmail,
            filtro.CoincidenciasEmail,
            limite: null);

        // Nombre de la cartera para el Excel y el nombre de archivo
        var carteras = await _carteraRepo.ObtenerTodasAsync();
        var nombreCartera = carteras
            .FirstOrDefault(c => c.ID == filtro.CarteraID)?.NombreCartera
            ?? filtro.CarteraID.ToString();

        var columnas = ResolverColumnas(filtro.ColumnasSeleccionadas);
        var diccionarios = filas.Select(f => FilaADiccionario(f, columnas)).ToList();
        var bytes = _excelExporter.GenerarBaseMadre(diccionarios, columnas, nombreCartera);

        var archivo = $"BaseMadre_{SanitizarNombre(nombreCartera)}_{DateTime.Today:yyyyMMdd}.xlsx";
        return (bytes, archivo);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Líder → siempre su LiderID del JWT, ignorando lo que mande el cliente
    /// (guard de seguridad: un líder no puede ver titulares de otro líder).
    /// Admin / Director → respeta el LiderID del filtro: un valor concreto
    /// restringe la consulta a ese líder; null = "Cartera Completo" (todos).
    /// </summary>
    private static int? ResolverLiderID(ContextoUsuario contexto, int? liderIDDelFiltro) =>
        contexto.Rol == Roles.Lider ? contexto.LiderID : liderIDDelFiltro;

    private static IEnumerable<ColumnaBaseMadre> ResolverColumnas(List<string> seleccionadas) =>
        seleccionadas.Count == 0
            ? ColumnasMadre.Todas
            : ColumnasMadre.Todas.Where(c => seleccionadas.Contains(c.Nombre));

    /// <summary>
    /// Convierte un BaseMadreRow a un diccionario { nombrePropiedad → valor }
    /// limitado a las columnas solicitadas, usando reflexión.
    /// </summary>
    private static Dictionary<string, object?> FilaADiccionario(
        BaseMadreRow fila, IEnumerable<ColumnaBaseMadre> columnas)
    {
        var tipo = typeof(BaseMadreRow);
        return columnas.ToDictionary(
            c => c.Nombre,
            c => tipo.GetProperty(c.Nombre)?.GetValue(fila));
    }

    private static string SanitizarNombre(string nombre) =>
        string.Concat(nombre.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
}
