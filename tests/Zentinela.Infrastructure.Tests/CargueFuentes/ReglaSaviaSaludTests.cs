using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Pasos 5 y 6 de sp_ProcesarSaviaSaludAuxiliar: celdas con varios números separados por coma.
/// El SP los volvía a unir con coma; la regla los reparte entre NumeroCelular y NumeroTelefono.
/// </summary>
public class ReglaSaviaSaludTests
{
    private const string Tel = "NumeroTelefono", Cel = "NumeroCelular";
    private static readonly ReglaSaviaSalud Regla = new();

    /// <summary>Regla + validación genérica, como en LimpiarFila. Devuelve "celular|telefono".</summary>
    private static (ContextoCargue Ctx, string Resultado) Limpiar(string? cel, string? tel)
    {
        var def = CatalogoCargueFuentes.SaviaSalud;
        var ctx = Contexto(def);
        var fila = Fila(ctx, (Cel, cel), (Tel, tel));
        Regla.Transformar(fila, ctx);
        for (var i = 0; i < def.ColumnasDato.Count; i++)
            fila.Datos[i] = CargueFuentesMotor.ValidarContacto(def.ColumnasDato[i], fila.Datos[i], ctx);
        return (ctx, $"{ctx.GetTexto(fila, Cel)}|{ctx.GetTexto(fila, Tel)}");
    }

    [Fact]
    public void Un_numero_por_celda_no_se_toca()
    {
        var (ctx, r) = Limpiar("3001234567", "3007654321");
        Assert.Equal("3001234567|3007654321", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Dos_celulares_en_la_celda_se_reparten_sin_concatenar()
    {
        // El SP guardaba '3147630194,3148795401' en NumeroCelular varchar(100).
        var (ctx, r) = Limpiar("3147630194,3148795401", null);
        Assert.Equal("3147630194|3148795401", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData("3147630194; 3148795401")]
    [InlineData("3147630194 / 3148795401")]
    [InlineData("314 763 0194, +57 314 879 5401")]
    public void Otros_separadores_y_formatos(string cel)
    {
        var (_, r) = Limpiar(cel, null);
        Assert.Equal("3147630194|3148795401", r);
    }

    [Fact]
    public void Con_telefono_propio_el_segundo_celular_se_descarta_con_aviso()
    {
        var (ctx, r) = Limpiar("3147630194,3148795401", "6045551234");
        Assert.Equal("3147630194|6045551234", r);
        Assert.Equal(1, Avisos(ctx, "Teléfono adicional de la lista descartado"));
    }

    [Fact]
    public void Fragmentos_invalidos_se_descartan_con_aviso()
    {
        // El SP quitaba los fragmentos de largo distinto de 10 (en silencio).
        var (ctx, r) = Limpiar("3147630194,12345,COPACABANA", null);
        Assert.Equal("3147630194|", r);
        Assert.Equal(2, Avisos(ctx, $"{Cel}: número de la lista que no es un teléfono válido"));
    }

    [Fact]
    public void Lista_en_telefono_con_celular_repetido()
    {
        var (ctx, r) = Limpiar("3147630194", "3147630194,6045551234");
        Assert.Equal("3147630194|6045551234", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Fijo_en_la_lista_del_celular_va_al_telefono()
    {
        var (ctx, r) = Limpiar("6045551234,3147630194", null);
        Assert.Equal("3147630194|6045551234", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Tres_numeros_conservan_dos()
    {
        var (ctx, r) = Limpiar("3147630194,3148795401,3001112222", null);
        Assert.Equal("3147630194|3148795401", r);
        Assert.Equal(1, Avisos(ctx, "Teléfono adicional de la lista descartado"));
    }

    [Fact]
    public void Fijo_solo_en_el_celular_pasa_al_telefono_vacio()
    {
        var (ctx, r) = Limpiar("6045551234", null);
        Assert.Equal("|6045551234", r);
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Fijo_en_el_celular_con_telefono_ocupado_lo_anula_el_motor()
    {
        var (ctx, r) = Limpiar("6045551234", "3001112222");
        Assert.Equal("|3001112222", r);
        Assert.Equal(1, Avisos(ctx, $"{Cel}: número fijo en la columna de celular, anulado"));
    }

    [Fact]
    public void Texto_en_el_telefono_lo_anula_el_motor()
    {
        // 'COPACABANA' (10 letras) pasaba el filtro LEN = 10 del SP.
        var (ctx, r) = Limpiar(null, "COPACABANA");
        Assert.Equal("|", r);
        Assert.Equal(1, Avisos(ctx, $"{Tel}: teléfono inválido anulado"));
    }

    [Fact]
    public void Lista_sin_ningun_numero_valido_deja_las_columnas_vacias()
    {
        var (ctx, r) = Limpiar("123,456", null);
        Assert.Equal("|", r);
        Assert.Equal(2, Avisos(ctx, "no es un teléfono válido"));
    }
}
