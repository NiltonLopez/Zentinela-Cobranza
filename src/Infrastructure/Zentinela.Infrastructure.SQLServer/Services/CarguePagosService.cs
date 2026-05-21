using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Pagos;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Cargue de pagos confirmados desde CSV por cartera.
/// Flujo: ValidarCsvAsync (parse + validación sin persistir) → ConfirmarCargueAsync (bulk insert).
///
/// El CSV puede tener las columnas en cualquier orden y puede incluir columnas extra.
/// El servicio localiza cada columna por nombre (case-insensitive, sin tildes ni espacios).
/// Columnas requeridas y alias aceptados:
///   Cédula      → cedula, cedula, numerodocumento, documento
///   Nombre      → nombre, nombrecliente, cliente
///   Valor       → valor, valorpagado, valorpago, montopagado
///   Fecha       → fecha, fechapago, fechadepago
///   Honorarios  → honorarios, honorario
///   UsuarioCtrl → usuariocontrol, usuario, usuariocontrolnext, control
///
/// LiderID se resuelve desde TitularCartera (Cedula + CarteraID).
/// AgenteControlID se resuelve por lookup Agente.UsuarioControlNext = UsuarioControl.
/// </summary>
public class CarguePagosService : ICarguePagosService
{
    private readonly CallCenterDbContext _context;
    private readonly ICalificadorCarteraCache _calificadorCache;

    public CarguePagosService(CallCenterDbContext context, ICalificadorCarteraCache calificadorCache)
    {
        _context          = context;
        _calificadorCache = calificadorCache;
    }

    public async Task<ValidarPagosResponse> ValidarArchivoAsync(Stream archivo, int carteraID, string extension)
    {
        // ── Extraer todas las filas como string[][] ───────────────────────────
        List<string[]> todasLasFilas;
        char separador = ',';

        if (extension == ".xlsx")
        {
            todasLasFilas = ExtraerFilasXlsx(archivo);
        }
        else
        {
            (todasLasFilas, separador) = await ExtraerFilasCsvAsync(archivo);
        }

        if (todasLasFilas.Count == 0)
            return new ValidarPagosResponse();

        // ── Leer encabezado y resolver índices por nombre ─────────────────────
        var headerParts = todasLasFilas[0];
        var headers = new Dictionary<string, int>();
        for (int i = 0; i < headerParts.Length; i++)
        {
            var clave = Normalizar(headerParts[i]);
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = i;
        }

        var lineaEncabezado = string.Join(separador, headerParts);
        var (idx, errorColumna) = ResolverIndices(headers, lineaEncabezado, separador);
        if (idx is null)
        {
            var errorEncabezado = new PagoFilaDto { EsValida = false, ErrorMensaje = errorColumna };
            return new ValidarPagosResponse
            {
                TotalFilas      = 0,
                CantidadErrores = 1,
                Errores         = [errorEncabezado],
            };
        }

        // ── Parsear filas de datos ────────────────────────────────────────────
        var filas = new List<PagoFilaDto>();

        for (int i = 1; i < todasLasFilas.Count; i++)
        {
            var partes = todasLasFilas[i];
            if (partes.All(string.IsNullOrWhiteSpace)) continue;
            filas.Add(ParsearPartes(partes, i + 1, idx));
        }

        var filasValidas = filas.Where(f => f.EsValida).ToList();

        // ── Lookup 1: LiderID desde TitularCartera ────────────────────────────
        var cedulas = filasValidas.Select(f => f.CedulaTitular).Distinct().ToList();

        var asignaciones = await _context.TitularCarteras
            .Where(tc => cedulas.Contains(tc.TitularID) && tc.CarteraID == carteraID)
            .Select(tc => new { tc.TitularID, tc.LiderID })
            .AsNoTracking()
            .ToListAsync();

        // DistinctBy: un titular puede aparecer varias veces en TitularCartera
        // (asignado a más de un líder). Tomamos la primera asignación encontrada.
        var liderPorCedula = asignaciones
            .DistinctBy(a => a.TitularID)
            .ToDictionary(a => a.TitularID, a => a.LiderID);

        foreach (var fila in filasValidas)
        {
            if (liderPorCedula.TryGetValue(fila.CedulaTitular, out var liderID))
                fila.LiderID = liderID;
            else
            {
                fila.EsValida     = false;
                fila.ErrorMensaje = $"Cédula {fila.CedulaTitular} no está asignada a esta cartera.";
            }
        }

        // ── Lookup 2: AgenteControlID desde Agente.UsuarioControlNext ─────────
        var usuariosControl = filasValidas
            .Where(f => f.EsValida && !string.IsNullOrWhiteSpace(f.UsuarioControl))
            .Select(f => f.UsuarioControl.Trim())
            .Distinct()
            .ToList();

        var agentes = await _context.Agentes
            .Where(a => a.UsuarioControlNext != null && usuariosControl.Contains(a.UsuarioControlNext))
            .Select(a => new { a.UsuarioControlNext, a.AgenteID })
            .AsNoTracking()
            .ToListAsync();

        var agentePorUsuario = agentes
            .DistinctBy(a => a.UsuarioControlNext!.ToLowerInvariant())
            .ToDictionary(
                a => a.UsuarioControlNext!,
                a => a.AgenteID,
                StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filasValidas.Where(f => f.EsValida))
        {
            if (agentePorUsuario.TryGetValue(fila.UsuarioControl.Trim(), out var agenteID))
                fila.AgenteControlID = agenteID;
            else
            {
                fila.EsValida     = false;
                fila.ErrorMensaje = $"Usuario Control '{fila.UsuarioControl}' no encontrado en el sistema.";
            }
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        var errores = filas.Where(f => !f.EsValida).ToList();

        return new ValidarPagosResponse
        {
            TotalFilas      = filas.Count,
            CantidadValidas = validas.Count,
            CantidadErrores = errores.Count,
            Preview         = validas.Take(10).ToList(),
            Errores         = errores,
            TodasLasValidas = validas,
        };
    }

    public async Task<int> ConfirmarCargueAsync(ConfirmarPagosRequest request, int usuarioControlID)
    {
        var ahora = DateTime.UtcNow;

        var pagos = request.FilasValidas.Select(f => new Pago
        {
            CedulaTitular    = f.CedulaTitular,
            NombreCliente    = f.NombreCliente.Trim(),
            ValorPagado      = f.ValorPagado,
            FechaPago        = f.FechaPago,
            Honorarios       = f.Honorarios,
            LiderID          = f.LiderID,
            AgenteControlID  = f.AgenteControlID,
            CarteraID        = request.CarteraID,
            UsuarioControlID = usuarioControlID,
            FechaRegistro    = ahora,
        }).ToList();

        _context.Pagos.AddRange(pagos);
        await _context.SaveChangesAsync();

        // Persistencia exitosa — los pagos no alteran las agregaciones actuales del
        // Calificador, pero sí podrían influir en versiones futuras del DTO (recaudo
        // por líder, etc.). Invalidar es barato y mantiene coherencia.
        _calificadorCache.InvalidarTodo();

        return pagos.Count;
    }

    // ── Última actualización ──────────────────────────────────────────────────

    public async Task<DateTime?> ObtenerUltimaActualizacionAsync(int carteraID) =>
        await _context.Pagos
            .Where(p => p.CarteraID == carteraID)
            .MaxAsync(p => (DateTime?)p.FechaRegistro);

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Pagos");

        var columnas = new[]
        {
            ("Cedula",         "Cedula"),
            ("Nombre",         "Nombre"),
            ("Valor",          "Valor"),
            ("Honorarios",     "Honorarios"),
            ("Fecha",          "Fecha"),
            ("UsuarioControl", "UsuarioControl"),
        };

        for (int i = 0; i < columnas.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = columnas[i].Item2;
            celda.Style.Font.Bold            = true;
            celda.Style.Font.FontColor       = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a56db");
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Fila de ejemplo en gris
        var ejemplo = new object[] { 1020304050L, "PEREZ JUAN", 1500000, 225000, "2026-04-21", "jperez" };
        for (int i = 0; i < ejemplo.Length; i++)
        {
            var celda = ws.Cell(2, i + 1);
            celda.Value = ejemplo[i].ToString();
            celda.Style.Font.FontColor = XLColor.Gray;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normaliza un nombre de columna para comparación:
    /// quita espacios, convierte a minúsculas y elimina tildes.
    /// Ej: "Número Documento" → "numerodocumento"
    /// </summary>
    private static string Normalizar(string s)
    {
        var sinTildes = s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .Trim();
    }

    private record IndicesColumnas(int Cedula, int Nombre, int Valor, int Fecha, int Honorarios, int UsuarioControl);

    private static readonly Dictionary<string, string> AliasColumnas = new(StringComparer.Ordinal)
    {
        // Cédula
        ["cedula"]           = "cedula",
        ["numerodocumento"]  = "cedula",
        ["documento"]        = "cedula",
        ["identificacion"]   = "cedula",
        // Nombre
        ["nombre"]           = "nombre",
        ["nombrecliente"]    = "nombre",
        ["nombrecompleto"]   = "nombre",
        ["cliente"]          = "nombre",
        // Valor
        ["valor"]            = "valor",
        ["valorpagado"]      = "valor",
        ["valorpago"]        = "valor",
        ["montopagado"]      = "valor",
        ["monto"]            = "valor",
        // Fecha
        ["fecha"]            = "fecha",
        ["fechapago"]        = "fecha",
        ["fechadepago"]      = "fecha",
        // Honorarios
        ["honorarios"]       = "honorarios",
        ["honorario"]        = "honorarios",
        // UsuarioControl
        ["usuariocontrol"]   = "usuariocontrol",
        ["usuario"]          = "usuariocontrol",
        ["usuariocontrolnext"] = "usuariocontrol",
        ["control"]          = "usuariocontrol",
    };

    /// <summary>
    /// Recorre los encabezados del archivo y resuelve el índice de cada columna requerida.
    /// Retorna null (con mensaje de error) si alguna columna obligatoria no se encuentra.
    /// </summary>
    private static (IndicesColumnas? idx, string error) ResolverIndices(
        Dictionary<string, int> headers, string lineaOriginal, char separador)
    {
        // Mapa campo→índice resuelto
        var resuelto = new Dictionary<string, int>();

        foreach (var (clave, indice) in headers)
        {
            if (AliasColumnas.TryGetValue(clave, out var campo) && !resuelto.ContainsKey(campo))
                resuelto[campo] = indice;
        }

        var requeridos = new[] { "cedula", "nombre", "valor", "fecha", "honorarios", "usuariocontrol" };
        var faltantes  = requeridos.Where(r => !resuelto.ContainsKey(r)).ToList();

        if (faltantes.Count > 0)
        {
            var encontradas = lineaOriginal.Split(separador).Select(h => h.Trim());
            var error = $"Columna(s) no encontrada(s): {string.Join(", ", faltantes)}. " +
                        $"Columnas en el archivo: {string.Join(", ", encontradas)}.";
            return (null, error);
        }

        return (new IndicesColumnas(
            Cedula:         resuelto["cedula"],
            Nombre:         resuelto["nombre"],
            Valor:          resuelto["valor"],
            Fecha:          resuelto["fecha"],
            Honorarios:     resuelto["honorarios"],
            UsuarioControl: resuelto["usuariocontrol"]
        ), string.Empty);
    }

    // ── Extractores de filas ─────────────────────────────────────────────────

    private static async Task<(List<string[]> filas, char sep)> ExtraerFilasCsvAsync(Stream csv)
    {
        var filas  = new List<string[]>();
        using var reader = new StreamReader(csv);

        var primera = await reader.ReadLineAsync();
        if (primera is null) return (filas, ',');

        var sep = primera.Contains(';') ? ';' : ',';
        filas.Add(primera.Split(sep));

        while (!reader.EndOfStream)
        {
            var linea = await reader.ReadLineAsync();
            if (linea is null) continue;
            filas.Add(linea.Split(sep));
        }
        return (filas, sep);
    }

    private static List<string[]> ExtraerFilasXlsx(Stream stream)
    {
        var filas = new List<string[]>();
        using var workbook = new XLWorkbook(stream);
        var rango = workbook.Worksheets.First().RangeUsed();
        if (rango is null) return filas;

        foreach (var fila in rango.Rows())
            filas.Add(fila.Cells().Select(CeldaATexto).ToArray());

        return filas;
    }

    /// <summary>
    /// Convierte el valor de una celda a string.
    /// Las celdas con tipo DateTime se formatean como yyyy-MM-dd para que
    /// el parser de fechas las resuelva correctamente sin depender del formato regional de Excel.
    /// </summary>
    private static string CeldaATexto(ClosedXML.Excel.IXLCell c)
    {
        if (c.DataType == XLDataType.DateTime)
            return c.GetDateTime().ToString("yyyy-MM-dd");

        // Fallback: algunos archivos guardan fechas como número OADate
        // si la celda tiene formato de fecha pero el tipo es Number.
        if (c.DataType == XLDataType.Number && c.Style.NumberFormat.Format.Contains("d"))
        {
            try { return DateTime.FromOADate(c.GetDouble()).ToString("yyyy-MM-dd"); }
            catch { /* no era una fecha OADate válida, continúa con GetString */ }
        }

        return c.GetString();
    }

    /// <summary>
    /// Parsea una fila (ya spliteada) usando los índices resueltos desde el encabezado.
    /// Columnas extra en el archivo se ignoran.
    /// </summary>
    private static PagoFilaDto ParsearPartes(string[] partes, int numeroLinea, IndicesColumnas idx)
    {
        var maxIdx = new[] { idx.Cedula, idx.Nombre, idx.Valor, idx.Fecha, idx.Honorarios, idx.UsuarioControl }.Max();
        if (partes.Length <= maxIdx)
            return new PagoFilaDto
            {
                EsValida     = false,
                ErrorMensaje = $"Línea {numeroLinea}: solo {partes.Length} columnas, se esperan al menos {maxIdx + 1}.",
            };

        if (!long.TryParse(partes[idx.Cedula].Trim(), out var cedula))
            return new PagoFilaDto
            {
                EsValida     = false,
                ErrorMensaje = $"Línea {numeroLinea}: cédula inválida '{partes[idx.Cedula].Trim()}'.",
            };

        if (!decimal.TryParse(partes[idx.Valor].Trim().Replace(".", "").Replace(",", "."),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var valor))
            return new PagoFilaDto
            {
                CedulaTitular = cedula,
                EsValida      = false,
                ErrorMensaje  = $"Línea {numeroLinea}: valor pagado inválido '{partes[idx.Valor].Trim()}'.",
            };

        if (!DateOnly.TryParse(partes[idx.Fecha].Trim(), out var fecha))
            return new PagoFilaDto
            {
                CedulaTitular = cedula,
                EsValida      = false,
                ErrorMensaje  = $"Línea {numeroLinea}: fecha inválida '{partes[idx.Fecha].Trim()}'. Use YYYY-MM-DD.",
            };

        if (!decimal.TryParse(partes[idx.Honorarios].Trim().Replace(".", "").Replace(",", "."),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var honorarios))
            return new PagoFilaDto
            {
                CedulaTitular = cedula,
                EsValida      = false,
                ErrorMensaje  = $"Línea {numeroLinea}: honorarios inválidos '{partes[idx.Honorarios].Trim()}'.",
            };

        var usuarioControl = partes[idx.UsuarioControl].Trim();
        if (string.IsNullOrWhiteSpace(usuarioControl))
            return new PagoFilaDto
            {
                CedulaTitular = cedula,
                EsValida      = false,
                ErrorMensaje  = $"Línea {numeroLinea}: Usuario Control no puede estar vacío.",
            };

        return new PagoFilaDto
        {
            CedulaTitular  = cedula,
            NombreCliente  = partes[idx.Nombre].Trim(),
            ValorPagado    = valor,
            FechaPago      = fecha,
            Honorarios     = honorarios,
            UsuarioControl = usuarioControl,
            EsValida       = true,
        };
    }
}
