using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestión de Histórico de Virtualidad (IVR / SMS / Correo).
/// Carga masiva con mapeo Cartera+Líder por NOMBRE en el archivo, y actualización
/// posterior del resultado (Estado) por PK. Espejo del patrón de VirtualidadIVRService.
/// </summary>
public class HistoricoVirtualidadService : IHistoricoVirtualidadService
{
    private readonly CallCenterDbContext _db;

    public HistoricoVirtualidadService(CallCenterDbContext db) => _db = db;

    // ── Catálogo por canal ──────────────────────────────────────────────────

    /// <summary>Estados válidos por canal (lista blanca). Ajustable con el negocio.</summary>
    public IReadOnlyDictionary<string, string[]> EstadosPorCanal { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["IVR"]    = ["Contestada", "No contestada", "Buzón", "Número errado"],
            ["SMS"]    = ["Entregado", "No entregado"],
            ["Correo"] = ["Entregado", "Rebotado", "Abierto"],
        };

    // Umbral de alerta por saturación de línea (>N envíos al mismo destino).
    // 0 = sin alerta (p. ej. Correo no aplica saturación de línea telefónica).
    private static readonly Dictionary<string, int> UmbralAlerta = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IVR"] = 5, ["SMS"] = 4, ["Correo"] = 0,
    };

    private static bool EsCanalValido(string canal) =>
        canal is "IVR" or "SMS" or "Correo";

    private static bool EsCorreo(string canal) =>
        string.Equals(canal, "Correo", StringComparison.OrdinalIgnoreCase);

    // ── Plantilla Solicitud ─────────────────────────────────────────────────

    public byte[] GenerarPlantilla(string canal)
    {
        var etiquetaDestino = EsCorreo(canal) ? "Email" : "NumeroTelefono";
        var columnas = new[] { "NumeroDocumento", etiquetaDestino, "Cartera", "Lider", "FechaEnvio" };
        var ejemplo = EsCorreo(canal)
            ? new object[] { 1020304050L, "titular@correo.com", "Cartera Ejemplo", "Carlos A Torres", "2026-04-24" }
            : new object[] { 1020304050L, "3001234567", "Cartera Ejemplo", "Carlos A Torres", "2026-04-24" };

        return ConstruirXlsx($"Solicitudes {canal}", columnas, ejemplo);
    }

    // ── Validación Solicitud ────────────────────────────────────────────────

    public async Task<ValidarSolicitudResponse> ValidarAsync(Stream excelStream, string canal)
    {
        if (!EsCanalValido(canal))
            return ErrorResponse($"Canal '{canal}' no soportado.");

        var filas = ParsearSolicitudes(excelStream, canal);
        if (filas.Count == 0)
            return new ValidarSolicitudResponse();

        // Precarga de diccionarios de resolución por nombre (una consulta por catálogo).
        var carteras = await _db.Carteras
            .Where(c => c.Activa)
            .Select(c => new { c.ID, c.NombreCartera })
            .AsNoTracking().ToListAsync();
        var carteraPorNombre = ConstruirIndicePorNombre(carteras.Select(c => (c.ID, c.NombreCartera)));

        var lideres = await _db.Lideres
            .Select(l => new { l.ID, l.Nombre })
            .AsNoTracking().ToListAsync();
        var liderPorNombre = ConstruirIndicePorNombre(lideres.Select(l => (l.ID, l.Nombre)));

        var pares = await _db.LiderCarteras
            .Where(lc => lc.Activo)
            .Select(lc => new { lc.LiderID, lc.CarteraID })
            .AsNoTracking().ToListAsync();
        var paresActivos = pares.Select(p => (p.LiderID, p.CarteraID)).ToHashSet();

        // Resolución cartera/líder por nombre + validación del par.
        foreach (var f in filas.Where(f => f.EsValida))
        {
            if (!carteraPorNombre.TryGetValue(Normalizar(f.NombreCartera), out var cartera))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"Cartera '{f.NombreCartera}' no encontrada (o inactiva).";
                continue;
            }
            f.CarteraID     = cartera.ID;
            f.NombreCartera = cartera.Nombre;

            if (!liderPorNombre.TryGetValue(Normalizar(f.NombreLider), out var lider))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"Líder '{f.NombreLider}' no encontrado.";
                continue;
            }
            f.LiderID     = lider.ID;
            f.NombreLider = lider.Nombre;

            if (!paresActivos.Contains((f.LiderID, f.CarteraID)))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"'{f.NombreLider}' no está asignado a la cartera '{f.NombreCartera}'.";
            }
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        var alertas = await CalcularAlertasAsync(validas, canal);

        return new ValidarSolicitudResponse
        {
            TotalFilas   = filas.Count,
            TotalValidas = validas.Count,
            TotalErrores = filas.Count - validas.Count,
            Preview      = filas.Take(10).ToList(),
            FilasValidas = validas,
            Alertas      = alertas,
        };
    }

    private async Task<List<AlertaSolicitudDto>> CalcularAlertasAsync(List<FilaSolicitudDto> validas, string canal)
    {
        var umbral = UmbralAlerta.GetValueOrDefault(canal, 0);
        if (umbral <= 0 || validas.Count == 0) return [];

        var destinos   = validas.Select(f => f.Destino).Distinct().ToList();
        var carteraIDs = validas.Select(f => f.CarteraID).Distinct().ToList();

        // Histórico de envíos por destino para este canal y las carteras involucradas.
        var historico = await _db.HistoricoVirtualidad
            .Where(s => s.Canal == canal && carteraIDs.Contains(s.CarteraID) && destinos.Contains(s.Destino))
            .GroupBy(s => s.Destino)
            .Select(g => new { Destino = g.Key, Total = g.Count() })
            .ToListAsync();

        return validas
            .GroupBy(f => new { f.Destino, f.NombreCartera })
            .Select(g => new AlertaSolicitudDto
            {
                Destino       = g.Key.Destino,
                NombreCartera = g.Key.NombreCartera,
                TotalEnvios   = g.Count() +
                    (historico.FirstOrDefault(h => h.Destino == g.Key.Destino)?.Total ?? 0),
            })
            .Where(a => a.TotalEnvios > umbral)
            .OrderByDescending(a => a.TotalEnvios)
            .ToList();
    }

    // ── Confirmación Solicitud ──────────────────────────────────────────────

    public async Task<int> ConfirmarSolicitudAsync(List<FilaSolicitudDto> filas, string canal)
    {
        if (!EsCanalValido(canal))
            throw new ArgumentException($"Canal '{canal}' no soportado.");

        var entidades = filas.Select(f => new HistoricoVirtualidad
        {
            Canal           = canal,
            NumeroDocumento = f.NumeroDocumento,
            Destino         = f.Destino,
            CarteraID       = f.CarteraID,
            LiderID         = f.LiderID,
            FechaEnvio      = f.FechaEnvio,
            Estado          = null, // sin resultado hasta la fase Resultado
        }).ToList();

        _db.HistoricoVirtualidad.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Fase Resultado ──────────────────────────────────────────────────────

    public byte[] GenerarPlantillaResultado(string canal)
    {
        var estados = EstadosPorCanal.GetValueOrDefault(canal, []);
        var ejemploEstado = estados.Length > 0 ? estados[0] : "Estado";
        // Nota: normalmente se usa "Descargar pendientes" (trae los ID reales).
        return ConstruirXlsx($"Resultado {canal}",
            new[] { "ID", "Estado" },
            new object[] { 1L, ejemploEstado });
    }

    public async Task<byte[]> DescargarPendientesAsync(string canal, int? carteraID, DateOnly? desde, DateOnly? hasta)
    {
        var query =
            from s in _db.HistoricoVirtualidad
            where s.Canal == canal && s.Estado == null
                && (!carteraID.HasValue || s.CarteraID == carteraID.Value)
                && (!desde.HasValue || s.FechaEnvio >= desde.Value)
                && (!hasta.HasValue || s.FechaEnvio <= hasta.Value)
            join c in _db.Carteras on s.CarteraID equals c.ID
            join l in _db.Lideres  on s.LiderID   equals l.ID
            orderby s.ID
            select new
            {
                s.ID, s.NumeroDocumento, s.Destino,
                c.NombreCartera, NombreLider = l.Nombre, s.FechaEnvio,
            };

        var pendientes = await query.AsNoTracking().ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"Pendientes {canal}");
        var columnas = new[] { "ID", "NumeroDocumento", "Destino", "Cartera", "Lider", "FechaEnvio", "Estado" };
        EscribirEncabezado(ws, columnas);

        int fila = 2;
        foreach (var p in pendientes)
        {
            ws.Cell(fila, 1).Value = p.ID;
            ws.Cell(fila, 2).Value = p.NumeroDocumento;
            ws.Cell(fila, 3).Value = p.Destino;
            ws.Cell(fila, 4).Value = p.NombreCartera;
            ws.Cell(fila, 5).Value = p.NombreLider;
            ws.Cell(fila, 6).Value = p.FechaEnvio.ToString("yyyy-MM-dd");
            // Columna Estado se deja vacía para que el usuario la diligencie.
            fila++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ValidarResultadoResponse> ValidarResultadoAsync(Stream excelStream, string canal)
    {
        if (!EsCanalValido(canal))
            return new ValidarResultadoResponse
            {
                TotalErrores = 1,
                Preview = [new FilaResultadoDto { EsValida = false, ErrorMensaje = $"Canal '{canal}' no soportado." }],
            };

        var estadosValidos = EstadosPorCanal.GetValueOrDefault(canal, [])
            .ToDictionary(Normalizar, e => e); // normalizado -> valor canónico

        var filas = ParsearResultados(excelStream);
        if (filas.Count == 0)
            return new ValidarResultadoResponse();

        // Validar existencia + canal de los ID cargados.
        var ids = filas.Where(f => f.EsValida).Select(f => f.ID).Distinct().ToList();
        var existentes = await _db.HistoricoVirtualidad
            .Where(s => ids.Contains(s.ID))
            .Select(s => new { s.ID, s.Canal })
            .AsNoTracking().ToListAsync();
        var canalPorId = existentes.ToDictionary(x => x.ID, x => x.Canal);

        foreach (var f in filas.Where(f => f.EsValida))
        {
            if (!canalPorId.TryGetValue(f.ID, out var canalReal))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"ID {f.ID} no existe.";
                continue;
            }
            if (!string.Equals(canalReal, canal, StringComparison.OrdinalIgnoreCase))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"ID {f.ID} pertenece al canal '{canalReal}', no a '{canal}'.";
                continue;
            }
            if (!estadosValidos.TryGetValue(Normalizar(f.Estado), out var estadoCanonico))
            {
                f.EsValida = false;
                f.ErrorMensaje = $"Estado '{f.Estado}' no válido para {canal}. Válidos: {string.Join(", ", EstadosPorCanal[canal])}.";
                continue;
            }
            f.Estado = estadoCanonico; // normaliza al valor del catálogo
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        return new ValidarResultadoResponse
        {
            TotalFilas   = filas.Count,
            TotalValidas = validas.Count,
            TotalErrores = filas.Count - validas.Count,
            Preview      = filas.Take(10).ToList(),
            FilasValidas = validas,
        };
    }

    public async Task<int> ConfirmarResultadoAsync(List<FilaResultadoDto> filas)
    {
        var porId = filas.Where(f => f.EsValida)
            .GroupBy(f => f.ID)
            .ToDictionary(g => g.Key, g => g.Last().Estado); // último gana si se repite ID
        if (porId.Count == 0) return 0;

        var ids = porId.Keys.ToList();
        var entidades = await _db.HistoricoVirtualidad
            .Where(s => ids.Contains(s.ID))
            .ToListAsync();

        var ahora = DateTime.Now;
        foreach (var e in entidades)
        {
            e.Estado = porId[e.ID];
            e.FechaResultado = ahora;
        }

        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Parseo Excel ────────────────────────────────────────────────────────

    private List<FilaSolicitudDto> ParsearSolicitudes(Stream stream, string canal)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var resultado = new List<FilaSolicitudDto>();

        var todas = ws.RowsUsed().ToList();
        if (todas.Count < 2) return resultado;

        var headers = LeerHeaders(todas[0]);
        int Get(params string[] a) => ResolverColumna(headers, a);

        int iDoc     = Get("numerodocumento", "documento", "cedula", "identificacion", "titularid");
        int iDestino = Get("numerotelefono", "telefono", "celular", "email", "correo", "destino");
        int iCartera = Get("cartera", "nombrecartera");
        int iLider   = Get("lider", "nombrelider", "liderasignado");
        int iFecha   = Get("fechaenvio", "fecha", "fechadeenvio");

        int numeroLinea = 1;
        foreach (var fila in todas.Skip(1))
        {
            numeroLinea++;
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            // Salta filas totalmente vacías.
            if (string.IsNullOrWhiteSpace(Cel(iDoc)) && string.IsNullOrWhiteSpace(Cel(iDestino))
                && string.IsNullOrWhiteSpace(Cel(iCartera)))
                continue;

            var dto = new FilaSolicitudDto { EsValida = true };

            if (!long.TryParse(Cel(iDoc), out var doc))
                Invalidar(dto, $"Línea {numeroLinea}: NumeroDocumento inválido.");
            else dto.NumeroDocumento = doc;

            dto.Destino       = Cel(iDestino);
            dto.NombreCartera = Cel(iCartera);
            dto.NombreLider   = Cel(iLider);

            if (string.IsNullOrWhiteSpace(dto.Destino))
                Invalidar(dto, $"Línea {numeroLinea}: {(EsCorreo(canal) ? "Email" : "NumeroTelefono")} vacío.");
            else if (EsCorreo(canal) && !dto.Destino.Contains('@'))
                Invalidar(dto, $"Línea {numeroLinea}: email '{dto.Destino}' inválido.");

            if (string.IsNullOrWhiteSpace(dto.NombreCartera))
                Invalidar(dto, $"Línea {numeroLinea}: Cartera vacía.");
            if (string.IsNullOrWhiteSpace(dto.NombreLider))
                Invalidar(dto, $"Línea {numeroLinea}: Lider vacío.");

            if (!DateOnly.TryParse(Cel(iFecha), CultureInfo.InvariantCulture, out var fecha)
                && !DateOnly.TryParseExact(Cel(iFecha), "dd/MM/yyyy", null, DateTimeStyles.None, out fecha))
                Invalidar(dto, $"Línea {numeroLinea}: FechaEnvio inválida.");
            else dto.FechaEnvio = fecha;

            resultado.Add(dto);
        }

        return resultado;
    }

    private static List<FilaResultadoDto> ParsearResultados(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var resultado = new List<FilaResultadoDto>();

        var todas = ws.RowsUsed().ToList();
        if (todas.Count < 2) return resultado;

        var headers = LeerHeaders(todas[0]);
        int iId     = ResolverColumna(headers, "id", "solicitudid");
        int iEstado = ResolverColumna(headers, "estado", "resultado");

        int numeroLinea = 1;
        foreach (var fila in todas.Skip(1))
        {
            numeroLinea++;
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            var idTxt = Cel(iId);
            var estado = Cel(iEstado);
            // Salta filas sin ID ni Estado (p. ej. pendientes no diligenciados).
            if (string.IsNullOrWhiteSpace(idTxt) && string.IsNullOrWhiteSpace(estado))
                continue;

            var dto = new FilaResultadoDto { EsValida = true, Estado = estado };

            if (!long.TryParse(idTxt, out var id))
                Invalidar(dto, $"Línea {numeroLinea}: ID inválido.");
            else dto.ID = id;

            if (string.IsNullOrWhiteSpace(estado))
                Invalidar(dto, $"Línea {numeroLinea}: Estado vacío.");

            resultado.Add(dto);
        }

        return resultado;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void Invalidar(FilaSolicitudDto dto, string msg)
    { dto.EsValida = false; dto.ErrorMensaje ??= msg; }

    private static void Invalidar(FilaResultadoDto dto, string msg)
    { dto.EsValida = false; dto.ErrorMensaje ??= msg; }

    private static ValidarSolicitudResponse ErrorResponse(string mensaje) => new()
    {
        TotalErrores = 1,
        Preview = [new FilaSolicitudDto { EsValida = false, ErrorMensaje = mensaje }],
    };

    /// <summary>Índice Normalizar(nombre) -> (ID, Nombre). Ante nombres duplicados
    /// normalizados, conserva el primero (no lanza).</summary>
    private static Dictionary<string, (int ID, string Nombre)> ConstruirIndicePorNombre(
        IEnumerable<(int ID, string Nombre)> items)
    {
        var dict = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        foreach (var (id, nombre) in items)
        {
            var clave = Normalizar(nombre);
            if (!string.IsNullOrEmpty(clave) && !dict.ContainsKey(clave))
                dict[clave] = (id, nombre);
        }
        return dict;
    }

    private static Dictionary<string, int> LeerHeaders(IXLRow encabezado)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var celda in encabezado.CellsUsed())
        {
            var clave = Normalizar(celda.GetString());
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = celda.Address.ColumnNumber;
        }
        return headers;
    }

    private static int ResolverColumna(Dictionary<string, int> headers, params string[] aliases)
    {
        foreach (var a in aliases)
            if (headers.TryGetValue(a, out var col)) return col;
        return 0;
    }

    private static byte[] ConstruirXlsx(string hoja, string[] columnas, object[] ejemplo)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(hoja);
        EscribirEncabezado(ws, columnas);
        for (int i = 0; i < ejemplo.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = ejemplo[i].ToString();
            c.Style.Font.FontColor = XLColor.Gray;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void EscribirEncabezado(IXLWorksheet ws, string[] columnas)
    {
        for (int i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static string Normalizar(string s)
    {
        var sinTildes = s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "").Replace("_", "").Replace("-", "")
            .Replace("*", "").Replace("(", "").Replace(")", "")
            .Trim();
    }
}
