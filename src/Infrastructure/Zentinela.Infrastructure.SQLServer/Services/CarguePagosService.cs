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
/// Deduplicación: una fila válida se omite (no se inserta) si su combinación
/// (cédula, fecha, valor) ya existe en la tabla Pago para esa cartera, o si se repite
/// dentro del mismo archivo. Las omitidas se devuelven en Duplicados, no en Errores.
///
/// El CSV puede tener las columnas en cualquier orden y puede incluir columnas extra.
/// El servicio localiza cada columna por nombre (case-insensitive, sin tildes ni espacios).
/// Columnas requeridas y alias aceptados:
///   Cédula      → cedula, numerodocumento, documento
///   Nombre      → nombre, nombrecliente, cliente
///   Valor       → valor, valorpagado, valorpago, montopagado
///   Fecha       → fecha, fechapago, fechadepago
///   Honorarios  → honorarios, honorario
///   UsuarioCtrl → usuariocontrol, usuario, usuariocontrolnext, control
///
/// Resolución de Líder y Agente:
///   - Si UsuarioControl = "BASE GENERAL" (case-insensitive): AgenteControlID queda NULL,
///     LiderID = único líder en Titular_Cartera para esa (cédula, cartera). 0 o >1 líderes → error.
///   - Si UsuarioControl normal: AgenteControlID = Agente.UsuarioControlNext lookup.
///     LiderID = único líder en Titular_Cartera; si hay varios líderes para la cédula en la
///     cartera, se desempata con CarteraAgente.LiderID del agente resuelto.
/// </summary>
public class CarguePagosService : ICarguePagosService
{
    /// <summary>
    /// Valor especial del campo Usuario Control que indica un pago sin asesor
    /// responsable. Case-insensitive: aceptamos "BASE GENERAL", "Base General",
    /// "base general", etc. AgenteControlID queda NULL para esos pagos.
    /// </summary>
    private const string BaseGeneralKeyword = "BASE GENERAL";

    private readonly CallCenterDbContext _context;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly ICalificadorCarteraCache _calificadorCache;

    public CarguePagosService(
        CallCenterDbContext context,
        RecursosHumanosDbContext rrhh,
        ICalificadorCarteraCache calificadorCache)
    {
        _context          = context;
        _rrhh             = rrhh;
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

        return await ResolverFilasAsync(filas, carteraID);
    }

    /// <summary>
    /// Resuelve y deduplica un conjunto de filas YA parseadas (mutación + partición): marca
    /// BASE GENERAL, resuelve AgenteControlID y LiderID (con desempate multi-líder vía
    /// Cartera_Agente, ADR-018) y marca duplicados (cédula, fecha, valor en BD o repetidos en el
    /// lote). Reutilizado por el cargue masivo (ValidarArchivoAsync) y el registro de pago individual.
    /// </summary>
    private async Task<ValidarPagosResponse> ResolverFilasAsync(List<PagoFilaDto> filas, int carteraID)
    {
        var filasValidas = filas.Where(f => f.EsValida).ToList();

        // ── Marcar filas BASE GENERAL ─────────────────────────────────────────
        // Valor especial (case-insensitive) que indica que el pago no tiene asesor
        // responsable y se atribuye solo al líder.
        foreach (var fila in filasValidas)
        {
            fila.EsBaseGeneral = string.Equals(
                fila.UsuarioControl.Trim(),
                BaseGeneralKeyword,
                StringComparison.OrdinalIgnoreCase);
        }

        // ── Lookup 1: AgenteControlID para filas NO BASE GENERAL ──────────────
        var usuariosControl = filasValidas
            .Where(f => !f.EsBaseGeneral && !string.IsNullOrWhiteSpace(f.UsuarioControl))
            .Select(f => f.UsuarioControl.Trim())
            .Distinct()
            .ToList();

        var agentes = usuariosControl.Count == 0
            ? []
            : await _context.Agentes
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

        foreach (var fila in filasValidas.Where(f => !f.EsBaseGeneral))
        {
            if (agentePorUsuario.TryGetValue(fila.UsuarioControl.Trim(), out var agenteID))
                fila.AgenteControlID = agenteID;
            else
            {
                fila.EsValida     = false;
                fila.ErrorMensaje = $"Usuario Control '{fila.UsuarioControl}' no encontrado en el sistema.";
            }
        }

        // ── Lookup 2: Asignaciones de Titular_Cartera para resolver LiderID ───
        var cedulas = filasValidas.Select(f => f.CedulaTitular).Distinct().ToList();

        var asignaciones = await _context.TitularCarteras
            .Where(tc => cedulas.Contains(tc.TitularID) && tc.CarteraID == carteraID)
            .Select(tc => new { tc.TitularID, tc.LiderID })
            .AsNoTracking()
            .ToListAsync();

        var lideresPorCedula = asignaciones
            .GroupBy(a => a.TitularID)
            .ToDictionary(g => g.Key, g => g.Select(a => a.LiderID).Distinct().ToList());

        // ── Lookup 3: CarteraAgente para desempatar líder en filas normales ───
        // Necesario solo cuando una cédula tiene >1 líderes en la cartera: el líder
        // del agente resuelto (vía Cartera_Agente) decide a quién se le atribuye el pago.
        var agenteIDsResueltos = filasValidas
            .Where(f => f.EsValida && !f.EsBaseGeneral && f.AgenteControlID.HasValue)
            .Select(f => f.AgenteControlID!.Value)
            .Distinct()
            .ToList();

        var liderPorAgenteDict = agenteIDsResueltos.Count == 0
            ? new Dictionary<long, int>()
            : (await _context.CarteraAgentes
                    .Where(ca => agenteIDsResueltos.Contains(ca.AgenteID) && ca.CarteraID == carteraID)
                    .Select(ca => new { ca.AgenteID, ca.LiderID })
                    .AsNoTracking()
                    .ToListAsync())
                .DistinctBy(x => x.AgenteID)
                .ToDictionary(x => x.AgenteID, x => x.LiderID);

        // ── Resolver LiderID para cada fila ───────────────────────────────────
        foreach (var fila in filasValidas.Where(f => f.EsValida))
        {
            if (!lideresPorCedula.TryGetValue(fila.CedulaTitular, out var lideres) || lideres.Count == 0)
            {
                fila.EsValida     = false;
                fila.ErrorMensaje = $"Cédula {fila.CedulaTitular} no está asignada a ningún líder en esta cartera.";
                continue;
            }

            if (lideres.Count == 1)
            {
                fila.LiderID = lideres[0];
                continue;
            }

            // Múltiples líderes para la cédula en esta cartera
            if (fila.EsBaseGeneral)
            {
                fila.EsValida     = false;
                fila.ErrorMensaje =
                    $"Cédula {fila.CedulaTitular} (BASE GENERAL) está asignada a {lideres.Count} líderes en esta cartera — no se puede determinar a cuál atribuir el pago.";
                continue;
            }

            // Pago normal: desempatar usando el líder del agente vía Cartera_Agente
            if (fila.AgenteControlID.HasValue &&
                liderPorAgenteDict.TryGetValue(fila.AgenteControlID.Value, out var liderDelAgente) &&
                lideres.Contains(liderDelAgente))
            {
                fila.LiderID = liderDelAgente;
            }
            else
            {
                fila.EsValida     = false;
                fila.ErrorMensaje =
                    $"Cédula {fila.CedulaTitular} está asignada a {lideres.Count} líderes en esta cartera y el agente '{fila.UsuarioControl}' no resuelve a ninguno de ellos vía Cartera_Agente — no se puede desempatar.";
            }
        }

        // ── Detección de duplicados (cédula, fecha, valor) por cartera ────────
        // Una fila válida se marca como duplicada si:
        //   a) ya existe esa combinación en la tabla Pago para esta cartera, o
        //   b) se repite dentro del mismo archivo (se conserva la primera ocurrencia).
        // Las duplicadas NO se insertan: se reportan aparte para que el usuario sepa qué se omitió.
        var candidatas       = filas.Where(f => f.EsValida).ToList();
        var combosExistentes = await ObtenerCombosExistentesAsync(candidatas, carteraID);

        var vistosEnArchivo = new HashSet<(long, DateOnly, decimal)>();
        foreach (var fila in candidatas)
        {
            var clave = (fila.CedulaTitular, fila.FechaPago, decimal.Round(fila.ValorPagado, 2));
            if (combosExistentes.Contains(clave) || !vistosEnArchivo.Add(clave))
            {
                fila.EsDuplicado  = true;
                fila.ErrorMensaje = "Pago ya cargado (misma cédula, fecha y valor).";
            }
        }

        var validas    = candidatas.Where(f => !f.EsDuplicado).ToList();
        var duplicados = candidatas.Where(f =>  f.EsDuplicado).ToList();
        var errores    = filas.Where(f => !f.EsValida).ToList();

        return new ValidarPagosResponse
        {
            TotalFilas         = filas.Count,
            CantidadValidas    = validas.Count,
            CantidadDuplicados = duplicados.Count,
            CantidadErrores    = errores.Count,
            Preview            = validas.Take(10).ToList(),
            Duplicados         = duplicados,
            Errores            = errores,
            TodasLasValidas    = validas,
        };
    }

    /// <summary>
    /// Devuelve el conjunto de combinaciones (cédula, fecha, valor) ya presentes en la tabla
    /// Pago para la cartera indicada, restringido a las cédulas de las filas candidatas.
    /// El valor se redondea a 2 decimales para alinear con el tipo decimal(18,2) de la columna.
    /// </summary>
    private async Task<HashSet<(long, DateOnly, decimal)>> ObtenerCombosExistentesAsync(
        List<PagoFilaDto> candidatas, int carteraID)
    {
        if (candidatas.Count == 0)
            return [];

        var cedulas = candidatas.Select(f => f.CedulaTitular).Distinct().ToList();

        var existentes = await _context.Pagos
            .Where(p => p.CarteraID == carteraID && cedulas.Contains(p.CedulaTitular))
            .Select(p => new { p.CedulaTitular, p.FechaPago, p.ValorPagado })
            .AsNoTracking()
            .ToListAsync();

        return existentes
            .Select(p => (p.CedulaTitular, p.FechaPago, decimal.Round(p.ValorPagado, 2)))
            .ToHashSet();
    }

    public async Task<int> ConfirmarCargueAsync(ConfirmarPagosRequest request, string usuarioRegistro)
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
            UsuarioRegistro  = usuarioRegistro,
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

    // ── Pago individual ───────────────────────────────────────────────────────

    public async Task<RegistrarPagoIndividualResponse> RegistrarPagoIndividualAsync(
        RegistrarPagoIndividualRequest request, string usuarioRegistro)
    {
        // El pago individual es, conceptualmente, una fila del cargue masivo: se modela como
        // PagoFilaDto y se valida/resuelve con EXACTAMENTE la misma lógica (ResolverFilasAsync).
        // Para BASE GENERAL se envía la palabra clave como UsuarioControl; si no, el username del
        // asesor seleccionado en el autocomplete.
        var fila = new PagoFilaDto
        {
            CedulaTitular  = request.CedulaTitular,
            NombreCliente  = request.NombreCliente,
            ValorPagado    = request.ValorPagado,
            FechaPago      = request.FechaPago,
            Honorarios     = request.Honorarios,
            UsuarioControl = request.EsBaseGeneral
                                ? BaseGeneralKeyword
                                : (request.UsuarioControl ?? string.Empty).Trim(),
            EsValida       = true,
        };

        var resultado = await ResolverFilasAsync([fila], request.CarteraID);

        // Una sola fila: cae en Errores (resolución falló), Duplicados (ya cargado) o TodasLasValidas.
        if (resultado.Errores.Count > 0)
            throw new PagoIndividualException(resultado.Errores[0].ErrorMensaje ?? "No se pudo validar el pago.");
        if (resultado.Duplicados.Count > 0)
            throw new PagoIndividualException(resultado.Duplicados[0].ErrorMensaje ?? "El pago ya está registrado.");

        var valida = resultado.TodasLasValidas[0];

        var pago = new Pago
        {
            CedulaTitular   = valida.CedulaTitular,
            NombreCliente   = valida.NombreCliente.Trim(),
            ValorPagado     = valida.ValorPagado,
            FechaPago       = valida.FechaPago,
            Honorarios      = valida.Honorarios,
            LiderID         = valida.LiderID,
            AgenteControlID = valida.AgenteControlID,
            CarteraID       = request.CarteraID,
            UsuarioRegistro = usuarioRegistro,
            FechaRegistro   = DateTime.UtcNow,
        };

        _context.Pagos.Add(pago);
        await _context.SaveChangesAsync();
        _calificadorCache.InvalidarTodo(); // paridad con el cargue masivo

        var liderNombre = await _context.Lideres
            .Where(l => l.ID == valida.LiderID)
            .Select(l => l.Nombre)
            .FirstOrDefaultAsync() ?? string.Empty;

        return new RegistrarPagoIndividualResponse
        {
            PagoID          = pago.PagoID,
            LiderID         = valida.LiderID,
            LiderNombre     = liderNombre,
            AgenteControlID = valida.AgenteControlID,
            EsBaseGeneral   = valida.EsBaseGeneral,
        };
    }

    /// <summary>
    /// Asesores de una cartera para el autocomplete del pago individual: los agentes asignados
    /// en Cartera_Agente que tienen UsuarioControlNext (sin él no se puede atribuir el pago).
    /// Nombres desde DBRecursosHumanos (cross-DB), filtrando empleados retirados. Filtra por
    /// <paramref name="q"/> sobre el nombre y limita a 10 resultados.
    /// </summary>
    public async Task<List<AsesorCarteraDto>> BuscarAsesoresAsync(int carteraID, string? q)
    {
        var agentes = await _context.CarteraAgentes
            .Where(ca => ca.CarteraID == carteraID)
            .Join(_context.Agentes,
                  ca => ca.AgenteID,
                  a  => a.AgenteID,
                  (ca, a) => new { a.AgenteID, a.UsuarioControlNext })
            .Where(x => x.UsuarioControlNext != null)
            .Distinct()
            .AsNoTracking()
            .ToListAsync();

        if (agentes.Count == 0)
            return [];

        var ids = agentes.Select(a => a.AgenteID).ToList();
        var nombres = await _rrhh.Empleados
            .Where(e => e.EstadoEmpleado && ids.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        var termino = q?.Trim();
        return agentes
            .Where(a => nombres.ContainsKey(a.AgenteID))
            .Select(a => new AsesorCarteraDto
            {
                AgenteID       = a.AgenteID,
                Nombre         = nombres[a.AgenteID],
                UsuarioControl = a.UsuarioControlNext!,
            })
            .Where(a => string.IsNullOrEmpty(termino)
                     || a.Nombre.Contains(termino, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Nombre)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Busca un titular por cédula dentro de una cartera (para autocompletar el nombre en el pago
    /// individual). Existe = false si la cédula no está asignada a la cartera — en ese caso el pago
    /// no resolvería líder.
    /// </summary>
    public async Task<TitularEnCarteraDto> BuscarTitularAsync(int carteraID, long cedula)
    {
        var asignado = await _context.TitularCarteras
            .AnyAsync(tc => tc.TitularID == cedula && tc.CarteraID == carteraID);

        if (!asignado)
            return new TitularEnCarteraDto { Existe = false };

        var nombre = await _context.Titulares
            .Where(t => t.NumeroDocumento == cedula)
            .Select(t => t.Nombre)
            .FirstOrDefaultAsync();

        return new TitularEnCarteraDto { Existe = true, Nombre = nombre };
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

        // Filas de ejemplo en gris: una con asesor, otra BASE GENERAL.
        var ejemplos = new object[][]
        {
            new object[] { 1020304050L, "PEREZ JUAN",   1500000, 225000, "2026-04-21", "jperez" },
            new object[] { 9876543210L, "GOMEZ MARIA",  900000,  135000, "2026-04-21", "BASE GENERAL" },
        };
        for (int r = 0; r < ejemplos.Length; r++)
        {
            for (int i = 0; i < ejemplos[r].Length; i++)
            {
                var celda = ws.Cell(2 + r, i + 1);
                celda.Value = ejemplos[r][i].ToString();
                celda.Style.Font.FontColor = XLColor.Gray;
            }
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
