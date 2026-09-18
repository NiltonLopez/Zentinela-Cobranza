using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Nombre de persona apto para un SMS GSM-7: sin tildes ni Ñ, solo A-Z, 0-9 y espacios
/// simples, en mayúsculas. Los caracteres especiales se REEMPLAZAN por espacio (no se
/// borran) para no pegar palabras: "LUIS-ALBERTO" → "LUIS ALBERTO", no "LUISALBERTO".
///
/// No confundir con <see cref="ArchivoTabular.Normalizar"/> (encabezados: quita los
/// espacios y deja minúsculas) ni con <c>AsignacionMasivaService.LimpiarNombre</c>
/// (conserva tildes porque va a la BD, no a un SMS).
/// </summary>
internal static class NormalizadorNombre
{
    private static readonly Regex NoPermitidos = new(@"[^A-Z0-9 ]", RegexOptions.Compiled);
    private static readonly Regex Espacios     = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Limpiar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return string.Empty;

        var texto = RepararMojibake(bruto);

        // FormD separa la base de la marca: Ñ → N + U+0303, á → a + U+0301. Se descarta la marca.
        var sinMarcas = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        var mayusculas = sinMarcas.ToUpperInvariant();
        var soloPermitidos = NoPermitidos.Replace(mayusculas, " ");
        return Espacios.Replace(soloPermitidos, " ").Trim();
    }

    /// <summary>
    /// Heurística para archivos guardados como Latin-1 y leídos como UTF-8 ("MARÃA" por
    /// "MARÍA"): si aparece "Ã", se reinterpretan los bytes. Sin esto FormD produce "MARAA".
    /// Solo se aplica cuando la reinterpretación no introduce el carácter de reemplazo.
    /// </summary>
    private static string RepararMojibake(string s)
    {
        if (!s.Contains('Ã') && !s.Contains('Â')) return s;
        try
        {
            var reparado = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(s));
            return reparado.Contains('�') ? s : reparado;
        }
        catch { return s; }
    }
}
