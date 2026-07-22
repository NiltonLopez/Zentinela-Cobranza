namespace Zentinela.Application.DTOs.CreadorZentinela;

/// <summary>
/// La selección para exportar a Excel supera el límite de filas que se pueden
/// materializar en memoria de forma segura (ClosedXML mantiene cada celda como objeto).
/// El controller la traduce a 400 sugiriendo usar la Descarga CSV, que transmite en
/// streaming sin límite de memoria.
/// </summary>
public sealed class ExportacionDemasiadoGrandeException(int limite)
    : Exception($"La selección supera {limite:N0} registros; el Excel no soporta ese volumen de forma segura. " +
                "Usa la Descarga CSV, que transmite sin límite de memoria.")
{
    public int Limite { get; } = limite;
}
