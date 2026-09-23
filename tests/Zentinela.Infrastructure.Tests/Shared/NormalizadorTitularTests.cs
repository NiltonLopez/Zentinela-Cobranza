using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class NormalizadorTitularTests
{
    [Theory]
    // Mojibake: UTF-8 leído como cp1252 (una pasada).
    [InlineData("PIÃ\u0091A ", "PIÑA", true, false)]        // hueco C1 0x91 preservado
    [InlineData("NIÃ‘O", "NIÑO", true, false)]         // U+2018 → 0x91 (no existe en Latin-1)
    [InlineData("MARÃ\u008DA", "MARIA", true, false)]       // hueco 0x8D
    [InlineData("Ã\u0081NGEL", "ANGEL", true, false)]       // hueco 0x81
    [InlineData("JOSÃ‰", "JOSE", true, false)]         // U+2030 → 0x89
    [InlineData("RAMÃ“N", "RAMON", true, false)]       // U+201C → 0x93
    [InlineData("ÃŠXITO", "EXITO", true, false)]       // U+0160 → 0x8A
    // Mojibake doble: dos pasadas.
    [InlineData("PEÃƒÂ‘A", "PEÑA", true, false)]
    // '?' irrecuperable: se conserva y se marca.
    [InlineData("PI?A LOPEZ", "PI?A LOPEZ", false, true)]
    // Limpieza general.
    [InlineData("  juan   pérez  ", "JUAN PEREZ", false, false)]
    [InlineData("MARÍA-JOSÉ O'NEIL", "MARIA JOSE O NEIL", false, false)]
    [InlineData("ÑUÑEZ Müller", "ÑUÑEZ MULLER", false, false)]
    [InlineData("Ana Ma. de la Cruz 2", "ANA MA DE LA CRUZ", false, false)]
    // 'Ã' suelta = byte perdido: NO se adivina la letra (era Ñ, Í o Á según el caso). Pasa a '?'
    // e irrecuperable. Antes se volvía 'A' en silencio y producía "MUAOZ" / "RAMAREZ".
    [InlineData("Ã", "?", false, true)]
    [InlineData("MUÃOZ BERNAL", "MU?OZ BERNAL", false, true)]         // era MUÑOZ (real)
    [InlineData("ORLANDO RAMÃREZ MONTAÑO", "ORLANDO RAM?REZ MONTAÑO", false, true)] // era RAMÍREZ (real)
    // Nombres MIXTOS (casos reales): una letra ya correcta en otra parte del nombre hacía fallar
    // la reparación completa. Ahora se repara por tramos y la letra sana se conserva.
    [InlineData("PERÉZ GARCÃƒÂ\u008DA MARIELA", "PEREZ GARCIA MARIELA", true, false)]
    [InlineData("RODRÃƒÂ\u008DGUEZ PEÑA MARCELA", "RODRIGUEZ PEÑA MARCELA", true, false)]
    [InlineData("CÃƒÂ\u0081ÑAMO MAVAREZ", "CAÑAMO MAVAREZ", true, false)]
    [InlineData("ORDÃƒÂ“ÑEZ GONZLEZ", "ORDOÑEZ GONZLEZ", true, false)]
    [InlineData("VÉLEZ ÃƒÂ\u0081LVAREZ GABRIELA", "VELEZ ALVAREZ GABRIELA", true, false)]
    // Espacio duro (NBSP) doblemente codificado "Ã‚Â " → espacio normal.
    [InlineData("HEIDYSÃ‚Â  PATRICIA", "HEIDYS PATRICIA", true, false)]
    // Una parte reparable y otra perdida en el mismo nombre: se repara lo que se puede y se marca.
    [InlineData("PEÃ‘A SÃNCHEZ", "PEÑA S?NCHEZ", true, true)]
    // Vacíos.
    [InlineData(null, "", false, false)]
    [InlineData("", "", false, false)]
    [InlineData("   ", "", false, false)]
    public void LimpiarNombre_casos(string? entrada, string esperado, bool reparado, bool irrecuperable)
    {
        var r = NormalizadorTitular.LimpiarNombre(entrada);

        Assert.Equal(esperado, r.Nombre);
        Assert.Equal(reparado, r.Reparado);
        Assert.Equal(irrecuperable, r.Irrecuperable);
    }

    [Fact]
    public void LimpiarNombre_trunca_a_70()
    {
        var r = NormalizadorTitular.LimpiarNombre(new string('A', 75));

        Assert.Equal(NormalizadorTitular.LargoMaximo, r.Nombre.Length);
        Assert.Equal(new string('A', 70), r.Nombre);
    }

    [Fact]
    public void LimpiarNombre_truncado_no_termina_en_espacio()
    {
        // El corte en 70 cae justo después de un espacio: se recorta.
        var entrada = new string('A', 69) + " BBBBB";
        var r = NormalizadorTitular.LimpiarNombre(entrada);

        Assert.Equal(new string('A', 69), r.Nombre);
    }

    [Fact]
    public void RepararMojibake_doble_requiere_dos_pasadas()
    {
        Assert.Equal("PEÑA", NormalizadorTitular.RepararMojibake("PEÃƒÂ‘A"));
    }

    [Fact]
    public void RepararMojibake_sin_senal_devuelve_la_misma_instancia()
    {
        const string limpio = "PIÑA";
        Assert.Same(limpio, NormalizadorTitular.RepararMojibake(limpio));
    }

    [Fact]
    public void RepararMojibake_repara_el_tramo_y_conserva_lo_que_esta_fuera_de_cp1252()
    {
        // Por tramos: un carácter que cp1252 no produce ('Ω') ya no bloquea la reparación del
        // par vecino, que es una Ñ inequívoca. Antes se devolvía todo intacto — el mismo defecto
        // que dejaba sin reparar "PERÉZ GARCÃƒÂA" por culpa de la É sana.
        Assert.Equal("ÑΩ", NormalizadorTitular.RepararMojibake("Ã\u0091Ω"));
    }

    [Fact]
    public void RepararMojibake_no_convierte_una_Ñ_correcta_en_otro_caracter()
    {
        // 0xD1 (Ñ) también es primer byte UTF-8 válido; si el reparador lo tratara como tal,
        // "PEÑA" con un carácter de continuación al lado se volvería cirílico.
        Assert.Equal("PEÑA", NormalizadorTitular.RepararMojibake("PEÑA"));
        Assert.Equal("PEÑ\u0091A", NormalizadorTitular.RepararMojibake("PEÑ\u0091A"));
    }

    [Theory]
    [InlineData("PIÃ‘A", true)]
    [InlineData("PI?A", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("PIÑA", false)]
    [InlineData("ANA", false)]
    public void EstaRoto_casos(string? nombreEnBD, bool esperado)
    {
        Assert.Equal(esperado, NormalizadorTitular.EstaRoto(nombreEnBD));
    }
}
