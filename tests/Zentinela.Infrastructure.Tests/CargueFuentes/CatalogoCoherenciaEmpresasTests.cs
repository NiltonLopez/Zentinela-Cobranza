using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

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
    public void Camara_coincide_con_el_destino_real()
    {
        // dbo.Camara: NumeroTelefono varchar(10), Email varchar(255), Municipio varchar(50),
        // Departamento varchar(70), EstadoEnCamara bit, FechaConsulta date.
        var d = CatalogoCargueFuentes.Camara;
        Assert.Equal(("dbo.Camara", "EstadoEnCamara", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroTelefono:Telefono:10", "Email:Email:255", "Municipio:Texto:50:sinflag", "Departamento:Texto:70:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.True(CatalogoReglasFuente.Obtener(d.Fuente).NecesitaPrefijos);
    }

    [Fact]
    public void CamaraEmpresa_coincide_con_el_destino_real()
    {
        // dbo.CamaraEmpresa: NitEmpresa bigint NULL (sin TitularID), NumeroTelefono varchar(10),
        // Email varchar(255), EstadoEnCamara bit, FechaConsulta date; heap sin FK.
        var d = CatalogoCargueFuentes.CamaraEmpresa;
        Assert.Equal(("dbo.CamaraEmpresa", "EstadoEnCamara", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["NumeroTelefono:Telefono:10", "Email:Email:255"], Firma(d));
        Assert.Equal("NitEmpresa", d.ColumnaClaveTitular);
        Assert.False(d.ClaveEsTitular || d.CreaTitular || d.CreaTitularCartera || d.TitularIdComoTexto);
        Assert.True(d.AplicaPoliticaVacias);
        Assert.Equal(["Departamento", "Municipio"], d.ColumnasArchivoExtra.Select(c => c.Nombre));
        foreach (var a in d.AliasDeClave) Assert.Equal(ArchivoTabular.Normalizar(a), a);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
    }

    [Fact]
    public void PositivaEmpresas_coincide_con_el_destino_real()
    {
        // dbo.PositivaEmpresas: NIT bigint, Empresa varchar(230), EstadoAfiliacion varchar(30),
        // EstadoEnPositivaEmpresa bit, FechaConsulta date.
        var d = CatalogoCargueFuentes.PositivaEmpresas;
        Assert.Equal(("dbo.PositivaEmpresas", "EstadoEnPositivaEmpresa", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["NIT:Entero:0", "Empresa:Texto:230", "EstadoAfiliacion:Texto:30:sinflag"], Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && d.ClaveEsTitular && d.AplicaPoliticaVacias);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    [Fact]
    public void ArlSura_coincide_con_el_destino_real()
    {
        // dbo.ARLSura: NIT bigint, Empresa varchar(100), EstadoEnSura bit, FechaConsulta date.
        var d = CatalogoCargueFuentes.ArlSura;
        Assert.Equal(("dbo.ARLSura", "EstadoEnSura", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["NIT:Entero:0", "Empresa:Texto:100"], Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && d.ClaveEsTitular && d.AplicaPoliticaVacias);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    [Fact]
    public void ArlSura_y_Sura_son_fuentes_distintas_sin_colision_de_nombre()
    {
        // Antecedente: bug por prefijos de nombre Sura/ARLSura (alias de BaseMadre, 2026-09-02).
        var eps = CatalogoCargueFuentes.Sura;
        var arl = CatalogoCargueFuentes.ArlSura;
        Assert.Same(eps, CatalogoCargueFuentes.Buscar("Sura"));
        Assert.Same(eps, CatalogoCargueFuentes.Buscar(" sura "));
        Assert.Same(arl, CatalogoCargueFuentes.Buscar("ARLSura"));
        Assert.Same(arl, CatalogoCargueFuentes.Buscar("arlsura"));
        Assert.Null(CatalogoCargueFuentes.Buscar("ARL"));
        Assert.Null(CatalogoCargueFuentes.Buscar("SuraARL"));
        Assert.NotEqual(eps.TablaDestino, arl.TablaDestino);
        Assert.NotEqual(eps.Etiqueta, arl.Etiqueta);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener("Sura"));
        Assert.Equal("Sura", CatalogoReglasFuente.Obtener("Sura").Fuente);
        Assert.Equal("ARLSura", CatalogoReglasFuente.Obtener("ARLSura").Fuente);
        // Mismo nombre de flag, tablas distintas: el SQL de una nunca nombra la tabla de la otra.
        Assert.DoesNotContain("[dbo].[Sura]", GeneradorSqlCargue.Insertar(arl));
        Assert.DoesNotContain("[dbo].[ARLSura]", GeneradorSqlCargue.Insertar(eps));
    }

    [Fact]
    public void Compensados_coincide_con_el_destino_real()
    {
        // dbo.Compensados: EPS varchar(50), UltimoPeriodoCompensado date, TipoAfiliado varchar(50),
        // EstadoEnCompensados INT NOT NULL (el #Carga lo lleva como bit; el INSERT lo convierte a 0/1).
        var d = CatalogoCargueFuentes.Compensados;
        Assert.Equal(("dbo.Compensados", "EstadoEnCompensados", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["EPS:Texto:50", "UltimoPeriodoCompensado:Fecha:0", "TipoAfiliado:Texto:50:sinflag"], Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && d.ClaveEsTitular && d.AplicaPoliticaVacias);
    }

    [Fact]
    public void Prefijos_suman_PrefijosDepartamento_como_filas_solo_departamento()
    {
        var sql = GeneradorSqlCargue.ConsultarPrefijos;
        Assert.Contains("FROM dbo.PrefijoMunicipio", sql);
        Assert.Contains("SELECT Departamento, NULL, CONVERT(varchar(3), Prefijo) FROM dbo.PrefijosDepartamento", sql);
        Assert.Contains("UNION ALL", sql);
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
