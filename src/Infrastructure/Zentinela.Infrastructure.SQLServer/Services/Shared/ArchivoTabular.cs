using System.Globalization;
using System.Runtime.CompilerServices;
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

    // ── Lectura por filas (streaming) para archivos grandes ─────────────────
    // Las versiones Extraer* materializan todo el archivo en List<string[]>. Para el cargue
    // de fuentes (archivos de cientos de miles de filas) se enumera fila por fila.

    /// <summary>Bytes que se inspeccionan para decidir si el CSV es UTF-8 o "ANSI".</summary>
    private const int MuestraCodificacion = 64 * 1024;

    private static readonly byte[] BomUtf8 = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Lee un CSV fila por fila con un parser RFC 4180: campos entre comillas dobles con
    /// <c>""</c> como comilla literal, separadores y saltos de línea dentro de comillas, y
    /// finales de línea <c>\r\n</c> o <c>\n</c>. Las líneas físicas vacías no producen fila.
    ///
    /// Separador: ';' o ',', el que más aparezca FUERA de comillas en la primera línea (ante
    /// empate gana ';', que es lo que exporta Excel en configuración regional colombiana).
    ///
    /// Codificación: BOM UTF-8 → UTF-8. Sin BOM se prueban los primeros 64 KB como UTF-8
    /// estricto; si fallan, se lee como Latin-1 (el CSV "ANSI" que guarda Excel en Windows).
    /// Sin esto "PIÑA" en ANSI llegaría como "PI�A" y el nombre se perdería.
    /// Limitación conocida: un archivo ANSI cuyos primeros 64 KB son ASCII puro se lee como
    /// UTF-8, y una Ñ posterior sale como U+FFFD.
    ///
    /// A diferencia de <see cref="ExtraerFilasCsvAsync"/>, NO cierra el stream recibido.
    /// </summary>
    public static async IAsyncEnumerable<string[]> LeerCsvPorFilasAsync(
        Stream csv, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Detectar la codificación exige releer el inicio: si el stream no permite Seek
        // (p. ej. el cuerpo de una petición HTTP) se copia a memoria.
        MemoryStream? copia = null;
        var fuente = csv;
        if (!csv.CanSeek)
        {
            copia = new MemoryStream();
            await csv.CopyToAsync(copia, ct);
            copia.Position = 0;
            fuente = copia;
        }

        try
        {
            var inicio = fuente.Position;
            var (codificacion, separador) = await DetectarCodificacionYSeparadorAsync(fuente, ct);
            fuente.Position = inicio;

            // Con UTF-8 se deja detectar el BOM para que el StreamReader lo consuma; con
            // Latin-1 no, porque bytes como FF FE son texto legítimo ("ÿþ"), no una marca.
            var esUtf8 = codificacion is UTF8Encoding;
            using var reader = new StreamReader(fuente, codificacion,
                detectEncodingFromByteOrderMarks: esUtf8, bufferSize: 64 * 1024, leaveOpen: true);

            var fila = new List<string>();
            var campo = new StringBuilder();
            var enComillas = false;
            var comillaPendiente = false;   // dentro de comillas se vio '"': ¿cierre o "" escapado?
            var inicioCampo = true;         // aún no se consumió nada del campo actual
            var filaTieneContenido = false; // distingue una línea vacía de una fila con un campo vacío
            var saltarLf = false;           // el carácter anterior fue '\r'

            var buffer = new char[16 * 1024];
            int leidos;
            while ((leidos = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                for (var i = 0; i < leidos; i++)
                {
                    var c = buffer[i];

                    if (saltarLf)
                    {
                        saltarLf = false;
                        if (c == '\n' && !enComillas) continue;
                    }

                    if (enComillas)
                    {
                        if (comillaPendiente)
                        {
                            comillaPendiente = false;
                            if (c == '"') { campo.Append('"'); continue; }
                            enComillas = false;   // era el cierre: c se procesa como texto normal abajo
                        }
                        else if (c == '"') { comillaPendiente = true; continue; }
                        else { campo.Append(c); continue; }
                    }

                    if (c == '"' && inicioCampo)
                    {
                        enComillas = true;
                        inicioCampo = false;
                        filaTieneContenido = true;
                        continue;
                    }

                    if (c == separador)
                    {
                        fila.Add(campo.ToString());
                        campo.Clear();
                        inicioCampo = true;
                        filaTieneContenido = true;
                        continue;
                    }

                    if (c is '\r' or '\n')
                    {
                        saltarLf = c == '\r';
                        if (filaTieneContenido || campo.Length > 0)
                        {
                            fila.Add(campo.ToString());
                            yield return fila.ToArray();
                        }
                        fila.Clear();
                        campo.Clear();
                        inicioCampo = true;
                        filaTieneContenido = false;
                        continue;
                    }

                    // Texto normal. Una comilla a mitad de un campo sin comillas se toma
                    // literal (tolerancia: Excel a veces exporta 15" o O"NEIL así).
                    campo.Append(c);
                    inicioCampo = false;
                    filaTieneContenido = true;
                }
            }

            // Última fila sin salto de línea final (una comilla de cierre pendiente ya
            // cerró el campo: no hay más caracteres que la conviertan en "").
            if (filaTieneContenido || campo.Length > 0)
            {
                fila.Add(campo.ToString());
                yield return fila.ToArray();
            }
        }
        finally
        {
            copia?.Dispose();
        }
    }

    /// <summary>
    /// Lee la muestra inicial (sin mover la posición lógica: el llamador la restaura) y decide
    /// codificación y separador.
    /// </summary>
    private static async Task<(Encoding Codificacion, char Separador)> DetectarCodificacionYSeparadorAsync(
        Stream fuente, CancellationToken ct)
    {
        var muestra = new byte[MuestraCodificacion];
        var total = 0;
        int n;
        while (total < muestra.Length
               && (n = await fuente.ReadAsync(muestra.AsMemory(total, muestra.Length - total), ct)) > 0)
            total += n;

        var finDeArchivo = total < muestra.Length;
        var desde = total >= BomUtf8.Length && muestra.AsSpan(0, BomUtf8.Length).SequenceEqual(BomUtf8)
            ? BomUtf8.Length
            : 0;

        string texto;
        Encoding codificacion;
        if (desde > 0)
        {
            codificacion = new UTF8Encoding(false);
            texto = codificacion.GetString(muestra, desde, total - desde);
        }
        else
        {
            try
            {
                // flush=false salvo en fin de archivo: el corte en 64 KB puede partir un
                // carácter multibyte, y eso no es un error de codificación.
                var estricto = new UTF8Encoding(false, throwOnInvalidBytes: true);
                var chars = new char[estricto.GetMaxCharCount(total)];
                var cuenta = estricto.GetDecoder().GetChars(muestra, 0, total, chars, 0, flush: finDeArchivo);
                texto = new string(chars, 0, cuenta);
                codificacion = new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                codificacion = Encoding.Latin1;
                texto = codificacion.GetString(muestra, 0, total);
            }
        }

        return (codificacion, DetectarSeparador(texto));
    }

    /// <summary>Cuenta ';' y ',' fuera de comillas en la primera línea lógica.</summary>
    private static char DetectarSeparador(string texto)
    {
        int puntoYComa = 0, coma = 0;
        var enComillas = false;
        foreach (var c in texto)
        {
            if (c == '"') { enComillas = !enComillas; continue; }   // "" escapado alterna dos veces: neutro
            if (enComillas) continue;
            if (c is '\r' or '\n') break;
            if (c == ';') puntoYComa++;
            else if (c == ',') coma++;
        }
        return puntoYComa > 0 && puntoYComa >= coma ? ';' : ',';
    }

    /// <summary>
    /// Como <see cref="ExtraerFilasXlsx"/> (misma hoja: "BASE" o la primera) pero enumerando
    /// fila por fila sin materializar la lista. Las filas vacías intermedias se omiten
    /// (<c>RowsUsed</c>); cada fila abarca todas las columnas del rango usado, así que los
    /// índices siguen alineados con el encabezado aunque haya celdas vacías.
    /// El workbook se libera al terminar (o abandonar) la enumeración.
    /// </summary>
    public static IEnumerable<string[]> LeerXlsxPorFilas(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var rango = HojaDeDatos(workbook).RangeUsed();
        if (rango is null) yield break;

        foreach (var fila in rango.RowsUsed())
            yield return fila.Cells().Select(CeldaATexto).ToArray();
    }

    /// <summary>
    /// Número de filas que produciría <see cref="LeerXlsxPorFilas"/> (encabezado incluido).
    /// Sirve para reportar progreso sin convertir celdas a texto.
    /// </summary>
    public static int ContarFilasXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return HojaDeDatos(workbook).RangeUsed()?.RowsUsed().Count() ?? 0;
    }

    private static IXLWorksheet HojaDeDatos(XLWorkbook workbook) =>
        workbook.Worksheets
            .FirstOrDefault(w => string.Equals(w.Name.Trim(), "BASE", StringComparison.OrdinalIgnoreCase))
        ?? workbook.Worksheets.First();
}
