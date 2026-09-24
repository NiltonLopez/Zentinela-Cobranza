using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// El catálogo (Application) y las reglas (Infrastructure) viven separados: estos tests
/// impiden que una fuente quede habilitada sin regla o con una definición que el motor no
/// pueda convertir en SQL.
/// </summary>
public class CatalogoCoherenciaTests
{
    public static TheoryData<string> Fuentes()
    {
        var datos = new TheoryData<string>();
        foreach (var f in CatalogoCargueFuentes.Todas) datos.Add(f.Fuente);
        return datos;
    }

    [Theory]
    [MemberData(nameof(Fuentes))]
    public void Fuente_habilitada_tiene_regla(string fuente)
    {
        var def = CatalogoCargueFuentes.Buscar(fuente)!;
        if (def.Habilitada) Assert.True(CatalogoReglasFuente.Existe(fuente), $"{fuente} no tiene regla registrada");
    }

    [Theory]
    [MemberData(nameof(Fuentes))]
    public void Definicion_es_generable(string fuente)
    {
        GeneradorSqlCargue.ValidarDefinicion(CatalogoCargueFuentes.Buscar(fuente)!);
    }

    [Theory]
    [MemberData(nameof(Fuentes))]
    public void Alias_estan_normalizados_y_textos_tienen_largo(string fuente)
    {
        var def = CatalogoCargueFuentes.Buscar(fuente)!;
        foreach (var c in def.ColumnasDato.Concat(def.ColumnasArchivoExtra))
        {
            // Una columna que produce la regla no se busca en el archivo: no lleva alias.
            if (c.DesdeArchivo) Assert.NotEmpty(c.Alias);
            else Assert.Empty(c.Alias);
            foreach (var a in c.Alias) Assert.Equal(ArchivoTabular.Normalizar(a), a);
            if (c.Tipo is not (TipoColumnaCargue.Entero or TipoColumnaCargue.Fecha))
                Assert.True(c.Largo > 0, $"{fuente}.{c.Nombre} necesita Largo");
        }
    }

    [Fact]
    public void Alias_comunes_estan_normalizados()
    {
        string[][] comunes =
        [
            CatalogoCargueFuentes.AliasTitularId, CatalogoCargueFuentes.AliasNombre,
            CatalogoCargueFuentes.AliasCarteraId, CatalogoCargueFuentes.AliasCartera,
            CatalogoCargueFuentes.AliasLiderId, CatalogoCargueFuentes.AliasLider,
        ];
        foreach (var a in comunes.SelectMany(x => x)) Assert.Equal(ArchivoTabular.Normalizar(a), a);
    }

    [Fact]
    public void Sura_coincide_con_el_destino_real()
    {
        // Verificado con sys.columns de dbo.Sura el 2026-09-23.
        var s = CatalogoCargueFuentes.Sura;
        Assert.Equal("dbo.Sura", s.TablaDestino);
        Assert.Equal("EstadoEnSura", s.ColumnaFlag);
        Assert.Equal("FechaConsulta", s.ColumnaFecha);
        Assert.Equal(TipoFechaSello.Date, s.TipoFecha);
        Assert.Equal(
            ["NumeroCelular:Celular:10", "Email:Email:255", "NIT:Entero:0", "Empresa:Texto:100",
             "TipoAfiliado:Texto:50", "EstadoAfiliacion:Texto:50", "UltimaFechaConsulta:Fecha:0"],
            s.ColumnasDato.Select(c => $"{c.Nombre}:{c.Tipo}:{c.Largo}"));
        Assert.False(s.ColumnasDato.Single(c => c.Nombre == "UltimaFechaConsulta").EntraAlFlag);
    }

    /// <summary>"Nombre:Tipo:Largo[:sinflag]" de cada columna de dato, para comparar con sys.columns.</summary>
    private static string[] Firma(DefinicionFuenteCargue d) =>
        d.ColumnasDato.Select(c => $"{c.Nombre}:{c.Tipo}:{c.Largo}{(c.EntraAlFlag ? "" : ":sinflag")}").ToArray();

    [Fact]
    public void Cajacopi_coincide_con_el_destino_real()
    {
        // Verificado con sys.columns de dbo.Cajacopi el 2026-09-23.
        var d = CatalogoCargueFuentes.Cajacopi;
        Assert.Equal(("dbo.Cajacopi", "EstadoEnCajacopi", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroTelefonoCelular:Celular:10", "NumeroTelefonoFijo:Fijo:10", "Email:Email:100",
             "Departamento:Texto:50:sinflag", "Municipio:Texto:50:sinflag", "FechaNacimiento:Fecha:0"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.True(CatalogoReglasFuente.Obtener(d.Fuente).NecesitaPrefijos);
    }

    [Fact]
    public void MutualSer_coincide_con_el_destino_real()
    {
        // Verificado con sys.columns de dbo.MutualSer el 2026-09-23.
        var d = CatalogoCargueFuentes.MutualSer;
        Assert.Equal(("dbo.MutualSer", "EstadoEnMutualSer", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroTelefonoCelular:Celular:10", "NumeroTelefonoCelular2:Celular:10", "NumeroTelefonoFijo:Fijo:10",
             "Email:Email:255", "Regimen:Texto:30:sinflag", "TipoAfiliado:Texto:50:sinflag",
             "Departamento:Texto:50:sinflag", "Municipio:Texto:50:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
    }

    [Fact]
    public void NuevaEps_coincide_con_el_destino_real()
    {
        // Verificado con sys.columns de dbo.NuevaEps el 2026-09-23: TitularID varchar(30).
        var d = CatalogoCargueFuentes.NuevaEps;
        Assert.Equal(("dbo.NuevaEps", "EstadoEnNuevaEps", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroTelefono:Celular:10", "EstadoAfiliacion:Texto:30", "TipoAfiliado:Texto:50",
             "FechaNacimiento:Fecha:0:sinflag", "Departamento:Texto:50:sinflag", "Municipio:Texto:50:sinflag"],
            Firma(d));
        Assert.True(d.TitularIdComoTexto && d.CreaTitular && d.CreaTitularCartera);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    [Fact]
    public void Simit_coincide_con_el_destino_real_y_con_SIMITAuxiliar()
    {
        // dbo.SIMIT: Placa varchar(10) NULL (sys.columns 2026-09-23). SIMITAuxiliar: Placa1..Placa15.
        var d = CatalogoCargueFuentes.Simit;
        Assert.Equal(("dbo.SIMIT", "EstadoEnSIMIT", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["Placa:Placa:10"], Firma(d));
        Assert.False(d.ColumnasDato[0].DesdeArchivo);
        Assert.Equal(Enumerable.Range(1, 15).Select(i => $"Placa{i}"), d.ColumnasArchivoExtra.Select(c => c.Nombre));
        Assert.Equal(CardinalidadCargue.VariasPorTitular, d.Cardinalidad);
        Assert.IsType<ReglaSimit>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    [Fact]
    public void DatosClaro_coincide_con_el_destino_real()
    {
        // dbo.DatosClaro: FechaInsercion date NULL, sin FK a Titular (sys.columns 2026-09-23).
        var d = CatalogoCargueFuentes.DatosClaro;
        Assert.Equal(("dbo.DatosClaro", null, "FechaInsercion", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(["NumeroTelefono:Telefono:10", "Email:Email:100"], Firma(d));
        Assert.False(d.CreaTitular || d.CreaTitularCartera || d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.VariasPorTitular, d.Cardinalidad);
        Assert.True(CatalogoReglasFuente.Obtener(d.Fuente).NecesitaListaNegraTelefonos);
    }

    // ── Fase 2 · grupo "otras" ──────────────────────────────────────────────

    [Fact]
    public void Fosyga_coincide_con_el_destino_real()
    {
        // Verificado con sys.columns de dbo.Fosyga el 2026-09-24: EPS varchar(120), Departamento
        // varchar(70), fechas date, FK a Titular. Flag = Estado, EPS o Régimen (paso 5 del SP).
        var d = CatalogoCargueFuentes.Fosyga;
        Assert.Equal(("dbo.Fosyga", "EstadoEnFosyga", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["Estado:Texto:30", "EPS:Texto:120", "Regimen:Texto:30",
             "FechaAfiliacionEfectiva:Fecha:0:sinflag", "FechaFinalizacionAfiliacion:Fecha:0:sinflag",
             "TipoAfiliado:Texto:50:sinflag", "Departamento:Texto:70:sinflag", "Municipio:Texto:50:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.True(d.ColumnasDato.All(c => c.EntraAClave));
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.IsType<ReglaFosyga>(CatalogoReglasFuente.Obtener(d.Fuente));
    }
}
