using System.Text.RegularExpressions;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Números enteros (teléfonos, documentos, NIT, enteros de fuentes) que llegan como TEXTO desde
/// un CSV o un XLSX, con la parte decimal que les pega la herramienta que exportó el archivo.
///
/// Formas verificadas el 2026-09-24 exportando a CSV con Excel en es-CO (el decimal es la coma):
/// formato General → <c>3001234567</c> (sin decimales); <c>0,00</c> → <c>3001234567,00</c>;
/// <c>#.##0,00</c> → <c>3.001.234.567,00</c>; <c>0,0</c> → <c>3001234567,0</c>;
/// <c>#.##0</c> → <c>3.001.234.567</c>. Un número de 12 dígitos en General sale como
/// <c>5,73001E+11</c>: se perdieron dígitos y NO se acepta (la notación científica trae una
/// letra o no cuadra en largo). pandas escribe <c>3001234567.0</c> cuando la columna es float
/// (porque tiene vacíos). En XLSX no pasa: <c>ArchivoTabular.CeldaATexto</c> ya convierte las
/// celdas numéricas a entero.
///
/// Regla: el separador del final (<c>.</c> o <c>,</c>) seguido de 1 o 2 dígitos es DECIMAL solo si
/// ese carácter no aparece antes en el número; si aparece, es agrupación de miles o formato de
/// teléfono (<c>300.123.45.00</c>) y no se toca. Así <c>3.001.234.567,00</c> y
/// <c>1,234,567.00</c> pierden la parte decimal, y <c>1.234</c> (tres dígitos: miles) no.
/// </summary>
internal static class NumeroEnTexto
{
    // entero + separador + 1 o 2 dígitos al final. El resto de la validación (que el separador no
    // aparezca antes) va en código: con una expresión regular sería ilegible.
    private static readonly Regex ParteDecimal = new(
        @"^(?<entero>.*\d)(?<sep>[.,])(?<dec>\d{1,2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Quita UNA parte decimal nula (<c>.0</c>, <c>.00</c>, <c>,0</c>, <c>,00</c>) del final, con
    /// el texto recortado. Cualquier otra cosa se devuelve igual (recortada): <c>'3001234567.0.0'</c>
    /// conserva su segundo <c>.0</c>, que después no cuadra como número.
    /// </summary>
    public static string QuitarDecimalNulo(string texto)
    {
        var t = texto.Trim();
        return TryParteDecimal(t, out var entero, out var decimales) && decimales.All(c => c == '0')
            ? entero
            : t;
    }

    /// <summary>
    /// El texto trae una parte decimal (separador decimal + 1 o 2 dígitos al final). Llamarlo
    /// DESPUÉS de <see cref="QuitarDecimalNulo"/>: lo que quede es un decimal real
    /// (<c>'300123456.7'</c>, <c>'12345,25'</c>) y un entero no lo acepta.
    /// </summary>
    public static bool TieneDecimales(string texto) => TryParteDecimal(texto.Trim(), out _, out _);

    private static bool TryParteDecimal(string t, out string entero, out string decimales)
    {
        entero = decimales = "";
        var m = ParteDecimal.Match(t);
        if (!m.Success) return false;
        var sep = m.Groups["sep"].Value[0];
        if (m.Groups["entero"].Value.Contains(sep)) return false;   // agrupación, no decimal
        entero = m.Groups["entero"].Value;
        decimales = m.Groups["dec"].Value;
        return true;
    }
}
