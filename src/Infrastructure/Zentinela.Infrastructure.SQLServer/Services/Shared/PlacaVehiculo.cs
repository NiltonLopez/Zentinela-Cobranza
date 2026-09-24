namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Forma canónica de una placa de vehículo: sin espacios ni guiones y en mayúsculas
/// ("abc 123" / "ABC-123" → "ABC123"), y la regla que decide si eso ES una placa.
/// La comparten la validación genérica del cargue (columnas tipo Placa) y la regla de SIMIT, que
/// arma las placas desde Placa1..15: si cada una normalizara o validara a su manera, la misma
/// placa quedaría como dos filas distintas en el destino.
///
/// La forma canónica debe coincidir EXACTAMENTE con la que usa el script
/// docs/sql/migrations/2026-09-24_simit_placas_legacy.sql para corregir dbo.SIMIT: si divergen,
/// la próxima carga de una placa legacy no la reconoce y la inserta como fila nueva.
/// </summary>
internal static class PlacaVehiculo
{
    public const int LargoMinimo = 5;
    public const int LargoMaximo = 7;

    /// <summary>
    /// La placa normalizada (sin espacios ni guiones, mayúsculas), o null si no queda nada.
    /// NO valida el formato: para eso <see cref="EsValida"/>.
    /// </summary>
    /// <remarks>
    /// El guion se quita porque es separador de digitación, no parte de la placa: en dbo.SIMIT
    /// (medido el 2026-09-24) aparecen 'CKH-13', 'UTP-35', 'TTT-06D', que son placas reales.
    /// </remarks>
    public static string? Normalizar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return null;
        var placa = string.Concat(bruto.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
        return placa.Length == 0 ? null : placa;
    }

    /// <summary>
    /// ¿La placa YA normalizada tiene forma de placa? 5 a 7 caracteres, solo A-Z y 0-9 (ASCII),
    /// con al menos una letra y al menos un dígito.
    /// </summary>
    /// <remarks>
    /// Por qué esta regla (dbo.SIMIT, 47.792 placas no nulas medidas el 2026-09-24): los formatos
    /// reales son AAA999 (carro, 8.792), AAA99A (moto, 31.386), AAA99 (moto vieja, 1.409), 999AAA
    /// (motocarro, 174), A99999 (remolque) y AA9999; todos caben en 5..7 con letra y dígito.
    /// Lo que la regla deja fuera son marcas de texto que el archivo traía en la columna de placa y
    /// que el SP legacy guardaba como placa con EstadoEnSIMIT = 1 (el titular contaba como
    /// "encontrado en SIMIT"): 'PAZ Y SALVO' (5.718 filas, truncado a 'PAZ Y SALV'), 'SIN', 'SIM',
    /// 'NO CARGO', 'NO REP', 'XXXXXX', 'AAAAAA', '99999', 'TL'... Sin letra o sin dígito no hay
    /// placa colombiana posible; con menos de 5 o más de 7 caracteres, tampoco.
    /// Los errores de digitación con letra y dígito (p. ej. 'IGBZ78D', 'AB3X02', ~160 filas) SÍ
    /// pasan: no se puede distinguir un typo de un formato raro, y descartarlos perdería datos.
    /// </remarks>
    public static bool EsValida(string? placa)
    {
        if (placa is null || placa.Length is < LargoMinimo or > LargoMaximo) return false;
        bool letra = false, digito = false;
        foreach (var c in placa)
        {
            if (c is >= 'A' and <= 'Z') letra = true;
            else if (c is >= '0' and <= '9') digito = true;
            else return false;   // minúscula, tilde, 'Ñ', '/', '.', '¿'...: no es la forma canónica
        }
        return letra && digito;
    }
}
