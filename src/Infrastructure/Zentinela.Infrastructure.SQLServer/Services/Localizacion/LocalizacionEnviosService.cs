using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using static Zentinela.Infrastructure.SQLServer.Services.Localizacion.GeneradorSqlEnvios;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Envíos a localización (ADR-040, etapa 3): lotes de titulares que un líder manda a buscar en una fuente,
/// su retorno (lo que volvió de la fuente) y su impacto (gestión, contacto, pagos y promesas después).
/// ADO.NET sobre <c>LocalizacionEnvio</c> / <c>LocalizacionEnvioDetalle</c> (excluidas de las migraciones,
/// como el resto del módulo); si faltan las tablas, <see cref="LocalizacionTablasFaltantesException"/> (503).
///
/// <para><b>Crear</b>: el cliente manda los mismos parámetros de la consulta de sugerencias que tiene en
/// pantalla más la fuente; el servidor RECALCULA las sugerencias (<see cref="ILocalizacionSugerenciasService"/>)
/// y guarda las filas de esa fuente del top (las que se ven y las que baja el Excel). No se aceptan
/// TitularIDs del cliente: el lote nunca puede traer titulares de otro par ni saltarse las exclusiones.</para>
///
/// <para>Las decisiones que no son SQL (qué cargue es el retorno, porcentajes, días, agregados por líder)
/// están en <see cref="RetornoEnvio"/>, <see cref="MetricasEnvio"/> y <see cref="UsoDepartamentoCalculo"/>.</para>
/// </summary>
internal sealed class LocalizacionEnviosService : ILocalizacionEnviosService
{
    private const int TimeoutSegundos = 300;
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly CallCenterDbContext _db;
    private readonly ILocalizacionSugerenciasService _sugerencias;
    private readonly IExcelExporter _excel;
    private readonly InformesLocalizacionSettings _opciones;
    private readonly ILogger<LocalizacionEnviosService> _log;
    private readonly TablasEnvio _tablas;
    private readonly SqlConnection? _conexionCompartida;

    public LocalizacionEnviosService(
        CallCenterDbContext db, ILocalizacionSugerenciasService sugerencias, IExcelExporter excel,
        IOptions<InformesLocalizacionSettings> opciones, ILogger<LocalizacionEnviosService> log)
        : this(db, sugerencias, excel, opciones, log, Produccion, null) { }

    /// <summary>
    /// Para pruebas: otras tablas (<c>#LocalizacionEnvio</c>…) y una conexión ya abierta en la que viven
    /// (las tablas temporales son de la sesión). El servicio no la cierra.
    /// </summary>
    internal LocalizacionEnviosService(
        CallCenterDbContext db, ILocalizacionSugerenciasService sugerencias, IExcelExporter excel,
        IOptions<InformesLocalizacionSettings> opciones, ILogger<LocalizacionEnviosService> log,
        TablasEnvio tablas, SqlConnection? conexionCompartida)
    {
        _db = db;
        _sugerencias = sugerencias;
        _excel = excel;
        _opciones = opciones.Value;
        _log = log;
        _tablas = tablas;
        _conexionCompartida = conexionCompartida;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Crear
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Valida lo que no depende de la BD (400). El alcance del Líder (que el par sea suyo) lo valida el
    /// controlador antes de llegar aquí.
    /// </summary>
    internal static FuenteLocalizacion ValidarCreacion(CrearEnvioLocalizacionDto p, EnviosLocalizacionSettings envios)
    {
        if (p.CarteraID <= 0) throw new ArgumentException("Elige una cartera.");
        if (p.LiderID is not > 0)
            throw new ArgumentException("Un lote es de un par líder–cartera: elige un líder (la cartera completa no se puede enviar como un lote).");
        if (p.Top < 1 || p.Top > envios.MaxTitularesLote)
            throw new ArgumentException($"Un lote tiene como máximo {envios.MaxTitularesLote.ToString("N0", Co)} titulares.");
        if (p.Observacion is { Length: > 500 })
            throw new ArgumentException("La observación admite hasta 500 caracteres.");

        // Sin fuente, ResolverFuentes devolvería todas las Funcionales: un lote es de UNA fuente.
        if (string.IsNullOrWhiteSpace(p.Fuente)) throw new ArgumentException("Elige la fuente del lote.");
        var fuente = LocalizacionSugerenciasService.ResolverFuentes([p.Fuente]).Single();
        if (p.Fuentes.Count > 0 && !p.Fuentes.Any(f => f.Equals(fuente.Clave, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"{fuente.EtiquetaCalificador} no está entre las fuentes de la consulta de sugerencias.");
        return fuente;
    }

    public async Task<EnvioCreadoDto> CrearAsync(CrearEnvioLocalizacionDto p, string usuario, CancellationToken ct = default)
    {
        var fuente = ValidarCreacion(p, _opciones.Envios);

        // 1. Las sugerencias, recalculadas con los parámetros de la pantalla (409 si corre el ETL de gestión).
        var parametros = new ParametrosSugerenciasLocalizacion
        {
            LiderID             = p.LiderID,
            CarteraID           = p.CarteraID,
            Fuentes             = p.Fuentes,
            UmbralDias          = p.UmbralDias,
            UmbralRefrescoDias  = p.UmbralRefrescoDias,
            SoloConSaldo        = p.SoloConSaldo,
            Top                 = p.Top,
            ExcluirEnviadosDias = p.ExcluirEnviadosDias,
            IncluirLocalizados  = p.IncluirLocalizados,
        };
        var sugerencias = await _sugerencias.ObtenerAsync(parametros, ct);
        var filas = sugerencias.Filas
            .Where(f => f.Fuente.Equals(fuente.Clave, StringComparison.OrdinalIgnoreCase))
            .Take(_opciones.Envios.MaxTitularesLote)
            .ToList();
        if (filas.Count == 0)
            throw new ArgumentException($"No hay sugerencias de {fuente.EtiquetaCalificador} con estos filtros: no se creó ningún lote.");

        var parametrosJson = JsonSerializer.Serialize(new
        {
            fuentes = sugerencias.Parametros.Fuentes,
            p.UmbralDias, p.UmbralRefrescoDias, p.SoloConSaldo, p.Top, p.ExcluirEnviadosDias, p.IncluirLocalizados,
            candidatos = sugerencias.Kpis.PorFuente.FirstOrDefault(x => x.Fuente == fuente.Clave)?.Candidatos,
        }, JsonWeb);
        if (parametrosJson.Length > 1000) parametrosJson = parametrosJson[..1000];

        var dto = await InsertarLoteAsync(p.LiderID!.Value, p.CarteraID, fuente.Clave, filas, parametrosJson,
                                          string.IsNullOrWhiteSpace(p.Observacion) ? null : p.Observacion.Trim(), usuario, ct);

        if (sugerencias.HayMas)
            dto.Avisos.Add($"El lote lleva las {filas.Count.ToString("N0", Co)} filas de {fuente.EtiquetaCalificador} del top pedido; "
                         + "hay más candidatos: sube el top para enviar más.");
        foreach (var a in sugerencias.Avisos.Where(a => !a.Contains("registro de envíos", StringComparison.Ordinal)))
            dto.Avisos.Add(a);
        return dto;
    }

    /// <summary>Inserta el lote (cabecera + detalle con foto) en una transacción. Filas ya calculadas por el servidor.</summary>
    internal async Task<EnvioCreadoDto> InsertarLoteAsync(
        int liderID, int carteraID, string fuente, IReadOnlyList<FilaSugerenciaDto> filas,
        string? parametros, string? observacion, string usuario, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(filas.Select(f => new
        {
            t = f.TitularID,
            c = f.Categoria,
            p = (short)Math.Clamp(f.Prioridad, short.MinValue, short.MaxValue),
            u = f.UltimaConsulta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }));

        await using var c = await ConexionAsync(ct);
        await using var cmd = new SqlCommand(Crear(_tablas), c.Conn) { CommandTimeout = TimeoutSegundos };
        var d = cmd.Parameters;
        d.Add("@LiderID", SqlDbType.Int).Value = liderID;
        d.Add("@CarteraID", SqlDbType.Int).Value = carteraID;
        d.Add("@Fuente", SqlDbType.VarChar, 30).Value = fuente;
        d.Add("@Parametros", SqlDbType.NVarChar, 1000).Value = (object?)parametros ?? DBNull.Value;
        d.Add("@Observacion", SqlDbType.NVarChar, 500).Value = (object?)observacion ?? DBNull.Value;
        d.Add("@Usuario", SqlDbType.VarChar, 100).Value = Recortar(usuario, 100);
        d.Add("@Filas", SqlDbType.NVarChar, -1).Value = json;

        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            await rd.ReadAsync(ct);
            if (rd.IsDBNull(0))
                throw new ArgumentException("Ninguno de los titulares sugeridos pertenece al par (o la relación líder–cartera ya no está activa): no se creó el lote.");
            var creado = new EnvioCreadoDto
            {
                EnvioID   = rd.GetInt32(0),
                Codigo    = rd.GetString(1),
                Titulares = rd.GetInt32(2),
            };
            var descartados = rd.GetInt32(3);
            if (descartados > 0)
                creado.Avisos.Add($"{descartados.ToString("N0", Co)} titulares ya no pertenecen al par y no entraron al lote.");
            _log.LogInformation("Lote de localización {Codigo} creado por {Usuario}: líder {LiderID}, cartera {CarteraID}, {Fuente}, {Titulares} titulares",
                creado.Codigo, usuario, liderID, carteraID, fuente, creado.Titulares);
            return creado;
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new LocalizacionTablasFaltantesException();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Transiciones
    // ════════════════════════════════════════════════════════════════════════

    public async Task<EnvioLocalizacionDto> CambiarEstadoAsync(
        int envioID, AccionEnvio accion, string usuario, string? motivo, int? liderAlcance, CancellationToken ct = default)
    {
        if (motivo is { Length: > 300 }) throw new ArgumentException("El motivo de anulación admite hasta 300 caracteres.");

        await using var c = await ConexionAsync(ct);
        int afectadas;
        string? estadoActual, codigo;
        await using (var cmd = new SqlCommand(Transicion(_tablas, accion), c.Conn) { CommandTimeout = TimeoutSegundos })
        {
            cmd.Parameters.Add("@EnvioID", SqlDbType.Int).Value = envioID;
            cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = Recortar(usuario, 100);
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 300).Value =
                string.IsNullOrWhiteSpace(motivo) ? DBNull.Value : motivo.Trim();
            cmd.Parameters.Add("@LiderAlcance", SqlDbType.Int).Value = (object?)liderAlcance ?? DBNull.Value;
            try
            {
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                await rd.ReadAsync(ct);
                afectadas = rd.GetInt32(0);
                estadoActual = rd.IsDBNull(1) ? null : rd.GetString(1);
                codigo = rd.IsDBNull(2) ? null : rd.GetString(2);
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                throw new LocalizacionTablasFaltantesException();
            }
        }

        if (estadoActual is null) throw new LocalizacionEnvioNoEncontradoException(envioID);
        if (afectadas == 0)
        {
            TransicionesEnvio.TryParse(estadoActual, out var actual);
            throw new LocalizacionEnvioConflictoException(
                TransicionesEnvio.MensajeNoPermitido(codigo ?? $"#{envioID}", actual, accion), estadoActual);
        }

        _log.LogInformation("Lote de localización {Codigo}: {Accion} por {Usuario}", codigo, accion, usuario);
        var lote = (await LeerCabecerasAsync(c.Conn, new FiltroCabeceras { EnvioID = envioID, LiderID = liderAlcance }, 1, ct)).Single();
        await CompletarMetricasAsync(c.Conn, [lote], ct);
        return lote.Dto;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Un lote
    // ════════════════════════════════════════════════════════════════════════

    public async Task<DetalleEnvioLocalizacionDto> ObtenerAsync(
        int envioID, int? liderAlcance, int pagina, int tamPagina, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamPagina = Math.Clamp(tamPagina, 1, _opciones.Envios.MaxTamPaginaDetalle);

        await using var c = await ConexionAsync(ct);
        var lote = (await LeerCabecerasAsync(c.Conn, new FiltroCabeceras { EnvioID = envioID, LiderID = liderAlcance }, 1, ct))
            .SingleOrDefault() ?? throw new LocalizacionEnvioNoEncontradoException(envioID);

        var filas = await CompletarMetricasAsync(c.Conn, [lote], ct, pagina: (envioID, (pagina - 1) * tamPagina, tamPagina));
        return new DetalleEnvioLocalizacionDto
        {
            Envio      = lote.Dto,
            Filas      = filas,
            Pagina     = pagina,
            TamPagina  = tamPagina,
            TotalFilas = lote.Dto.Titulares,
        };
    }

    public async Task<ArchivoEnvioLocalizacion> ExcelAsync(int envioID, int? liderAlcance, CancellationToken ct = default)
    {
        await using var c = await ConexionAsync(ct);
        var lote = (await LeerCabecerasAsync(c.Conn, new FiltroCabeceras { EnvioID = envioID, LiderID = liderAlcance }, 1, ct))
            .SingleOrDefault()?.Dto ?? throw new LocalizacionEnvioNoEncontradoException(envioID);
        var def = CatalogoCargueFuentes.Buscar(lote.Fuente)
            ?? throw new ArgumentException($"La fuente {lote.Fuente} no está en el Cargue de Fuentes: no hay formato de Excel.");

        var filas = new List<FilaListaLocalizacion>();
        await using (var cmd = new SqlCommand(FilasExcel(_tablas), c.Conn) { CommandTimeout = TimeoutSegundos })
        {
            cmd.Parameters.Add("@EnvioID", SqlDbType.Int).Value = envioID;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                filas.Add(new FilaListaLocalizacion(rd.GetInt64(0), rd.IsDBNull(1) ? null : rd.GetString(1).Trim(),
                                                    lote.CarteraID, lote.Cartera, lote.LiderID, lote.Lider));
        }

        var bytes = _excel.GenerarListaLocalizacion(def, filas);
        return new ArchivoEnvioLocalizacion(bytes, $"Localizacion_{def.Fuente}_{lote.Codigo}_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Lista y uso del departamento
    // ════════════════════════════════════════════════════════════════════════

    public async Task<ListaEnviosLocalizacionDto> ListarAsync(FiltroEnviosLocalizacion f, CancellationToken ct = default)
    {
        string? estado = null;
        if (!string.IsNullOrWhiteSpace(f.Estado))
        {
            if (!TransicionesEnvio.TryParse(f.Estado, out var e)) throw new ArgumentException($"Estado desconocido: '{f.Estado}'.");
            estado = TransicionesEnvio.Codigo(e);
        }
        var fuente = string.IsNullOrWhiteSpace(f.Fuente)
            ? null
            : (CatalogoFuentesLocalizacion.Buscar(f.Fuente) ?? throw new ArgumentException($"Fuente desconocida: '{f.Fuente}'.")).Clave;

        var max = _opciones.Envios.MaxLotesLista;
        await using var c = await ConexionAsync(ct);
        var lotes = await LeerCabecerasAsync(c.Conn, new FiltroCabeceras
        {
            LiderID = f.LiderID, CarteraID = f.CarteraID, Fuente = fuente, Estado = estado,
            Desde = f.Desde?.Date, Hasta = f.Hasta?.Date.AddDays(1),
        }, max + 1, ct);

        var recortado = lotes.Count > max;
        if (recortado) lotes = lotes.Take(max).ToList();
        await CompletarMetricasAsync(c.Conn, lotes.Where(l => l.Dto.FechaEnvio is not null).ToList(), ct);
        return new ListaEnviosLocalizacionDto { Envios = lotes.Select(l => l.Dto).ToList(), Recortado = recortado };
    }

    public async Task<UsoEnviosPorPar> UsoPorParAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        await using var c = await ConexionAsync(ct);
        var (lotes, _, colas, corte) = await LeerUsoAsync(c.Conn, desde, hasta, null, ct);
        return new UsoEnviosPorPar(lotes, colas, corte);
    }

    public async Task<UsoDepartamentoDto> UsoDepartamentoAsync(DateTime desde, DateTime hasta, int? liderID, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("La fecha final es anterior a la inicial.");

        await using var c = await ConexionAsync(ct);
        var (lotes, cabeceras, colas, corte) = await LeerUsoAsync(c.Conn, desde, hasta, liderID, ct);

        // Borradores (sin fecha: lo que está armado y no se ha enviado) y líderes con relación activa.
        var borradores = new Dictionary<int, int>();
        await using (var cmd = new SqlCommand(BorradoresPorLider(_tablas), c.Conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct)) borradores[rd.GetInt32(0)] = rd.GetInt32(1);

        var lideres = new Dictionary<int, (string Nombre, long? Documento)>();
        await using (var cmd = new SqlCommand(LideresActivos, c.Conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                lideres[rd.GetInt32(0)] = (rd.GetString(1).Trim(), rd.IsDBNull(2) ? null : rd.GetInt64(2));
        foreach (var l in cabeceras)
            lideres.TryAdd(l.Dto.LiderID, (l.Dto.Lider, l.Dto.DocumentoLider));

        var ids = lideres.Keys.Where(id => liderID is null || id == liderID).ToList();
        var dto = new UsoDepartamentoDto { Desde = desde.Date, Hasta = hasta.Date, CorteCola = corte };

        foreach (var id in ids)
        {
            var suyos = lotes.Where(l => l.LiderID == id).ToList();
            int? cola = corte is null ? null : colas.Where(x => x.LiderID == id).Sum(x => x.Cola);
            var fila = FilaUso(UsoDepartamentoCalculo.Agregar(suyos, cola));
            fila.LiderID = id;
            fila.Nombre = lideres[id].Nombre;
            fila.Documento = lideres[id].Documento;
            fila.Borradores = borradores.GetValueOrDefault(id);
            dto.Lideres.Add(fila);
        }

        dto.Lideres = dto.Lideres
            .OrderByDescending(x => x.Enviados)
            .ThenBy(x => x.Nombre, StringComparer.Create(Co, true))
            .ToList();

        var colaTotal = corte is null ? (int?)null : colas.Where(x => ids.Contains(x.LiderID)).Sum(x => x.Cola);
        dto.Total = FilaUso(UsoDepartamentoCalculo.Agregar(lotes.Where(l => ids.Contains(l.LiderID)).ToList(), colaTotal));
        dto.Total.Nombre = "Total";
        dto.Total.Borradores = ids.Sum(borradores.GetValueOrDefault);

        if (corte is null)
            dto.Avisos.Add("Todavía no hay un corte de localización: la cola sugerida (y el % enviado) no se puede estimar.");
        if (lotes.Count == 0)
            dto.Avisos.Add("No hay lotes enviados en el período.");
        return dto;
    }

    private static FilaUsoLiderDto FilaUso(UsoAgregado u) => new()
    {
        Lotes               = u.Lotes,
        Enviados            = u.Metricas.Titulares,
        Cola                = u.Cola,
        PctColaEnviada      = u.PctColaEnviada,
        Buscados            = u.Metricas.Buscados,
        PctRetorno          = u.PctRetorno,
        Localizados         = u.Metricas.Localizados,
        PctLocalizados      = u.PctLocalizados,
        Gestionados         = u.Metricas.Gestionados,
        PasaronAContacto    = u.Metricas.PasaronAContacto,
        TitularesConPago    = u.Metricas.TitularesConPago,
        ValorPagado         = u.Metricas.ValorPagado,
        Honorarios          = u.Metricas.Honorarios,
        PromesasVigentes    = u.Metricas.PromesasVigentes,
        LotesConRetorno     = u.LotesConRetorno,
        DiasRetornoPromedio = u.DiasRetornoPromedio,
    };

    /// <summary>Lotes enviados (ENVIADO o CERRADO) con FechaEnvio en [desde, hasta], con sus métricas, y la cola por par.</summary>
    private async Task<(List<UsoLote> Lotes, List<LoteLeido> Cabeceras, List<ColaPar> Colas, DateTime? Corte)> LeerUsoAsync(
        SqlConnection conn, DateTime desde, DateTime hasta, int? liderID, CancellationToken ct)
    {
        var cabeceras = await LeerCabecerasAsync(conn, new FiltroCabeceras
        {
            LiderID = liderID, EnvioDesde = desde.Date, EnvioHasta = hasta.Date.AddDays(1), SoloEnviados = true,
        }, int.MaxValue, ct);
        await CompletarMetricasAsync(conn, cabeceras, ct);

        var lotes = cabeceras.Select(l => new UsoLote(
            l.Dto.EnvioID, l.Dto.LiderID, l.Dto.CarteraID, l.Dto.FechaEnvio!.Value,
            l.Metricas, l.Dto.Retorno?.DiasRetorno)).ToList();

        var (colas, corte) = await LeerColasAsync(conn, ct);
        return (lotes, cabeceras, colas, corte);
    }

    /// <summary>Cola aproximada por par del último corte OK; ([], null) si no hay corte.</summary>
    private async Task<(List<ColaPar> Colas, DateTime? Corte)> LeerColasAsync(SqlConnection conn, CancellationToken ct)
    {
        var funcionales = GeneradorSqlSugerencias.Funcionales;
        var filas = new List<(int LiderID, int CarteraID, int Total, int Buscados, int Encontrados, int Viejos180)>();
        DateTime? corte = null;
        await using (var cmd = new SqlCommand(ColaUltimoCorte(_tablas, funcionales), conn) { CommandTimeout = TimeoutSegundos })
        {
            try
            {
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct) && !rd.IsDBNull(0)) corte = rd.GetDateTime(0);
                await rd.NextResultAsync(ct);
                while (await rd.ReadAsync(ct))
                    filas.Add((rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4), rd.GetInt32(5)));
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                throw new LocalizacionTablasFaltantesException();
            }
        }
        if (corte is null) return ([], null);

        var colas = filas
            .GroupBy(f => (f.LiderID, f.CarteraID))
            .Select(g => new ColaPar(g.Key.LiderID, g.Key.CarteraID,
                UsoDepartamentoCalculo.Cola(g.Select(x => (x.Total, x.Buscados, x.Encontrados, x.Viejos180)))))
            .ToList();
        return (colas, corte);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Cabeceras y métricas
    // ════════════════════════════════════════════════════════════════════════

    internal sealed class FiltroCabeceras
    {
        public int? EnvioID { get; init; }
        public int? LiderID { get; init; }
        public int? CarteraID { get; init; }
        public string? Fuente { get; init; }
        public string? Estado { get; init; }
        public DateTime? Desde { get; init; }
        public DateTime? Hasta { get; init; }
        public DateTime? EnvioDesde { get; init; }
        public DateTime? EnvioHasta { get; init; }
        public bool SoloEnviados { get; init; }
    }

    /// <summary>Una cabecera leída, con lo que hace falta para calcular su retorno (no viaja al cliente).</summary>
    private sealed class LoteLeido(EnvioLocalizacionDto dto, long? cargueVinculado)
    {
        public EnvioLocalizacionDto Dto { get; } = dto;
        /// <summary>CargueEjecucionID guardado en el lote (vínculo explícito, etapa 4).</summary>
        public long? CargueVinculado { get; } = cargueVinculado;
        public MetricasCrudasEnvio Metricas { get; set; } = MetricasCrudasEnvio.Vacias;
    }

    private async Task<List<LoteLeido>> LeerCabecerasAsync(
        SqlConnection conn, FiltroCabeceras f, int max, CancellationToken ct)
    {
        var lista = new List<LoteLeido>();
        await using var cmd = new SqlCommand(Cabeceras(_tablas), conn) { CommandTimeout = TimeoutSegundos };
        var d = cmd.Parameters;
        d.Add("@Max", SqlDbType.Int).Value = max;
        d.Add("@EnvioID", SqlDbType.Int).Value = (object?)f.EnvioID ?? DBNull.Value;
        d.Add("@LiderID", SqlDbType.Int).Value = (object?)f.LiderID ?? DBNull.Value;
        d.Add("@CarteraID", SqlDbType.Int).Value = (object?)f.CarteraID ?? DBNull.Value;
        d.Add("@Fuente", SqlDbType.VarChar, 30).Value = (object?)f.Fuente ?? DBNull.Value;
        d.Add("@Estado", SqlDbType.VarChar, 10).Value = (object?)f.Estado ?? DBNull.Value;
        d.Add("@Desde", SqlDbType.DateTime2).Value = (object?)f.Desde ?? DBNull.Value;
        d.Add("@Hasta", SqlDbType.DateTime2).Value = (object?)f.Hasta ?? DBNull.Value;
        d.Add("@EnvioDesde", SqlDbType.DateTime2).Value = (object?)f.EnvioDesde ?? DBNull.Value;
        d.Add("@EnvioHasta", SqlDbType.DateTime2).Value = (object?)f.EnvioHasta ?? DBNull.Value;
        d.Add("@SoloEnviados", SqlDbType.Bit).Value = f.SoloEnviados;

        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
                DateTime? F(int i) => rd.IsDBNull(i) ? null : rd.GetDateTime(i);
                var liderID = rd.GetInt32(2);
                var carteraID = rd.GetInt32(5);
                var fuente = rd.GetString(7);
                lista.Add(new LoteLeido(new EnvioLocalizacionDto
                {
                    EnvioID          = rd.GetInt32(0),
                    Codigo           = rd.GetString(1),
                    LiderID          = liderID,
                    Lider            = S(3)?.Trim() ?? $"Líder {liderID}",
                    DocumentoLider   = rd.IsDBNull(4) ? null : rd.GetInt64(4),
                    CarteraID        = carteraID,
                    Cartera          = S(6)?.Trim() ?? $"Cartera {carteraID}",
                    Fuente           = fuente,
                    EtiquetaFuente   = CatalogoFuentesLocalizacion.Buscar(fuente)?.EtiquetaCalificador ?? fuente,
                    Estado           = rd.GetString(8),
                    Titulares        = rd.GetInt32(9),
                    Parametros       = S(10),
                    Observacion      = S(11),
                    CreadoPor        = rd.GetString(12),
                    FechaCreacion    = rd.GetDateTime(13),
                    EnviadoPor       = S(14),
                    FechaEnvio       = F(15),
                    CerradoPor       = S(16),
                    FechaCierre      = F(17),
                    AnuladoPor       = S(18),
                    FechaAnulacion   = F(19),
                    MotivoAnulacion  = S(20),
                }, rd.IsDBNull(21) ? null : rd.GetInt64(21)));
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new LocalizacionTablasFaltantesException();
        }
        return lista;
    }

    /// <summary>
    /// Calcula retorno e impacto de los lotes (en el lugar) y, si se pide, devuelve una página del detalle.
    /// Los lotes sin enviar no tienen retorno ni impacto; con página se leen igual (flags en 0).
    /// </summary>
    private async Task<List<FilaDetalleEnvioDto>> CompletarMetricasAsync(
        SqlConnection conn, IReadOnlyList<LoteLeido> lotes, CancellationToken ct,
        (int EnvioID, int Offset, int Tam)? pagina = null)
    {
        var detalle = new List<FilaDetalleEnvioDto>();
        if (lotes.Count == 0) return detalle;

        // 1. El retorno de cada lote lo elige C#: primer cargue OK de su fuente después del envío.
        var cargues = lotes.Any(l => l.Dto.FechaEnvio is not null) ? await LeerCarguesAsync(conn, ct) : [];
        var umbrales = new Dictionary<int, UmbralRetorno?>();
        foreach (var l in lotes)
        {
            var cargue = RetornoEnvio.ElegirCargue(l.Dto.Fuente, l.Dto.FechaEnvio, cargues, UsuariosPruebaCargue.EsDePrueba, l.CargueVinculado);
            umbrales[l.Dto.EnvioID] = RetornoEnvio.Umbral(l.Dto.FechaEnvio, cargue);
        }

        var fuentes = lotes.Select(l => CatalogoFuentesLocalizacion.Buscar(l.Dto.Fuente))
            .Where(f => f is not null && f.EnPanelCalificador).Select(f => f!).DistinctBy(f => f.Clave).ToList();
        var jsonLotes = JsonSerializer.Serialize(lotes.Select(l => new
        {
            e = l.Dto.EnvioID,
            d = umbrales[l.Dto.EnvioID]?.Desde.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            i = umbrales[l.Dto.EnvioID]?.Inclusivo ?? false,
        }));

        var crudas = new Dictionary<int, MetricasCrudasEnvio>();
        await using (var cmd = new SqlCommand(Metricas(_tablas, fuentes, pagina is not null), conn) { CommandTimeout = TimeoutSegundos })
        {
            var d = cmd.Parameters;
            d.Add("@Lotes", SqlDbType.NVarChar, -1).Value = jsonLotes;
            d.Add("@PerfilesContacto", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(PerfilesContacto.ClavesNormalizadas);
            d.Add("@PaginaEnvioID", SqlDbType.Int).Value = (object?)pagina?.EnvioID ?? DBNull.Value;
            d.Add("@Offset", SqlDbType.Int).Value = pagina?.Offset ?? 0;
            d.Add("@Tam", SqlDbType.Int).Value = pagina?.Tam ?? 1;

            try
            {
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                    crudas[rd.GetInt32(0)] = new MetricasCrudasEnvio(
                        rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3), rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                        rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7), rd.GetInt32(8),
                        rd.GetDecimal(9), rd.GetDecimal(10), rd.GetInt32(11));

                if (pagina is not null && await rd.NextResultAsync(ct))
                    while (await rd.ReadAsync(ct))
                        detalle.Add(LeerFilaDetalle(rd));
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                throw new LocalizacionTablasFaltantesException();
            }
        }

        foreach (var l in lotes)
        {
            var m = crudas.GetValueOrDefault(l.Dto.EnvioID) ?? MetricasCrudasEnvio.Vacias;
            l.Metricas = m;
            l.Dto.Retorno = MetricasEnvio.Retorno(l.Dto.FechaEnvio, umbrales[l.Dto.EnvioID], m);
            l.Dto.Impacto = MetricasEnvio.Impacto(l.Dto.FechaEnvio, m);
        }
        return detalle;
    }

    private static FilaDetalleEnvioDto LeerFilaDetalle(SqlDataReader rd)
    {
        DateTime? F(int i) => rd.IsDBNull(i) ? null : rd.GetDateTime(i);
        string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i).Trim();
        return new FilaDetalleEnvioDto
        {
            TitularID            = rd.GetInt64(0),
            Nombre               = S(1),
            Categoria            = rd.GetString(2),
            Prioridad            = rd.GetInt16(3),
            UltimaConsultaPrevia = F(4),
            SaldoFoto            = rd.IsDBNull(5) ? null : rd.GetDecimal(5),
            PerfilFoto           = S(6),
            GestionFoto          = F(7),
            Buscado              = rd.GetByte(8) == 1,
            Localizado           = rd.GetByte(9) == 1,
            FechaDato            = F(10),
            PerfilActual         = S(11),
            GestionActual        = F(12),
            Gestionado           = rd.GetByte(13) == 1,
            PasoAContacto        = rd.GetByte(14) == 1,
            ValorPagado          = rd.GetDecimal(15),
            PromesaVigente       = F(16),
        };
    }

    private async Task<List<CargueRetorno>> LeerCarguesAsync(SqlConnection conn, CancellationToken ct)
    {
        var lista = new List<CargueRetorno>();
        await using var cmd = new SqlCommand(CarguesOk, conn) { CommandTimeout = TimeoutSegundos };
        cmd.Parameters.Add("@PatronHarness", SqlDbType.VarChar, 100).Value = UsuariosPruebaCargue.Prefijo + "%";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add(new CargueRetorno(
                rd.GetInt64(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3),
                rd.IsDBNull(4) ? null : rd.GetDateTime(4), rd.IsDBNull(5) ? null : rd.GetDateTime(5)));
        return lista;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Conexión
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>La conexión de la llamada: propia (se cierra al terminar) o la compartida de las pruebas.</summary>
    private sealed class Conexion(SqlConnection conn, bool propia) : IAsyncDisposable
    {
        public SqlConnection Conn { get; } = conn;
        public ValueTask DisposeAsync() => propia ? Conn.DisposeAsync() : ValueTask.CompletedTask;
    }

    private async Task<Conexion> ConexionAsync(CancellationToken ct)
    {
        if (_conexionCompartida is not null) return new Conexion(_conexionCompartida, propia: false);
        var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        return new Conexion(conn, propia: true);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
