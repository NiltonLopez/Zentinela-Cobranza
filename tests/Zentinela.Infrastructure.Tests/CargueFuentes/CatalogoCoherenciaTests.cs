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
            Assert.NotEmpty(c.Alias);
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
}
