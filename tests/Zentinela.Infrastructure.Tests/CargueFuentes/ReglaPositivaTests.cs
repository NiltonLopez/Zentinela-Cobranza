using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Paso 4 de sp_ProcesarPositivaAuxiliar: el fijo que llega en la columna de celular pasa al
/// fijo, sin concatenar. Lo que la regla deja mal ubicado lo anula después el motor.
/// </summary>
public class ReglaPositivaTests
{
    private const string Cel = "NumeroTelefonoCelular", Fijo = "NumeroTelefonoFijo";
    private static readonly ReglaPositiva Regla = new();

    private static (ContextoCargue Ctx, string Resultado) Transformar(string? cel, string? fijo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Positiva);
        var fila = Fila(ctx, (Cel, cel), (Fijo, fijo));
        Regla.Transformar(fila, ctx);
        return (ctx, $"{ctx.GetTexto(fila, Cel)}|{ctx.GetTexto(fila, Fijo)}");
    }

    [Fact]
    public void No_pide_prefijos_ni_lista_negra_de_telefonos()
    {
        IReglaFuente r = Regla;
        Assert.False(r.NecesitaPrefijos);
        Assert.False(r.NecesitaListaNegraTelefonos);
    }

    [Fact]
    public void Fijo_en_el_celular_pasa_al_fijo_vacio()
    {
        var (ctx, r) = Transformar("6012345678", null);
        Assert.Equal("|6012345678", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData("601 234 5678")]
    [InlineData("601-234-5678")]
    public void Fijo_en_el_celular_con_separadores_se_normaliza_al_moverlo(string cel)
    {
        var (_, r) = Transformar(cel, null);
        Assert.Equal("|6012345678", r);
    }

    [Fact]
    public void Con_otro_fijo_en_su_columna_no_se_concatena_y_se_avisa()
    {
        // El SP dejaba '6049876543, 6012345678' en NumeroTelefonoFijo varchar(30).
        var (ctx, r) = Transformar("6012345678", "6049876543");
        Assert.Equal("|6049876543", r);
        Assert.Equal(1, Avisos(ctx, $"{Cel}: fijo en la columna de celular descartado"));
    }

    [Fact]
    public void El_mismo_fijo_en_las_dos_columnas_no_es_conflicto()
    {
        var (ctx, r) = Transformar("6012345678", "601 234 5678");
        Assert.Equal("|601 234 5678", r);   // el fijo queda como vino: lo normaliza el motor
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Columnas_intercambiadas_cada_numero_a_la_suya()
    {
        var (ctx, r) = Transformar("6012345678", "3001234567");
        Assert.Equal("3001234567|6012345678", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Fijo_basura_se_reemplaza_con_advertencia()
    {
        var (ctx, r) = Transformar("6012345678", "SIN DATO");
        Assert.Equal("|6012345678", r);
        Assert.Equal(1, Avisos(ctx, $"{Fijo}: valor inválido reemplazado"));
    }

    [Theory]
    [InlineData("3001234567", "6012345678")]   // todo en su lugar
    [InlineData("3001234567", null)]
    [InlineData("573001234567", null)]         // celular con 57: lo normaliza el motor
    [InlineData("6123456", null)]              // empieza en 6 pero no es fijo de 10: lo anula el motor
    [InlineData("0", null)]
    [InlineData(null, "3001234567")]           // celular en el fijo sin fijo en el celular: lo anula el motor
    [InlineData(null, null)]
    public void Lo_que_no_es_un_fijo_en_el_celular_no_se_toca(string? cel, string? fijo)
    {
        var (ctx, r) = Transformar(cel, fijo);
        Assert.Equal($"{cel}|{fijo}", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Motor_completo_anula_el_celular_que_empieza_en_6_y_no_es_fijo()
    {
        // Cadena completa regla + validación genérica, como en LimpiarFila.
        var def = CatalogoCargueFuentes.Positiva;
        var ctx = Contexto(def);
        var fila = Fila(ctx, (Cel, "6123456"), (Fijo, null));
        Regla.Transformar(fila, ctx);
        for (var i = 0; i < def.ColumnasDato.Count; i++)
            fila.Datos[i] = CargueFuentesMotor.ValidarContacto(def.ColumnasDato[i], fila.Datos[i], ctx);
        Assert.Null(ctx.Get(fila, Cel));
        Assert.Null(ctx.Get(fila, Fijo));
        Assert.Equal(1, Avisos(ctx, $"{Cel}: celular inválido anulado"));
    }
}
