using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Zentinela.Application.DTOs.Carteras;
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
        // AdjustToContents() y el borde interior por celda escanean/estilan TODAS las
        // celdas → O(filas×cols), carísimo en hojas grandes (medido: ~25s de formato en
        // 190k filas). Solo se aplican en hojas chicas; en grandes se usan anchos fijos
        // y sin bordes para mantener la generación rápida y con poca memoria.
        const int umbralFormatoCompleto = 5_000;
        var esGrande = listaFilas.Count > umbralFormatoCompleto;

        if (!esGrande && listaColumnas.Count > 0 && listaFilas.Count > 0)
        {
            var rango = ws.RangeUsed();
            if (rango != null)
            {
                rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rango.Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
            }
        }

        if (esGrande)
            ws.Columns(1, Math.Max(1, listaColumnas.Count)).Width = 22;
        else
            ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerarPlantillaAsignaciones()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Plantilla");

        // ── Encabezados — nombres exactos de las columnas de BD ──────────────
        var encabezados = new[] { "NumeroDocumento", "Nombre" };
        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = encabezados[i];
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

    public byte[] GenerarPlantillaAsignacionesCompleto()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Plantilla");

        // ── Encabezados — modo "Cartera Completo": incluye LiderID por fila ────
        var encabezados = new[] { "NumeroDocumento", "Nombre", "LiderID" };
        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Fila de ejemplo (en gris, solo orientación) ───────────────────────
        ws.Cell(2, 1).Value = "1020304050";
        ws.Cell(2, 2).Value = "APELLIDO NOMBRE (ejemplo — puede borrarse)";
        ws.Cell(2, 3).Value = "12";
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
        ("TitularID",          "TitularID"),
        ("AgenteID",           "AgenteID"),
        ("Nombre",             "Nombre"),
        ("UltimoPerfil",       "UltimoPerfil"),
        ("MejorPerfil",        "MejorPerfil"),
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
        int SaldoTotal, int NumeroMarcado, int Rotacion,
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
            SaldoTotal:         Get("saldototal"),
            NumeroMarcado:      Get("numeromarcado"),
            Rotacion:           Get("rotacion"),
            FechaUltimaGestion: Get("fechaultimagestion"),
            FechaPromesa:       Get("fechapromesa"),
            ValorPromesa:       Get("valorpromesa"),
            MotivoNoPago:       Get("motivonopago"),
            FechaActualizacion: Get("fechaactualizacion"));
    }

    public List<FilaReporteRaw> ParsearFilasReporte(Stream stream, string extension)
        => extension == ".csv"
            ? ParsearFilasReporteCsv(stream)
            : ParsearFilasReporteXlsx(stream);

    private static List<FilaReporteRaw> ParsearFilasReporteXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<FilaReporteRaw>();

        var todasFilas = ws.RowsUsed().ToList();
        if (todasFilas.Count == 0) return result;

        // ── Encabezado → índices 1-based ─────────────────────────────────────
        var filaEncabezado = todasFilas[0];
        var headers        = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var celda in filaEncabezado.CellsUsed())
        {
            var clave = NormalizarColumna(celda.GetString());
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = celda.Address.ColumnNumber; // 1-based
        }

        var idx = ResolverIndicesExcel(headers);

        string Cell(IXLRow fila, int col) =>
            col > 0 ? fila.Cell(col).GetString().Trim() : string.Empty;

        foreach (var fila in todasFilas.Skip(1))
        {
            var titularStr = Cell(fila, idx.TitularID);
            var agenteStr  = Cell(fila, idx.AgenteID);
            if (string.IsNullOrEmpty(titularStr) && string.IsNullOrEmpty(agenteStr)) continue;

            result.Add(ConstruirFila(
                col => Cell(fila, col), idx));
        }

        return result;
    }

    private static List<FilaReporteRaw> ParsearFilasReporteCsv(Stream stream)
    {
        var result = new List<FilaReporteRaw>();
        using var reader = new StreamReader(stream);

        var headerLine = reader.ReadLine();
        if (headerLine is null) return result;

        var sep     = headerLine.Contains(';') ? ';' : ',';
        var partsH  = headerLine.Split(sep);

        // Construir headers con índices 1-based para reutilizar ResolverIndicesExcel
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < partsH.Length; i++)
        {
            var clave = NormalizarColumna(partsH[i]);
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = i + 1; // 1-based
        }

        var idx = ResolverIndicesExcel(headers);

        while (!reader.EndOfStream)
        {
            var linea = reader.ReadLine();
            if (linea is null) continue;
            var partes = linea.Split(sep);

            // 1-based accessor compatible con ResolverIndicesExcel
            string Col(int col) =>
                col > 0 && col <= partes.Length ? partes[col - 1].Trim() : string.Empty;

            var titularStr = Col(idx.TitularID);
            var agenteStr  = Col(idx.AgenteID);
            if (string.IsNullOrEmpty(titularStr) && string.IsNullOrEmpty(agenteStr)) continue;

            result.Add(ConstruirFila(Col, idx));
        }

        return result;
    }

    // Construye FilaReporteRaw desde cualquier fuente (XLSX o CSV) vía un delegado de columna.
    private static FilaReporteRaw ConstruirFila(Func<int, string> col, IndicesExcel idx)
        => new()
        {
            TitularID          = long.TryParse(col(idx.TitularID),          out var tid) ? tid : null,
            UsuarioControl     = NullIfEmpty(col(idx.AgenteID)),
            Nombre             = NullIfEmpty(col(idx.Nombre)),
            UltimoPerfil       = NullIfEmpty(col(idx.UltimoPerfil)),
            MejorPerfil        = NullIfEmpty(col(idx.MejorPerfil)),
            SaldoTotal         = decimal.TryParse(col(idx.SaldoTotal),      out var sal) ? sal : null,
            NumeroMarcado      = NullIfEmpty(col(idx.NumeroMarcado)),
            Rotacion           = int.TryParse(col(idx.Rotacion),            out var rot) ? rot : null,
            FechaUltimaGestion = ParseDate(col(idx.FechaUltimaGestion)),
            FechaPromesa       = ParseDate(col(idx.FechaPromesa)),
            ValorPromesa       = decimal.TryParse(col(idx.ValorPromesa),    out var vp)  ? vp  : null,
            MotivoNoPago       = NullIfEmpty(col(idx.MotivoNoPago)),
            FechaActualizacion = DateTime.TryParse(col(idx.FechaActualizacion), out var fa) ? fa : null,
        };

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

    public List<(long NumeroDocumento, string Nombre)> ParsearFilasAsignacion(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<(long, string)>();

        foreach (var fila in ws.RowsUsed().Skip(1)) // fila 1 = encabezados
        {
            var texto = fila.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(texto)) continue;
            if (!long.TryParse(texto, out var numDoc)) continue;

            var nombre = fila.Cell(2).GetString().Trim();
            result.Add((numDoc, nombre));
        }

        return result;
    }

    public List<(long NumeroDocumento, string Nombre, long? LiderID)> ParsearFilasAsignacionCompleto(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws     = workbook.Worksheet(1);
        var result = new List<(long, string, long?)>();

        foreach (var fila in ws.RowsUsed().Skip(1)) // fila 1 = encabezados
        {
            var texto = fila.Cell(1).GetString().Trim();
            if (string.IsNullOrEmpty(texto)) continue;
            if (!long.TryParse(texto, out var numDoc)) continue;

            var nombre     = fila.Cell(2).GetString().Trim();
            var liderTexto = fila.Cell(3).GetString().Trim();
            long? liderID  = long.TryParse(liderTexto, out var lid) ? lid : null;

            result.Add((numDoc, nombre, liderID));
        }

        return result;
    }

    public byte[] GenerarComparativoCartera(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada,
        string nombreOtraCartera)
    {
        var lista = titulares.ToList();

        using var workbook = new XLWorkbook();
        var nombreHoja = nombreOtraCartera.Length > 31
            ? nombreOtraCartera[..31]
            : nombreOtraCartera;
        var ws = workbook.Worksheets.Add(nombreHoja);

        // ── Encabezados ───────────────────────────────────────────────────────
        var encabezados = new[]
        {
            "Número Documento", "Nombre",
            $"Categoría en {nombreCarteraSeleccionada}",
            $"Perfil en {nombreCarteraSeleccionada}",
            $"Categoría en {nombreOtraCartera}",
            $"Perfil en {nombreOtraCartera}",
            $"Teléfono ({nombreCarteraSeleccionada})",
            $"Teléfono ({nombreOtraCartera})",
            "Saldo Total", "Fecha Última Gestión", "Fecha Promesa", "Valor Promesa",
        };

        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Datos ─────────────────────────────────────────────────────────────
        for (var row = 0; row < lista.Count; row++)
        {
            var t = lista[row];
            var r = row + 2;

            ws.Cell(r, 1).Value = t.NumeroDocumento;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Cell(r, 2).Value = t.Nombre;
            ws.Cell(r, 3).Value = t.CategoriaSeleccionada      ?? "";
            ws.Cell(r, 4).Value = t.PerfilSeleccionada         ?? "";
            ws.Cell(r, 5).Value = t.CategoriaOtraCartera       ?? "";
            ws.Cell(r, 6).Value = t.PerfilOtraCartera          ?? "";
            ws.Cell(r, 7).Value = t.NumeroMarcadoSeleccionada  ?? "";
            ws.Cell(r, 8).Value = t.NumeroMarcadoOtra          ?? "";

            if (t.SaldoTotal.HasValue)
            {
                ws.Cell(r, 9).Value = t.SaldoTotal.Value;
                ws.Cell(r, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            if (t.FechaUltimaGestion.HasValue)
            {
                ws.Cell(r, 10).Value = t.FechaUltimaGestion.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 10).Style.NumberFormat.Format = "DD/MM/YYYY";
            }

            if (t.FechaPromesa.HasValue)
            {
                ws.Cell(r, 11).Value = t.FechaPromesa.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 11).Style.NumberFormat.Format = "DD/MM/YYYY";
            }

            if (t.ValorPromesa.HasValue)
            {
                ws.Cell(r, 12).Value = t.ValorPromesa.Value;
                ws.Cell(r, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        // ── Formato final ─────────────────────────────────────────────────────
        var rango = ws.RangeUsed();
        if (rango != null)
        {
            rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rango.Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerarComparativoCompleto(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada)
    {
        var lista = titulares.ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Comparativo Completo");

        // ── Encabezados ───────────────────────────────────────────────────────
        var encabezados = new[]
        {
            "Cartera de referencia", "Número Documento", "Nombre",
            $"Categoría en {nombreCarteraSeleccionada}",
            $"Perfil en {nombreCarteraSeleccionada}",
            "Categoría en cartera de referencia",
            "Perfil en cartera de referencia",
            $"Teléfono ({nombreCarteraSeleccionada})",
            "Teléfono (cartera de referencia)",
            "Saldo Total", "Fecha Última Gestión", "Fecha Promesa", "Valor Promesa",
        };

        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Datos ─────────────────────────────────────────────────────────────
        for (var row = 0; row < lista.Count; row++)
        {
            var t = lista[row];
            var r = row + 2;

            ws.Cell(r, 1).Value = t.NombreOtraCartera;

            ws.Cell(r, 2).Value = t.NumeroDocumento;
            ws.Cell(r, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            ws.Cell(r, 3).Value = t.Nombre;
            ws.Cell(r, 4).Value = t.CategoriaSeleccionada      ?? "";
            ws.Cell(r, 5).Value = t.PerfilSeleccionada         ?? "";
            ws.Cell(r, 6).Value = t.CategoriaOtraCartera       ?? "";
            ws.Cell(r, 7).Value = t.PerfilOtraCartera          ?? "";
            ws.Cell(r, 8).Value = t.NumeroMarcadoSeleccionada  ?? "";
            ws.Cell(r, 9).Value = t.NumeroMarcadoOtra          ?? "";

            if (t.SaldoTotal.HasValue)
            {
                ws.Cell(r, 10).Value = t.SaldoTotal.Value;
                ws.Cell(r, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }

            if (t.FechaUltimaGestion.HasValue)
            {
                ws.Cell(r, 11).Value = t.FechaUltimaGestion.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 11).Style.NumberFormat.Format = "DD/MM/YYYY";
            }

            if (t.FechaPromesa.HasValue)
            {
                ws.Cell(r, 12).Value = t.FechaPromesa.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 12).Style.NumberFormat.Format = "DD/MM/YYYY";
            }

            if (t.ValorPromesa.HasValue)
            {
                ws.Cell(r, 13).Value = t.ValorPromesa.Value;
                ws.Cell(r, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        // ── Formato final ─────────────────────────────────────────────────────
        var rango = ws.RangeUsed();
        if (rango != null)
        {
            rango.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rango.Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
