namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Forma canónica de una placa de vehículo: sin espacios y en mayúsculas ("abc 123" → "ABC123").
/// La comparten la validación genérica del cargue (columnas tipo Placa) y la regla de SIMIT, que
/// arma las placas desde Placa1..15: si cada una normalizara a su manera, la misma placa
/// quedaría como dos filas distintas en el destino.
/// </summary>
internal static class PlacaVehiculo
{
    /// <summary>La placa normalizada, o null si no queda nada.</summary>
    public static string? Normalizar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;
        var placa = string.Concat(bruto.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return placa.Length == 0 ? null : placa;
    }
}
