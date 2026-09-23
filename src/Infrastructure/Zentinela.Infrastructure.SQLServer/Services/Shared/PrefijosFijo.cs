namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Resuelve el indicativo de teléfono fijo (601–608) a partir de departamento y municipio,
/// con la tabla <c>PrefijoMunicipio</c> (1.118 filas: Municipio, Departamento, Prefijo).
///
/// Sirve para completar fijos de 7 dígitos que llegan sin indicativo. Como un prefijo
/// equivocado produce un número que SÍ marca (a otra persona), ante cualquier ambigüedad
/// se devuelve <c>null</c> en vez de adivinar.
///
/// Orden de resolución:
/// 1. (departamento, municipio) exacto.
/// 2. Si el departamento existe en la tabla: su prefijo solo si TODOS sus municipios lo
///    comparten; si no es unívoco → null. No se sigue al paso 3: si el municipio no está en
///    ese departamento, buscarlo en otro sería tomar el prefijo de un homónimo.
/// 3. Solo municipio (sin departamento, o con uno que no está en la tabla): su prefijo si es
///    único. Hay 68 municipios repetidos entre departamentos; si sus prefijos difieren → null.
///    Si no vino municipio y el departamento no está en la tabla, se prueba el departamento
///    como municipio: Bogotá es distrito y municipio a la vez; la tabla real lo guarda como
///    ('BOGOTA', 'BOGOTA, D.C.') y las fuentes lo mandan como departamento 'BOGOTA D.C.'.
///
/// Las claves se normalizan con <see cref="ArchivoTabular.Normalizar"/> (sin tildes, sin
/// espacios, minúsculas) y además sin puntos ni comas, para que "BOGOTÁ, D.C.",
/// "BOGOTA D.C." y "Bogota DC" coincidan.
/// </summary>
internal sealed class PrefijosFijo
{
    // Cada clave guarda el CONJUNTO de prefijos vistos: resolver = conjunto de tamaño 1.
    private readonly Dictionary<(string Departamento, string Municipio), HashSet<string>> _porPar = new();
    private readonly Dictionary<string, HashSet<string>> _porDepartamento = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _porMunicipio    = new(StringComparer.Ordinal);

    private PrefijosFijo() { }

    public static PrefijosFijo Cargar(IEnumerable<(string Departamento, string Municipio, string Prefijo)> filas)
    {
        var tabla = new PrefijosFijo();
        foreach (var (departamento, municipio, prefijoBruto) in filas)
        {
            var prefijo = prefijoBruto?.Trim();
            if (string.IsNullOrEmpty(prefijo)) continue;

            var d = Clave(departamento);
            var m = Clave(municipio);

            if (d.Length > 0 && m.Length > 0) Agregar(tabla._porPar, (d, m), prefijo);
            if (d.Length > 0) Agregar(tabla._porDepartamento, d, prefijo);
            if (m.Length > 0) Agregar(tabla._porMunicipio, m, prefijo);
        }
        return tabla;
    }

    /// <summary>Prefijo de 3 dígitos, o <c>null</c> si no se puede determinar sin ambigüedad.</summary>
    public string? Resolver(string? departamento, string? municipio)
    {
        var d = Clave(departamento);
        var m = Clave(municipio);

        if (d.Length > 0 && m.Length > 0 && _porPar.TryGetValue((d, m), out var exacto))
            return Univoco(exacto);

        if (d.Length > 0 && _porDepartamento.TryGetValue(d, out var delDepartamento))
            return Univoco(delDepartamento);

        if (m.Length > 0 && _porMunicipio.TryGetValue(m, out var delMunicipio))
            return Univoco(delMunicipio);

        // Departamento desconocido y sin municipio: ¿un distrito que la tabla guarda como municipio?
        if (m.Length == 0 && d.Length > 0 && _porMunicipio.TryGetValue(d, out var comoMunicipio))
            return Univoco(comoMunicipio);

        return null;
    }

    private static string Clave(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : ArchivoTabular.Normalizar(s).Replace(".", "").Replace(",", "");

    private static string? Univoco(HashSet<string> prefijos) =>
        prefijos.Count == 1 ? prefijos.First() : null;

    private static void Agregar<TClave>(Dictionary<TClave, HashSet<string>> indice, TClave clave, string prefijo)
        where TClave : notnull
    {
        if (!indice.TryGetValue(clave, out var conjunto))
            indice[clave] = conjunto = new HashSet<string>(StringComparer.Ordinal);
        conjunto.Add(prefijo);
    }
}
