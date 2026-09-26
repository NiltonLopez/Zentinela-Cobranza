using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;
using SqlEnv = Zentinela.Infrastructure.SQLServer.Services.Localizacion.GeneradorSqlEnvios;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Vínculo automático lote ↔ cargue de retorno (ADR-040, etapa 4): la decisión (vincular, cerrar solo) y el SQL que
/// busca el cargue por CargueFuenteReversion. Se probó además contra la ejecución real 509 de Fosyga con lotes
/// simulados en tablas #temp (ADR-040).
/// </summary>
public class VinculoRetornoTests
{
    private static readonly DateTime Fin = new(2026, 9, 24, 15, 38, 4);

    [Fact]
    public void Sin_titulares_del_lote_en_el_cargue_no_se_vincula()
    {
        Assert.Null(VinculoRetorno.Decidir(new CandidatoVinculo(1, 300, 509, Fin, 0), cerrarAlCubrirPct: null));
    }

    [Fact]
    public void Por_defecto_vincula_sin_cerrar()
    {
        var d = VinculoRetorno.Decidir(new CandidatoVinculo(1, 300, 509, Fin, 200), cerrarAlCubrirPct: null)!;
        Assert.Equal(509, d.EjecucionID);
        Assert.Equal(Fin, d.FechaRetorno);
        Assert.Equal(66.67m, d.PctCubierto);
        Assert.False(d.Cerrar);
    }

    [Theory]
    [InlineData(200, 60, true)]
    [InlineData(200, 66.67, true)]    // inclusivo
    [InlineData(200, 66.68, false)]
    [InlineData(300, 100, true)]
    [InlineData(1, 1, false)]         // 0,33 %
    public void Cierra_solo_si_la_cobertura_alcanza_el_umbral(int enCargue, double umbral, bool cierra)
    {
        var d = VinculoRetorno.Decidir(new CandidatoVinculo(1, 300, 509, Fin, enCargue), (decimal)umbral)!;
        Assert.Equal(cierra, d.Cerrar);
    }

    [Fact]
    public void Candidatos_cruzan_por_la_reversion_de_la_ejecucion_y_eligen_el_primer_cargue()
    {
        var sql = SqlEnv.CandidatosVinculo(SqlEnv.Produccion).Replace("\r\n", "\n");
        Assert.Contains("FROM dbo.LocalizacionEnvio e", sql);
        Assert.Contains("FROM dbo.LocalizacionEnvioDetalle d", sql);
        Assert.Contains("FROM dbo.CargueFuenteReversion r", sql);
        Assert.Contains("r.EjecucionID = c.EjecucionID", sql);
        Assert.Contains("r.TitularID = CAST(d.TitularID AS varchar(30))", sql);   // la reversión guarda la clave como texto
        Assert.Contains("c.FechaFin > e.FechaEnvio", sql);
        Assert.Contains("c.Usuario NOT LIKE @PatronHarness", sql);
        Assert.Contains("c.Error NOT LIKE 'REVERTIDA%'", sql);
        Assert.Contains("ORDER BY c.FechaFin, c.EjecucionID", sql);
        Assert.Contains("e.Estado = 'ENVIADO'", sql);
        Assert.Contains("e.CargueEjecucionID IS NULL", sql);
        Assert.Contains("(@Fuente IS NULL OR e.Fuente = @Fuente)", sql);
    }

    [Fact]
    public void Aplicar_solo_toca_lotes_que_siguen_enviados_y_sin_vinculo()
    {
        var sql = SqlEnv.AplicarVinculos(SqlEnv.Produccion).Replace("\r\n", "\n");
        Assert.Contains("WHERE e.Estado = 'ENVIADO' AND e.CargueEjecucionID IS NULL;", sql);
        Assert.Contains("Estado            = CASE WHEN v.Cerrar = 1 THEN 'CERRADO' ELSE e.Estado END", sql);
        Assert.Contains("FechaCierre       = CASE WHEN v.Cerrar = 1 THEN SYSDATETIME() ELSE e.FechaCierre END", sql);
        Assert.Contains("OUTPUT inserted.EnvioID, inserted.Estado", sql);
        Assert.Equal("sistema (retorno automático)", LocalizacionRetornoService.UsuarioSistema);
    }

    [Fact]
    public void Con_prefijo_temporal_no_escribe_en_tablas_permanentes()
    {
        var t = SqlEnv.TablasEnvio.ConPrefijo("#");
        var sql = (SqlEnv.CandidatosVinculo(t) + SqlEnv.AplicarVinculos(t)).Replace("\r\n", "\n");
        Assert.DoesNotContain("dbo.LocalizacionEnvio", sql);
        Assert.DoesNotMatch(@"(INSERT INTO|UPDATE|DELETE FROM)\s+dbo\.", sql);
        // Las lecturas del cargue sí son de dbo (solo lectura).
        Assert.Contains("dbo.CargueFuenteReversion", sql);
    }

    [Fact]
    public void Un_lote_vinculado_toma_ese_cargue_como_retorno()
    {
        // El cálculo de la etapa 3 respeta el vínculo aunque haya un cargue OK anterior de la misma fuente.
        CargueRetorno[] cargues =
        [
            new(500, "Fosyga", "ana", "OK", new DateTime(2026, 9, 21, 9, 0, 0), new DateTime(2026, 9, 21)),
            new(509, "Fosyga", "juan", "OK", Fin, new DateTime(2026, 9, 24)),
        ];
        var envio = new DateTime(2026, 9, 20, 10, 0, 0);
        Assert.Equal(500, RetornoEnvio.ElegirCargue("Fosyga", envio, cargues, _ => false)!.EjecucionID);
        Assert.Equal(509, RetornoEnvio.ElegirCargue("Fosyga", envio, cargues, _ => false, vinculado: 509)!.EjecucionID);
    }
}
