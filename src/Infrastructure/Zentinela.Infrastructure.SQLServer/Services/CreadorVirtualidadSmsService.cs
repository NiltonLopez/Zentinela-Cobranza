using System.Data;
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

    public byte[] GenerarPlantilla()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("BASE");
        var columnas = CatalogoCreadorVirtualidad.ColumnasPlantilla;
        for (var i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        object[] ejemplo = ["1020304050", "MARIA JOSE PEREZ GOMEZ", "2026-09-01", "No contacto", "3001234567", "Pelicano Propia", "Banco Bellavida"];
        for (var i = 0; i < ejemplo.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = ejemplo[i].ToString();
            c.Style.Font.FontColor = XLColor.Gray;
        }
        // Cédula y número como texto: evita que Excel los convierta a 1,02E+09.
        ws.Column(1).Style.NumberFormat.Format = "@";
        ws.Column(5).Style.NumberFormat.Format = "@";
        // Cartera y Empresa son opcionales: se marcan en la cabecera.
        ws.Cell(1, 6).Value = "Cartera (opcional)";
        ws.Cell(1, 7).Value = "Empresa (opcional)";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
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
    /// Agente no tiene nombre ni teléfono: el nombre sale de RRHH (otro DbContext → join en
    /// memoria por NumeroDocumento) y el contacto de AgenteContactoVirtualidad.
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

        var contactos = await _db.AgenteContactosVirtualidad
            .AsNoTracking()
            .Where(c => agenteIDs.Contains(c.AgenteID))
            .ToDictionaryAsync(c => c.AgenteID, ct);

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

    public async Task<int> GuardarContactosAsync(
        IReadOnlyList<ContactoAsesorRequest> contactos, string usuario, CancellationToken ct = default)
    {
        var validos = contactos.Where(c => EsCelularValido(c.NumeroCorporativo) && !string.IsNullOrWhiteSpace(c.LinkWhatsapp)).ToList();
        if (validos.Count == 0) return 0;

        var ids = validos.Select(c => c.AgenteID).Distinct().ToList();
        var existentes = await _db.AgenteContactosVirtualidad
            .Where(c => ids.Contains(c.AgenteID))
            .ToDictionaryAsync(c => c.AgenteID, ct);
        var agentesReales = await _db.Agentes.Where(a => ids.Contains(a.AgenteID)).Select(a => a.AgenteID).ToHashSetAsync(ct);

        var ahora = DateTime.Now;
        var escritos = 0;
        foreach (var c in validos)
        {
            if (!agentesReales.Contains(c.AgenteID)) continue; // la FK lo rechazaría igual
            var link = c.LinkWhatsapp.Trim();
            if (link.Length > 100) link = link[..100];

            if (existentes.TryGetValue(c.AgenteID, out var e))
            {
                e.NumeroCorporativo    = c.NumeroCorporativo.Trim();
                e.LinkWhatsapp         = link;
                e.FechaActualizacion   = ahora;
                e.UsuarioActualizacion = usuario;
            }
            else
            {
                _db.AgenteContactosVirtualidad.Add(new AgenteContactoVirtualidad
                {
                    AgenteID             = c.AgenteID,
                    NumeroCorporativo    = c.NumeroCorporativo.Trim(),
                    LinkWhatsapp         = link,
                    FechaActualizacion   = ahora,
                    UsuarioActualizacion = usuario,
                });
            }
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
            throw new InvalidOperationException("Debes indicar al menos un asesor con celular (10 dígitos, empieza en 3) y link.");

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
                                + d.ExcluidosNombreVacio + d.ExcluidosDuplicado,
            HuellaArchivo       = d.Huella,
            NombreArchivo       = Path.GetFileName(entrada.NombreArchivo),
        };
        _db.EstrategiasSms.Add(estrategia);
        await _db.SaveChangesAsync(ct);

        var nombre = $"estrategia_sms_{entrada.LiderID}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return new ResultadoGeneracionSms(xlsx, estrategia.ID, nombre, filas.Count, excedidos.Count);
    }

    // ── Pipeline de depuración (compartido) ─────────────────────────────────

    private sealed class Depuracion
    {
        public string  Huella           = string.Empty;
        public string? HuellaListaNegra;
        public int TotalLeidas;
        public int ExcluidosCedulaInvalida, ExcluidosTelefonoInvalido, ExcluidosFijo, ExcluidosNombreVacio;
        public int ExcluidosPerfilArchivo, ExcluidosPerfilBD;
        public int ExcluidosListaNegraTabla, ExcluidosListaNegraArchivo, ExcluidosDuplicado;
        public int TotalNumerosListaNegraArchivo;
        public List<string>  ColumnasEncontradas = [];
        public List<string>  ColumnasFaltantes   = [];
        public List<string>  ColumnasOpcionalesFaltantes = [];
        public List<string>  Errores             = [];
        public List<FilaSmsDto> Validas          = [];

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
            TotalNumerosListaNegraArchivo = TotalNumerosListaNegraArchivo,
            ColumnasEncontradas         = ColumnasEncontradas,
            ColumnasFaltantes           = ColumnasFaltantes,
            ColumnasOpcionalesFaltantes = ColumnasOpcionalesFaltantes,
            Muestra                     = Validas.Take(CatalogoCreadorVirtualidad.FilasMuestra).ToList(),
            ErroresAgrupados            = AgruparErrores(Errores),
        };
    }

    /// <summary>Fila candidata mientras pasa por las exclusiones que necesitan BD.</summary>
    private sealed record Candidata(int Linea, FilaSmsDto Fila);

    private async Task<Depuracion> DepurarAsync(EntradaSms entrada, CancellationToken ct)
    {
        var d = new Depuracion();
        var (huella, filas) = await LeerArchivoAsync(entrada.Archivo, entrada.NombreArchivo);
        d.Huella = huella;

        if (filas.Count == 0)
        {
            d.Errores.Add("El archivo está vacío.");
            return d;
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
            d.Errores.Add($"Faltan columnas obligatorias: {string.Join(", ", d.ColumnasFaltantes)}.");
            return d;
        }

        // ── Lista negra por archivo (opcional): se lee antes para tener el set listo ──
        var negraArchivo = new HashSet<string>(StringComparer.Ordinal);
        if (entrada.ArchivoListaNegra is not null)
        {
            var (huellaLn, numerosLn, errorLn) = await LeerListaNegraArchivoAsync(entrada.ArchivoListaNegra, entrada.NombreArchivoListaNegra ?? "lista_negra.xlsx");
            d.HuellaListaNegra = huellaLn;
            negraArchivo = numerosLn;
            d.TotalNumerosListaNegraArchivo = numerosLn.Count;
            if (errorLn is not null) d.Errores.Add(errorLn);
        }

        // ── Exclusiones locales (orden fijo) ──
        var candidatas = new List<Candidata>(filas.Count);
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
                d.Errores.Add($"Línea {linea}: la fila tiene menos columnas que el encabezado (¿una coma dentro de un campo?).");
                continue;
            }

            var cedulaTxt = SoloDigitos(QuitarSufijoDecimal(Cel(partes, iCedula)));
            if (cedulaTxt.Length < 5 || !long.TryParse(cedulaTxt, out var cedula) || cedula <= 0)
            {
                d.ExcluidosCedulaInvalida++;
                d.Errores.Add($"Línea {linea}: cédula inválida (vacía, sin dígitos o demasiado corta).");
                continue;
            }

            var (numero, esCelular) = TelefonoColombiano.Normalizar(Cel(partes, iNumero));
            if (numero is null)
            {
                d.ExcluidosTelefonoInvalido++;
                d.Errores.Add($"Línea {linea}: número marcado inválido (no son 10 dígitos).");
                continue;
            }
            if (!esCelular)
            {
                d.ExcluidosFijo++;
                d.Errores.Add("Número fijo: los SMS solo van a celulares.");
                continue;
            }

            var nombre = NormalizadorNombre.Limpiar(Cel(partes, iNombre));
            if (nombre.Length == 0)
            {
                d.ExcluidosNombreVacio++;
                d.Errores.Add("Nombre vacío después de la limpieza.");
                continue;
            }

            var perfilBruto = ColapsarEspacios(Cel(partes, iPerfil));
            if (CatalogoCreadorVirtualidad.PerfilesExcluidosClave.Contains(CatalogoCreadorVirtualidad.ClaveComparacion(perfilBruto)))
            {
                d.ExcluidosPerfilArchivo++;
                d.Errores.Add($"Perfil '{perfilBruto}' no se puede contactar (según el archivo).");
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
            if (cedulasPerfilBD.Contains(f.Cedula))
            {
                d.ExcluidosPerfilBD++;
                d.Errores.Add("Titular con perfil no contactable en el histórico de gestiones (Paz y salvo, PQR, No gestionar, Fallecido…).");
                continue;
            }
            if (numerosNegraBD.Contains(f.NumeroMarcado))
            {
                d.ExcluidosListaNegraTabla++;
                d.Errores.Add("Número en lista negra (carteras seleccionadas).");
                continue;
            }
            if (negraArchivo.Contains(f.NumeroMarcado))
            {
                d.ExcluidosListaNegraArchivo++;
                d.Errores.Add("Número en la lista negra cargada por archivo.");
                continue;
            }
            if (!vistos.Add(f.NumeroMarcado))
            {
                d.ExcluidosDuplicado++;
                d.Errores.Add("Número marcado repetido en el archivo (se conserva la primera fila).");
                continue;
            }
            d.Validas.Add(f);
        }
        return d;
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
    /// XLSX/CSV opcional con números a excluir en esta estrategia. Columna resuelta por
    /// alias (misma lista que Uso de Base de Datos). Devuelve un mensaje de error en vez
    /// de lanzar: un archivo de lista negra malo no debe tumbar la validación principal.
    /// </summary>
    private static async Task<(string Huella, HashSet<string> Numeros, string? Error)> LeerListaNegraArchivoAsync(
        Stream archivo, string nombre)
    {
        var numeros = new HashSet<string>(StringComparer.Ordinal);
        var (huella, filas) = await LeerArchivoAsync(archivo, nombre);
        if (filas.Count == 0) return (huella, numeros, "La lista negra por archivo está vacía.");

        var encabezados = filas[0];
        var indice = -1;
        for (var i = 0; i < encabezados.Length; i++)
        {
            if (!AliasColumnaTelefono.Todos.Contains(ArchivoTabular.Normalizar(encabezados[i]))) continue;
            indice = i;
            break;
        }
        if (indice < 0)
        {
            var vistos = string.Join(", ", encabezados.Select(h => h.Trim()).Where(h => h.Length > 0));
            return (huella, numeros, $"Lista negra por archivo: no se encontró una columna de teléfono. Columnas: {vistos}.");
        }

        for (var i = 1; i < filas.Count; i++)
        {
            if (indice >= filas[i].Length) continue;
            var (n, _) = TelefonoColombiano.Normalizar(filas[i][indice]);
            if (n is not null) numeros.Add(n);
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

    private static readonly Regex CelularRegex = new(@"^3\d{9}$", RegexOptions.Compiled);
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

    private static readonly Regex PrefijoLinea = new(@"^Línea \d+: ", RegexOptions.Compiled);
    private static List<ErrorAgrupadoDto> AgruparErrores(IEnumerable<string?> mensajes) =>
        mensajes
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => PrefijoLinea.Replace(m!, ""))
            .GroupBy(m => m)
            .Select(g => new ErrorAgrupadoDto { Mensaje = g.Key, Cantidad = g.Count() })
            .OrderByDescending(e => e.Cantidad)
            .ToList();
}
