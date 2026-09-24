using System.Text.RegularExpressions;
using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Cámara de Comercio (empresas): traducción de <c>sp_ProcesarCamaraEmpresaAuxiliar</c> (respaldo
/// en docs/sql/legacy/). La clave de la fila es el NIT de la empresa (<c>NitEmpresa</c>, definición
/// con <c>ColumnaClaveTitular</c>): el SP no tocaba Titular ni Titular_Cartera y el motor tampoco.
///
/// Lo que se tradujo:
/// <list type="bullet">
/// <item>Paso 3 del SP: teléfono de 7 dígitos → indicativo del departamento. Aquí con el mismo
///   helper que Cámara (<see cref="ReglaCamara.PrefijarFijoDe7"/>) sobre el Departamento (y el
///   Municipio, opcional) del archivo, que no se guardan (<c>ColumnasArchivoExtra</c>); sin
///   indicativo resoluble, el número se anula con advertencia.</item>
/// <item>Pasos 1 (dedup por NIT), 2 (flag), 4 (fecha), 5 (INSERT) y 6 (dedup en destino): el motor.</item>
/// </list>
///
/// Propio del cargue nuevo:
/// <list type="bullet">
/// <item><see cref="NormalizarClave"/>: 'NIT-DV' ('900123456-7', '900.123.456-7') → '900123456'. En
///   la Auxiliar el NIT era bigint, así que el DV nunca llegaba al SP; el motor, que ignora lo que
///   no es dígito, lo pegaría al NIT ('9001234567') y la fila no cruzaría con <c>ARLSura.NIT</c>.</item>
/// <item>Validación del teléfono y del email con <c>ListaNegraCorreos</c> exacta (el SP no validaba
///   ni cruzaba la lista), relleno del formulario en Departamento/Municipio y política de vacías,
///   como en <see cref="ReglaCamara"/>.</item>
/// </list>
///
/// Defectos del SP que NO se copian: prefijo por <c>UPPER(Departamento)</c> exacto con tildes y 7
/// dígitos guardados cuando no casaba; flag calculado antes de prefijar; dedup del paso 6 sobre
/// TODA la tabla CamaraEmpresa.
/// </summary>
internal sealed class ReglaCamaraEmpresa : IReglaFuente
{
    private const string Telefono = "NumeroTelefono";

    // Índices en ColumnasArchivoExtra (Departamento, Municipio).
    private static readonly int ExtraDepartamento = IndiceExtra("Departamento");
    private static readonly int ExtraMunicipio = IndiceExtra("Municipio");

    // NIT con dígito de verificación separado por guion: dígitos (con puntos o espacios) - un dígito.
    private static readonly Regex NitConDv = new(@"^\s*(\d[\d.\s]*?)\s*-\s*\d\s*$", RegexOptions.Compiled);

    public string Fuente => CatalogoCargueFuentes.CamaraEmpresa.Fuente;

    public bool NecesitaPrefijos => true;

    public string? NormalizarClave(string? bruto, ContextoCargue ctx)
    {
        if (bruto is null) return null;
        var m = NitConDv.Match(bruto);
        if (!m.Success) return bruto;
        ctx.Informar("NitEmpresa con dígito de verificación ('NIT-DV'): se guardó el NIT sin el DV");
        return m.Groups[1].Value;
    }

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        var departamento = ReglaCamara.LimpiarLocalizacion(Extra(fila, ExtraDepartamento), "Departamento", ctx);
        var municipio = ReglaCamara.LimpiarLocalizacion(Extra(fila, ExtraMunicipio), "Municipio", ctx);
        ctx.Set(fila, Telefono, ReglaCamara.PrefijarFijoDe7(ctx.GetTexto(fila, Telefono), departamento, municipio, Telefono, ctx));
    }

    private static string? Extra(FilaCargue fila, int indice) =>
        indice < fila.Extra.Length ? CargueFuentesMotor.LimpiarTexto(fila.Extra[indice]) : null;

    private static int IndiceExtra(string nombre)
    {
        var extras = CatalogoCargueFuentes.CamaraEmpresa.ColumnasArchivoExtra;
        for (var i = 0; i < extras.Count; i++)
            if (extras[i].Nombre == nombre) return i;
        throw new InvalidOperationException($"CamaraEmpresa no tiene la columna extra '{nombre}'.");
    }
}
