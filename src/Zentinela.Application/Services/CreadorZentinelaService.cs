using System.Globalization;
using System.Text;
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

    // ── Excel: streaming reader → list de dicts → ClosedXML ──────────────────
    //
    // Usamos StreamFilasAsync para que SQL proyecte solo las columnas pedidas
    // (no SELECT *) y para evitar la materialización de BaseMadreRow + reflexión
    // por celda. ClosedXML sigue construyendo el workbook en memoria — para
    // carteras muy grandes preferí el endpoint CSV (EscribirCsvAsync).
    public async Task<(byte[] Bytes, string NombreArchivo)> ExportarExcelAsync(
        ContextoUsuario contexto, FiltroBaseMadreRequest filtro)
    {
        var liderID  = ResolverLiderID(contexto, filtro.LiderID);
        var columnas = ResolverColumnas(filtro.ColumnasSeleccionadas).ToList();
        var diccionarios = new List<Dictionary<string, object?>>(capacity: 1024);

        await _baseMadreRepo.StreamFilasAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.PromesasVigentes, filtro.RotacionMaxima,
            filtro.SaldoMinimo, filtro.TipoAfiliado, filtro.EstadoFosyga,
            filtro.FuentesCelular,
            filtro.CoincidenciasCelular,
            filtro.FuentesEmail,
            filtro.CoincidenciasEmail,
            propiedadesSeleccionadas: columnas.Select(c => c.Nombre).ToList(),
            procesarFila: reader =>
            {
                var dict = new Dictionary<string, object?>(columnas.Count);
                for (var i = 0; i < columnas.Count; i++)
                    dict[columnas[i].Nombre] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                diccionarios.Add(dict);
                return ValueTask.CompletedTask;
            });

        // Nombre de la cartera para el Excel y el nombre de archivo.
        var carteras = await _carteraRepo.ObtenerTodasAsync();
        var nombreCartera = carteras
            .FirstOrDefault(c => c.ID == filtro.CarteraID)?.NombreCartera
            ?? filtro.CarteraID.ToString();

        var bytes = _excelExporter.GenerarBaseMadre(diccionarios, columnas, nombreCartera);

        var archivo = $"BaseMadre_{SanitizarNombre(nombreCartera)}_{DateTime.Today:yyyyMMdd}.xlsx";
        return (bytes, archivo);
    }

    // ── CSV: streaming reader → texto al stream sin pasar por memoria ────────
    //
    // Escribe BOM UTF-8, encabezados (etiquetas humanas) y luego cada fila al
    // stream salida. Separador `;` para que Excel ES-CO lo abra con doble click.
    // Devuelve el nombre de archivo recomendado para que el controller lo ponga
    // en Content-Disposition.
    public async Task EscribirCsvAsync(
        ContextoUsuario contexto, FiltroBaseMadreRequest filtro,
        Stream output, CancellationToken ct = default)
    {
        var liderID  = ResolverLiderID(contexto, filtro.LiderID);
        var columnas = ResolverColumnas(filtro.ColumnasSeleccionadas).ToList();

        // BOM UTF-8 para que Excel detecte el encoding correctamente con tildes.
        await output.WriteAsync(Bom, ct);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);

        // Encabezado: etiquetas humanas (con tildes) — coherente con el Excel.
        await writer.WriteLineAsync(string.Join(';', columnas.Select(c => EscaparCsv(c.Etiqueta))));

        await _baseMadreRepo.StreamFilasAsync(
            filtro.CarteraID, liderID,
            filtro.UltimoPerfil, filtro.MejorPerfil,
            filtro.PromesasVigentes, filtro.RotacionMaxima,
            filtro.SaldoMinimo, filtro.TipoAfiliado, filtro.EstadoFosyga,
            filtro.FuentesCelular,
            filtro.CoincidenciasCelular,
            filtro.FuentesEmail,
            filtro.CoincidenciasEmail,
            propiedadesSeleccionadas: columnas.Select(c => c.Nombre).ToList(),
            procesarFila: async reader =>
            {
                for (var i = 0; i < columnas.Count; i++)
                {
                    if (i > 0) await writer.WriteAsync(';');
                    if (!reader.IsDBNull(i))
                        await writer.WriteAsync(EscaparCsv(FormatearValorCsv(reader.GetValue(i), columnas[i].Tipo)));
                }
                await writer.WriteAsync('\n');
            },
            ct: ct);

        await writer.FlushAsync(ct);
    }

    public async Task<string> ResolverNombreArchivoAsync(int carteraID, string extension)
    {
        var carteras = await _carteraRepo.ObtenerTodasAsync();
        var nombreCartera = carteras
            .FirstOrDefault(c => c.ID == carteraID)?.NombreCartera
            ?? carteraID.ToString();
        return $"BaseMadre_{SanitizarNombre(nombreCartera)}_{DateTime.Today:yyyyMMdd}.{extension}";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

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
    /// limitado a las columnas solicitadas, usando reflexión. Solo lo usa el
    /// preview (10 filas) — los caminos de exportar leen del DbDataReader.
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

    /// <summary>
    /// Formatea un valor del reader a string para CSV. Fechas en dd/MM/yyyy
    /// para que Excel ES-CO las interprete correctamente. Números con cultura
    /// invariante (punto decimal) para no chocar con el separador `;` ni con
    /// la coma decimal de Excel ES — Excel ES igual reconoce ambos al abrir CSV.
    /// </summary>
    private static string FormatearValorCsv(object valor, TipoColumna tipo) => valor switch
    {
        DateOnly d                          => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTime dt                         => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        decimal m                           => m.ToString(CultureInfo.InvariantCulture),
        double dbl                          => dbl.ToString(CultureInfo.InvariantCulture),
        float f                             => f.ToString(CultureInfo.InvariantCulture),
        _                                   => valor.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Quoting CSV (RFC 4180): si el campo contiene `;`, `"`, salto de línea
    /// o retorno de carro → envolver en `"` y escapar las comillas internas
    /// duplicándolas. En caso contrario lo dejamos tal cual (más compacto).
    /// </summary>
    private static string EscaparCsv(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return string.Empty;
        var requiereQuoting = valor.IndexOfAny([';', '"', '\n', '\r']) >= 0;
        if (!requiereQuoting) return valor;
        return $"\"{valor.Replace("\"", "\"\"")}\"";
    }
}
