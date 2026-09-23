using System.Text.RegularExpressions;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Lista negra de correos (tabla <c>ListaNegraCorreos.CorreoErroneo</c>) evaluada por
/// IGUALDAD, no por subcadena.
///
/// Los SP viejos aplicaban <c>LIKE '%' + CorreoErroneo + '%'</c>: con entradas como
/// "a@gmail.com" o "nt@gmail.com" en la lista, cualquier correo que las contuviera
/// ("lauramejiacarepa@gmail.com") quedaba anulado. En Cámara eso borró 67.826 correos
/// válidos. Aquí una entrada con '@' bloquea ese correo exacto, y una entrada sin '@' con
/// forma de dominio ("uniquindio.edu.co") bloquea todo el dominio exacto.
///
/// Las entradas que no son ni correo ni dominio ("NOTEINE") se ignoran y se cuentan en
/// <see cref="Ignoradas"/> para que el cargue lo reporte: con igualdad no bloquearían nada,
/// y con subcadena bloqueaban de más.
/// </summary>
internal sealed class ListaNegraCorreos
{
    private static readonly Regex FormaDominio = new(@"^[a-z0-9.-]+\.[a-z]{2,}$", RegexOptions.Compiled);

    private readonly HashSet<string> _emails   = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dominios = new(StringComparer.Ordinal);

    /// <summary>Correos exactos distintos en la lista.</summary>
    public int Emails => _emails.Count;

    /// <summary>Dominios distintos en la lista.</summary>
    public int Dominios => _dominios.Count;

    /// <summary>Entradas descartadas por no tener forma de correo ni de dominio.</summary>
    public int Ignoradas { get; private set; }

    private ListaNegraCorreos() { }

    /// <summary>
    /// Clasifica cada entrada (trim + minúsculas) como correo, dominio o ignorada.
    /// Las entradas vacías o nulas se saltan sin contarse: son filas vacías, no datos malos.
    /// </summary>
    public static ListaNegraCorreos Cargar(IEnumerable<string> entradas)
    {
        var lista = new ListaNegraCorreos();
        foreach (var bruta in entradas)
        {
            if (string.IsNullOrWhiteSpace(bruta)) continue;
            var entrada = bruta.Trim().ToLowerInvariant();

            if (entrada.Contains('@'))
                lista._emails.Add(entrada);
            else if (FormaDominio.IsMatch(entrada))
                lista._dominios.Add(entrada);
            else
                lista.Ignoradas++;
        }
        return lista;
    }

    /// <summary>
    /// ¿El correo (ya normalizado con <see cref="EmailColombiano.Normalizar"/>) está bloqueado,
    /// por sí mismo o por su dominio?
    /// </summary>
    public bool Contiene(string emailNormalizado) =>
        _emails.Contains(emailNormalizado)
        || _dominios.Contains(EmailColombiano.Dominio(emailNormalizado));
}
