using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Validaciones genéricas del motor que no dependen de la BD: la columna de tipo
/// <c>Telefono</c>, el email que no cabe en su columna y el aviso de nombre truncado
/// (hallazgos B1, B2 y B3 de docs/sql/legacy/PARIDAD.md); la parte decimal nula de Excel/pandas
/// (S3 de PARIDAD-salud.md), el rango del entero y el aviso de fijo (H1 y H2 de PARIDAD-otras.md)
/// y el NIT con DV (H4 de PARIDAD-empresas.md).
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

    // ── S3 (PARIDAD-salud.md): parte decimal nula de Excel / pandas ──────────

    [Theory]
    [InlineData("3001234567.0")]
    [InlineData("3001234567,00")]
    [InlineData("3.001.234.567,00")]
    public void Celular_con_parte_decimal_nula_entra(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);

        var r = CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "NumeroCelular"), entrada, ctx);

        Assert.Equal("3001234567", r);
        Assert.Empty(ctx.Advertencias());
    }

    [Theory]
    [InlineData("1234567", 1234567L)]
    [InlineData("1.234.567", 1234567L)]
    [InlineData("1234567.0", 1234567L)]
    [InlineData("1234567.00", 1234567L)]        // antes: '123456700', otro documento
    [InlineData("1234567,00", 1234567L)]
    [InlineData("1.234.567,00", 1234567L)]
    public void Documento_con_parte_decimal_nula_se_lee(string entrada, long esperado)
    {
        Assert.True(CargueFuentesMotor.TryDocumento(entrada, out var doc));
        Assert.Equal(esperado, doc);
    }

    [Theory]
    [InlineData("1234567.5")]     // antes: 12345675
    [InlineData("12345678,25")]
    public void Documento_con_decimales_reales_es_invalido(string entrada)
    {
        Assert.False(CargueFuentesMotor.TryDocumento(entrada, out _));
    }

    [Theory]
    [InlineData("2021", 2021L)]
    [InlineData("2021.0", 2021L)]
    [InlineData("2021,00", 2021L)]
    public void Entero_con_parte_decimal_nula_se_lee(string entrada, long esperado)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);

        var r = CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "Modelo"), entrada, ctx);

        Assert.Equal(esperado, r);
        Assert.Empty(ctx.Advertencias());
    }

    [Theory]
    [InlineData("2021.5")]
    [InlineData("12,34")]      // antes: 1234
    public void Entero_con_decimales_reales_se_anula_con_aviso(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);

        var r = CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "Modelo"), entrada, ctx);

        Assert.Null(r);
        Assert.Equal(1, Avisos(ctx, "Modelo: valor con decimales anulado"));
    }

    // ── H1 (PARIDAD-otras.md): entero que no cabe en el tipo del destino ─────

    [Fact]
    public void Entero_int_fuera_de_rango_se_anula_con_aviso_agrupado()
    {
        // RGM.Modelo es int: 2147483648 haría fallar el INSERT de todo el archivo.
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var col = Columna(ctx.Definicion, "Modelo");

        Assert.Equal(2147483647L, CargueFuentesMotor.TiparEntero(col, "2147483647", ctx));
        Assert.Null(CargueFuentesMotor.TiparEntero(col, "2147483648", ctx));
        Assert.Null(CargueFuentesMotor.TiparEntero(col, "99999999999", ctx));

        Assert.Equal(2, Avisos(ctx, "Modelo: número fuera del rango de la columna (int) anulado"));
        Assert.Single(ctx.Advertencias());
    }

    [Fact]
    public void Entero_bigint_fuera_de_rango_se_anula_como_fuera_de_rango()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rues);
        var col = Columna(ctx.Definicion, "NumeroMatricula");

        Assert.Equal(2147483648L, CargueFuentesMotor.TiparEntero(col, "2147483648", ctx));   // bigint: cabe
        Assert.Null(CargueFuentesMotor.TiparEntero(col, "99999999999999999999", ctx));

        Assert.Equal(1, Avisos(ctx, "NumeroMatricula: número fuera del rango de la columna (bigint) anulado"));
        Assert.Equal(0, Avisos(ctx, "no numérico"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("0.0")]
    public void Entero_cero_es_vacio_sin_aviso(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);

        Assert.Null(CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "NIT"), entrada, ctx));
        Assert.Empty(ctx.Advertencias());
    }

    // ── H4 (PARIDAD-empresas.md): NIT con dígito de verificación ─────────────

    [Theory]
    [InlineData("900123456-7")]
    [InlineData("900.123.456-7")]
    [InlineData("900123456 - 7")]
    public void Nit_con_dv_pierde_el_dv_con_aviso_informativo(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.PositivaEmpresas);

        var r = CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "NIT"), entrada, ctx);

        Assert.Equal(900123456L, r);
        Assert.Empty(ctx.Advertencias());
        var info = Assert.Single(ctx.Info());
        Assert.Equal(("NIT: dígito de verificación quitado", 1), (info.Mensaje, info.Cantidad));
    }

    [Fact]
    public void Nit_sin_dv_no_informa()
    {
        var ctx = Contexto(CatalogoCargueFuentes.ArlSura);

        Assert.Equal(900123456L, CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "NIT"), "900.123.456", ctx));
        Assert.Empty(ctx.Info());
    }

    [Fact]
    public void Guion_en_un_entero_que_no_es_nit_sigue_siendo_no_numerico()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rues);

        Assert.Null(CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "NumeroMatricula"), "123456-7", ctx));
        Assert.Equal(1, Avisos(ctx, "NumeroMatricula: valor no numérico anulado"));
        Assert.Empty(ctx.Info());
    }

    [Fact]
    public void Nit_con_dv_que_igual_se_anula_no_informa_el_dv()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sos);

        Assert.Null(CargueFuentesMotor.TiparEntero(Columna(ctx.Definicion, "NitEmpresa"), "99999999999999999999-7", ctx));
        Assert.Equal(1, Avisos(ctx, "NitEmpresa: número fuera del rango de la columna (bigint) anulado"));
        Assert.Empty(ctx.Info());
    }

    // ── H2 (PARIDAD-otras.md): "fijo" solo si empieza en 6 ──────────────────

    [Fact]
    public void Celular_con_fijo_de_verdad_avisa_fijo()
    {
        var ctx = Contexto(CatalogoCargueFuentes.TigoUne);

        Assert.Null(CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "NumeroTelefono"), "6012345678", ctx));

        Assert.Equal(1, Avisos(ctx, "NumeroTelefono: número fijo en la columna de celular, anulado"));
        Assert.Equal(0, Avisos(ctx, "celular inválido"));
    }

    [Theory]
    [InlineData("5551234567")]   // 10 dígitos que no empiezan en 3 ni en 6 (U05 de TigoUne)
    [InlineData("1234567890")]
    [InlineData("1234567")]
    public void Celular_que_no_es_celular_ni_fijo_avisa_invalido(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.TigoUne);

        Assert.Null(CargueFuentesMotor.ValidarContacto(Columna(ctx.Definicion, "NumeroTelefono"), entrada, ctx));

        Assert.Equal(1, Avisos(ctx, "NumeroTelefono: celular inválido anulado"));
        Assert.Equal(0, Avisos(ctx, "fijo"));
    }
}
