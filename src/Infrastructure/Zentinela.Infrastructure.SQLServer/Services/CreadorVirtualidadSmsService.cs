using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.CreadorVirtualidad;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using M = Zentinela.Application.CatalogoCreadorVirtualidad.Motivos;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Creador de Virtualidad — canal SMS (ADR-037).
///
/// Un solo pipeline (<see cref="DepurarAsync"/>) alimenta los tres pasos con archivo:
/// validar, anotar el catálogo y generar. Cada fila recibe UN motivo de exclusión, en
/// orden fijo, para que los conteos sumen exacto y para que lo que el líder vio en el
/// preview sea exactamente lo que sale en el archivo.
///
/// El archivo se re-sube en cada paso (patrón ADR-031): la huella SHA-256 garantiza que
/// es el mismo. Lo que NO garantiza es la BD: si entre validar y generar llegan gestiones
/// nuevas, los conteos del paso 4 pueden diferir; los auditados son los del paso 4.
/// </summary>
public class CreadorVirtualidadSmsService : ICreadorVirtualidadSmsService
{
    private readonly CallCenterDbContext      _db;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly IExcelExporter           _exporter;

    public CreadorVirtualidadSmsService(
        CallCenterDbContext db, RecursosHumanosDbContext rrhh, IExcelExporter exporter)
    {
        _db       = db;
        _rrhh     = rrhh;
        _exporter = exporter;
    }

    // ── Plantilla ───────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla() => ConstruirPlantilla();

    /// <summary>
    /// Plantilla de dos hojas. "BASE" (la primera, la única que se lee) trae SOLO los
    /// encabezados; el ejemplo va en una hoja aparte que el lector nunca abre.
    ///
    /// Antes el ejemplo estaba en la fila 2 de "BASE" y, si el líder no lo borraba, se
    /// procesaba como un titular real. Además de sacarlo de ahí, la depuración excluye esa
    /// fila con motivo propio por si llega una plantilla vieja (ver FilaEjemplo en el catálogo).
    /// </summary>
    internal const string AvisoHojaEjemplo =
        "ESTA HOJA ES SOLO UN EJEMPLO Y NO SE PROCESA. Llena tus titulares en la hoja \"" +
        CatalogoCreadorVirtualidad.HojaBase + "\", con los mismos encabezados y sin filas de ejemplo.";

    internal static byte[] ConstruirPlantilla()
    {
        using var wb = new XLWorkbook();
        var columnas = CatalogoCreadorVirtualidad.ColumnasPlantilla;

        // ── BASE: solo encabezados ──
        var ws = wb.Worksheets.Add(CatalogoCreadorVirtualidad.HojaBase);
        EscribirEncabezados(ws, fila: 1, conComentarios: true);
        // Cédula y número como texto en TODA la columna (todas las filas que el líder
        // llene): evita que Excel los convierta a 1,02E+09 o les quite ceros.
        ws.Column(1).Style.NumberFormat.Format = "@";
        ws.Column(5).Style.NumberFormat.Format = "@";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        // Sin filas de datos, AdjustToContents deja las columnas del ancho del encabezado:
        // se les da un mínimo para que quepan cédulas, nombres y números al escribir.
        foreach (var col in ws.Columns(1, columnas.Length))
            if (col.Width < 18) col.Width = 18;
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 34);

        // ── EJEMPLO: cómo se llena, con aviso de que no se procesa ──
        var ej = wb.Worksheets.Add(CatalogoCreadorVirtualidad.HojaEjemplo);
        ej.Cell(1, 1).Value = AvisoHojaEjemplo;
        var aviso = ej.Range(1, 1, 1, columnas.Length).Merge();
        aviso.Style.Font.Bold            = true;
        aviso.Style.Font.FontColor       = XLColor.FromHtml("#92400e");
        aviso.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef3c7");
        aviso.Style.Alignment.WrapText   = true;
        aviso.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ej.Row(1).Height = 32;

        EscribirEncabezados(ej, fila: 3, conComentarios: false);
        var valores = CatalogoCreadorVirtualidad.FilaEjemplo.Valores;
        ej.Column(1).Style.NumberFormat.Format = "@";
        ej.Column(5).Style.NumberFormat.Format = "@";
        for (var i = 0; i < valores.Length; i++)
        {
            var c = ej.Cell(4, i + 1);
            c.Value = valores[i];
            c.Style.Font.FontColor = XLColor.Gray;
        }
        ej.Columns(1, columnas.Length).AdjustToContents(3, 4);

        wb.Worksheet(CatalogoCreadorVirtualidad.HojaBase).SetTabActive();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void EscribirEncabezados(IXLWorksheet ws, int fila, bool conComentarios)
    {
        var columnas = CatalogoCreadorVirtualidad.ColumnasPlantilla;
        for (var i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(fila, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Cartera y Empresa son opcionales. Se señalan con un color distinto y un
        // comentario, NO cambiándoles el nombre: la plantilla anterior las titulaba
        // "Cartera (opcional)" y "Empresa (opcional)", y como ArchivoTabular.Normalizar
        // quita los paréntesis el encabezado quedaba en "carteraopcional", que no
        // coincide con ningún alias. Resultado: el archivo se llenaba entero y los
        // mensajes con {CARTERA} o {EMPRESA} salían deshabilitados diciendo que las
        // columnas no estaban. El encabezado tiene que ser exactamente el nombre.
        foreach (var col in new[] { 6, 7 })
        {
            var h = ws.Cell(fila, col);
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#64748b");
            if (conComentarios)
                h.GetComment().AddText("Opcional. Solo hace falta si el mensaje usa {CARTERA} o {EMPRESA}. No cambies el nombre de la columna.");
        }
    }

    // ── Catálogos de apoyo ──────────────────────────────────────────────────

    public async Task<List<CarteraListaNegraDto>> ListarCarterasListaNegraAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT Cartera, COUNT(DISTINCT NumeroTelefono) AS Total
              FROM dbo.ListaNegraTelefonos
             WHERE Cartera IS NOT NULL AND Cartera <> ''
             GROUP BY Cartera
             ORDER BY Cartera;
            """;
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        var laAbriYo = conn.State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var lista = new List<CarteraListaNegraDto>();
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            await using var rd  = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var valor = rd.GetString(0);
                lista.Add(new CarteraListaNegraDto
                {
                    Valor        = valor,
                    Etiqueta     = valor.Replace('_', ' '),
                    TotalNumeros = rd.GetInt32(1),
                });
            }
            return lista;
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    public async Task<List<AsesorEstrategiaDto>> ListarAsesoresDelLiderAsync(int liderID, CancellationToken ct = default)
    {
        await ExigirLiderActivoAsync(liderID, ct);

        var ids = await _db.CarteraAgentes
            .Where(ca => ca.LiderID == liderID)
            .Select(ca => ca.AgenteID)
            .Distinct()
            .ToListAsync(ct);
        if (ids.Count == 0) return [];

        var activos = await _db.Agentes
            .Where(a => a.Activo && ids.Contains(a.AgenteID))
            .Select(a => a.AgenteID)
            .ToListAsync(ct);

        return await ComponerAsesoresAsync(activos, precargados: true, ct);
    }

    public async Task<List<AsesorEstrategiaDto>> ListarAgentesActivosAsync(CancellationToken ct = default)
    {
        var activos = await _db.Agentes.Where(a => a.Activo).Select(a => a.AgenteID).ToListAsync(ct);
        return await ComponerAsesoresAsync(activos, precargados: false, ct);
    }

    /// <summary>
    /// Agente no tiene nombre: sale de RRHH (otro DbContext → join en memoria por
    /// NumeroDocumento). El contacto (número corporativo + link) vive en las columnas de
    /// Agente desde 2026-09-18.
    /// </summary>
    private async Task<List<AsesorEstrategiaDto>> ComponerAsesoresAsync(
        List<long> agenteIDs, bool precargados, CancellationToken ct)
    {
        if (agenteIDs.Count == 0) return [];

        var empleados = await _rrhh.Empleados
            .AsNoTracking()
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .Select(e => new { e.NumeroDocumento, e.Nombre, e.Apellidos })
            .ToListAsync(ct);
        var nombrePorDoc = empleados
            .GroupBy(e => e.NumeroDocumento)
            .ToDictionary(g => g.Key, g => (g.First().Nombre + " " + g.First().Apellidos).Trim());

        var contactos = await _db.Agentes
            .AsNoTracking()
            .Where(a => agenteIDs.Contains(a.AgenteID))
            .Select(a => new { a.AgenteID, a.NumeroCorporativo, a.LinkWhatsapp })
            .ToDictionaryAsync(a => a.AgenteID, ct);

        return agenteIDs
            .Select(id =>
            {
                contactos.TryGetValue(id, out var contacto);
                return new AsesorEstrategiaDto
                {
                    AgenteID          = id,
                    Nombre            = nombrePorDoc.TryGetValue(id, out var n) && n.Length > 0 ? n : $"Agente {id}",
                    NumeroCorporativo = contacto?.NumeroCorporativo,
                    LinkWhatsapp      = contacto?.LinkWhatsapp,
                    Precargado        = precargados,
                };
            })
            .OrderBy(a => a.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Escribe número corporativo + link en Agente. Ignora IDs que no existan.</summary>
    public async Task<int> GuardarContactosAsync(
        IReadOnlyList<ContactoAsesorRequest> contactos, string usuario, CancellationToken ct = default)
    {
        var validos = contactos.Where(c => EsCelularValido(c.NumeroCorporativo) && !string.IsNullOrWhiteSpace(c.LinkWhatsapp)).ToList();
        if (validos.Count == 0) return 0;

        var ids = validos.Select(c => c.AgenteID).Distinct().ToList();
        var agentes = await _db.Agentes
            .Where(a => ids.Contains(a.AgenteID))
            .ToDictionaryAsync(a => a.AgenteID, ct);

        var ahora = DateTime.Now;
        var escritos = 0;
        foreach (var c in validos)
        {
            if (!agentes.TryGetValue(c.AgenteID, out var a)) continue;
            var link = c.LinkWhatsapp.Trim();
            if (link.Length > 100) link = link[..100];

            a.NumeroCorporativo      = c.NumeroCorporativo.Trim();
            a.LinkWhatsapp           = link;
            a.ContactoActualizadoEn  = ahora;
            a.ContactoActualizadoPor = usuario;
            escritos++;
        }
        await _db.SaveChangesAsync(ct);
        return escritos;
    }

    // ── Paso 1: validar ─────────────────────────────────────────────────────

    public async Task<ValidarArchivoSmsResponse> ValidarAsync(EntradaSms entrada, CancellationToken ct = default)
    {
        await ExigirLiderActivoAsync(entrada.LiderID, ct);
        var d = await DepurarAsync(entrada, ct);
        return d.AResponse();
    }

    // ── Paso 3: catálogo anotado ────────────────────────────────────────────

    public async Task<CatalogoMensajesResponse> MensajesDisponiblesAsync(
        EntradaSms entrada, string huellaEsperada, IReadOnlyList<string> links, CancellationToken ct = default)
    {
        await ExigirLiderActivoAsync(entrada.LiderID, ct);
        var d = await DepurarAsync(entrada, ct);
        ExigirMismaHuella(d.Huella, huellaEsperada);

        var linkMax = links.Select(l => (l ?? string.Empty).Trim().Length).DefaultIfEmpty(0).Max();
        var mensajes = await _db.MensajesAutorizados.AsNoTracking()
            .Where(m => m.Activo)
            .OrderBy(m => m.Titulo)
            .ToListAsync(ct);

        var respuesta = new CatalogoMensajesResponse
        {
            HuellaArchivo  = d.Huella,
            TotalTitulares = d.Validas.Count,
            LinkMasLargo   = linkMax,
        };

        foreach (var m in mensajes)
        {
            var (excedidos, longitudMax) = CalcularExcedidos(d.Validas, m.Texto, linkMax);
            var sinColumna = VariablesSinColumna(m.Texto, d.ColumnasOpcionalesFaltantes);
            var dto = new MensajeDisponibleDto
            {
                ID             = m.ID,
                Titulo         = m.Titulo,
                Texto          = m.Texto,
                LongitudBase   = CatalogoCreadorVirtualidad.LongitudBase(m.Texto),
                LinkMasLargo   = linkMax,
                LongitudMaxima = longitudMax,
                TotalExcedidos = excedidos.Count,
                Excedidos      = excedidos.Take(CatalogoCreadorVirtualidad.MaxExcedidosEnRespuesta).ToList(),
            };
            (dto.Estado, dto.Motivo) = ClasificarMensaje(excedidos.Count);
            if (sinColumna.Count > 0)
            {
                // Usa una variable cuya columna no vino en el archivo: no se puede armar el SMS.
                dto.VariablesSinColumna = sinColumna;
                dto.Estado = EstadoMensajeDisponible.Deshabilitado;
                dto.Motivo = $"El archivo no trae la columna {string.Join(" ni ", sinColumna.Select(p => CatalogoCreadorVirtualidad.PlaceholderPorColumnaOpcional[p]))} que este mensaje necesita ({string.Join(", ", sinColumna)}).";
            }
            respuesta.Mensajes.Add(dto);
        }
        return respuesta;
    }

    /// <summary>Placeholders del mensaje cuya columna opcional no vino en el archivo.</summary>
    private static List<string> VariablesSinColumna(string texto, List<string> columnasFaltantes) =>
        CatalogoCreadorVirtualidad.PlaceholderPorColumnaOpcional
            .Where(kv => columnasFaltantes.Contains(kv.Value) && CatalogoCreadorVirtualidad.UsaPlaceholder(texto, kv.Key))
            .Select(kv => kv.Key)
            .ToList();

    private static (EstadoMensajeDisponible Estado, string? Motivo) ClasificarMensaje(int excedidos)
    {
        if (excedidos == 0) return (EstadoMensajeDisponible.Disponible, null);
        if (excedidos <= CatalogoCreadorVirtualidad.UmbralAdvertencia)
            return (EstadoMensajeDisponible.Advertencia,
                    $"Puede utilizarlo pero se pasa en {excedidos} titular{(excedidos == 1 ? "" : "es")}; " +
                    $"se excluir{(excedidos == 1 ? "á" : "án")} del archivo.");
        return (EstadoMensajeDisponible.Deshabilitado,
                $"Se pasa de {CatalogoCreadorVirtualidad.LimiteSms} caracteres en {excedidos} titulares " +
                $"(máximo permitido: {CatalogoCreadorVirtualidad.UmbralAdvertencia}).");
    }

    /// <summary>
    /// Titulares cuyo SMS supera el límite en el peor caso (celular de 10 + link más largo).
    /// Ordenados de mayor a menor longitud para que "cuáles" muestre primero los peores.
    /// </summary>
    private static (List<TitularExcedidoDto> Excedidos, int LongitudMaxima) CalcularExcedidos(
        List<FilaSmsDto> filas, string plantilla, int linkMax)
    {
        var linkPeor = new string('x', linkMax);
        var excedidos = new List<TitularExcedidoDto>();
        var maxima = 0;
        foreach (var f in filas)
        {
            var len = CatalogoCreadorVirtualidad.LongitudPeorCaso(plantilla, f.Nombre, f.Cartera, f.Empresa, linkPeor);
            if (len > maxima) maxima = len;
            if (len > CatalogoCreadorVirtualidad.LimiteSms)
                excedidos.Add(new TitularExcedidoDto { Cedula = f.Cedula, Nombre = f.Nombre, Longitud = len });
        }
        excedidos.Sort((a, b) => b.Longitud.CompareTo(a.Longitud));
        return (excedidos, maxima);
    }

    // ── Paso 4: generar ─────────────────────────────────────────────────────

    public async Task<ResultadoGeneracionSms> GenerarAsync(
        EntradaSms entrada, string huellaEsperada, GenerarEstrategiaSmsRequest request, string usuario,
        CancellationToken ct = default)
    {
        await ExigirLiderActivoAsync(entrada.LiderID, ct);

        var asesores = request.Asesores
            .Where(a => EsCelularValido(a.NumeroCorporativo) && !string.IsNullOrWhiteSpace(a.LinkWhatsapp))
            .Select(a => (Celular: a.NumeroCorporativo.Trim(), Link: a.LinkWhatsapp.Trim()))
            .ToList();
        if (asesores.Count == 0)
            throw new InvalidOperationException("Debes indicar al menos una fila con número válido (10 dígitos, empieza en 3 o en 6) y link.");

        var mensaje = await _db.MensajesAutorizados.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ID == request.MensajeAutorizadoID, ct)
            ?? throw new KeyNotFoundException("El mensaje autorizado no existe.");
        if (!mensaje.Activo)
            throw new InvalidOperationException("El mensaje autorizado está inactivo. Elige otro.");

        var d = await DepurarAsync(entrada, ct);
        ExigirMismaHuella(d.Huella, huellaEsperada);

        // Mismo cálculo que el paso 3: peor link. Así el N que vio el líder es el N borrado.
        var linkMax = asesores.Max(a => a.Link.Length);
        var faltan = VariablesSinColumna(mensaje.Texto, d.ColumnasOpcionalesFaltantes);
        if (faltan.Count > 0)
            throw new InvalidOperationException(
                $"El archivo no trae la columna que necesita {string.Join(" y ", faltan)}. Agrega la columna o elige otro mensaje.");

        var (excedidos, _) = CalcularExcedidos(d.Validas, mensaje.Texto, linkMax);
        if (excedidos.Count > CatalogoCreadorVirtualidad.UmbralAdvertencia)
            throw new InvalidOperationException(
                $"Este mensaje se pasa de {CatalogoCreadorVirtualidad.LimiteSms} caracteres en {excedidos.Count} titulares " +
                $"(máximo permitido: {CatalogoCreadorVirtualidad.UmbralAdvertencia}). Elige otro mensaje.");

        var cedulasExcedidas = excedidos.Select(e => e.Cedula).ToHashSet();
        var titulares = d.Validas.Where(f => !cedulasExcedidas.Contains(f.Cedula)).ToArray();

        // Reparto equitativo aleatorio: barajar titulares y repartir en ronda. Con N asesores,
        // ningún asesor recibe más de 1 titular que otro — un sorteo puro sí desbalancea.
        Random.Shared.Shuffle(titulares);
        var filas = new List<FilaSmsGeneradaDto>(titulares.Length);
        for (var i = 0; i < titulares.Length; i++)
        {
            var t = titulares[i];
            var (celular, link) = asesores[i % asesores.Count];
            filas.Add(new FilaSmsGeneradaDto
            {
                Cedula        = t.Cedula,
                Nombre        = t.Nombre,
                FechaGestion  = t.FechaGestion,
                UltimoPerfil  = t.UltimoPerfil,
                NumeroMarcado = t.NumeroMarcado,
                Cartera       = t.Cartera,
                Empresa       = t.Empresa,
                CelularAsesor = celular,
                LinkAsesor    = link,
                Mensaje       = CatalogoCreadorVirtualidad.ArmarMensaje(mensaje.Texto, t.Nombre, t.Cartera, t.Empresa, celular, link),
            });
        }
        // El archivo se ordena por cédula para que sea legible; el sorteo ya quedó en la asignación.
        filas.Sort((a, b) => a.Cedula.CompareTo(b.Cedula));

        var xlsx = _exporter.GenerarEstrategiaSms(filas);

        var estrategia = new EstrategiaSms
        {
            LiderID             = entrada.LiderID,
            UsuarioGenero       = usuario,
            FechaGeneracion     = DateTime.Now,
            MensajeAutorizadoID = mensaje.ID,
            TotalLeidos         = d.TotalLeidas,
            TotalGenerados      = filas.Count,
            ExcluidosPerfil     = d.ExcluidosPerfilArchivo + d.ExcluidosPerfilBD,
            ExcluidosListaNegra = d.ExcluidosListaNegraTabla + d.ExcluidosListaNegraArchivo,
            ExcluidosLongitud   = excedidos.Count,
            ExcluidosOtros      = d.ExcluidosCedulaInvalida + d.ExcluidosTelefonoInvalido + d.ExcluidosFijo
                                + d.ExcluidosNombreVacio + d.ExcluidosDuplicado + d.ExcluidosFilaEjemplo,
            HuellaArchivo       = d.Huella,
            NombreArchivo       = Path.GetFileName(entrada.NombreArchivo),
        };
        _db.EstrategiasSms.Add(estrategia);
        await _db.SaveChangesAsync(ct);

        var nombre = $"estrategia_sms_{entrada.LiderID}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return new ResultadoGeneracionSms(xlsx, estrategia.ID, nombre, filas.Count, excedidos.Count);
    }

    // ── Pipeline de depuración (compartido) ─────────────────────────────────

    internal sealed class Depuracion
    {
        public string  Huella           = string.Empty;
        public string? HuellaListaNegra;
        public int TotalLeidas;
        public int ExcluidosCedulaInvalida, ExcluidosTelefonoInvalido, ExcluidosFijo, ExcluidosNombreVacio;
        public int ExcluidosPerfilArchivo, ExcluidosPerfilBD;
        public int ExcluidosListaNegraTabla, ExcluidosListaNegraArchivo, ExcluidosDuplicado;
        public int ExcluidosFilaEjemplo;
        public int TotalNumerosListaNegraArchivo;
        public List<string>  ColumnasEncontradas = [];
        public List<string>  ColumnasFaltantes   = [];
        public List<string>  ColumnasOpcionalesFaltantes = [];
        public List<ErrorFila> Errores           = [];
        public List<FilaSmsDto> Validas          = [];

        /// <summary>El archivo entero no se puede procesar (vacío o sin columnas obligatorias).</summary>
        public bool ArchivoRechazado;

        /// <summary>
        /// Suma de todas las exclusiones por fila. Como cada fila tiene UN solo motivo,
        /// <c>TotalLeidas − Validas.Count</c> (al terminar) es exactamente este número.
        /// </summary>
        public int TotalExcluidos =>
            ExcluidosCedulaInvalida + ExcluidosTelefonoInvalido + ExcluidosFijo + ExcluidosNombreVacio
            + ExcluidosPerfilArchivo + ExcluidosPerfilBD + ExcluidosListaNegraTabla + ExcluidosListaNegraArchivo
            + ExcluidosDuplicado + ExcluidosFilaEjemplo;

        /// <summary>Registra una exclusión con su motivo, la línea y el dato que la identifica.</summary>
        public void Excluir(string motivo, int linea, string mensaje, string? detalle = null) =>
            Errores.Add(new ErrorFila(motivo, linea, mensaje, detalle));

        public ValidarArchivoSmsResponse AResponse() => new()
        {
            HuellaArchivo               = Huella,
            HuellaListaNegra            = HuellaListaNegra,
            TotalLeidas                 = TotalLeidas,
            TotalValidas                = Validas.Count,
            ExcluidosCedulaInvalida     = ExcluidosCedulaInvalida,
            ExcluidosTelefonoInvalido   = ExcluidosTelefonoInvalido,
            ExcluidosFijo               = ExcluidosFijo,
            ExcluidosNombreVacio        = ExcluidosNombreVacio,
            ExcluidosPerfilArchivo      = ExcluidosPerfilArchivo,
            ExcluidosPerfilBD           = ExcluidosPerfilBD,
            ExcluidosListaNegraTabla    = ExcluidosListaNegraTabla,
            ExcluidosListaNegraArchivo  = ExcluidosListaNegraArchivo,
            ExcluidosDuplicado          = ExcluidosDuplicado,
            ExcluidosFilaEjemplo        = ExcluidosFilaEjemplo,
            TotalNumerosListaNegraArchivo = TotalNumerosListaNegraArchivo,
            ColumnasEncontradas         = ColumnasEncontradas,
            ColumnasFaltantes           = ColumnasFaltantes,
            ColumnasOpcionalesFaltantes = ColumnasOpcionalesFaltantes,
            Muestra                     = Validas.Take(CatalogoCreadorVirtualidad.FilasMuestra).ToList(),
            ErroresAgrupados            = AgruparErrores(Errores),
        };
    }

    /// <summary>Fila candidata mientras pasa por las exclusiones que necesitan BD.</summary>
    internal sealed record Candidata(int Linea, FilaSmsDto Fila);

    /// <summary>
    /// Una exclusión concreta. Se guarda la línea y el dato que identifica la fila para
    /// poder mostrar ejemplos al desplegar el conteo; el motivo es lo que agrupa.
    /// </summary>
    internal sealed record ErrorFila(string Motivo, int Linea, string Mensaje, string? Detalle);

    private async Task<Depuracion> DepurarAsync(EntradaSms entrada, CancellationToken ct)
    {
        var (huella, filas) = await LeerArchivoAsync(entrada.Archivo, entrada.NombreArchivo);
        var (d, candidatas) = DepurarLocal(filas);
        d.Huella = huella;
        if (d.ArchivoRechazado) return d;

        // ── Lista negra por archivo (opcional): se lee antes del cruce para tener el set listo ──
        var negraArchivo = new HashSet<string>(StringComparer.Ordinal);
        if (entrada.ArchivoListaNegra is not null)
        {
            var (huellaLn, numerosLn, errorLn) = await LeerListaNegraArchivoAsync(entrada.ArchivoListaNegra, entrada.NombreArchivoListaNegra ?? "lista_negra.xlsx");
            d.HuellaListaNegra = huellaLn;
            negraArchivo = numerosLn;
            d.TotalNumerosListaNegraArchivo = numerosLn.Count;
            if (errorLn is not null) d.Excluir(M.ListaNegraArchivo, 0, errorLn);
        }

        if (candidatas.Count == 0) return d;

        // ── Exclusiones contra BD (un viaje cada una) ──
        var cedulasPerfilBD = await TitularesConPerfilExcluidoAsync(candidatas.Select(c => c.Fila.Cedula).Distinct().ToList(), ct);
        var numerosNegraBD  = entrada.CarterasListaNegra.Count == 0
            ? []
            : await NumerosEnListaNegraAsync(candidatas.Select(c => c.Fila.NumeroMarcado).Distinct().ToList(), entrada.CarterasListaNegra, ct);

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in candidatas)
        {
            var f = c.Fila;
            var quien = $"cédula {f.Cedula} · {f.NumeroMarcado}";
            if (cedulasPerfilBD.Contains(f.Cedula))
            {
                d.ExcluidosPerfilBD++;
                d.Excluir(M.PerfilBD, c.Linea,
                          "Titular con perfil no contactable en el histórico de gestiones (Paz y salvo, PQR, No gestionar, Fallecido…).", quien);
                continue;
            }
            if (numerosNegraBD.Contains(f.NumeroMarcado))
            {
                d.ExcluidosListaNegraTabla++;
                d.Excluir(M.ListaNegraTabla, c.Linea, "Número en lista negra (carteras seleccionadas).", quien);
                continue;
            }
            if (negraArchivo.Contains(f.NumeroMarcado))
            {
                d.ExcluidosListaNegraArchivo++;
                d.Excluir(M.ListaNegraArchivo, c.Linea, "Número en la lista negra cargada por archivo.", quien);
                continue;
            }
            if (!vistos.Add(f.NumeroMarcado))
            {
                d.ExcluidosDuplicado++;
                d.Excluir(M.Duplicado, c.Linea, "Número marcado repetido en el archivo (se conserva la primera fila).", quien);
                continue;
            }
            d.Validas.Add(f);
        }
        return d;
    }

    /// <summary>
    /// Primera mitad del pipeline: columnas y exclusiones que no necesitan BD, en orden
    /// fijo. Es pura (solo las filas ya leídas) para poder probarla sin base de datos.
    /// Devuelve las filas que siguen en carrera para el cruce contra BD.
    /// </summary>
    internal static (Depuracion D, List<Candidata> Candidatas) DepurarLocal(List<string[]> filas)
    {
        var d = new Depuracion();
        var candidatas = new List<Candidata>(filas.Count);

        if (filas.Count == 0)
        {
            d.ArchivoRechazado = true;
            d.Excluir(M.Archivo, 0, "El archivo está vacío.");
            return (d, candidatas);
        }

        // ── Columnas: por nombre, sin adivinar ──
        var headers = ArchivoTabular.LeerHeaders(filas[0]);
        var iCedula   = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.Cedula);
        var iNombre   = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.Nombre);
        var iFecha    = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.FechaGestion);
        var iPerfil   = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.UltimoPerfil);
        var iNumero   = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.NumeroMarcado);
        var iCartera  = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.Cartera);
        var iEmpresa  = ArchivoTabular.ResolverColumna(headers, CatalogoCreadorVirtualidad.AliasSms.Empresa);
        if (iCartera == ArchivoTabular.ColumnaNoEncontrada) d.ColumnasOpcionalesFaltantes.Add("Cartera");
        if (iEmpresa == ArchivoTabular.ColumnaNoEncontrada) d.ColumnasOpcionalesFaltantes.Add("Empresa");

        d.ColumnasEncontradas = filas[0].Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        var esperadas = CatalogoCreadorVirtualidad.ColumnasPlantilla;
        var indices   = new[] { iCedula, iNombre, iFecha, iPerfil, iNumero };
        for (var i = 0; i < indices.Length; i++)
            if (indices[i] == ArchivoTabular.ColumnaNoEncontrada) d.ColumnasFaltantes.Add(esperadas[i]);
        if (d.ColumnasFaltantes.Count > 0)
        {
            d.ArchivoRechazado = true;
            d.Excluir(M.Archivo, 0, $"Faltan columnas obligatorias: {string.Join(", ", d.ColumnasFaltantes)}.");
            return (d, candidatas);
        }

        // ── Exclusiones locales (orden fijo) ──
        var totalColumnas = filas[0].Length;
        for (var i = 1; i < filas.Count; i++)
        {
            var partes = filas[i];
            if (partes.All(string.IsNullOrWhiteSpace)) continue;
            d.TotalLeidas++;
            var linea = i + 1;

            // El CSV se parte con Split plano: una coma dentro de un nombre corre la fila.
            if (partes.Length < totalColumnas)
            {
                d.ExcluidosCedulaInvalida++;
                d.Excluir(M.CedulaInvalida, linea, "La fila tiene menos columnas que el encabezado (¿una coma dentro de un campo?).");
                continue;
            }

            var cedulaCruda = ColapsarEspacios(Cel(partes, iCedula));
            var cedulaTxt = SoloDigitos(QuitarSufijoDecimal(cedulaCruda));
            if (cedulaTxt.Length < 5 || !long.TryParse(cedulaTxt, out var cedula) || cedula <= 0)
            {
                d.ExcluidosCedulaInvalida++;
                d.Excluir(M.CedulaInvalida, linea, "Cédula inválida (vacía, sin dígitos o demasiado corta).",
                          cedulaCruda.Length > 0 ? $"cédula '{Recortar(cedulaCruda)}'" : "cédula vacía");
                continue;
            }

            var numeroCrudo = ColapsarEspacios(Cel(partes, iNumero));
            var (numero, esCelular) = TelefonoColombiano.Normalizar(numeroCrudo);
            if (numero is null)
            {
                d.ExcluidosTelefonoInvalido++;
                d.Excluir(M.TelefonoInvalido, linea, "Número marcado inválido (no son 10 dígitos).",
                          $"cédula {cedula} · '{Recortar(numeroCrudo)}'");
                continue;
            }

            // La fila de ejemplo de las plantillas viejas (venía en la fila 2 de BASE). Su
            // cédula y su número son válidos, así que sin esta regla pasaba todos los filtros
            // y salía un SMS. Se compara ya normalizado (cédula como número, celular nacional)
            // para atraparla aunque Excel la haya guardado como 1020304050.0 o con +57.
            if (CatalogoCreadorVirtualidad.FilaEjemplo.Es(cedula.ToString(CultureInfo.InvariantCulture), numero))
            {
                d.ExcluidosFilaEjemplo++;
                d.Excluir(M.FilaEjemplo, linea, CatalogoCreadorVirtualidad.FilaEjemplo.MensajeExclusion,
                          $"cédula {cedula} · {numero}");
                continue;
            }

            if (!esCelular)
            {
                d.ExcluidosFijo++;
                d.Excluir(M.Fijo, linea, "Número fijo: los SMS solo van a celulares.", $"cédula {cedula} · {numero}");
                continue;
            }

            var nombre = NormalizadorNombre.Limpiar(Cel(partes, iNombre));
            if (nombre.Length == 0)
            {
                d.ExcluidosNombreVacio++;
                d.Excluir(M.NombreVacio, linea, "Nombre vacío después de la limpieza.", $"cédula {cedula} · {numero}");
                continue;
            }

            var perfilBruto = ColapsarEspacios(Cel(partes, iPerfil));
            if (CatalogoCreadorVirtualidad.PerfilesExcluidosClave.Contains(CatalogoCreadorVirtualidad.ClaveComparacion(perfilBruto)))
            {
                d.ExcluidosPerfilArchivo++;
                d.Excluir(M.PerfilArchivo, linea, $"Perfil '{perfilBruto}' no se puede contactar (según el archivo).",
                          $"cédula {cedula} · {numero}");
                continue;
            }

            var fechaTxt = ColapsarEspacios(Cel(partes, iFecha));
            if (ArchivoTabular.TryParseFecha(fechaTxt, out var fecha)) fechaTxt = fecha.ToString("yyyy-MM-dd");

            candidatas.Add(new Candidata(linea, new FilaSmsDto
            {
                Cedula        = cedula,
                Nombre        = nombre,
                FechaGestion  = fechaTxt,
                UltimoPerfil  = perfilBruto,
                NumeroMarcado = numero,
                Cartera       = iCartera >= 0 ? NormalizadorNombre.LimpiarTexto(Cel(partes, iCartera)) : string.Empty,
                Empresa       = iEmpresa >= 0 ? NormalizadorNombre.LimpiarTexto(Cel(partes, iEmpresa)) : string.Empty,
            }));
        }
        return (d, candidatas);
    }

    // ── Consultas contra BD ─────────────────────────────────────────────────

    /// <summary>
    /// Titulares con algún registro de perfil excluido en HistoricoEstadoTitular. #temp +
    /// hash join (un solo scan de la tabla, ~3M filas). COLLATE CI_AI: sin mayúsculas ni
    /// tildes, porque en BD conviven "No gestionar" y "NO GESTIONAR".
    /// </summary>
    private async Task<HashSet<long>> TitularesConPerfilExcluidoAsync(List<long> cedulas, CancellationToken ct)
    {
        var resultado = new HashSet<long>();
        if (cedulas.Count == 0) return resultado;

        var perfiles = CatalogoCreadorVirtualidad.PerfilesExcluidosSms;
        var parametros = string.Join(", ", perfiles.Select((_, i) => $"@p{i}"));
        var sql = $"""
            SELECT DISTINCT t.TitularID
              FROM #Titulares t
              JOIN dbo.HistoricoEstadoTitular h ON h.TitularID = t.TitularID
             WHERE h.UltimoPerfil IS NOT NULL
               AND h.UltimoPerfil COLLATE Latin1_General_CI_AI IN ({parametros})
            OPTION (HASH JOIN);
            """;

        await EjecutarConTempAsync(
            "CREATE TABLE #Titulares (TitularID bigint NOT NULL PRIMARY KEY CLUSTERED);",
            "#Titulares", "TitularID", typeof(long), cedulas.Cast<object>(),
            sql,
            cmd => { for (var i = 0; i < perfiles.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", perfiles[i]); },
            rd => resultado.Add(rd.GetInt64(0)),
            ct);
        return resultado;
    }

    /// <summary>
    /// Números presentes en ListaNegraTelefonos para las carteras elegidas. Cartera se
    /// compara tal cual está en BD (con guiones bajos); la UI solo cambia la etiqueta.
    /// </summary>
    private async Task<HashSet<string>> NumerosEnListaNegraAsync(
        List<string> numeros, IReadOnlyList<string> carteras, CancellationToken ct)
    {
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        if (numeros.Count == 0 || carteras.Count == 0) return resultado;

        const string sql = """
            SELECT DISTINCT ln.NumeroTelefono
              FROM #Telefonos t
              JOIN dbo.ListaNegraTelefonos ln ON ln.NumeroTelefono = t.Numero
              JOIN #CarterasLN c             ON c.Cartera = ln.Cartera
            OPTION (HASH JOIN);
            """;

        var conn = (SqlConnection)_db.Database.GetDbConnection();
        var laAbriYo = conn.State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using (var crear = new SqlCommand("""
                CREATE TABLE #Telefonos  (Numero  varchar(10) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY CLUSTERED);
                CREATE TABLE #CarterasLN (Cartera varchar(50) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY CLUSTERED);
                """, conn))
                await crear.ExecuteNonQueryAsync(ct);

            await BulkAsync(conn, "#Telefonos", "Numero", typeof(string), numeros.Cast<object>(), ct);
            await BulkAsync(conn, "#CarterasLN", "Cartera", typeof(string),
                carteras.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().Cast<object>(), ct);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            await using var rd  = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) resultado.Add(rd.GetString(0));
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
        return resultado;
    }

    private async Task EjecutarConTempAsync(
        string ddlTemp, string tabla, string columna, Type tipo, IEnumerable<object> valores,
        string sql, Action<SqlCommand> parametros, Action<SqlDataReader> leer, CancellationToken ct)
    {
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        // Solo cerramos si la abrimos nosotros: cerrarla tiraría las #temp de otro código de la request.
        var laAbriYo = conn.State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using (var crear = new SqlCommand(ddlTemp, conn))
                await crear.ExecuteNonQueryAsync(ct);

            await BulkAsync(conn, tabla, columna, tipo, valores, ct);

            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            parametros(cmd);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) leer(rd);
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    private static async Task BulkAsync(
        SqlConnection conn, string tabla, string columna, Type tipo, IEnumerable<object> valores, CancellationToken ct)
    {
        var dt = new DataTable();
        dt.Columns.Add(columna, tipo);
        foreach (var v in valores) dt.Rows.Add(v);

        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = tabla,
            BatchSize            = 10_000,
            BulkCopyTimeout      = 120,
        };
        bulk.ColumnMappings.Add(columna, columna);
        await bulk.WriteToServerAsync(dt, ct);
    }

    // ── Lectura de archivos ─────────────────────────────────────────────────

    private static async Task<(string Huella, List<string[]> Filas)> LeerArchivoAsync(Stream archivo, string nombreArchivo)
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

    /// <summary>
    /// XLSX/CSV opcional con números a excluir en esta estrategia.
    ///
    /// Se toma SIEMPRE la PRIMERA COLUMNA, sin mirar el encabezado. Es lo contrario de lo
    /// que hace el archivo principal (ADR-004: resolver por nombre, no adivinar), y es
    /// deliberado: una lista negra es una columna de números pegada de donde sea —una
    /// consulta, un correo, otro Excel— y exigirle un encabezado de una lista cerrada
    /// rechazaba archivos perfectamente válidos. Acá no hay ambigüedad que resolver: solo
    /// hay una cosa que leer.
    ///
    /// Se recorre desde la fila 0 porque puede no haber encabezado. Si lo hay, no parsea
    /// como teléfono y se descarta solo, sin necesidad de detectarlo.
    ///
    /// Devuelve un mensaje de error en vez de lanzar: un archivo de lista negra malo no
    /// debe tumbar la validación principal.
    /// </summary>
    private static async Task<(string Huella, HashSet<string> Numeros, string? Error)> LeerListaNegraArchivoAsync(
        Stream archivo, string nombre)
    {
        var numeros = new HashSet<string>(StringComparer.Ordinal);
        var (huella, filas) = await LeerArchivoAsync(archivo, nombre);
        if (filas.Count == 0) return (huella, numeros, "La lista negra por archivo está vacía.");

        for (var i = 0; i < filas.Count; i++)
        {
            if (filas[i].Length == 0) continue;
            var (n, _) = TelefonoColombiano.Normalizar(filas[i][0]);
            if (n is not null) numeros.Add(n);
        }

        if (numeros.Count == 0)
        {
            var primera = filas[0].Length > 0 ? filas[0][0].Trim() : string.Empty;
            return (huella, numeros,
                $"Lista negra por archivo: la primera columna no trae ningún teléfono válido de 10 dígitos" +
                (primera.Length > 0 ? $" (el primer valor era '{Recortar(primera)}')." : "."));
        }
        return (huella, numeros, null);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task ExigirLiderActivoAsync(int liderID, CancellationToken ct)
    {
        if (!await _db.Lideres.AnyAsync(l => l.ID == liderID && l.Activo, ct))
            throw new KeyNotFoundException($"El líder {liderID} no existe o está inactivo.");
    }

    private static void ExigirMismaHuella(string huella, string esperada)
    {
        if (!string.Equals(huella, esperada, StringComparison.OrdinalIgnoreCase))
            throw new EstrategiaSmsConflictoException(
                "El archivo cambió desde que se validó. Vuelve a cargarlo desde el paso 1.");
    }

    /// <summary>
    /// Número desde el que virtualidad distribuye. Acepta móvil (3…) y fijo nacional (6…),
    /// los dos de 10 dígitos: hay asesores que atienden desde una línea fija corporativa y
    /// exigir el 3 los dejaba afuera. NO se reutiliza TelefonoColombiano.NormalizarCelular
    /// porque ese sí filtra fijos a propósito, para las columnas de celular de las fuentes.
    /// </summary>
    private static readonly Regex CelularRegex = new(@"^[36]\d{9}$", RegexOptions.Compiled);
    private static bool EsCelularValido(string? s) => s is not null && CelularRegex.IsMatch(s.Trim());

    private static string Cel(string[] partes, int i) => i >= 0 && i < partes.Length ? partes[i] : string.Empty;

    private static string SoloDigitos(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string QuitarSufijoDecimal(string s)
    {
        var t = s.Trim();
        return t.EndsWith(".0", StringComparison.Ordinal) ? t[..^2] : t;
    }

    private static readonly Regex Espacios = new(@"\s+", RegexOptions.Compiled);
    private static string ColapsarEspacios(string s) => Espacios.Replace(s ?? string.Empty, " ").Trim();

    /// <summary>
    /// Agrupa por (motivo, mensaje) y conserva hasta diez ejemplos concretos por grupo.
    /// Se agrupa también por mensaje y no solo por motivo para no fundir en un renglón
    /// perfiles distintos: "Perfil 'PQR'…" y "Perfil 'Fallecido'…" comparten motivo pero
    /// son cosas distintas para quien lee.
    /// </summary>
    private static List<ErrorAgrupadoDto> AgruparErrores(IEnumerable<ErrorFila> errores) =>
        errores
            .GroupBy(e => new { e.Motivo, e.Mensaje })
            .Select(g => new ErrorAgrupadoDto
            {
                Motivo   = g.Key.Motivo,
                Mensaje  = g.Key.Mensaje,
                Cantidad = g.Count(),
                // Línea 0 = problema del archivo entero (vacío, columnas), no de una fila:
                // ahí no hay número de línea que mostrar.
                Ejemplos = g.OrderBy(e => e.Linea)
                            .Take(CatalogoCreadorVirtualidad.EjemplosPorGrupo)
                            .Select(e => e.Linea <= 0
                                ? (e.Detalle ?? string.Empty)
                                : e.Detalle is null ? $"Línea {e.Linea}" : $"Línea {e.Linea} · {e.Detalle}")
                            .Where(s => s.Length > 0)
                            .ToList(),
            })
            .OrderByDescending(e => e.Cantidad)
            .ToList();

    /// <summary>Corta un valor del archivo para que un campo basura no infle la respuesta.</summary>
    private static string Recortar(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
