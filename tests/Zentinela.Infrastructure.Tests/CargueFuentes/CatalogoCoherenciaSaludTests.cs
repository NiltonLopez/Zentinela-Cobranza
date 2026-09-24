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
}
