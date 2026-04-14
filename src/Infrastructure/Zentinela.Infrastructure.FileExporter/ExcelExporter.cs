using ClosedXML.Excel;
using Zentinela.Application.DTOs.CreadorZentinela;
using Zentinela.Application.DTOs.Reportes;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.FileExporter;

public class ExcelExporter : IExcelExporter
{
    public byte[] GenerarBaseMadre(
        IEnumerable<Dictionary<string, object?>> filas,
        IEnumerable<ColumnaBaseMadre> columnas,
        string nombreCartera)
    {
        var listaColumnas = columnas.ToList();
        var listaFilas    = filas.ToList();

        using var workbook  = new XLWorkbook();
        // El nombre de hoja en Excel tiene límite de 31 caracteres
        var nombreHoja = nombreCartera.Length > 31
            ? nombreCartera[..31]
            : nombreCartera;
        var ws = workbook.Worksheets.Add(nombreHoja);

        // ── Encabezados ───────────────────────────────────────────────────────
        for (var col = 0; col < listaColumnas.Count; col++)
        {
            var celda = ws.Cell(1, col + 1);
            celda.Value = listaColumnas[col].Etiqueta;
            celda.Style.Font.Bold        = true;
            celda.Style.Font.FontColor   = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Datos ─────────────────────────────────────────────────────────────
        for (var row = 0; row < listaFilas.Count; row++)
        {
            var fila = listaFilas[row];
            for (var col = 0; col < listaColumnas.Count; col++)
            {
                var columna = listaColumnas[col];
                fila.TryGetValue(columna.Nombre, out var valor);

                var celda = ws.Cell(row + 2, col + 1);
                EstablecerValor(celda, valor, columna.Tipo);
            }
        }

        // ── Formato final ─────────────────────────────────────────────────────
        if (listaColumnas.Count > 0 && listaFilas.Count >= 0)
        {
            var rango = ws.RangeUsed();
            if (rango != null)
            {
                rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rango.Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerarPlantillaAsignaciones()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Plantilla");

        // ── Encabezados ───────────────────────────────────────────────────────
        var encabezados = new[] { ("NumeroDocumento", "Número de documento *"), ("Nombre", "Nombre (referencia)") };
        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = encabezados[i].Item2;
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Fila de ejemplo (en gris, solo orientación) ───────────────────────
        ws.Cell(2, 1).Value = "1020304050";
        ws.Cell(2, 2).Value = "APELLIDO NOMBRE (ejemplo — puede borrarse)";
        ws.Row(2).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Plantilla y parser para Cargue de Reportes ───────────────────────────

    // Columnas del reporte — CarteraID viene de la UI, no del archivo.
    // Nombre es obligatorio solo para titulares nuevos.
    private static readonly (string Campo, string Etiqueta)[] ColumnasReporte =
    [
        ("TitularID",          "TitularID *"),
        ("AgenteID",           "AgenteID *"),
        ("Nombre",             "Nombre (obligatorio si es titular nuevo)"),
        ("UltimoPerfil",       "UltimoPerfil"),
        ("MejorPerfil",        "MejorPerfil"),
        ("FechaMejorPerfil",   "FechaMejorPerfil"),
        ("SaldoTotal",         "SaldoTotal"),
        ("NumeroMarcado",      "NumeroMarcado"),
        ("Rotacion",           "Rotacion"),
        ("FechaUltimaGestion", "FechaUltimaGestion"),
        ("FechaPromesa",       "FechaPromesa"),
        ("ValorPromesa",       "ValorPromesa"),
        ("MotivoNoPago",       "MotivoNoPago"),
        ("FechaActualizacion", "FechaActualizacion"),
    ];

    public byte[] GenerarPlantillaReporte()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Plantilla");

        for (var i = 0; i < ColumnasReporte.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = ColumnasReporte[i].Etiqueta;
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Fila de ejemplo en gris (sin CarteraID — la UI lo provee)
        var ejemplos = new object?[] { 1020304050L, 98765, "PEREZ JUAN", "Incumplido",
            "Acuerdo de pago total", null, 1500000m, null, 1, "2026-04-01", null, null, null, DateTime.Today };
        for (var i = 0; i < ejemplos.Length; i++)
        {
            var celda = ws.Cell(2, i + 1);
            celda.Value = ejemplos[i]?.ToString() ?? string.Empty;
            celda.Style.Font.FontColor = XLColor.Gray;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public List<FilaReporteRaw> ParsearFilasReporte(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<FilaReporteRaw>();

        // Layout de columnas (CarteraID se inyecta desde la UI, no viene en el archivo):
        // 1=TitularID, 2=AgenteID, 3=Nombre, 4=UltimoPerfil, 5=MejorPerfil,
        // 6=FechaMejorPerfil, 7=SaldoTotal, 8=NumeroMarcado, 9=Rotacion,
        // 10=FechaUltimaGestion, 11=FechaPromesa, 12=ValorPromesa,
        // 13=MotivoNoPago, 14=FechaActualizacion
        foreach (var fila in ws.RowsUsed().Skip(1))
        {
            string Cell(int col) => fila.Cell(col).GetString().Trim();

            // Ignorar filas completamente vacías (sin TitularID ni AgenteID)
            if (string.IsNullOrEmpty(Cell(1)) && string.IsNullOrEmpty(Cell(2)))
                continue;

            result.Add(new FilaReporteRaw
            {
                TitularID          = long.TryParse(Cell(1),    out var tid)  ? tid    : null,
                AgenteID           = int.TryParse(Cell(2),     out var aid)  ? aid    : null,
                Nombre             = NullIfEmpty(Cell(3)),
                // CarteraID se inyecta desde la UI en ValidarExcelAsync
                UltimoPerfil       = NullIfEmpty(Cell(4)),
                MejorPerfil        = NullIfEmpty(Cell(5)),
                FechaMejorPerfil   = ParseDate(Cell(6)),
                SaldoTotal         = decimal.TryParse(Cell(7),  out var sal)  ? sal    : null,
                NumeroMarcado      = NullIfEmpty(Cell(8)),
                Rotacion           = int.TryParse(Cell(9),     out var rot)  ? rot    : null,
                FechaUltimaGestion = ParseDate(Cell(10)),
                FechaPromesa       = ParseDate(Cell(11)),
                ValorPromesa       = decimal.TryParse(Cell(12), out var vp)   ? vp     : null,
                MotivoNoPago       = NullIfEmpty(Cell(13)),
                FechaActualizacion = DateTime.TryParse(Cell(14), out var fa)  ? fa     : null,
            });
        }

        return result;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static DateOnly? ParseDate(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        // Intentar formato ISO primero, luego dd/MM/yyyy
        if (DateOnly.TryParse(s, out var d)) return d;
        if (DateOnly.TryParseExact(s, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d2)) return d2;
        return null;
    }

    public List<long> ParsearNumerosDocumento(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<long>();

        foreach (var fila in ws.RowsUsed().Skip(1)) // fila 1 = encabezados
        {
            var texto = fila.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(texto)) continue;
            if (long.TryParse(texto, out var numDoc))
                result.Add(numDoc);
        }

        return result;
    }

    private static void EstablecerValor(IXLCell celda, object? valor, TipoColumna tipo)
    {
        if (valor is null)
        {
            celda.Value = Blank.Value;
            return;
        }

        switch (tipo)
        {
            case TipoColumna.Numero:
                if (valor is long l)         celda.Value = l;
                else if (valor is int i)     celda.Value = i;
                else if (valor is decimal d) celda.Value = d;
                else                         celda.Value = valor.ToString();
                celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;

            case TipoColumna.Fecha:
                if (valor is DateOnly do_)   celda.Value = do_.ToDateTime(TimeOnly.MinValue);
                else if (valor is DateTime dt) celda.Value = dt;
                else                         celda.Value = valor.ToString();
                celda.Style.NumberFormat.Format = "DD/MM/YYYY";
                break;

            default:
                celda.Value = valor.ToString();
                break;
        }
    }
}
