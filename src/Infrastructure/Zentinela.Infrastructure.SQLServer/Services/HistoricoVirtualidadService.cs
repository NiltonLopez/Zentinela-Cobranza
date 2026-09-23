using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// El archivo enviado a confirmar no corresponde al que se validó, o no cumple el contrato
/// mínimo (columnas). El controller la traduce a 409 Conflict.
/// </summary>
public class CargaIvrConflictoException(string mensaje) : Exception(mensaje);

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
    // La lista de estados y el modo de carga viven en CatalogoVirtualidad (capa Application),
    // que es la fuente única que también consume el frontend por GET /catalogo.

    // Umbral de alerta por saturación de línea (>N envíos al mismo destino).
    // 0 = sin alerta (p. ej. Correo no aplica saturación de línea telefónica).
    private static readonly Dictionary<string, int> UmbralAlerta = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IVR"] = 5, ["SMS"] = 4, ["Correo"] = 0,
    };

    private static bool EsCanalValido(string canal) => CatalogoVirtualidad.EsCanalValido(canal);

    private static bool EsCorreo(string canal) =>
        string.Equals(canal, CatalogoVirtualidad.CanalCorreo, StringComparison.OrdinalIgnoreCase);

    public CatalogoCanalDto ObtenerCatalogo(string canal)
    {
        var esIvr = CatalogoVirtualidad.EsIvr(canal);
        return new CatalogoCanalDto
        {
            Canal   = canal,
            Modo    = CatalogoVirtualidad.ModoDe(canal),
            Estados = CatalogoVirtualidad.EstadosPorCanal.GetValueOrDefault(canal, []),
            MapeoEstados = esIvr
                ? CatalogoVirtualidad.IvrLastApp
                    .Select(x => new MapeoEstadoDto
                    {
                        CodigoCrudo = x.Codigo, Estado = x.Estado, Significado = x.Significado,
                    })
                    .ToList()
                : [],
            ColumnasEsperadas    = esIvr ? CatalogoVirtualidad.ColumnasIvr : [],
            ExtensionesAceptadas = esIvr ? [".csv", ".xlsx"] : [".xlsx"],
        };
    }

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

        var indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(_db);

        foreach (var f in filas.Where(f => f.EsValida))
        {
            var r = indices.Resolver(f.NombreCartera, f.NombreLider);
            if (r.Error is not null) { f.EsValida = false; f.ErrorMensaje = r.Error; continue; }
            f.CarteraID = r.CarteraID; f.NombreCartera = r.NombreCartera;
            f.LiderID   = r.LiderID;   f.NombreLider   = r.NombreLider;
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        var alertas = await CalcularAlertasAsync(validas, canal);

        return new ValidarSolicitudResponse
        {
            TotalFilas       = filas.Count,
            TotalValidas     = validas.Count,
            TotalErrores     = filas.Count - validas.Count,
            Preview          = filas.Take(10).ToList(),
            FilasValidas     = validas,
            Alertas          = alertas,
            ErroresAgrupados = AgruparErrores(filas.Where(f => !f.EsValida).Select(f => f.ErrorMensaje)),
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
                ErroresAgrupados = [new ErrorAgrupadoDto { Mensaje = $"Canal '{canal}' no soportado.", Cantidad = 1 }],
            };

        var estadosValidos = CatalogoVirtualidad.EstadosPorCanal.GetValueOrDefault(canal, [])
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
                f.ErrorMensaje = $"Estado '{f.Estado}' no válido para {canal}. Válidos: {string.Join(", ", CatalogoVirtualidad.EstadosPorCanal[canal])}.";
                continue;
            }
            f.Estado = estadoCanonico; // normaliza al valor del catálogo
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        return new ValidarResultadoResponse
        {
            TotalFilas       = filas.Count,
            TotalValidas     = validas.Count,
            TotalErrores     = filas.Count - validas.Count,
            Preview          = filas.Take(10).ToList(),
            FilasValidas     = validas,
            ErroresAgrupados = AgruparErrores(filas.Where(f => !f.EsValida).Select(f => f.ErrorMensaje)),
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

        foreach (var e in entidades)
            e.Estado = porId[e.ID];

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

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  Carga única — canal IVR (ADR-031)                                    ║
    // ╚═══════════════════════════════════════════════════════════════════════╝
    // El archivo del marcador ya trae el resultado de cada llamada, así que no hay
    // fase de Solicitud: se inserta la fila completa con el Estado ya traducido.

    public async Task<ValidarCargaIvrResponse> ValidarCargaIvrAsync(Stream archivo, string nombreArchivo)
    {
        var (huella, filasCrudas) = await LeerArchivoAsync(archivo, nombreArchivo);
        var resp = new ValidarCargaIvrResponse { HuellaArchivo = huella };

        if (filasCrudas.Count < 2)
        {
            resp.ErroresAgrupados = [new ErrorAgrupadoDto
            {
                Mensaje  = "El archivo no tiene filas de datos (solo encabezado o vacío).",
                Cantidad = 1,
            }];
            return resp;
        }

        var headers = ArchivoTabular.LeerHeaders(filasCrudas[0]);
        resp.ColumnasEncontradas = filasCrudas[0].Select(h => h.Trim()).Where(h => h.Length > 0).ToList();

        var idx = ResolverColumnasIvr(headers, out var faltantes);
        if (faltantes.Count > 0)
        {
            // No se adivina ninguna columna: en un informe operativo, elegir mal
            // produciría cifras incorrectas en silencio (mismo criterio que ADR-030).
            resp.ColumnasFaltantes = faltantes;
            resp.ErroresAgrupados  = [new ErrorAgrupadoDto
            {
                Mensaje  = $"Faltan columnas obligatorias: {string.Join(", ", faltantes)}.",
                Cantidad = 1,
            }];
            return resp;
        }

        var filas = ParsearFilasIvr(filasCrudas, headers.Count, idx);
        resp.TotalFilas = filas.Count;
        if (filas.Count == 0) return resp;

        var indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(_db);
        foreach (var f in filas.Where(f => f.EsValida))
        {
            var r = indices.Resolver(f.NombreCartera, f.NombreLider);
            if (r.Error is not null) { f.EsValida = false; f.ErrorMensaje = r.Error; continue; }
            f.CarteraID = r.CarteraID; f.NombreCartera = r.NombreCartera;
            f.LiderID   = r.LiderID;   f.NombreLider   = r.NombreLider;
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        var invalidas = filas.Where(f => !f.EsValida).ToList();

        resp.TotalValidas = validas.Count;
        resp.TotalErrores = invalidas.Count;
        resp.Preview      = filas.Take(10).ToList();
        resp.ErroresAgrupados = AgruparErrores(invalidas.Select(f => f.ErrorMensaje));

        // Códigos "Last App" no catalogados: la fila se rechaza (decisión del negocio),
        // pero el código se expone aparte para que un código nuevo del marcador salte a
        // la vista en vez de perderse entre el resto de los errores.
        var desconocidos = invalidas
            .Where(f => !string.IsNullOrEmpty(f.CodigoCrudo)
                     && !CatalogoVirtualidad.IvrEstadoPorCodigo.ContainsKey(
                            CatalogoVirtualidad.NormalizarCodigo(f.CodigoCrudo)))
            .ToList();
        resp.TotalCodigoDesconocido = desconocidos.Count;
        resp.CodigosDesconocidos = desconocidos
            .Select(f => f.CodigoCrudo)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        if (validas.Count == 0) return resp;

        resp.FechaMin = validas.Min(f => f.FechaEnvio);
        resp.FechaMax = validas.Max(f => f.FechaEnvio);

        // Se agrupa por Estado, no por el código crudo: el archivo puede traer "Playback"
        // y "playback" y son el mismo resultado. Se muestra el código CANÓNICO del catálogo
        // (relación 1:1 con el estado) para poder auditar el mapeo de un vistazo.
        var codigoCanonico = CatalogoVirtualidad.IvrLastApp
            .ToDictionary(x => x.Estado, x => x.Codigo, StringComparer.Ordinal);

        resp.DesglosePorEstado = validas
            .GroupBy(f => f.Estado)
            .Select(g => new ConteoEstadoDto
            {
                Estado      = g.Key ?? "(sin estado)",
                CodigoCrudo = g.Key is not null && codigoCanonico.TryGetValue(g.Key, out var cod)
                                  ? cod
                                  : g.First().CodigoCrudo,
                Cantidad    = g.Count(),
                Porcentaje  = Math.Round(100m * g.Count() / validas.Count, 2),
            })
            .OrderByDescending(x => x.Cantidad)
            .ToList();

        resp.DesglosePorCartera = validas
            .GroupBy(f => new { f.NombreCartera, f.NombreLider })
            .Select(g => new ConteoCarteraDto
            {
                NombreCartera = g.Key.NombreCartera,
                NombreLider   = g.Key.NombreLider,
                Cantidad      = g.Count(),
            })
            .OrderByDescending(x => x.Cantidad)
            .ToList();

        // Filas idénticas repetidas dentro del propio archivo: señal de un archivo
        // concatenado dos veces. No se descartan (en IVR un reintento real produce
        // filas casi idénticas), solo se cuentan.
        resp.FilasRepetidasEnArchivo = validas.Count - validas
            .Select(f => (f.NumeroDocumento, f.Destino, f.FechaEnvio, f.CarteraID, f.LiderID, f.Estado))
            .Distinct()
            .Count();

        resp.Solapamientos = await CalcularSolapamientoAsync(validas);
        resp.Alertas       = await CalcularAlertasIvrAsync(validas);

        return resp;
    }

    public async Task<ConfirmarCargaIvrResponse> ConfirmarCargaIvrAsync(
        Stream archivo, string nombreArchivo, string huellaEsperada)
    {
        // Se re-parsea en el servidor en vez de recibir las filas del cliente: evita el
        // round-trip de ~100 MB con archivos grandes y garantiza que lo insertado sea
        // exactamente lo validado (el cliente no dicta CarteraID/LiderID).
        var (huella, filasCrudas) = await LeerArchivoAsync(archivo, nombreArchivo);
        if (!string.Equals(huella, huellaEsperada, StringComparison.OrdinalIgnoreCase))
            throw new CargaIvrConflictoException(
                "El archivo cambió desde la validación. Vuelve a validarlo antes de confirmar.");

        if (filasCrudas.Count < 2)
            return new ConfirmarCargaIvrResponse { Mensaje = "El archivo no tiene filas de datos." };

        var headers = ArchivoTabular.LeerHeaders(filasCrudas[0]);
        var idx = ResolverColumnasIvr(headers, out var faltantes);
        if (faltantes.Count > 0)
            throw new CargaIvrConflictoException($"Faltan columnas obligatorias: {string.Join(", ", faltantes)}.");

        var filas = ParsearFilasIvr(filasCrudas, headers.Count, idx);
        var indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(_db);
        foreach (var f in filas.Where(f => f.EsValida))
        {
            var r = indices.Resolver(f.NombreCartera, f.NombreLider);
            if (r.Error is not null) { f.EsValida = false; f.ErrorMensaje = r.Error; continue; }
            f.CarteraID = r.CarteraID; f.LiderID = r.LiderID;
        }

        var validas = filas.Where(f => f.EsValida).ToList();
        if (validas.Count == 0)
            return new ConfirmarCargaIvrResponse
            {
                Descartados = filas.Count,
                Mensaje     = "Ninguna fila del archivo resultó válida; no se cargó nada.",
            };

        // Sello uniforme del lote (no el DEFAULT GETDATE() por fila): hace el cargue
        // reversible con un DELETE ... WHERE Canal='IVR' AND FechaCargue='<sello>'.
        var sello = DateTime.Now;
        await InsertarIvrEnBloqueAsync(validas, sello);

        return new ConfirmarCargaIvrResponse
        {
            Insertados  = validas.Count,
            Descartados = filas.Count - validas.Count,
            FechaCargue = sello,
            Mensaje     = $"Se cargaron {validas.Count:N0} registros de IVR.",
        };
    }

    // ── Parseo IVR ──────────────────────────────────────────────────────────

    private sealed record ColumnasIvr(int Cartera, int Lider, int Fecha, int Numero, int LastApp, int Documento);

    private static ColumnasIvr ResolverColumnasIvr(Dictionary<string, int> headers, out List<string> faltantes)
    {
        var idx = new ColumnasIvr(
            Cartera:   ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.Cartera),
            Lider:     ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.Lider),
            Fecha:     ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.Fecha),
            Numero:    ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.Numero),
            LastApp:   ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.LastApp),
            Documento: ArchivoTabular.ResolverColumna(headers, CatalogoVirtualidad.AliasIvr.Documento));

        faltantes = [];
        if (idx.Cartera   < 0) faltantes.Add("Cartera");
        if (idx.Lider     < 0) faltantes.Add("Lider");
        if (idx.Fecha     < 0) faltantes.Add("Fecha Marcado");
        if (idx.Numero    < 0) faltantes.Add("Número");
        if (idx.LastApp   < 0) faltantes.Add("Last App");
        if (idx.Documento < 0) faltantes.Add("Cedula");
        return idx;
    }

    private static List<FilaIvrDto> ParsearFilasIvr(
        List<string[]> filasCrudas, int totalColumnas, ColumnasIvr idx)
    {
        var resultado = new List<FilaIvrDto>(filasCrudas.Count - 1);

        for (var i = 1; i < filasCrudas.Count; i++)
        {
            var partes = filasCrudas[i];
            if (partes.All(string.IsNullOrWhiteSpace)) continue;

            var numeroLinea = i + 1;
            var dto = new FilaIvrDto { EsValida = true };
            resultado.Add(dto);

            // El CSV se parte con Split plano: una fila con menos columnas que el
            // encabezado viene corrida (comillas con separador adentro) y leerla
            // mapearía valores equivocados en silencio.
            if (partes.Length < totalColumnas)
            {
                Invalidar(dto, $"Línea {numeroLinea}: la fila tiene {partes.Length} columnas y se esperaban {totalColumnas} (¿separador o comillas?).");
                continue;
            }

            string Cel(int c) => c >= 0 && c < partes.Length ? partes[c].Trim() : string.Empty;

            var doc = SoloDigitos(Cel(idx.Documento));
            if (doc.Length == 0 || !long.TryParse(doc, out var numeroDocumento))
                Invalidar(dto, $"Línea {numeroLinea}: Cedula inválida o vacía.");
            else
                dto.NumeroDocumento = numeroDocumento;

            // Solo trim + quitar el sufijo ".0" de Excel: NO se normaliza el teléfono,
            // porque el histórico ya guardado está en formato nacional de 10 dígitos y
            // normalizar solo lo nuevo partiría la misma línea en dos poblaciones.
            dto.Destino = QuitarSufijoDecimal(Cel(idx.Numero));
            if (dto.Destino.Length == 0)
                Invalidar(dto, $"Línea {numeroLinea}: Número vacío.");

            dto.NombreCartera = Cel(idx.Cartera);
            if (dto.NombreCartera.Length == 0)
                Invalidar(dto, $"Línea {numeroLinea}: Cartera vacía.");

            dto.NombreLider = Cel(idx.Lider);
            if (dto.NombreLider.Length == 0)
                Invalidar(dto, $"Línea {numeroLinea}: Lider vacío.");

            if (!ArchivoTabular.TryParseFecha(Cel(idx.Fecha), out var fecha))
                Invalidar(dto, $"Línea {numeroLinea}: Fecha Marcado inválida ('{Cel(idx.Fecha)}').");
            else
                dto.FechaEnvio = fecha;

            dto.CodigoCrudo = Cel(idx.LastApp);
            if (dto.CodigoCrudo.Length == 0)
                Invalidar(dto, $"Línea {numeroLinea}: Last App vacío.");
            else if (CatalogoVirtualidad.IvrEstadoPorCodigo.TryGetValue(
                         CatalogoVirtualidad.NormalizarCodigo(dto.CodigoCrudo), out var estado))
                dto.Estado = estado;
            else
                // El mensaje incluye el código para que AgruparErrores lo colapse en una
                // línea por código, con su conteo.
                Invalidar(dto, $"Last App '{dto.CodigoCrudo}' no está en el catálogo de estados de IVR.");
        }

        return resultado;
    }

    /// <summary>Registros IVR ya cargados para las mismas carteras y rango de fechas del archivo.</summary>
    private async Task<List<SolapamientoDto>> CalcularSolapamientoAsync(List<FilaIvrDto> validas)
    {
        var carteraIDs = validas.Select(f => f.CarteraID).Distinct().ToList();
        var min = validas.Min(f => f.FechaEnvio);
        var max = validas.Max(f => f.FechaEnvio);

        var existentes = await _db.HistoricoVirtualidad
            .Where(h => h.Canal == CatalogoVirtualidad.CanalIvr
                     && carteraIDs.Contains(h.CarteraID)
                     && h.FechaEnvio >= min && h.FechaEnvio <= max)
            .GroupBy(h => h.CarteraID)
            .Select(g => new { CarteraID = g.Key, Total = g.Count() })
            .ToListAsync();

        if (existentes.Count == 0) return [];

        var nombrePorCartera = validas
            .GroupBy(f => f.CarteraID)
            .ToDictionary(g => g.Key, g => (Nombre: g.First().NombreCartera, EnArchivo: g.Count()));

        return existentes
            .Select(e => new SolapamientoDto
            {
                CarteraID     = e.CarteraID,
                NombreCartera = nombrePorCartera.TryGetValue(e.CarteraID, out var n) ? n.Nombre : $"Cartera {e.CarteraID}",
                EnArchivo     = nombrePorCartera.TryGetValue(e.CarteraID, out var v) ? v.EnArchivo : 0,
                YaEnBaseDatos = e.Total,
            })
            .OrderByDescending(x => x.YaEnBaseDatos)
            .ToList();
    }

    private async Task<List<AlertaSolicitudDto>> CalcularAlertasIvrAsync(List<FilaIvrDto> validas)
    {
        var umbral = UmbralAlerta.GetValueOrDefault(CatalogoVirtualidad.CanalIvr, 0);
        if (umbral <= 0 || validas.Count == 0) return [];

        return validas
            .GroupBy(f => new { f.Destino, f.NombreCartera })
            .Select(g => new AlertaSolicitudDto
            {
                Destino       = g.Key.Destino,
                NombreCartera = g.Key.NombreCartera,
                TotalEnvios   = g.Count(),
            })
            .Where(a => a.TotalEnvios > umbral)
            .OrderByDescending(a => a.TotalEnvios)
            .Take(200)
            .ToList();
    }

    // ── Inserción masiva ────────────────────────────────────────────────────

    private async Task InsertarIvrEnBloqueAsync(List<FilaIvrDto> validas, DateTime sello)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Canal",           typeof(string));
        tabla.Columns.Add("NumeroDocumento", typeof(long));
        tabla.Columns.Add("Destino",         typeof(string));
        tabla.Columns.Add("CarteraID",       typeof(int));
        tabla.Columns.Add("LiderID",         typeof(int));
        // DateOnly no lo soporta DataTable/SqlBulkCopy: va como DateTime a una columna date.
        tabla.Columns.Add("FechaEnvio",      typeof(DateTime));
        tabla.Columns.Add("Estado",          typeof(string));
        tabla.Columns.Add("FechaCargue",     typeof(DateTime));

        foreach (var f in validas)
            tabla.Rows.Add(
                CatalogoVirtualidad.CanalIvr, f.NumeroDocumento, f.Destino,
                f.CarteraID, f.LiderID, f.FechaEnvio.ToDateTime(TimeOnly.MinValue),
                (object?)f.Estado ?? DBNull.Value, sello);

        var laAbriYo = _db.Database.GetDbConnection().State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var conn = (SqlConnection)_db.Database.GetDbConnection();
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();

            // CheckConstraints: sin esta opción SqlBulkCopy DESACTIVA la verificación y
            // dejaría FK_HistoricoVirtualidad_LiderCartera marcada como untrusted.
            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, sqlTx)
            {
                DestinationTableName = "dbo.HistoricoVirtualidad",
                BatchSize            = 10_000,
                BulkCopyTimeout      = 600,
            };
            // Mapeo por NOMBRE obligatorio: ID es IDENTITY y no va en el DataTable, así
            // que el mapeo posicional desalinearía todas las columnas.
            foreach (DataColumn col in tabla.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

            await bulk.WriteToServerAsync(tabla);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    // ── Lectura de archivo (CSV o XLSX) + huella ────────────────────────────

    /// <summary>
    /// Copia el stream a memoria para poder calcular la huella SHA-256 y parsear el mismo
    /// contenido. La huella ata la confirmación al archivo exacto que se validó.
    /// </summary>
    private static async Task<(string Huella, List<string[]> Filas)> LeerArchivoAsync(
        Stream archivo, string nombreArchivo)
    {
        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms);
        ms.Position = 0;

        var huella = Convert.ToHexString(await SHA256.HashDataAsync(ms));
        ms.Position = 0;

        var ext = Path.GetExtension(nombreArchivo).ToLowerInvariant();
        var filas = ext == ".csv"
            ? (await ArchivoTabular.ExtraerFilasCsvAsync(ms)).Filas
            : ArchivoTabular.ExtraerFilasXlsx(ms);

        return (huella, filas);
    }

    private static string SoloDigitos(string s) =>
        new(s.Where(char.IsDigit).ToArray());

    /// <summary>Quita el ".0" con el que Excel suele exportar números enteros.</summary>
    private static string QuitarSufijoDecimal(string s)
    {
        var t = s.Trim();
        return t.EndsWith(".0", StringComparison.Ordinal) ? t[..^2] : t;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void Invalidar(FilaIvrDto dto, string msg)
    { dto.EsValida = false; dto.ErrorMensaje ??= msg; }

    private static void Invalidar(FilaSolicitudDto dto, string msg)
    { dto.EsValida = false; dto.ErrorMensaje ??= msg; }

    private static void Invalidar(FilaResultadoDto dto, string msg)
    { dto.EsValida = false; dto.ErrorMensaje ??= msg; }

    // Los mensajes de parseo llevan "Línea N: " al inicio (distinto por fila); se quita
    // para agrupar por el tipo de error real, en vez de mostrar una línea repetida por
    // cada fila del archivo. Los mensajes de resolución (cartera/líder no encontrado) no
    // llevan ese prefijo y ya agrupan naturalmente por el nombre específico que falló.
    private static readonly Regex PrefijoLinea = new(@"^Línea \d+: ", RegexOptions.Compiled);

    private static List<ErrorAgrupadoDto> AgruparErrores(IEnumerable<string?> mensajes) =>
        mensajes
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => PrefijoLinea.Replace(m!, ""))
            .GroupBy(m => m)
            .Select(g => new ErrorAgrupadoDto { Mensaje = g.Key, Cantidad = g.Count() })
            .OrderByDescending(e => e.Cantidad)
            .ToList();

    private static ValidarSolicitudResponse ErrorResponse(string mensaje) => new()
    {
        TotalErrores = 1,
        Preview = [new FilaSolicitudDto { EsValida = false, ErrorMensaje = mensaje }],
        ErroresAgrupados = [new ErrorAgrupadoDto { Mensaje = mensaje, Cantidad = 1 }],
    };

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
