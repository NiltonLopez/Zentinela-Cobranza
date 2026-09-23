using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Cajacopi: traducción de <c>sp_ProcesarCajacopiAuxiliar</c> (respaldo en docs/sql/legacy/).
///
/// Lo que se tradujo (todo en <see cref="Transformar"/>, antes de la validación genérica):
/// <list type="bullet">
/// <item>Paso 5 del SP: fijo de 7 dígitos → indicativo por departamento/municipio. Aquí con
///   <c>PrefijosFijo.Resolver</c> (clave sin tildes, puntos ni comas); si no se resuelve sin
///   ambigüedad, el fijo se ANULA con advertencia: un indicativo adivinado marca a otra persona.</item>
/// <item>Paso 9: fijo con '11111' → NULL (relleno del proveedor). Se mira sobre los dígitos que
///   mandó la fuente, antes de ponerle indicativo: "601" + "1111234" contendría '11111' sin
///   que el número original lo tuviera.</item>
/// <item>Paso 9: correo con 'NOTIEN' → NULL (se compara sin espacios: "NO TIENE" también cae).</item>
/// <item>Pasos 9 "fijo NOT LIKE '6%'" y "celular NOT LIKE '3%'": los hace el motor (fijo 6xx y
///   celular 3xx de 10 dígitos), igual que vacíos, lista negra de correos, dedup, Titular y relaciones.</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Calculaba <c>EstadoEnCajacopi</c> ANTES de limpiar: un titular con solo un fijo '1111111'
///   quedaba con flag 1 y ningún dato. Aquí el motor calcula el flag después de esta regla y de
///   la validación, así que refleja lo que realmente se guarda.</item>
/// <item>El prefijo se buscaba con dos LEFT JOIN por UPPER(texto) exacto (con tildes) a
///   PrefijoMunicipio: un municipio repetido en varios departamentos multiplicaba filas, y
///   'BOGOTÁ' no casaba con 'BOGOTA'.</item>
/// <item>Si el departamento no casaba, dejaba el fijo de 7 dígitos tal cual y el paso 9 lo anulaba
///   después sin decir nada; aquí se anula con advertencia contada.</item>
/// <item>Las limpiezas del paso 9 corrían sobre TODA la tabla Cajacopi en cada carga, no sobre lo cargado.</item>
/// </list>
/// </summary>
internal sealed class ReglaCajacopi : IReglaFuente
{
    private const string Fijo = "NumeroTelefonoFijo";
    private const string Email = "Email";
    private const string Departamento = "Departamento";
    private const string Municipio = "Municipio";

    public string Fuente => CatalogoCargueFuentes.Cajacopi.Fuente;

    public bool NecesitaPrefijos => true;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        var fijo = ctx.GetTexto(fila, Fijo);
        if (fijo is not null)
        {
            var digitos = Digitos(fijo);
            if (digitos.Contains("11111", StringComparison.Ordinal))
            {
                ctx.Set(fila, Fijo, null);
                ctx.Advertir($"{Fijo}: fijo de relleno ('11111') anulado");
            }
            else if (digitos.Length == 7)
            {
                var prefijo = ctx.Prefijos?.Resolver(ctx.GetTexto(fila, Departamento), ctx.GetTexto(fila, Municipio));
                if (prefijo is null)
                {
                    ctx.Set(fila, Fijo, null);
                    ctx.Advertir($"{Fijo}: fijo de 7 dígitos sin prefijo resoluble por departamento/municipio, anulado");
                }
                else
                {
                    ctx.Set(fila, Fijo, prefijo + digitos);
                }
            }
            // Cualquier otro largo sigue a la validación genérica (fijo 6xx de 10 dígitos o se anula).
        }

        var email = ctx.GetTexto(fila, Email);
        if (email is not null && string.Concat(email.Where(c => !char.IsWhiteSpace(c))).Contains("NOTIEN", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Set(fila, Email, null);
            ctx.Advertir($"{Email}: 'NO TIENE' anulado");
        }
    }

    /// <summary>Dígitos del fijo, sin el ".0" que deja Excel en celdas numéricas.</summary>
    private static string Digitos(string texto)
    {
        var t = texto.Trim();
        if (t.EndsWith(".0", StringComparison.Ordinal)) t = t[..^2];
        return string.Concat(t.Where(char.IsDigit));
    }
}
