using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

/// <summary>
/// Forma canónica y regla de formato de placas. Los casos inválidos son marcas reales medidas
/// en dbo.SIMIT el 2026-09-24 (el SP legacy las guardaba como placa con EstadoEnSIMIT = 1).
/// </summary>
public class PlacaVehiculoTests
{
    [Theory]
    [InlineData("abc 123", "ABC123")]
    [InlineData(" QUR 25 ", "QUR25")]
    [InlineData("CKH-13", "CKH13")]            // guion = separador de digitación
    [InlineData("smg\t08b", "SMG08B")]
    [InlineData("MCC 18", "MCC18")]        // espacio de no separación (CHAR(160))
    [InlineData("PAZ Y SALVO", "PAZYSALVO")]    // normaliza, pero NO es válida (ver EsValida)
    [InlineData("   ", null)]
    [InlineData("-", null)]
    [InlineData(null, null)]
    public void Normalizar_casos(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, PlacaVehiculo.Normalizar(entrada));
    }

    [Theory]
    [InlineData("ABC123")]    // carro AAA999
    [InlineData("ABC12D")]    // moto AAA99A
    [InlineData("ABC12")]     // moto vieja AAA99 (5 caracteres: el mínimo)
    [InlineData("123ABC")]    // motocarro 999AAA
    [InlineData("R12345")]    // remolque A99999
    [InlineData("AB1234")]    // AA9999
    [InlineData("IGBZ78D")]   // typo de 7 con letra y dígito: pasa (no se distingue de un formato raro)
    [InlineData("AB3X02")]
    public void EsValida_formatos_reales(string placa)
    {
        Assert.True(PlacaVehiculo.EsValida(placa));
    }

    [Theory]
    [InlineData("PAZ Y SALVO")]
    [InlineData("NO CARGO")]
    [InlineData("SIN")]
    [InlineData("N/A")]
    [InlineData("XXXXXX")]    // sin dígito
    [InlineData("AAAAAA")]
    [InlineData("99999")]     // sin letra
    [InlineData("0")]
    [InlineData("SIM")]
    [InlineData("TL")]
    [InlineData("NO REP")]
    [InlineData("SIN PLA")]
    [InlineData("YWB26¿")]    // basura de encoding
    [InlineData("T.L")]
    [InlineData("AB12")]      // 4: corta
    [InlineData("AJG1H18A")]  // 8: larga
    [InlineData("ABCDEFGHIJK")]
    public void EsValida_marcas_de_texto_no_son_placa(string bruto)
    {
        Assert.False(PlacaVehiculo.EsValida(PlacaVehiculo.Normalizar(bruto)));
    }

    [Theory]
    [InlineData("abc123")]    // exige la forma YA normalizada
    [InlineData("ABC 123")]
    [InlineData("ÑAB123")]
    [InlineData(null)]
    public void EsValida_exige_forma_canonica(string? placa)
    {
        Assert.False(PlacaVehiculo.EsValida(placa));
    }
}
