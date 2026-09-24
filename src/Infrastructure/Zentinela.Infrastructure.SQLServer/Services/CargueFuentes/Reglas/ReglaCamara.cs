using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Cámara de Comercio (personas): traducción de <c>sp_ProcesarCamaraAuxiliar</c> (respaldo en
/// docs/sql/legacy/).
///
/// Lo que se tradujo (en <see cref="Transformar"/>, antes de la validación genérica):
/// <list type="bullet">
/// <item>Paso 5 del SP: teléfono de 7 dígitos → indicativo del departamento
///   (<c>PrefijosDepartamento</c>). Aquí con <c>PrefijosFijo.Resolver</c> sobre
///   <c>PrefijoMunicipio</c> + <c>PrefijosDepartamento</c> (departamento o municipio, sin tildes ni
///   puntos); si no se resuelve sin ambigüedad, el número se ANULA con advertencia: un indicativo
///   adivinado marca a otra persona.</item>
/// <item>Pasos 1 a 3 (Titular, Titular_Cartera, dedup por titular), 4 (flag), 6 (fecha), 7 (INSERT)
///   y 8 (dedup en destino): el motor.</item>
/// </list>
///
/// Lo que el motor aplica aquí y el SP NO hacía:
/// <list type="bullet">
/// <item>Validación del teléfono (celular 3xx o fijo 60x de 10 dígitos) y del email, con
///   <c>ListaNegraCorreos</c> por igualdad exacta: el SP de Cámara no validaba ni cruzaba la lista
///   negra (hoy <c>dbo.Camara</c> tiene 'NO TIENE' y celulares en la columna Email).</item>
/// <item>Textos de relleno del formulario del proveedor en Departamento ('Seleccione un
///   departamento': 227 filas; 'Cargando...': 15) y Municipio/Departamento sin ninguna letra
///   ('.'): se anulan con advertencia (el SP los guardaba como dato).</item>
/// <item>Política de vacías: el SP no purgaba vacías ni descartaba la fila vacía de un titular con datos.</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>El prefijo se buscaba con <c>UPPER(Departamento) = PrefijosDepartamento.Departamento</c>
///   exacto: 'BOGOTA D.C.' no casaba con 'BOGOTÁ D.C.', y el municipio no se usaba.</item>
/// <item>Si el departamento no casaba, el número de 7 dígitos se guardaba tal cual (hoy hay
///   3.189 en <c>dbo.Camara</c>): no se marca desde un celular.</item>
/// <item>El flag se calculaba antes de prefijar y contaba '' como dato.</item>
/// <item>Dedup del paso 8 sobre TODA la tabla Camara, con clave recortada (teléfono y email).</item>
/// </list>
/// </summary>
internal sealed class ReglaCamara : IReglaFuente
{
    private const string Telefono = "NumeroTelefono";
    private const string Departamento = "Departamento";
    private const string Municipio = "Municipio";

    public string Fuente => CatalogoCargueFuentes.Camara.Fuente;

    public bool NecesitaPrefijos => true;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        ctx.Set(fila, Departamento, LimpiarLocalizacion(ctx.GetTexto(fila, Departamento), Departamento, ctx));
        ctx.Set(fila, Municipio, LimpiarLocalizacion(ctx.GetTexto(fila, Municipio), Municipio, ctx));

        var prefijado = PrefijarFijoDe7(ctx.GetTexto(fila, Telefono), ctx.GetTexto(fila, Departamento),
            ctx.GetTexto(fila, Municipio), Telefono, ctx);
        ctx.Set(fila, Telefono, prefijado);
    }

    // Textos que el formulario web del proveedor deja en el campo cuando no hay dato. Claves
    // normalizadas (ArchivoTabular.Normalizar, sin puntos).
    private static readonly HashSet<string> Relleno = new(StringComparer.Ordinal)
    {
        "seleccioneundepartamento", "seleccioneunmunicipio", "seleccione", "cargando",
    };

    /// <summary>
    /// Departamento o municipio: los textos de relleno del formulario y los valores sin ninguna
    /// letra ('.', '-') se anulan con advertencia. Todo lo demás pasa tal cual (hay direcciones
    /// en el exterior: 'HESSE', 'REINO UNIDO').
    /// </summary>
    internal static string? LimpiarLocalizacion(string? valor, string columna, ContextoCargue ctx)
    {
        if (valor is null) return null;
        if (!valor.Any(char.IsLetter))
        {
            ctx.Advertir($"{columna}: valor sin letras anulado");
            return null;
        }
        if (Relleno.Contains(ArchivoTabular.Normalizar(valor).Replace(".", "")))
        {
            ctx.Advertir($"{columna}: texto de relleno del formulario ('Seleccione…', 'Cargando…') anulado");
            return null;
        }
        return valor;
    }

    /// <summary>
    /// Teléfono de 7 dígitos (fijo sin indicativo) → indicativo + número, resuelto por
    /// departamento/municipio; si no se resuelve, null con advertencia. Cualquier otro largo se
    /// devuelve tal cual: lo valida el motor (celular 3xx o fijo 60x de 10 dígitos).
    /// Compartido con la regla de CamaraEmpresa (mismo paso en su SP).
    /// </summary>
    internal static string? PrefijarFijoDe7(string? telefono, string? departamento, string? municipio, string columna, ContextoCargue ctx)
    {
        if (telefono is null) return null;
        var digitos = Digitos(telefono);
        if (digitos.Length != 7) return telefono;

        var prefijo = ctx.Prefijos?.Resolver(departamento, municipio);
        if (prefijo is null)
        {
            ctx.Advertir($"{columna}: fijo de 7 dígitos sin prefijo resoluble por departamento/municipio, anulado");
            return null;
        }
        return prefijo + digitos;
    }

    /// <summary>Dígitos del número, sin el ".0" que deja Excel en celdas numéricas.</summary>
    private static string Digitos(string texto)
    {
        var t = texto.Trim();
        if (t.EndsWith(".0", StringComparison.Ordinal)) t = t[..^2];
        return string.Concat(t.Where(char.IsDigit));
    }
}
