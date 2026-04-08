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

    public async Task<PreviewBaseMadreResponse> ObtenerPreviewAsync(
        ContextoUsuario contexto, FiltroBaseMadreRequest filtro)
    {
        var liderID = ResolverLiderID(contexto);
        var (filas, total) = await _baseMadreRepo.ObtenerAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.TieneFechaPromesa,
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
        var liderID = ResolverLiderID(contexto);
        var (filas, _) = await _baseMadreRepo.ObtenerAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.TieneFechaPromesa,
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
    /// Líder → usa su LiderID para filtrar solo sus titulares.
    /// Admin / Director → null (sin restricción por líder).
    /// </summary>
    private static int? ResolverLiderID(ContextoUsuario contexto) =>
        contexto.Rol == Roles.Lider ? contexto.LiderID : null;

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
