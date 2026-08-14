using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Extracción de filas de archivos tabulares (CSV / XLSX) como <c>string[][]</c>, y
/// normalización de nombres de columna para resolverlas por nombre y no por posición (ADR-004).
///
/// Estos helpers existen duplicados en varios services del proyecto (CarguePagosService,
/// CargueReporteService, HistoricoVirtualidadService, VirtualidadLlamadasService, ExcelExporter).
/// Esta clase es la versión compartida para el código nuevo; consolidar los duplicados
/// existentes es un refactor aparte, con su propio riesgo.
/// </summary>
internal static class ArchivoTabular
{
    /// <summary>Centinela de <see cref="ResolverColumna"/> cuando ningún alias matchea.</summary>
    public const int ColumnaNoEncontrada = -1;

    /// <summary>
    /// Normaliza un nombre de columna para comparación: quita tildes, espacios, guiones,
    /// guiones bajos, asteriscos y paréntesis, y pasa a minúsculas.
    /// Ej: "Número de Teléfono" → "numerodetelefono"; "Fecha Marcado" → "fechamarcado".
    /// </summary>
    public static string Normalizar(string s)
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
    /// Indexa la fila de encabezado: nombre normalizado → índice de columna (0-based).
    /// Ante encabezados que normalizan al mismo valor, gana el primero.
    /// </summary>
    public static Dictionary<string, int> LeerHeaders(string[] filaEncabezado)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < filaEncabezado.Length; i++)
        {
            var clave = Normalizar(filaEncabezado[i]);
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = i;
        }
        return headers;
    }

    /// <summary>
    /// Devuelve el índice del primer alias que exista en los encabezados, o
    /// <see cref="ColumnaNoEncontrada"/> (-1) si ninguno.
    ///
    /// OJO: el centinela es -1 y NO 0, porque acá los índices son 0-based (sobre
    /// <c>string[]</c>) y el 0 es una columna perfectamente válida. Las versiones
    /// basadas en ClosedXML usan 0 como "no encontrada" porque ahí son 1-based.
    /// Los alias deben venir YA normalizados.
    /// </summary>
    public static int ResolverColumna(Dictionary<string, int> headers, params string[] alias)
    {
        foreach (var a in alias)
            if (headers.TryGetValue(a, out var col)) return col;
        return ColumnaNoEncontrada;
    }

    /// <summary>
    /// Parsea una fecha tolerando los formatos que llegan en archivos reales:
    /// ISO (yyyy-MM-dd), dd/MM/yyyy, fecha con hora, y el número OADate que produce
    /// Excel cuando la celda tiene formato de fecha pero se exporta como número.
    /// </summary>
    public static bool TryParseFecha(string texto, out DateOnly fecha)
    {
        fecha = default;
        var s = (texto ?? string.Empty).Trim();
        if (s.Length == 0) return false;

        // ORDEN CRÍTICO: los formatos explícitos van ANTES de DateTime.TryParse.
        // InvariantCulture interpreta "01/08/2026" como MM/dd (8 de enero), y los archivos
        // que se cargan acá vienen en dd/MM (1 de agosto). Dejar TryParse primero corrompe
        // la fecha en silencio, sin error ni aviso.
        var formatos = new[]
        {
            // ISO primero: no es ambiguo.
            "yyyy-MM-dd", "yyyy/MM/dd",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
            // Día/mes: la convención de los archivos locales.
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss",
        };
        if (DateTime.TryParseExact(s, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
        { fecha = DateOnly.FromDateTime(dt2); return true; }

        // Último recurso para formatos no contemplados (p. ej. "August 1, 2026" o MM/dd).
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
        { fecha = DateOnly.FromDateTime(dt1); return true; }

        // OADate: Excel exporta la fecha como número de serie (p. ej. 46234).
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
            && serial > 1 && serial < 60000)
        {
            try { fecha = DateOnly.FromDateTime(DateTime.FromOADate(serial)); return true; }
            catch { /* no era un OADate válido */ }
        }

        return false;
    }

    /// <summary>
    /// Lee un CSV completo. Detecta el separador (';' o ',') mirando la primera línea.
    /// No maneja campos entrecomillados — es el comportamiento heredado del resto del
    /// proyecto; el consumidor debe tolerar filas con menos columnas de las esperadas.
    /// </summary>
    public static async Task<(List<string[]> Filas, char Separador)> ExtraerFilasCsvAsync(Stream csv)
    {
        var filas = new List<string[]>();
        using var reader = new StreamReader(csv);

        var primera = await reader.ReadLineAsync();
        if (primera is null) return (filas, ',');

        var sep = primera.Contains(';') ? ';' : ',';
        filas.Add(primera.Split(sep));

        while (!reader.EndOfStream)
        {
            var linea = await reader.ReadLineAsync();
            if (linea is null) continue;
            filas.Add(linea.Split(sep));
        }
        return (filas, sep);
    }

    /// <summary>
    /// Lee un XLSX completo. Los archivos de proveedores externos suelen traer varias
    /// hojas (resumen, gráficos, notas) con la data cruda en una hoja llamada "BASE";
    /// se busca esa hoja por nombre y, si no existe, se usa la primera.
    /// </summary>
    public static List<string[]> ExtraerFilasXlsx(Stream stream)
    {
        var filas = new List<string[]>();
        using var workbook = new XLWorkbook(stream);

        var hoja = workbook.Worksheets
            .FirstOrDefault(w => string.Equals(w.Name.Trim(), "BASE", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        var rango = hoja.RangeUsed();
        if (rango is null) return filas;

        foreach (var fila in rango.Rows())
            filas.Add(fila.Cells().Select(CeldaATexto).ToArray());

        return filas;
    }

    /// <summary>
    /// Convierte el valor de una celda a texto conservando la representación útil:
    /// fechas como yyyy-MM-dd y números enteros sin notación científica.
    /// </summary>
    public static string CeldaATexto(IXLCell c)
    {
        if (c.DataType == XLDataType.DateTime)
            return c.GetDateTime().ToString("yyyy-MM-dd");

        // Algunos archivos guardan fechas como número OADate cuando la celda tiene
        // formato de fecha pero el tipo es Number.
        if (c.DataType == XLDataType.Number && c.Style.NumberFormat.Format.Contains('d'))
        {
            try { return DateTime.FromOADate(c.GetDouble()).ToString("yyyy-MM-dd"); }
            catch { /* no era un OADate válido, sigue abajo */ }
        }

        // Celda numérica que NO es fecha: un teléfono guardado como número devolvería
        // "3,00123E+09" vía GetString() y se perdería el archivo entero. Se fuerza la
        // representación entera sin separadores.
        if (c.DataType == XLDataType.Number)
        {
            var d = c.GetDouble();
            if (Math.Abs(d % 1) < double.Epsilon)
                return d.ToString("F0", CultureInfo.InvariantCulture);
        }

        return c.GetString();
    }
}
