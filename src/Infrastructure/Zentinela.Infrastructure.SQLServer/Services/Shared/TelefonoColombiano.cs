namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Normalización de números telefónicos colombianos a los 10 dígitos con los que se
/// almacenan en las tablas de fuentes externas de DBCallCenter.
///
/// A diferencia de <c>CargueReporteService.ValidarNumeroMarcado</c> (que exige empezar
/// en 3 y por tanto descarta los fijos), aquí se aceptan también los fijos: las columnas
/// de fijo de las fuentes tienen volumen real (Positiva ~331k, Cajacopi ~20k, MutualSer ~6,7k),
/// y SOS.Telefono guarda mayoritariamente celulares pese al nombre. Filtrar por "empieza
/// en 3" descartaría cientos de miles de registros legítimos.
/// </summary>
internal static class TelefonoColombiano
{
    /// <summary>
    /// Limpia y valida un número. Devuelve los 10 dígitos y si es celular, o
    /// <c>(null, false)</c> si no es un teléfono válido.
    ///
    /// Tolera: separadores y espacios, sufijo ".0" de Excel, e indicativo de país
    /// (57 / +57 / 0057) delante de un celular.
    /// </summary>
    public static (string? Numero, bool EsCelular) Normalizar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return (null, false);

        var digitos = new string(bruto.Where(char.IsDigit).ToArray());
        if (digitos.Length == 0) return (null, false);

        // Indicativo de país delante de un celular: 57 + 3XXXXXXXXX (12 dígitos),
        // o 0057 + 3XXXXXXXXX (14). Se conserva solo el número nacional.
        if (digitos.Length is 12 or 14)
        {
            var sinIndicativo = digitos[^10..];
            var prefijo       = digitos[..^10];
            if (prefijo is "57" or "0057" && sinIndicativo[0] == '3')
                digitos = sinIndicativo;
        }

        if (digitos.Length != 10) return (null, false);

        return (digitos, digitos[0] == '3');
    }
}
