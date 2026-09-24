using System.Text.RegularExpressions;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// NIT colombiano escrito con su dígito de verificación ('NIT-DV'). Las tablas de DBCallCenter
/// guardan el NIT SIN el DV (bigint): <c>ARLSura.NIT = CamaraEmpresa.NitEmpresa</c> cruza por ese
/// número. Quien lee dígitos ignorando lo demás pegaría el DV al NIT ('900123456-7' →
/// 9001234567) y la fila no cruzaría con nada; quien exige solo dígitos lo anularía.
/// </summary>
internal static class NitColombiano
{
    // Dígitos (con puntos o espacios de miles) + guion + UN dígito. Sin guion no hay forma de
    // saber si el último dígito es DV: '9001234567' se deja tal cual.
    private static readonly Regex NitConDv = new(@"^\s*(\d[\d.\s]*?)\s*-\s*\d\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 'NIT-DV' ('900123456-7', '900.123.456 - 7') → el NIT sin el DV ('900123456', '900.123.456';
    /// los separadores de miles los quita quien lo convierta a número). Cualquier otra cosa se
    /// devuelve igual y <paramref name="quitado"/> queda en false.
    /// </summary>
    public static string? QuitarDigitoVerificacion(string? bruto, out bool quitado)
    {
        quitado = false;
        if (bruto is null) return null;
        var m = NitConDv.Match(bruto);
        if (!m.Success) return bruto;
        quitado = true;
        return m.Groups[1].Value;
    }
}
