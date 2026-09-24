using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Definiciones de la Fase 2, grupo salud, contra el destino REAL (sys.columns de DBCallCenter,
/// 2026-09-24). Separado de <see cref="CatalogoCoherenciaTests"/> para que los grupos que
/// agregan fuentes en paralelo no choquen en el merge; las teorías genéricas de ese archivo
/// (tiene regla, es generable, alias normalizados) ya cubren estas fuentes.
/// </summary>
public class CatalogoCoherenciaSaludTests
{
    /// <summary>"Nombre:Tipo:Largo[:sinflag]" de cada columna de dato, para comparar con sys.columns.</summary>
    private static string[] Firma(DefinicionFuenteCargue d) =>
        d.ColumnasDato.Select(c => $"{c.Nombre}:{c.Tipo}:{c.Largo}{(c.EntraAlFlag ? "" : ":sinflag")}").ToArray();

    private static void SinAliasRepetidos(DefinicionFuenteCargue d)
    {
        // Dos columnas de la misma fuente con un alias común: el archivo alimentaría la primera
        // en silencio y la segunda nunca se encontraría.
        var repetidos = d.ColumnasDato.Concat(d.ColumnasArchivoExtra)
            .SelectMany(c => c.Alias).GroupBy(a => a).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(repetidos);
    }

    [Fact]
    public void Positiva_coincide_con_el_destino_real()
    {
        var d = CatalogoCargueFuentes.Positiva;
        Assert.Equal(("dbo.Positiva", "EstadoEnPositiva", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        // NumeroTelefonoFijo es varchar(30) en el destino (el SP concatenaba ahí dos fijos).
        Assert.Equal(
            ["NumeroTelefonoCelular:Celular:10", "NumeroTelefonoFijo:Fijo:30", "Email:Email:100",
             "FechaNacimiento:Fecha:0:sinflag", "Departamento:Texto:50:sinflag", "Municipio:Texto:50:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.Equal(2, d.FaseIntegracion);
        Assert.IsType<ReglaPositiva>(CatalogoReglasFuente.Obtener(d.Fuente));
        SinAliasRepetidos(d);
    }

    [Fact]
    public void SaludTotal_coincide_con_el_destino_real()
    {
        var d = CatalogoCargueFuentes.SaludTotal;
        Assert.Equal(("dbo.SaludTotal", "EstadoEnSaludTotal", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        // El flag del SP: NumeroTelefono, Email y NIT (lo demás es afiliación y empleador).
        Assert.Equal(
            ["NumeroTelefono:Celular:10", "Email:Email:255", "FechaNacimiento:Fecha:0:sinflag",
             "EstadoAfiliacion:Texto:30:sinflag", "TipoAfiliado:Texto:50:sinflag", "Ciudad:Texto:50:sinflag",
             "NIT:Entero:0", "Empresa:Texto:100:sinflag", "Cargo:Texto:60:sinflag",
             "UltimaFechaPago:Fecha:0:sinflag", "IngresoBase:Texto:20:sinflag"],
            Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.Equal(2, d.FaseIntegracion);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
        SinAliasRepetidos(d);
    }

    [Fact]
    public void Sos_coincide_con_el_destino_real()
    {
        var d = CatalogoCargueFuentes.Sos;
        Assert.Equal(("dbo.SOS", "EstadoEnSOS", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        // Las cuatro columnas entran al flag, como en el SP. Direccion es varchar(70) en el
        // destino (la Auxiliar admitía 150).
        Assert.Equal(["NitEmpresa:Entero:0", "RazonSocial:Texto:100", "Telefono:Celular:10", "Direccion:Texto:70"], Firma(d));
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
        SinAliasRepetidos(d);
        // "razonsocial" y "nit" también son alias comunes (Nombre, TitularID): en SOS son del dato,
        // y el motor no los usa para las comunes.
        Assert.Contains("razonsocial", d.ColumnasDato.Single(c => c.Nombre == "RazonSocial").Alias);
        Assert.Contains("razonsocial", CatalogoCargueFuentes.AliasNombre);
    }

    [Fact]
    public void Coosalud_coincide_con_el_destino_real()
    {
        var d = CatalogoCargueFuentes.Coosalud;
        Assert.Equal(("dbo.Coosalud", "EstadoEnCoosalud", "FechaConsulta", TipoFechaSello.Date),
                     (d.TablaDestino, d.ColumnaFlag, d.ColumnaFecha, d.TipoFecha));
        Assert.Equal(
            ["NumeroTelefono:Celular:10", "Email:Email:255", "MunicipioOrigen:Texto:50:sinflag",
             "DepartamentoOrigen:Texto:50:sinflag", "Regimen:Texto:50:sinflag", "TipoAfiliado:Texto:50:sinflag",
             "EstadoAfiliacion:Texto:50:sinflag", "NivelSisben:Texto:50:sinflag", "FechaNacimiento:Fecha:0:sinflag"],
            Firma(d));
        // Sin FK a Titular en el destino, pero el SP creaba Titular y relaciones: el motor también.
        Assert.True(d.CreaTitular && d.CreaTitularCartera && !d.TitularIdComoTexto && d.AplicaPoliticaVacias);
        Assert.Equal(CardinalidadCargue.UnaPorTitular, d.Cardinalidad);
        Assert.IsType<ReglaGenerica>(CatalogoReglasFuente.Obtener(d.Fuente));
        SinAliasRepetidos(d);
    }
}
