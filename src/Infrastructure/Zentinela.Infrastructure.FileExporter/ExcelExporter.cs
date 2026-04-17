using System.Globalization;
using System.Text;
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
        var ejemplos = new object?[] { 1020304050L, "jperez", "PEREZ JUAN", "Incumplido",
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

    // ── Tipos y alias para resolución flexible de columnas ──────────────────

    private record IndicesExcel(
        int TitularID, int AgenteID, int Nombre, int UltimoPerfil, int MejorPerfil,
        int FechaMejorPerfil, int SaldoTotal, int NumeroMarcado, int Rotacion,
        int FechaUltimaGestion, int FechaPromesa, int ValorPromesa, int MotivoNoPago,
        int FechaActualizacion);

    private static readonly Dictionary<string, string> AliasColumnasExcel = new(StringComparer.Ordinal)
    {
        // TitularID
        ["titularid"]            = "titularid",
        ["titular"]              = "titularid",
        ["numerodocumento"]      = "titularid",
        ["documento"]            = "titularid",
        ["cedula"]               = "titularid",
        ["identificacion"]       = "titularid",
        // AgenteID
        ["agenteid"]             = "agenteid",
        ["agente"]               = "agenteid",
        ["ultusugestion"]        = "agenteid",
        // Nombre
        ["nombre"]               = "nombre",
        ["nombrecliente"]        = "nombre",
        ["nombrecompleto"]       = "nombre",
        ["cliente"]              = "nombre",
        // UltimoPerfil
        ["ultimoperfil"]         = "ultimoperfil",
        ["perfil"]               = "ultimoperfil",
        // MejorPerfil
        ["mejorperfil"]          = "mejorperfil",
        // FechaMejorPerfil
        ["fechamejorperfil"]     = "fechamejorperfil",
        ["fechamejor"]           = "fechamejorperfil",
        // SaldoTotal
        ["saldototal"]           = "saldototal",
        ["saldo"]                = "saldototal",
        ["totalapagar"]          = "saldototal",
        ["inicobro365"]          = "saldototal",
        ["saldocapital"]         = "saldototal",
        ["valortotalcredito"]    = "saldototal",
        ["vlrsaldototal"]        = "saldototal",
        ["pagototal"]            = "saldototal",
        // NumeroMarcado
        ["numeromarcado"]        = "numeromarcado",
        ["marcado"]              = "numeromarcado",
        // Rotacion
        ["rotacion"]             = "rotacion",
        // FechaUltimaGestion
        ["fechaultimagestion"]   = "fechaultimagestion",
        ["ultimagestion"]        = "fechaultimagestion",
        ["fechagestion"]         = "fechaultimagestion",
        // FechaPromesa
        ["fechapromesa"]         = "fechapromesa",
        ["promesa"]              = "fechapromesa",
        // ValorPromesa
        ["valorpromesa"]         = "valorpromesa",
        // MotivoNoPago
        ["motivonopago"]         = "motivonopago",
        ["motivo"]               = "motivonopago",
        ["motvnopago"]           = "motivonopago",
        // FechaActualizacion
        ["fechaactualizacion"]   = "fechaactualizacion",
        ["actualizacion"]        = "fechaactualizacion",
        ["fechaact"]             = "fechaactualizacion",
    };

    /// <summary>
    /// Normaliza un encabezado de columna: quita tildes, espacios, asteriscos y pasa a minúsculas.
    /// Ej: "TitularID *" → "titularid", "Fecha Última Gestión" → "fechaultimagestion"
    /// </summary>
    private static string NormalizarColumna(string s)
    {
        var sinTildes = s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .Replace("*", "")
            .Replace("(", "")
            .Replace(")", "")
            .Trim();
    }

    /// <summary>
    /// Resuelve el índice (1-based) de cada columna del archivo.
    /// Columnas no encontradas quedan en 0; Cell() devuelve string.Empty para índice 0.
    /// </summary>
    private static IndicesExcel ResolverIndicesExcel(Dictionary<string, int> headers)
    {
        var resuelto = new Dictionary<string, int>();
        foreach (var (clave, col) in headers)
        {
            if (AliasColumnasExcel.TryGetValue(clave, out var campo) && !resuelto.ContainsKey(campo))
                resuelto[campo] = col;
        }

        int Get(string campo) => resuelto.TryGetValue(campo, out var c) ? c : 0;

        return new IndicesExcel(
            TitularID:          Get("titularid"),
            AgenteID:           Get("agenteid"),
            Nombre:             Get("nombre"),
            UltimoPerfil:       Get("ultimoperfil"),
            MejorPerfil:        Get("mejorperfil"),
            FechaMejorPerfil:   Get("fechamejorperfil"),
            SaldoTotal:         Get("saldototal"),
            NumeroMarcado:      Get("numeromarcado"),
            Rotacion:           Get("rotacion"),
            FechaUltimaGestion: Get("fechaultimagestion"),
            FechaPromesa:       Get("fechapromesa"),
            ValorPromesa:       Get("valorpromesa"),
            MotivoNoPago:       Get("motivonopago"),
            FechaActualizacion: Get("fechaactualizacion"));
    }

    public List<FilaReporteRaw> ParsearFilasReporte(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<FilaReporteRaw>();

        var todasFilas = ws.RowsUsed().ToList();
        if (todasFilas.Count == 0) return result;

        // ── Leer encabezado y resolver índices por nombre ─────────────────────
        var filaEncabezado = todasFilas[0];
        var headers        = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var celda in filaEncabezado.CellsUsed())
        {
            var clave = NormalizarColumna(celda.GetString());
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = celda.Address.ColumnNumber; // 1-based
        }

        var idx = ResolverIndicesExcel(headers);

        // Función segura: col=0 significa columna no encontrada → string.Empty
        string Cell(IXLRow fila, int col) =>
            col > 0 ? fila.Cell(col).GetString().Trim() : string.Empty;

        // ── Parsear filas de datos ────────────────────────────────────────────
        // CarteraID se inyecta desde la UI en ValidarExcelAsync, no viene en el archivo
        foreach (var fila in todasFilas.Skip(1))
        {
            var titularStr = Cell(fila, idx.TitularID);
            var agenteStr  = Cell(fila, idx.AgenteID);

            // Ignorar filas completamente vacías
            if (string.IsNullOrEmpty(titularStr) && string.IsNullOrEmpty(agenteStr))
                continue;

            result.Add(new FilaReporteRaw
            {
                TitularID          = long.TryParse(titularStr,                      out var tid) ? tid : null,
                UsuarioControl     = NullIfEmpty(agenteStr),
                Nombre             = NullIfEmpty(Cell(fila, idx.Nombre)),
                UltimoPerfil       = NullIfEmpty(Cell(fila, idx.UltimoPerfil)),
                MejorPerfil        = NullIfEmpty(Cell(fila, idx.MejorPerfil)),
                FechaMejorPerfil   = ParseDate(Cell(fila, idx.FechaMejorPerfil)),
                SaldoTotal         = decimal.TryParse(Cell(fila, idx.SaldoTotal),   out var sal) ? sal : null,
                NumeroMarcado      = NullIfEmpty(Cell(fila, idx.NumeroMarcado)),
                Rotacion           = int.TryParse(Cell(fila, idx.Rotacion),         out var rot) ? rot : null,
                FechaUltimaGestion = ParseDate(Cell(fila, idx.FechaUltimaGestion)),
                FechaPromesa       = ParseDate(Cell(fila, idx.FechaPromesa)),
                ValorPromesa       = decimal.TryParse(Cell(fila, idx.ValorPromesa), out var vp)  ? vp  : null,
                MotivoNoPago       = NullIfEmpty(Cell(fila, idx.MotivoNoPago)),
                FechaActualizacion = DateTime.TryParse(Cell(fila, idx.FechaActualizacion), out var fa) ? fa : null,
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
