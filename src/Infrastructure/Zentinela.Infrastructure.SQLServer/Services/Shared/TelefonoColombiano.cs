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
    /// Tolera: separadores y espacios, UNA parte decimal nula (".0", ".00", ",0", ",00") de un CSV
    /// exportado por Excel o pandas (ver <see cref="NumeroEnTexto"/>), e indicativo de país
    /// (57 / +57 / 0057) delante de un celular. Un decimal real ('300123456.7') no es un teléfono:
    /// contar solo sus dígitos lo convertiría en otro número.
    /// </summary>
    public static (string? Numero, bool EsCelular) Normalizar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return (null, false);

        // Antes de contar dígitos: con el ".0" contado, '3001234567.0' tendría 11 y se anularía,
        // y '300123456.0' (9 dígitos) pasaría por un celular de 10.
        var texto = NumeroEnTexto.QuitarDecimalNulo(bruto);
        if (NumeroEnTexto.TieneDecimales(texto)) return (null, false);

        var digitos = new string(texto.Where(char.IsDigit).ToArray());
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

    /// <summary>
    /// Para columnas de celular: el número solo si es celular (empieza en 3); si no, <c>null</c>.
    /// Un fijo que llegue en la columna de celular se descarta aquí en lugar de guardarse
    /// donde el marcador lo trataría como móvil (SMS, WhatsApp).
    /// </summary>
    public static string? NormalizarCelular(string? bruto)
    {
        var (numero, esCelular) = Normalizar(bruto);
        return esCelular ? numero : null;
    }

    /// <summary>
    /// Para columnas de fijo: el número solo si NO es celular y empieza en 6 (fijos
    /// nacionales con indicativo 601–608 desde la migración de 2021 a 10 dígitos).
    /// Un celular en la columna de fijo devuelve <c>null</c>: el llamador decide si lo
    /// reubica en la de celular, pero este helper no lo mezcla.
    /// </summary>
    public static string? NormalizarFijo(string? bruto)
    {
        var (numero, esCelular) = Normalizar(bruto);
        return numero is not null && !esCelular && numero[0] == '6' ? numero : null;
    }
}

/// <summary>
/// Encabezados aceptados para una columna de teléfono, ya normalizados por
/// <see cref="ArchivoTabular.Normalizar"/>. Compartida por Uso de Base de Datos y por el
/// Creador de Virtualidad (lista negra por archivo). Se resuelve por nombre, no por
/// posición (ADR-004): si ninguno matchea, el archivo se rechaza indicando los
/// encabezados que sí traía — deliberadamente NO se adivina la columna.
/// </summary>
internal static class AliasColumnaTelefono
{
    public static readonly HashSet<string> Todos = new(StringComparer.Ordinal)
    {
        "numerotelefono", "numerodetelefono", "telefono", "numero", "numerolinea", "linea",
        "celular", "numerocelular", "numerodecelular", "movil", "numeromovil",
        "numerotelefonocelular", "telefonocelular", "numeromarcado", "marcado",
        "msisdn", "destino", "numerodestino", "telefonodestino", "numerocontacto", "contacto",
    };
}
