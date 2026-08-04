using System;
using Zentinela.Application.Config;

namespace Zentinela.API.Tests;

// Cobertura del clasificador de categorías estandarizadas del Comparador de
// Carteras (ADR-029). Las reglas son excluyentes y se evalúan en orden:
// YaPago > Promesa > Tercero > SinContacto > ContactoDirecto > SinGestion > Otros.
// El diseño busca que Otros quede como bucket residual mínimo.
public class PerfilClasificacionTests
{
    // Fecha fija para que la ventana dinámica (año pasado + año actual) sea determinista.
    private static readonly DateOnly Hoy = new(2026, 7, 23);

    private static CategoriaPerfil Clasificar(
        string? mejorPerfil = null,
        string? ultimoPerfil = null,
        DateOnly? fechaPromesa = null,
        int? rotacion = null,
        DateOnly? fechaUltimaGestion = null)
        => PerfilClasificacion.Clasificar(mejorPerfil, ultimoPerfil, fechaPromesa, rotacion, fechaUltimaGestion, Hoy);

    // ── Ya pago ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Paz y salvo")]
    [InlineData("paz y salvo")]          // case-insensitive
    [InlineData("Consignación por aplicar")]
    [InlineData("Ya pago")]
    [InlineData("Pago reflejado")]
    [InlineData("Pago  reflejado")]      // así aparece en la BD (doble espacio)
    [InlineData("Caso especial")]
    [InlineData("Pago Total")]           // legacy
    [InlineData("Normalizado")]          // legacy
    public void YaPago_agrupa_paz_y_salvo_y_ya_pago(string perfil) =>
        Assert.Equal(CategoriaPerfil.YaPago, Clasificar(mejorPerfil: perfil));

    [Fact]
    public void YaPago_gana_aunque_haya_fecha_promesa_vigente() =>
        Assert.Equal(CategoriaPerfil.YaPago,
            Clasificar(mejorPerfil: "Paz y salvo", fechaPromesa: new DateOnly(2026, 5, 1)));

    // ── Promesa ───────────────────────────────────────────────────────────────

    [Fact]
    public void Promesa_por_fecha_vigente_con_cualquier_tipificacion() =>
        Assert.Equal(CategoriaPerfil.Promesa,
            Clasificar(mejorPerfil: "Renuente", fechaPromesa: new DateOnly(2026, 3, 10)));

    [Fact]
    public void Promesa_por_fecha_del_anio_pasado() =>
        Assert.Equal(CategoriaPerfil.Promesa,
            Clasificar(mejorPerfil: "Sin contacto", fechaPromesa: new DateOnly(2025, 1, 1)));

    // Un acuerdo/compromiso de pago clasifica Promesa solo si además tiene
    // FechaUltimaGestion vigente (misma ventana de 2 años que FechaPromesa) —
    // confirmado por negocio 2026-07-24: un acuerdo viejo sin gestión reciente
    // ya no debe pesar como Promesa (ver caso real TitularID 40771493 / LUMEN).
    [Theory]
    [InlineData("Acuerdo de pago")]
    [InlineData("Acuerdo de pago total")]
    [InlineData("Acuerdo de pago abono")]
    [InlineData("Compromiso de pago total")]
    [InlineData("Compromiso de abono")]
    [InlineData("En acuerdo")]
    [InlineData("Gestion Whatsapp / Promesa de Pago")]
    [InlineData("Promesa de Pago")]      // legacy
    public void Promesa_por_perfil_de_acuerdo_con_gestion_vigente(string perfil) =>
        Assert.Equal(CategoriaPerfil.Promesa,
            Clasificar(mejorPerfil: perfil, fechaUltimaGestion: new DateOnly(2026, 6, 1)));

    [Theory]
    [InlineData("Acuerdo de pago")]
    [InlineData("Acuerdo de pago total")]
    [InlineData("Compromiso de pago total")]
    public void ContactoDirecto_por_perfil_de_acuerdo_sin_gestion_vigente(string perfil) =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto,
            Clasificar(mejorPerfil: perfil, fechaUltimaGestion: new DateOnly(2022, 12, 20)));

    [Fact]
    public void ContactoDirecto_por_perfil_de_acuerdo_sin_ninguna_fecha() =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto,
            Clasificar(mejorPerfil: "Acuerdo de pago total"));

    // ── Tercero ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Contacto con tercero")]
    [InlineData("Mensaje con tercero")]
    [InlineData("Razón con tercero")]    // regla "contiene tercero"
    [InlineData("Familiar")]
    [InlineData("Contacto con familiar")] // regla "contiene familiar"
    public void Tercero_por_perfil_alusivo_a_tercero(string perfil) =>
        Assert.Equal(CategoriaPerfil.Tercero, Clasificar(mejorPerfil: perfil));

    [Fact]
    public void Tercero_gana_a_una_fecha_promesa_vencida() =>
        Assert.Equal(CategoriaPerfil.Tercero,
            Clasificar(mejorPerfil: "Mensaje con tercero", fechaPromesa: new DateOnly(2023, 1, 1)));

    // ── Sin contacto ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sin contacto")]
    [InlineData("No contacto")]          // legacy
    [InlineData("Mensaje en buzón")]
    [InlineData("Numero equivocado")]
    [InlineData("Equivocado")]
    [InlineData("Ilocalizado")]
    [InlineData("Mensaje de voz")]
    [InlineData("Ilocalizado total")]
    [InlineData("Datos localización")]
    [InlineData("Datos Localizacion")]
    [InlineData("No contestan")]
    [InlineData("Correo")]
    [InlineData("SMS")]
    [InlineData("Carteo")]
    [InlineData("Reclamación o derecho de petición")]
    [InlineData("Reclamacion o derecho de peticion")]
    [InlineData("IVR")]
    [InlineData("Se deja mensaje en contestadora")]
    [InlineData("Numero fuera de servicio")]
    [InlineData("Linea errada")]
    [InlineData("Ilocalizado parcial")]
    [InlineData("No contactado")]
    [InlineData("Solicitud Masivo")]
    [InlineData("Se solicita carta")]
    [InlineData("No contesta")]
    [InlineData("Fuera de servicio")]
    [InlineData("Envio masivo : WPP-Mail")]
    [InlineData("Telefono ocupado")]
    [InlineData("Datos de Localizacion")]
    [InlineData("Cliente Fallecido")]
    [InlineData("Contestar Buzon")]
    [InlineData("Se Dejo Mensaje")]
    [InlineData("No Se Dejo Mensaje")]
    [InlineData("No lo conocen")]
    [InlineData("Informativo Chat")]
    [InlineData("Gestion Whatsapp / No Intencion Pago")]
    public void SinContacto_por_marcacion_sin_contacto(string perfil) =>
        Assert.Equal(CategoriaPerfil.SinContacto, Clasificar(mejorPerfil: perfil));

    [Fact]
    public void SinContacto_gana_a_una_fecha_promesa_vencida() =>
        Assert.Equal(CategoriaPerfil.SinContacto,
            Clasificar(mejorPerfil: "Sin contacto", fechaPromesa: new DateOnly(2023, 1, 1)));

    // Casos de "sin gestión" incompleto: no cumplen las 3 condiciones a la vez
    // (falta rotación 0, o los dos perfiles no coinciden). Van a Sin contacto,
    // no a Otros — hubo al menos un intento de marcación registrado.

    [Fact]
    public void SinGestionIncompleto_por_rotacion_mayor_a_cero_va_a_SinContacto() =>
        Assert.Equal(CategoriaPerfil.SinContacto, Clasificar(rotacion: 3));

    [Fact]
    public void SinGestionIncompleto_ambos_sin_gestion_pero_rotacion_no_cero_va_a_SinContacto() =>
        Assert.Equal(CategoriaPerfil.SinContacto,
            Clasificar(mejorPerfil: "Sin gestion", ultimoPerfil: "Sin gestion", rotacion: 2));

    [Fact]
    public void SinGestionIncompleto_perfiles_no_coinciden_va_a_SinContacto() =>
        Assert.Equal(CategoriaPerfil.SinContacto,
            Clasificar(mejorPerfil: "Sin gestion", ultimoPerfil: "Renuente", rotacion: 0));

    // ── Contacto directo ──────────────────────────────────────────────────────
    // Entra TODO contacto directo sin importar fecha (a diferencia de Promesa,
    // que sí depende de vigencia).

    [Theory]
    [InlineData("No hubo acuerdo")]
    [InlineData("Renuente")]
    [InlineData("Fraude o suplantacion de identidad")]
    [InlineData("No gestionar")]
    [InlineData("Volver a llamar")]
    [InlineData("Realizo abono")]
    [InlineData("Abonando")]
    [InlineData("Recordar")]
    [InlineData("Cliente pacta plan de pago")]
    [InlineData("Posible acuerdo")]
    [InlineData("Compromiso de pago")]
    [InlineData("Compromiso de pago con campaña(descuento)")]
    [InlineData("Abonado")]
    [InlineData("Recordacion de pago")]
    [InlineData("Recordatorio")]
    [InlineData("Recordatorio pago")]
    [InlineData("Corre fecha")]
    [InlineData("Solicitud de información")]
    [InlineData("Solicitud De Informacion")]
    [InlineData("Recordatorio de pago")]
    [InlineData("Refinanciacion sin pago")]
    [InlineData("Pago no reflejado")]
    public void ContactoDirecto_sin_importar_fecha(string perfil) =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto, Clasificar(mejorPerfil: perfil));

    [Fact]
    public void ContactoDirecto_con_fecha_promesa_vencida_y_perfil_de_contacto_directo() =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto,
            Clasificar(mejorPerfil: "Renuente", fechaPromesa: new DateOnly(2024, 1, 1)));

    // Catch-all: una FechaPromesa vencida (no vigente, no capturada por Promesa)
    // que llega hasta aquí también cuenta como Contacto directo — hubo una
    // promesa alguna vez, aunque vieja.

    [Fact]
    public void FechaPromesaVencida_sin_perfil_de_acuerdo_va_a_ContactoDirecto() =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto,
            Clasificar(mejorPerfil: "PQR", fechaPromesa: new DateOnly(2024, 12, 31)));

    [Fact]
    public void FechaPromesaVencida_con_mejorPerfil_vacio_va_a_ContactoDirecto() =>
        Assert.Equal(CategoriaPerfil.ContactoDirecto,
            Clasificar(fechaPromesa: new DateOnly(2020, 1, 1)));

    // ── Sin gestión ───────────────────────────────────────────────────────────
    // Único caso realmente "nunca tocado": ambos perfiles sin gestión Y rotación 0.

    [Fact]
    public void SinGestion_con_perfiles_nulos_y_rotacion_cero() =>
        Assert.Equal(CategoriaPerfil.SinGestion, Clasificar(rotacion: 0));

    [Fact]
    public void SinGestion_con_rotacion_null_cuenta_como_cero() =>
        Assert.Equal(CategoriaPerfil.SinGestion,
            Clasificar(mejorPerfil: "Sin gestion", ultimoPerfil: "Sin gestion"));

    // ── Otros ─────────────────────────────────────────────────────────────────
    // Solo debe quedar un perfil real, no mapeado a ninguna categoría, y sin
    // FechaPromesa (con fecha, cae en Promesa o Contacto directo).

    [Fact]
    public void PerfilNoMapeado_sin_fecha_promesa_va_a_Otros() =>
        Assert.Equal(CategoriaPerfil.Otros, Clasificar(mejorPerfil: "Codigo Nuevo XYZ"));

    // ── Orden y etiquetas ─────────────────────────────────────────────────────

    [Fact]
    public void El_peso_respeta_el_orden_de_categorias()
    {
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.YaPago)
                  > PerfilClasificacion.Peso(CategoriaPerfil.Promesa));
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.Promesa)
                  > PerfilClasificacion.Peso(CategoriaPerfil.ContactoDirecto));
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.ContactoDirecto)
                  > PerfilClasificacion.Peso(CategoriaPerfil.Tercero));
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.Tercero)
                  > PerfilClasificacion.Peso(CategoriaPerfil.SinContacto));
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.SinContacto)
                  > PerfilClasificacion.Peso(CategoriaPerfil.SinGestion));
        Assert.True(PerfilClasificacion.Peso(CategoriaPerfil.SinGestion)
                  > PerfilClasificacion.Peso(CategoriaPerfil.Otros));
    }

    [Fact]
    public void Toda_categoria_tiene_etiqueta()
    {
        foreach (CategoriaPerfil c in Enum.GetValues<CategoriaPerfil>())
            Assert.False(string.IsNullOrWhiteSpace(PerfilClasificacion.Etiqueta(c)));
    }
}
