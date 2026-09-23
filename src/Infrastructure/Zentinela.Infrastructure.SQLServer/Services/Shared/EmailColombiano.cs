namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Normalización mínima de correos electrónicos de las fuentes de datos.
///
/// Deliberadamente NO es una validación RFC 5322: los archivos de las fuentes traen correos
/// digitados a mano ("Juan.Perez@Gmail.COM ", "juan perez@gmail.com") y lo que interesa es
/// (1) guardarlos en una forma canónica para que la lista negra y la deduplicación comparen
/// por igualdad, y (2) descartar lo que evidentemente no es un correo ("sin arroba",
/// "a@b", "@x.com"). Un correo raro pero plausible se deja pasar: rechazarlo es perder un
/// canal de contacto por un falso positivo.
/// </summary>
internal static class EmailColombiano
{
    /// <summary>
    /// Devuelve el correo en minúsculas, sin espacios (ni en los extremos ni internos), o
    /// <c>null</c> si no tiene forma de correo o excede <paramref name="largo"/> (el tamaño
    /// de la columna destino: un correo truncado es un correo equivocado, no uno corto).
    /// </summary>
    public static string? Normalizar(string? bruto, int largo)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;

        // Los espacios internos son errores de digitación ("juan perez@gmail.com"): ningún
        // proveedor de correo los admite, así que quitarlos recupera el correo en vez de perderlo.
        var email = new string(bruto.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        if (email.Length == 0 || email.Length > largo) return null;

        var arroba = email.IndexOf('@');
        if (arroba < 0 || arroba != email.LastIndexOf('@')) return null;       // exactamente un '@'
        if (email.IndexOf('.', arroba + 1) < 0) return null;                    // el dominio lleva punto

        var primero = email[0];
        var ultimo  = email[^1];
        if (primero is '.' or '@' || ultimo is '.' or '@') return null;

        return email;
    }

    /// <summary>
    /// Dominio de un correo YA normalizado (lo que va después del '@'), o cadena vacía si no
    /// trae '@'. Se usa para la lista negra por dominio ("uniquindio.edu.co").
    /// </summary>
    public static string Dominio(string emailNormalizado)
    {
        var arroba = emailNormalizado.IndexOf('@');
        return arroba < 0 ? string.Empty : emailNormalizado[(arroba + 1)..];
    }
}
