using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Fase 2, grupo empresas: cada definición contra lo que dijo <c>sys.columns</c> de su tabla
/// destino real (2026-09-24). Archivo aparte de <see cref="CatalogoCoherenciaTests"/> para que
/// los grupos de la Fase 2 no choquen en el merge; las pruebas genéricas (regla registrada,
/// definición generable, alias normalizados) ya recorren <c>CatalogoCargueFuentes.Todas</c>.
/// </summary>
public class CatalogoCoherenciaEmpresasTests
{
    /// <summary>"Nombre:Tipo:Largo[:sinflag]" de cada columna de dato, para comparar con sys.columns.</summary>
    private static string[] Firma(DefinicionFuenteCargue d) =>
        d.ColumnasDato.Select(c => $"{c.Nombre}:{c.Tipo}:{c.Largo}{(c.EntraAlFlag ? "" : ":sinflag")}").ToArray();

    [Fact]
    public void Rues_coincide_con_el_destino_real()
    {
        // dbo.RUES: NumeroMatricula bigint, EstadoEmpresa varchar(20), ActividadEconomica varchar(250),
        // NombreEmpresa varchar(70), EstadoEnRUES bit, FechaConsulta DATETIME (no date).
        var d = CatalogoCargueFuentes.Rues;
        Assert.Equal(("dbo.RUES", "EstadoEnRUES", "FechaConsulta", TipoFechaSello.DateTime),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroMatricula:Entero:0", "EstadoEmpresa:Texto:20", "ActividadEconomica:Texto:250",
             "NombreEmpresa:Texto:70:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    [Fact]
    public void Rues_toma_el_sello_con_hora_del_servidor()
    {
        // El SP guardaba GETDATE() y la tabla tiene filas con hora: un sello solo-fecha dejaría
        // la carga nueva "antes" que una fila del SP del mismo día en el ORDER BY FechaConsulta DESC.
        // Truncado al segundo para que la auditoría datetime2(0) lo guarde exacto.
        Assert.Equal("SELECT CONVERT(datetime, CONVERT(char(19), GETDATE(), 120), 120);",
                     GeneradorSqlCargue.LeerSello(CatalogoCargueFuentes.Rues));
    }

    [Fact]
    public void Fuentes_de_la_fase_2_de_empresas_no_colisionan_con_las_demas()
    {
        // Claves de ruta y etiquetas únicas sin importar mayúsculas (Sura y ARLSura: ver ADR-039).
        var fuentes = CatalogoCargueFuentes.Todas.Select(f => f.Fuente.ToUpperInvariant()).ToList();
        var etiquetas = CatalogoCargueFuentes.Todas.Select(f => f.Etiqueta.ToUpperInvariant()).ToList();
        var tablas = CatalogoCargueFuentes.Todas.Select(f => f.TablaDestino.ToUpperInvariant()).ToList();
        Assert.Equal(fuentes.Count, fuentes.Distinct().Count());
        Assert.Equal(etiquetas.Count, etiquetas.Distinct().Count());
        Assert.Equal(tablas.Count, tablas.Distinct().Count());
    }
}
