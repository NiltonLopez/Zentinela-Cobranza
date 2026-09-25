using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Coherencia del catálogo de fuentes de localización (ADR-040) con las otras dos fuentes de
/// verdad que toca: el Cargue de Fuentes (<see cref="CatalogoCargueFuentes"/>: tablas y columnas) y
/// el Calificador (etiquetas del panel y metadata de la descarga por celda, ADR-019).
/// </summary>
public class CatalogoFuentesLocalizacionTests
{
    private static readonly IReadOnlyList<FuenteLocalizacion> Panel =
        CatalogoFuentesLocalizacion.DelPanel.ToList();

    // ── Tamaño y unicidad ────────────────────────────────────────────────────

    [Fact]
    public void Hay_21_fuentes_en_el_panel_y_22_en_total()
    {
        Assert.Equal(21, Panel.Count);
        Assert.Equal(22, CatalogoFuentesLocalizacion.Todas.Count);
        Assert.Equal("CamaraEmpresa", Assert.Single(CatalogoFuentesLocalizacion.Todas, f => !f.EnPanelCalificador).Clave);
    }

    [Fact]
    public void Claves_slugs_y_etiquetas_son_unicas()
    {
        var todas = CatalogoFuentesLocalizacion.Todas;
        Assert.Equal(todas.Count, todas.Select(f => f.Clave).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(todas.Count, todas.Select(f => f.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(todas.Count, todas.Select(f => f.EtiquetaCalificador).Distinct().Count());
    }

    [Fact]
    public void Fosyga_es_Funcional()
    {
        Assert.Equal(EstadoFuente.Funcional, CatalogoFuentesLocalizacion.Buscar("Fosyga")!.Estado);
    }

    /// <summary>
    /// Espejo del <c>ESTADO_FUENTE</c> que tenía el front del Calificador antes del ADR-040, con el
    /// único cambio pedido por el usuario (Fosyga: Intermitente → Funcional). Si el equipo cambia
    /// un estado, se cambia aquí también: el test existe para que ese cambio sea a propósito.
    /// </summary>
    [Fact]
    public void Estados_y_notas_son_los_del_Calificador_con_Fosyga_Funcional()
    {
        var esperado = new Dictionary<string, (EstadoFuente Estado, string? Nota)>
        {
            ["Cajacopi"]      = (EstadoFuente.Funcional,    null),
            ["Cámara"]        = (EstadoFuente.Intermitente, null),
            ["Salud Total"]   = (EstadoFuente.Inactivo,     null),
            ["Fosyga"]        = (EstadoFuente.Funcional,    null),
            ["Compensados"]   = (EstadoFuente.Intermitente, null),
            ["Positiva"]      = (EstadoFuente.Funcional,    null),
            ["Tigo"]          = (EstadoFuente.Inactivo,     null),
            ["TigoUne"]       = (EstadoFuente.Funcional,    null),
            ["Coosalud"]      = (EstadoFuente.Funcional,    null),
            ["Savia Salud"]   = (EstadoFuente.Funcional,    null),
            ["Sura"]          = (EstadoFuente.Funcional,    null),
            ["SOS"]           = (EstadoFuente.Funcional,    null),
            ["Claro"]         = (EstadoFuente.Inactivo,     null),
            ["MiVacuna"]      = (EstadoFuente.Funcional,    null),
            ["MutualSer"]     = (EstadoFuente.Funcional,    null),
            ["NuevaEps"]      = (EstadoFuente.Funcional,    null),
            ["ARL Sura"]      = (EstadoFuente.Funcional,    "solo nombre de empresa"),
            ["Positiva Emp."] = (EstadoFuente.Funcional,    null),
            ["RUES"]          = (EstadoFuente.Funcional,    null),
            ["SIMIT"]         = (EstadoFuente.Funcional,    null),
            ["RGM"]           = (EstadoFuente.Funcional,    null),
        };

        Assert.Equal(esperado.Count, Panel.Count);
        foreach (var f in Panel)
        {
            Assert.True(esperado.TryGetValue(f.EtiquetaCalificador, out var e), $"Sin espejo para '{f.EtiquetaCalificador}'");
            Assert.Equal(e.Estado, f.Estado);
            Assert.Equal(e.Nota, f.Nota);
        }
    }

    // ── Contra el Calificador ────────────────────────────────────────────────

    /// <summary>
    /// Las etiquetas (y su orden) son EXACTAMENTE las filas que arma el Calificador: el front
    /// cruza el chip de estado por <c>EtiquetaCalificador</c> = <c>LocalizacionFuente.Fuente</c>.
    /// </summary>
    [Fact]
    public void Etiquetas_y_orden_coinciden_con_las_filas_del_Calificador()
    {
        var delCalificador = CalificadorCarteraService.EtiquetasLocalizacion.Select(e => e.Etiqueta).ToList();
        Assert.Equal(delCalificador, Panel.Select(f => f.EtiquetaCalificador).ToList());
    }

    /// <summary>
    /// Slug, tabla y columna de fecha coinciden con la metadata de la descarga por celda del
    /// Calificador (diccionario privado <c>Fuentes</c>, leído por reflexión).
    /// </summary>
    [Fact]
    public void Slug_tabla_y_fecha_coinciden_con_la_descarga_por_celda_del_Calificador()
    {
        var campo = typeof(CalificadorCarteraService).GetField("Fuentes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(campo);
        var diccionario = (IDictionary)campo!.GetValue(null)!;

        var metas = new Dictionary<string, (string Tabla, string Fecha)>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry par in diccionario)
        {
            var meta = par.Value!;
            var tipo = meta.GetType();
            metas[(string)par.Key] = (
                (string)tipo.GetProperty("TablaFuente")!.GetValue(meta)!,
                (string)tipo.GetProperty("ColumnaFecha")!.GetValue(meta)!);
        }

        Assert.Equal(
            metas.Keys.OrderBy(k => k, StringComparer.Ordinal),
            Panel.Select(f => f.Slug).OrderBy(k => k, StringComparer.Ordinal));

        foreach (var f in Panel)
        {
            var (tabla, fecha) = metas[f.Slug];
            Assert.Equal("dbo." + tabla, f.Tabla);
            Assert.Equal(fecha, f.ColumnaFecha);
        }
    }

    // ── Contra el Cargue de Fuentes ──────────────────────────────────────────

    [Fact]
    public void Cada_fuente_existe_en_el_Cargue_con_la_misma_tabla_fecha_y_clave()
    {
        foreach (var f in CatalogoFuentesLocalizacion.Todas)
        {
            var def = CatalogoCargueFuentes.Buscar(f.Clave);
            Assert.True(def is not null, $"'{f.Clave}' no está en CatalogoCargueFuentes");
            Assert.Equal(def!.Fuente, f.Clave); // mismo casing: la clave es la misma cadena
            Assert.Equal(def.TablaDestino, f.Tabla);
            Assert.Equal(def.ColumnaFecha, f.ColumnaFecha);
            Assert.Equal(def.TitularIdComoTexto, f.TitularIdComoTexto);
            Assert.Equal(def.ColumnaClaveTitular, f.ColumnaClave);
        }
    }

    /// <summary>
    /// Toda columna que cita un predicado es una columna de dato del cargue de esa fuente. Hoy no
    /// hay excepciones (todas las columnas que la BaseMadre proyecta para el "encontrado" las
    /// escribe el cargue); si alguna vez un predicado necesita una columna legítima de la tabla
    /// que el cargue no escribe, se agrega aquí con su porqué.
    /// </summary>
    [Fact]
    public void Las_columnas_del_predicado_existen_en_la_definicion_del_cargue()
    {
        var excepciones = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in CatalogoFuentesLocalizacion.Todas)
        {
            var def = CatalogoCargueFuentes.Buscar(f.Clave)!;
            var columnasCargue = def.ColumnasDato.Select(c => c.Nombre).ToHashSet(StringComparer.Ordinal);
            var citadas = ColumnasCitadas(f.PredicadoEncontrado);

            Assert.NotEmpty(citadas);
            foreach (var col in citadas)
            {
                var permitida = columnasCargue.Contains(col)
                    || (excepciones.TryGetValue(f.Clave, out var ex) && ex.Contains(col));
                Assert.True(permitida, $"{f.Clave}: el predicado cita '{col}', que no es columna de dato del cargue");
            }
        }
    }

    [Fact]
    public void Los_predicados_solo_usan_el_alias_s_y_tienen_parentesis_balanceados()
    {
        foreach (var f in CatalogoFuentesLocalizacion.Todas)
        {
            var p = f.PredicadoEncontrado;
            // Ningún otro alias de tabla (el del Calificador es t.) ni corchetes de la BaseMadre.
            Assert.DoesNotMatch(new Regex(@"(?<![\w.])(?!s\.)[A-Za-z]{1,3}\.[A-Za-z]"), p);
            Assert.DoesNotContain("[", p);
            Assert.Equal(p.Count(c => c == '('), p.Count(c => c == ')'));
        }
    }

    [Fact]
    public void Las_edades_se_calculan_desde_FechaNacimiento_solo_en_Cajacopi_y_Positiva()
    {
        // En la BaseMadre solo [Edad CajaCopi] y [Edad Positiva] entran al "encontrado" del
        // Calificador; las demás fechas de nacimiento no cuentan.
        var conEdad = CatalogoFuentesLocalizacion.Todas
            .Where(f => f.PredicadoEncontrado.Contains("DATEDIFF(YEAR, s.FechaNacimiento"))
            .Select(f => f.Clave)
            .OrderBy(c => c)
            .ToList();
        Assert.Equal(["Cajacopi", "Positiva"], conEdad);
    }

    private static HashSet<string> ColumnasCitadas(string predicado) =>
        Regex.Matches(predicado, @"\bs\.(\w+)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
}
