using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Validaciones genéricas del motor que no dependen de la BD: la columna de tipo
/// <c>Telefono</c>, el email que no cabe en su columna y el aviso de nombre truncado
/// (hallazgos B1, B2 y B3 de docs/sql/legacy/PARIDAD.md).
/// </summary>
public class CargueFuentesMotorValidacionTests
{
    private static ColumnaFuenteCargue Columna(DefinicionFuenteCargue def, string nombre) =>
        def.ColumnasDato.Single(c => c.Nombre == nombre);

    // ── B1: Telefono = celular (3xx) o fijo (60x) ─────────────────────────────

    [Theory]
    [InlineData("3001234567", "3001234567")]
    [InlineData("+57 300 123 4567", "3001234567")]
    [InlineData("6041234567", "6041234567")]
    [InlineData("601-234-5678", "6012345678")]
    public void Telefono_celular_o_fijo_entra(string entrada, string esperado)
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "NumeroTelefono"), entrada, ctx);

        Assert.Equal(esperado, r);
        Assert.Empty(ctx.Advertencias());
    }

    [Theory]
    [InlineData("5551234567")]   // 10 dígitos que no empiezan en 3 ni en 6 (caso D11 de la paridad)
    [InlineData("1234567890")]
    [InlineData("1234567")]      // 7 dígitos
    public void Telefono_que_no_es_celular_ni_fijo_se_anula_con_aviso(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "NumeroTelefono"), entrada, ctx);

        Assert.Null(r);
        Assert.Equal(1, Avisos(ctx, "NumeroTelefono: teléfono inválido anulado"));
    }

    // ── B3: email demasiado largo ≠ email con formato inválido ────────────────

    [Fact]
    public void Email_mas_largo_que_la_columna_se_anula_como_demasiado_largo()
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);
        var email = new string('a', 100) + "@gmail.com";   // 110 caracteres, Email varchar(100)

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "Email"), email, ctx);

        Assert.Null(r);
        Assert.Equal(1, Avisos(ctx, "Email: email de más de 100 caracteres anulado"));
        Assert.Equal(0, Avisos(ctx, "formato inválido"));
    }

    [Fact]
    public void Email_de_largo_exacto_entra()
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);
        var email = new string('a', 90) + "@gmail.com";    // 100 caracteres

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "Email"), email, ctx);

        Assert.Equal(email, r);
        Assert.Empty(ctx.Advertencias());
    }

    [Theory]
    [InlineData("sin arroba")]
    [InlineData("a@b")]
    public void Email_sin_forma_de_correo_se_anula_como_formato_invalido(string email)
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "Email"), email, ctx);

        Assert.Null(r);
        Assert.Equal(1, Avisos(ctx, "Email: email con formato inválido anulado"));
        Assert.Equal(0, Avisos(ctx, "caracteres anulado"));
    }

    [Fact]
    public void Email_largo_y_sin_forma_se_informa_como_formato_invalido()
    {
        // Si no es un correo, que además sea largo es irrelevante: el aviso es el de formato.
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "Email"), new string('a', 150), ctx);

        Assert.Null(r);
        Assert.Equal(1, Avisos(ctx, "formato inválido"));
    }

    // ── B2: nombre truncado a 70 ──────────────────────────────────────────────

    [Fact]
    public void Nombre_de_mas_de_70_se_trunca_con_aviso_agrupado()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        var largo = "NOMBRE MUY LARGO DE UNA RAZON SOCIAL QUE NO CABE EN SETENTA CARACTERES SA";   // 73

        var f1 = Fila(ctx);
        var f2 = Fila(ctx);
        CargueFuentesMotor.AsignarNombre(f1, largo, ctx);
        CargueFuentesMotor.AsignarNombre(f2, largo + " DOS", ctx);

        Assert.Equal(NormalizadorTitular.LargoMaximo, f1.Nombre!.Length);
        Assert.Equal(2, Avisos(ctx, "Nombre: truncado a 70 caracteres"));
        Assert.Single(ctx.Advertencias());   // un solo mensaje, con cantidad 2
    }

    [Fact]
    public void Nombre_que_cabe_no_avisa()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        var fila = Fila(ctx);

        CargueFuentesMotor.AsignarNombre(fila, "Juan Pérez", ctx);

        Assert.Equal("JUAN PEREZ", fila.Nombre);
        Assert.Empty(ctx.Advertencias());
    }
}
