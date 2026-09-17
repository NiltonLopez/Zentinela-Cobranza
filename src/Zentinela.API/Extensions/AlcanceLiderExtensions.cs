using System.Security.Claims;
using Zentinela.Application;

namespace Zentinela.API.Extensions;

/// <summary>
/// Resuelve qué líder puede ver una consulta según el rol del usuario autenticado.
///
/// Un Líder solo ve lo suyo, y eso se decide AQUÍ, en el servidor: el <c>liderID</c>
/// que llegue por query string se ignora por completo para ese rol y se usa el del
/// claim <c>liderID</c> del token. Ocultar el selector en la UI no basta — cualquiera
/// puede editar la URL. Los demás roles (Admin, Director, Auxiliar) conservan el
/// filtro opcional tal cual lo pidieron.
/// </summary>
public static class AlcanceLiderExtensions
{
    /// <summary>
    /// Devuelve <c>false</c> cuando el usuario tiene rol Líder pero el token no trae
    /// claim <c>liderID</c>: no hay forma de acotarle la consulta, así que el controller
    /// debe responder <c>403</c> en vez de dejarlo ver todo por accidente.
    /// </summary>
    public static bool TryResolverLiderID(this ClaimsPrincipal user, int? solicitado, out int? efectivo)
    {
        var rol = user.FindFirstValue(ClaimTypes.Role);
        if (rol != Roles.Lider)
        {
            efectivo = solicitado;
            return true;
        }

        if (int.TryParse(user.FindFirstValue("liderID"), out var propio))
        {
            efectivo = propio;
            return true;
        }

        efectivo = null;
        return false;
    }
}
