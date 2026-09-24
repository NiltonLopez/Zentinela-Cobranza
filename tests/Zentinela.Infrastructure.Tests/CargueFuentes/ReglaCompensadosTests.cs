using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Traducción de sp_ProcesarCompensadosAuxiliar: marca 'NO SE ENCUENTRA' anulada; el flag sale de EPS,
/// período o la columna Estado del archivo ("encontrado sin datos", FlagDesdeArchivo).
/// </summary>
public class ReglaCompensadosTests
{
    private readonly ReglaCompensados _regla = new();

    [Theory]
    [InlineData("NO SE ENCUENTRA")]
    [InlineData("No se encuentra")]
    [InlineData("NO ENCONTRADO")]
    [InlineData("SIN INFORMACIÓN")]
    public void Marca_de_no_encontrado_en_TipoAfiliado_se_anula(string marca)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("TipoAfiliado", marca));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "TipoAfiliado"));
        Assert.Equal(1, Avisos(ctx, "TipoAfiliado: marca 'NO SE ENCUENTRA'"));
    }

    [Fact]
    public void Marca_en_EPS_tambien_se_anula()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("EPS", "NO SE ENCUENTRA"), ("TipoAfiliado", "COTIZANTE"));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "EPS"));
        Assert.Equal("COTIZANTE", ctx.Get(fila, "TipoAfiliado"));
    }

    [Theory]
    [InlineData("Nueva Eps", "COTIZANTE")]
    [InlineData("Salud Total", "BENEFICIARIO")]
    [InlineData("Sanitas", "ADICIONAL")]
    public void Datos_reales_pasan_sin_tocar(string eps, string tipo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("EPS", eps), ("TipoAfiliado", tipo));

        _regla.Transformar(fila, ctx);

        Assert.Equal(eps, ctx.Get(fila, "EPS"));
        Assert.Equal(tipo, ctx.Get(fila, "TipoAfiliado"));
        Assert.Empty(ctx.Advertencias());
    }

    [Fact]
    public void El_flag_sale_de_EPS_periodo_y_la_columna_Estado_del_archivo_que_no_se_guarda()
    {
        var d = CatalogoCargueFuentes.Compensados;
        Assert.Equal(["EPS", "UltimoPeriodoCompensado"], d.ColumnasDato.Where(c => c.EntraAlFlag).Select(c => c.Nombre));
        // Estado: extra (no se guarda), opcional, alimenta el flag.
        var estado = Assert.Single(d.ColumnasArchivoExtra);
        Assert.Equal("Estado", estado.Nombre);
        Assert.True(estado.FlagDesdeArchivo);
        Assert.False(estado.Obligatoria);
        Assert.Contains("estado", estado.Alias);
        Assert.DoesNotContain(d.ColumnasDato, c => c.Alias.Contains("estado"));
        Assert.True(d.TieneFlagDesdeArchivo);
        Assert.IsType<ReglaCompensados>(CatalogoReglasFuente.Obtener(d.Fuente));
    }

    // ── Estado del archivo: "encontrado sin datos" (H2 de PARIDAD-empresas.md) ─────

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]      // Excel / pandas
    [InlineData(" SI ")]
    [InlineData("Sí")]
    [InlineData("ENCONTRADO")]
    [InlineData("Compensado")]
    [InlineData("TRUE")]
    public void Estado_encontrado_se_lee_como_encontrado(string valor) =>
        Assert.Equal(FlagEnArchivo.Lectura.Encontrado, FlagEnArchivo.Leer(valor));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("NO")]
    [InlineData("No se encuentra")]
    [InlineData("NO ENCONTRADO")]
    [InlineData("NULL")]
    [InlineData("N/A")]
    public void Estado_vacio_o_de_no_encontrado_no_enciende(string? valor) =>
        Assert.Equal(FlagEnArchivo.Lectura.NoEncontrado, FlagEnArchivo.Leer(valor));

    [Theory]
    [InlineData("2")]
    [InlineData("ACTIVO")]
    [InlineData("PENDIENTE")]
    public void Estado_desconocido_no_se_reconoce(string valor) =>
        Assert.Equal(FlagEnArchivo.Lectura.NoReconocido, FlagEnArchivo.Leer(valor));

    [Fact]
    public void Solo_Estado_encontrado_enciende_el_flag_sin_datos()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = FilaConExtra(ctx, ["1"]);

        var f = Procesar(fila, ctx);

        Assert.True(f.Flag);
        Assert.True(f.FlagSoloPorArchivo);
        Assert.Equal("Estado", f.FlagDesdeColumnaArchivo);
        Assert.All(f.Datos, Assert.Null);
        Assert.Empty(ctx.Advertencias());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("NO SE ENCUENTRA")]
    [InlineData("")]
    [InlineData(null)]   // el archivo no trae la columna Estado: cálculo de siempre
    public void Estado_de_no_encontrado_deja_el_flag_en_0(string? estado)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = FilaConExtra(ctx, [estado], ("TipoAfiliado", "ADICIONAL"));

        var f = Procesar(fila, ctx);

        Assert.False(f.Flag);
        Assert.False(f.FlagSoloPorArchivo);
        Assert.Empty(ctx.Advertencias());
    }

    [Fact]
    public void Con_EPS_el_flag_no_es_solo_por_Estado()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = FilaConExtra(ctx, ["1"], ("EPS", "Nueva Eps"));

        var f = Procesar(fila, ctx);

        Assert.True(f.Flag);
        Assert.False(f.FlagSoloPorArchivo);
    }

    [Fact]
    public void Estado_desconocido_avisa_y_no_enciende()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = FilaConExtra(ctx, ["PENDIENTE"]);

        var f = Procesar(fila, ctx);

        Assert.False(f.Flag);
        Assert.Equal(1, Avisos(ctx, "Estado: valor no reconocido"));
    }

    [Fact]
    public void Info_agrupada_cuenta_las_filas_encontradas_sin_datos()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var filas = new[]
        {
            Procesar(FilaConExtra(ctx, ["1"]), ctx),
            Procesar(FilaConExtra(ctx, ["SI"]), ctx),
            Procesar(FilaConExtra(ctx, ["1"], ("EPS", "Sanitas")), ctx),
            Procesar(FilaConExtra(ctx, ["0"]), ctx),
        };

        CargueFuentesMotor.InformarFlagSoloPorArchivo(filas, ctx);

        var info = Assert.Single(ctx.Info());
        Assert.Equal("Estado: encontrado sin datos (EstadoEnCompensados = 1 solo por la columna del archivo)", info.Mensaje);
        Assert.Equal(2, info.Cantidad);
    }

    [Fact]
    public void Repetido_sin_datos_conserva_el_encontrado_sobre_el_no_encontrado()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var encontrado = Procesar(FilaConExtra(ctx, ["1"]), ctx, linea: 2);
        var noEncontrado = Procesar(FilaConExtra(ctx, ["0"]), ctx, linea: 3);   // la última del archivo

        var r = CargueFuentesMotor.Deduplicar([encontrado, noEncontrado], ctx.Definicion, ctx, out var duplicadas);

        Assert.Same(encontrado, Assert.Single(r));
        Assert.Equal(1, duplicadas);
    }

    [Fact]
    public void Otras_fuentes_no_tienen_flag_desde_archivo()
    {
        Assert.All(CatalogoCargueFuentes.Todas.Where(d => d.Fuente != CatalogoCargueFuentes.Compensados.Fuente),
                   d => Assert.False(d.TieneFlagDesdeArchivo, d.Fuente));
    }

    /// <summary>Lo que hace el motor con la fila tras tiparla: Estado, regla y flag.</summary>
    private FilaCargue Procesar(FilaCargue fila, ContextoCargue ctx, int linea = 2)
    {
        var f = new FilaCargue { Linea = linea, TitularID = fila.TitularID, Datos = fila.Datos, Extra = fila.Extra };
        CargueFuentesMotor.LeerFlagDesdeArchivo(f, ctx);
        _regla.Transformar(f, ctx);
        CargueFuentesMotor.CalcularFlag(f, ctx.Definicion);
        return f;
    }
}
