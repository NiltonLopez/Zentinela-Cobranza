using System.Globalization;
using System.Text;
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

    public CarguePagosService(CallCenterDbContext context)
    {
        _context = context;
    }

    public async Task<ValidarPagosResponse> ValidarCsvAsync(Stream csv, int carteraID)
    {
        using var reader = new StreamReader(csv);

        // ── Leer encabezado y resolver índices por nombre ─────────────────────
        var lineaEncabezado = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(lineaEncabezado))
            return new ValidarPagosResponse();

        var separador = lineaEncabezado.Contains(';') ? ';' : ',';
        var headers   = lineaEncabezado
            .Split(separador)
            .Select((h, i) => (Clave: Normalizar(h), Indice: i))
            .ToDictionary(x => x.Clave, x => x.Indice);

        var (idx, errorColumna) = ResolverIndices(headers, lineaEncabezado, separador);
        if (idx is null)
        {
            return new ValidarPagosResponse
            {
                TotalFilas      = 0,
                CantidadErrores = 1,
                Preview = [new PagoFilaDto { EsValida = false, ErrorMensaje = errorColumna }],
            };
        }

        // ── Parsear filas ─────────────────────────────────────────────────────
        var filas = new List<PagoFilaDto>();
        int numeroLinea = 1;

        while (!reader.EndOfStream)
        {
            var linea = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(linea)) continue;

            numeroLinea++;
            filas.Add(ParsearLinea(linea, numeroLinea, separador, idx));
        }

        var filasValidas = filas.Where(f => f.EsValida).ToList();

        // ── Lookup 1: LiderID desde TitularCartera ────────────────────────────
        var cedulas = filasValidas.Select(f => f.CedulaTitular).Distinct().ToList();

        var asignaciones = await _context.TitularCarteras
            .Where(tc => cedulas.Contains(tc.TitularID) && tc.CarteraID == carteraID)
            .Select(tc => new { tc.TitularID, tc.LiderID })
            .AsNoTracking()
            .ToListAsync();

        var liderPorCedula = asignaciones.ToDictionary(a => a.TitularID, a => a.LiderID);

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

        var agentePorUsuario = agentes.ToDictionary(
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

        return new ValidarPagosResponse
        {
            TotalFilas      = filas.Count,
            CantidadValidas = validas.Count,
            CantidadErrores = filas.Count(f => !f.EsValida),
            Preview         = filas.Take(10).ToList(),
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

        return pagos.Count;
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

    /// <summary>
    /// Parsea una línea usando los índices resueltos desde el encabezado.
    /// Columnas extra en el archivo se ignoran.
    /// </summary>
    private static PagoFilaDto ParsearLinea(string linea, int numeroLinea, char separador, IndicesColumnas idx)
    {
        var partes = linea.Split(separador);

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
