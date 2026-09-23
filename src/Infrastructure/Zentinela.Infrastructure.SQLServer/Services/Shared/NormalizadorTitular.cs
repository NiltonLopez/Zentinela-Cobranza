using System.Globalization;
using System.Text;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Nombre de titular apto para <c>Titular.Nombre</c>: mayúsculas, sin tildes ni diéresis,
/// CON Ñ, solo letras y espacios simples, máximo 70 caracteres.
///
/// Existe aparte de <see cref="NormalizadorNombre"/> porque aquel es para SMS GSM-7 y quita
/// la Ñ ("PEÑA" → "PENA"), lo cual en la BD es perder información: el titular se llama PEÑA.
///
/// Contexto de datos: en producción <c>Titular.Nombre</c> tiene ~11.485 nombres con mojibake
/// (UTF-8 leído como cp1252, a veces DOBLE: "PIÃƒÂ‘A") y ~2.356 con '?' que ya no se pueden
/// recuperar (el carácter original se perdió en una conversión a ASCII). Este helper repara
/// lo primero y marca lo segundo, para que el cargue pueda decidir si pisa un nombre roto.
/// </summary>
internal static class NormalizadorTitular
{
    /// <summary>Largo de la columna <c>Titular.Nombre</c>.</summary>
    public const int LargoMaximo = 70;

    /// <summary>
    /// <paramref name="Reparado"/>: se corrigió mojibake. <paramref name="Irrecuperable"/>: el
    /// nombre trae '?' (carácter perdido); se conserva tal cual para que sea visible y auditable.
    /// </summary>
    public sealed record Resultado(string Nombre, bool Reparado, bool Irrecuperable);

    // Marcador temporal para la Ñ: FormD la descompondría en N + U+0303 y se perdería la virgulilla
    // junto con las tildes. U+0001 no puede venir en un nombre legítimo.
    private const char MarcaEnie = '\u0001';

    private const int MaxPasadasMojibake = 2;

    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Los 27 caracteres que cp1252 ubica en 0x80–0x9F (donde Latin-1 tiene controles C1).
    /// Es la razón de no usar <c>Encoding.Latin1</c>: "Ã‘" (Ñ doble-leída) lleva U+2018,
    /// que en Latin-1 no existe y se convierte en '?'. Tabla propia para no depender de
    /// <c>CodePagesEncodingProvider</c> (habría que registrarlo globalmente al arranque).
    /// </summary>
    private static readonly Dictionary<char, byte> EspecialesCp1252 = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84,
        ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
        ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C,
        ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
        ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
        ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
        ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    public static Resultado LimpiarNombre(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return new Resultado(string.Empty, false, false);

        var texto = RepararMojibake(bruto);
        var reparado = !string.Equals(texto, bruto, StringComparison.Ordinal);

        // Una 'Ã'/'Â' que sobrevive a la reparación es un byte PERDIDO: el segundo byte de la
        // letra (C3 91 = Ñ, C3 8D = Í, C3 81 = Á) se descartó en algún sistema intermedio.
        // Medido en Titular: "MUÃOZ" era MUÑOZ, "DARÃO" DARÍO, "RAMÃREZ" RAMÍREZ — la misma 'Ã'
        // suelta puede venir de tres letras distintas, así que NO se adivina: pasa a '?' y la
        // fila queda marcada irrecuperable. Dejarla seguir produciría "MUAOZ" y "RAMAREZ" en
        // silencio (FormD convierte Ã en A). No hay 'Ã' legítima en los nombres de la base.
        texto = texto.Replace('Ã', '?').Replace('Â', '?');

        // FormC primero: una Ñ que llegue ya descompuesta (N + U+0303) se recompone y así
        // también queda protegida por el marcador.
        texto = texto.Normalize(NormalizationForm.FormC)
            .Replace('Ñ', MarcaEnie)
            .Replace('ñ', MarcaEnie);

        // FormD separa base y marca (á → a + U+0301, Ü → U + U+0308); se descarta la marca.
        var sinMarcas = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sinMarcas.Append(c);

        var mayusculas = sinMarcas.Replace(MarcaEnie, 'Ñ').ToString().ToUpperInvariant();

        // Todo lo que no sea A-Z, Ñ, espacio o '?' se REEMPLAZA por espacio (no se borra) para
        // no pegar palabras: "MARIA-JOSE" → "MARIA JOSE". Los dígitos también caen: "CRUZ 2"
        // en un nombre es basura de digitación. El '?' se conserva a propósito: es la marca
        // de un carácter irrecuperable y borrarlo escondería el daño.
        var limpio = new StringBuilder(mayusculas.Length);
        var ultimoFueEspacio = true;   // true al inicio: descarta espacios iniciales
        foreach (var c in mayusculas)
        {
            var permitido = c is (>= 'A' and <= 'Z') or 'Ñ' or '?';
            if (permitido)
            {
                limpio.Append(c);
                ultimoFueEspacio = false;
            }
            else if (!ultimoFueEspacio)
            {
                limpio.Append(' ');
                ultimoFueEspacio = true;
            }
        }

        var nombre = limpio.ToString().Trim();
        var irrecuperable = nombre.Contains('?');

        if (nombre.Length > LargoMaximo)
            nombre = nombre[..LargoMaximo].TrimEnd();

        return new Resultado(nombre, reparado, irrecuperable);
    }

    /// <summary>
    /// Deshace UTF-8 leído como cp1252 ("PIÃ‘A" → "PIÑA"), hasta dos veces para el caso
    /// doble ("PEÃƒÂ‘A" → "PEÃ\u0091A" → "PEÑA"). Repara POR TRAMOS: solo toca cada par
    /// lead+continuación, y deja intacto todo lo demás. Una secuencia que no decodifica se
    /// conserva tal cual: mejor un nombre feo que uno inventado.
    /// </summary>
    public static string RepararMojibake(string s)
    {
        var actual = s;
        for (var pasada = 0; pasada < MaxPasadasMojibake && TieneSenalMojibake(actual); pasada++)
        {
            var siguiente = UnaPasada(actual);
            if (string.Equals(siguiente, actual, StringComparison.Ordinal)) break;
            actual = siguiente;
        }
        return actual;
    }

    /// <summary>
    /// Nombre en BD que conviene reemplazar si el archivo trae uno limpio: vacío, con
    /// mojibake (Ã / Â) o con '?' irrecuperable.
    /// </summary>
    public static bool EstaRoto(string? nombreEnBD) =>
        string.IsNullOrWhiteSpace(nombreEnBD)
        || nombreEnBD.Contains('Ã')
        || nombreEnBD.Contains('Â')
        || nombreEnBD.Contains('?');

    // 'Ã' (U+00C3) y 'Â' (U+00C2) son el primer byte (C3 / C2) de toda letra acentuada del
    // español en UTF-8; si no aparecen, no hay mojibake que reparar.
    private static bool TieneSenalMojibake(string s) =>
        s.Contains('Ã') || s.Contains('Â');

    /// <summary>
    /// Una pasada por tramos. Por qué no se convierte el nombre ENTERO a bytes: una letra ya
    /// correcta en otra parte del nombre ("PERÉZ GARCÃƒÂA") se vuelve el byte 0xC9, que solo
    /// es UTF-8 inválido, y hacía fallar la decodificación completa — la parte rota quedaba sin
    /// reparar por culpa de la sana. Medido: 291 de 11.504 nombres rotos reales caían así.
    ///
    /// Solo 'Ã' (C3) y 'Â' (C2) se tratan como primer byte: son los que producen las letras
    /// del español (U+0080–U+00FF). No se usa un rango más amplio porque una Ñ correcta (0xD1)
    /// también es primer byte UTF-8 válido y convertiría "PEÑA" en cirílico.
    /// </summary>
    private static string UnaPasada(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if ((c is 'Ã' or 'Â') && i + 1 < s.Length
                && ByteCp1252(s[i + 1]) is byte cont && cont is >= 0x80 and <= 0xBF)
            {
                var lead = c == 'Ã' ? (byte)0xC3 : (byte)0xC2;
                try
                {
                    sb.Append(Utf8Estricto.GetString([lead, cont]));
                    i++;   // se consumió también la continuación
                    continue;
                }
                catch (DecoderFallbackException) { /* se conserva el original */ }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Byte cp1252 que produce el carácter, o null si cp1252 no puede producirlo.
    /// Incluye 0x80–0x9F como controles C1: son los "huecos" de cp1252 (0x81, 0x8D, 0x8F,
    /// 0x90, 0x9D) que al leer UTF-8 como cp1252 quedan como el control literal.
    /// </summary>
    private static byte? ByteCp1252(char c) =>
        c < 0x100 ? (byte)c : EspecialesCp1252.TryGetValue(c, out var b) ? b : null;
}
