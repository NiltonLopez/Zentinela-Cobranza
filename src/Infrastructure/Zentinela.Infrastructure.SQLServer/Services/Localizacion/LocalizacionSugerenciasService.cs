using System.Data;
using System.Diagnostics;
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
using static Zentinela.Infrastructure.SQLServer.Services.Localizacion.DespliegueSugerencias;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Sugerencias de localización (ADR-040, etapa 2): a quién conviene mandar a buscar, en qué fuente y con
/// qué prioridad. En línea sobre los titulares del par (<see cref="GeneradorSqlSugerencias"/>, un solo
/// batch), ADO.NET, timeout de 300 s. No usa las tablas del precálculo: funciona aunque el usuario no haya
/// aplicado el script; sin las tablas de envíos simplemente no excluye a los enviados recientes (y lo avisa).
///
/// <para>Antes de calcular mira <c>vw_EtlEstadoTitularEstado</c>: saldo, perfil y última gestión salen de
/// <c>UltimoEstadoTitular</c>, así que mientras el ETL la está reescribiendo la respuesta es un 409 (vía
/// <see cref="LocalizacionEtlEnCursoException"/>); si la última carga OK tiene más de 72 h, un aviso.</para>
/// </summary>
internal sealed class LocalizacionSugerenciasService : ILocalizacionSugerenciasService
{
    private const int TimeoutSegundos = 300;
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    private readonly CallCenterDbContext _db;
    private readonly InformesLocalizacionSettings _opciones;
    private readonly ILogger<LocalizacionSugerenciasService> _log;

    public LocalizacionSugerenciasService(
        CallCenterDbContext db, IOptions<InformesLocalizacionSettings> opciones, ILogger<LocalizacionSugerenciasService> log)
    {
        _db = db;
        _opciones = opciones.Value;
        _log = log;
    }

    /// <summary>
    /// Fuentes pedidas → entradas del catálogo. Solo Funcionales del panel; vacío = todas. Lanza
    /// <see cref="ArgumentException"/> con un mensaje para el usuario ante una clave desconocida o no Funcional.
    /// </summary>
    internal static List<FuenteLocalizacion> ResolverFuentes(IEnumerable<string>? claves)
    {
        var pedidas = (claves ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pedidas.Count == 0) return GeneradorSqlSugerencias.Funcionales.ToList();

        var resultado = new List<FuenteLocalizacion>();
        foreach (var clave in pedidas)
        {
            var f = GeneradorSqlSugerencias.Funcionales.FirstOrDefault(x => x.Clave.Equals(clave, StringComparison.OrdinalIgnoreCase));
            if (f is null)
            {
                var existe = CatalogoFuentesLocalizacion.Buscar(clave);
                throw new ArgumentException(existe is null
                    ? $"Fuente desconocida: '{clave}'."
                    : $"{existe.EtiquetaCalificador} está marcada como {existe.Estado}: solo se sugieren búsquedas en fuentes Funcionales.");
            }
            resultado.Add(f);
        }
        // En el orden del catálogo (el del panel del Calificador).
        return GeneradorSqlSugerencias.Funcionales.Where(resultado.Contains).ToList();
    }

    /// <summary>Valida rangos; lanza <see cref="ArgumentException"/> con un mensaje para el usuario.</summary>
    internal static void Validar(ParametrosSugerenciasLocalizacion p, SugerenciasLocalizacionSettings pesos)
    {
        if (p.CarteraID <= 0) throw new ArgumentException("Elige una cartera.");
        if (p.UmbralDias is < 1 or > 3650) throw new ArgumentException("El umbral de 'no encontrado y viejo' debe estar entre 1 y 3.650 días.");
        if (p.UmbralRefrescoDias is < 1 or > 3650) throw new ArgumentException("El umbral de refresco debe estar entre 1 y 3.650 días.");
        if (p.Top < 1 || p.Top > pesos.TopMaximo) throw new ArgumentException($"El top debe estar entre 1 y {pesos.TopMaximo.ToString("N0", Co)}.");
        if (p.ExcluirEnviadosDias is < 0 or > 365) throw new ArgumentException("La ventana de enviados debe estar entre 0 y 365 días.");
    }

    public async Task<SugerenciasLocalizacionDto> ObtenerAsync(ParametrosSugerenciasLocalizacion p, CancellationToken ct = default)
    {
        var pesos = _opciones.Sugerencias;
        Validar(p, pesos);
        var fuentes = ResolverFuentes(p.Fuentes);
        p.Fuentes = fuentes.Select(f => f.Clave).ToList();
        var pedidas = GeneradorSqlSugerencias.IndicesPedidas(fuentes)
            .Select(i => new FuenteBit(GeneradorSqlSugerencias.Funcionales[i].Clave, GeneradorSqlSugerencias.Bit(i)))
            .ToList();

        var reloj = Stopwatch.StartNew();
        var dto = new SugerenciasLocalizacionDto { LiderID = p.LiderID, CarteraID = p.CarteraID, Parametros = p };

        await using var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);

        // ── 0. ETL de gestión y nombres ─────────────────────────────────────
        // "Hoy" es el del servidor de BD: con él se comparan FechaConsulta, FechaPromesa y la última gestión.
        var (nombreCartera, lideres, hoy, filasPar) = await VerificarEtlYLeerNombresAsync(conn, dto, p, pesos, ct);
        var msPrevio = reloj.ElapsedMilliseconds;

        // ── 1. El batch ─────────────────────────────────────────────────────
        var json = JsonSerializer.Serialize(PerfilesSugerencia.Tabla(pesos).Select(x => new
        {
            p = x.Clave,
            m = x.Exclusion is { } m ? PerfilesSugerencia.Codigo(m) : null,
            pts = x.Puntos,
            yp = x.YaPago,
        }));

        decimal? p90 = null;
        var grupos = new List<GrupoMascaras>();
        var titularesTop = new List<TitularTop>();

        await using (var cmd = new SqlCommand(GeneradorSqlSugerencias.Batch(fuentes, filasPar), conn) { CommandTimeout = TimeoutSegundos })
        {
            var d = cmd.Parameters;
            d.Add("@CarteraID", SqlDbType.Int).Value = p.CarteraID;
            d.Add("@LiderID", SqlDbType.Int).Value = (object?)p.LiderID ?? DBNull.Value;
            d.Add("@Perfiles", SqlDbType.NVarChar, -1).Value = json;
            d.Add("@Hoy", SqlDbType.Date).Value = hoy.ToDateTime(TimeOnly.MinValue);
            d.Add("@LimUmbral", SqlDbType.Date).Value = Dia(hoy, p.UmbralDias);
            d.Add("@LimRefresco", SqlDbType.Date).Value = p.UmbralRefrescoDias is { } r ? Dia(hoy, r) : DBNull.Value;
            d.Add("@LimFresco", SqlDbType.Date).Value = Dia(hoy, pesos.DiasContactoFresco);
            d.Add("@LimEnvio", SqlDbType.DateTime2).Value = p.ExcluirEnviadosDias > 0 ? Dia(hoy, p.ExcluirEnviadosDias) : DBNull.Value;
            d.Add("@Lim30", SqlDbType.Date).Value = Dia(hoy, 30);
            d.Add("@Lim90", SqlDbType.Date).Value = Dia(hoy, 90);
            d.Add("@SoloConSaldo", SqlDbType.Bit).Value = p.SoloConSaldo;
            d.Add("@IncluirLocalizados", SqlDbType.Bit).Value = p.IncluirLocalizados;
            d.Add("@Top", SqlDbType.Int).Value = p.Top;
            d.Add("@PtsSaldoMax", SqlDbType.Int).Value = pesos.PuntosSaldoMax;
            d.Add("@PtsGestion90", SqlDbType.Int).Value = pesos.PuntosGestionMas90;
            d.Add("@PtsGestion30", SqlDbType.Int).Value = pesos.PuntosGestionMas30;
            d.Add("@PtsNuncaFuncional", SqlDbType.Int).Value = pesos.PuntosNuncaFuncional;
            d.Add("@PtsNuncaFuente", SqlDbType.Int).Value = pesos.PuntosNuncaFuente;
            d.Add("@PtsNoEncViejo", SqlDbType.Int).Value = pesos.PuntosNoEncontradoViejo;
            d.Add("@PenContacto", SqlDbType.Int).Value = pesos.PenalizacionContactoFresco;

            await using var rd = await cmd.ExecuteReaderAsync(ct);

            // 1. Universo.
            await rd.ReadAsync(ct);
            dto.Kpis.TitularesPar = rd.GetInt32(0);
            dto.Kpis.TitularesConSaldo = rd.GetInt32(1);
            p90 = rd.IsDBNull(2) ? null : rd.GetDecimal(2);
            dto.Kpis.P90Saldo = p90;
            dto.RegistroEnviosDisponible = rd.GetBoolean(3);

            // 2. Combinaciones de máscaras.
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct))
                grupos.Add(new GrupoMascaras(
                    rd.IsDBNull(0) ? null : PerfilesSugerencia.MotivoDesdeCodigo(rd.GetString(0)),
                    rd.GetBoolean(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetInt64(4), rd.GetInt64(5),
                    rd.GetInt32(6), rd.GetInt32(7), rd.GetDecimal(8)));

            // 3. Los mejores titulares (los que ya no tienen ninguna fila candidata traen OrdenTop NULL).
            await rd.NextResultAsync(ct);
            var columnaOrden = 19 + pedidas.Count;
            while (await rd.ReadAsync(ct))
                if (!rd.IsDBNull(columnaOrden)) titularesTop.Add(LeerTitular(rd, pedidas.Count, lideres));
        }

        // ── 2. Despliegue por fuente, KPIs y top ────────────────────────────
        AgregarAvisosDeUniverso(dto, p);
        var conteos = Conteos(grupos, pedidas);
        var (titularesPorMotivo, titularesCandidatos, saldoCandidatos) = Titulares(grupos);
        dto.Kpis.TitularesCandidatos = titularesCandidatos;
        dto.Kpis.SaldoCandidatos = saldoCandidatos;
        ArmarKpis(dto, p, fuentes, conteos, titularesPorMotivo);

        var desajustes = 0;
        foreach (var fila in Top(titularesTop, pedidas, p.Top, pesos))
        {
            dto.Filas.Add(MapearFila(fila, p.CarteraID, nombreCartera));
            if (!CoincideConCalculoCSharp(fila, p90, p.IncluirLocalizados, pesos, hoy)) desajustes++;
        }
        if (desajustes > 0)
            _log.LogWarning("Sugerencias de localización: {Desajustes} filas con puntaje SQL distinto del de PrioridadSugerencia "
                          + "(cartera {CarteraID}, líder {LiderID}). Revisar GeneradorSqlSugerencias.", desajustes, p.CarteraID, p.LiderID);

        dto.HayMas = dto.Kpis.Candidatos > dto.Filas.Count;
        dto.DuracionMs = (int)reloj.ElapsedMilliseconds;
        _log.LogDebug("Sugerencias de localización (cartera {CarteraID}, líder {LiderID}, {Fuentes} fuentes, top {Top}): "
                    + "conexión, ETL y nombres {MsPrevio} ms; cálculo {MsCalculo} ms; {Grupos} combinaciones de máscaras",
            p.CarteraID, p.LiderID, fuentes.Count, p.Top, msPrevio, dto.DuracionMs - msPrevio, grupos.Count);
        return dto;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ETL, nombres y avisos
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Frescura del ETL de gestión (409 si está corriendo, aviso si es vieja), nombre de la cartera y nombres
    /// de los líderes con relación activa en ella, en una sola ida a la BD. La antigüedad de la corrida en curso
    /// se calcula en SQL: el reloj del servidor de BD puede no ser el de la API.
    /// </summary>
    private async Task<(string Cartera, Dictionary<int, string> Lideres, DateOnly Hoy, int FilasPar)> VerificarEtlYLeerNombresAsync(
        SqlConnection conn, SugerenciasLocalizacionDto dto, ParametrosSugerenciasLocalizacion p,
        SugerenciasLocalizacionSettings pesos, CancellationToken ct)
    {
        string? cartera = null;
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var filasPar = 0;
        var lideres = new Dictionary<int, string>();
        DateTime? ultimaOk = null, ultimoIntento = null;
        int? horas = null, minutosIntento = null;
        string? resultado = null;
        var etlLeido = false;

        await using (var cmd = new SqlCommand("""
            SELECT NombreCartera, CAST(SYSDATETIME() AS date) AS Hoy,
                   (SELECT COUNT(*) FROM dbo.Titular_Cartera tc
                     WHERE tc.CarteraID = @CarteraID AND (@LiderID IS NULL OR tc.LiderID = @LiderID)) AS FilasPar
              FROM dbo.Cartera WHERE ID = @CarteraID
            OPTION (RECOMPILE);
            SELECT DISTINCT l.ID, l.Nombre
              FROM dbo.Lider_Cartera lc INNER JOIN dbo.Lider l ON l.ID = lc.LiderID
             WHERE lc.CarteraID = @CarteraID AND lc.Activo = 1;
            SELECT TOP (1) UltimaCargaOk, HorasDesdeCargaOk, UltimoIntento, ResultadoUltimoIntento,
                   DATEDIFF(MINUTE, UltimoIntento, SYSDATETIME()) AS MinutosDesdeIntento
              FROM dbo.vw_EtlEstadoTitularEstado;
            """, conn))
        {
            cmd.Parameters.Add("@CarteraID", SqlDbType.Int).Value = p.CarteraID;
            cmd.Parameters.Add("@LiderID", SqlDbType.Int).Value = (object?)p.LiderID ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct) && !rd.IsDBNull(0))
            {
                cartera = rd.GetString(0).Trim();
                hoy = DateOnly.FromDateTime(rd.GetDateTime(1));
                filasPar = rd.GetInt32(2);
            }
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct)) lideres[rd.GetInt32(0)] = rd.IsDBNull(1) ? $"Líder {rd.GetInt32(0)}" : rd.GetString(1).Trim();
            try
            {
                await rd.NextResultAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    etlLeido = true;
                    ultimaOk = rd.IsDBNull(0) ? null : rd.GetDateTime(0);
                    horas = rd.IsDBNull(1) ? null : rd.GetInt32(1);
                    ultimoIntento = rd.IsDBNull(2) ? null : rd.GetDateTime(2);
                    resultado = rd.IsDBNull(3) ? null : rd.GetString(3);
                    minutosIntento = rd.IsDBNull(4) ? null : rd.GetInt32(4);
                }
            }
            catch (SqlException ex)
            {
                _log.LogWarning(ex, "No se pudo leer vw_EtlEstadoTitularEstado para las sugerencias de localización");
            }
        }

        if (cartera is null) throw new ArgumentException($"La cartera {p.CarteraID} no existe.");
        dto.EtiquetaAlcance = p.LiderID is { } l
            ? $"{cartera} — {lideres.GetValueOrDefault(l) ?? $"Líder {l}"}"
            : $"{cartera} (cartera completa)";
        dto.DatosGestionAl = ultimaOk;
        dto.HorasDesdeCargaGestion = horas;

        if (!etlLeido)
        {
            dto.Avisos.Add("No se pudo verificar la frescura de los datos de gestión (saldo, perfil y última gestión).");
            return (cartera, lideres, hoy, filasPar);
        }

        if (string.Equals(resultado, "EN CURSO", StringComparison.OrdinalIgnoreCase) && ultimoIntento is { } inicio)
        {
            if (minutosIntento is null || minutosIntento < pesos.MinutosEtlColgado)
                throw new LocalizacionEtlEnCursoException(inicio);
            dto.Avisos.Add($"La actualización de los datos de gestión que empezó el {inicio.ToString("dd/MM/yyyy HH:mm", Co)} "
                         + "sigue marcada en curso: parece colgada. Se usan los datos de la última carga buena.");
        }

        if (horas > pesos.HorasDatosGestionViejos)
            dto.Avisos.Add($"Los datos de gestión (saldo, perfil y última gestión) son de hace {horas} horas "
                         + $"(última carga buena: {ultimaOk?.ToString("dd/MM/yyyy HH:mm", Co) ?? "—"}). "
                         + "Las prioridades y las exclusiones pueden estar desactualizadas.");
        return (cartera, lideres, hoy, filasPar);
    }

    private static void AgregarAvisosDeUniverso(SugerenciasLocalizacionDto dto, ParametrosSugerenciasLocalizacion p)
    {
        var k = dto.Kpis;
        if (k.TitularesPar == 0)
            dto.Avisos.Add("Este par no tiene titulares asignados (o la relación líder-cartera no está activa).");
        else if (k.TitularesConSaldo == 0)
            dto.Avisos.Add("Ningún titular de esta cartera tiene saldo en los datos de gestión (UltimoEstadoTitular): "
                         + (p.SoloConSaldo
                             ? "con \"Solo con saldo\" no queda ningún candidato. Desmárcalo para ver las sugerencias."
                             : "el puntaje por saldo es 0 para todos."));

        if (!dto.RegistroEnviosDisponible)
            dto.Avisos.Add("Aún no hay registro de envíos a localización (falta aplicar "
                         + $"{LocalizacionTablasFaltantesException.Script}): no se excluyó a nadie por envíos recientes.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Lectura, filas y KPIs
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Una fila del result set 3 (columnas en el orden de <see cref="GeneradorSqlSugerencias.Batch"/>).</summary>
    private static TitularTop LeerTitular(SqlDataReader rd, int pedidas, Dictionary<int, string> lideres)
    {
        var ultimas = new DateTime?[pedidas];
        for (var i = 0; i < pedidas; i++) ultimas[i] = rd.IsDBNull(19 + i) ? null : rd.GetDateTime(19 + i);
        var liderID = rd.GetInt32(2);
        return new TitularTop(
            TitularID:          rd.GetInt64(0),
            Nombre:             rd.IsDBNull(1) ? null : rd.GetString(1).Trim(),
            LiderID:            liderID,
            Lider:              lideres.GetValueOrDefault(liderID),
            Saldo:              rd.IsDBNull(3) ? null : rd.GetDecimal(3),
            UltimoPerfil:       rd.IsDBNull(4) ? null : rd.GetString(4),
            FechaUltimaGestion: rd.IsDBNull(5) ? null : rd.GetDateTime(5),
            PtsPerfil:          rd.GetInt32(6),
            PtsSaldo:           rd.GetInt32(7),
            PtsGestion:         rd.GetInt32(8),
            PtsNuncaFuncional:  rd.GetInt32(9),
            PtsContacto:        rd.GetInt32(10),
            PtsBase:            rd.GetInt32(11),
            EncMask:            rd.GetInt64(12),
            BuscadoFuncional:   rd.GetBoolean(13),
            ConFresco:          rd.GetBoolean(14),
            EnvMask:            rd.GetInt64(15),
            Cat1:               rd.GetInt64(16),
            Cat2:               rd.GetInt64(17),
            Cat3:               rd.GetInt64(18),
            UltimasConsultas:   ultimas);
    }

    private static FilaSugerenciaDto MapearFila(FilaTop f, int carteraID, string cartera)
    {
        var t = f.Titular;
        var fuente = CatalogoFuentesLocalizacion.Buscar(f.Fuente);
        return new FilaSugerenciaDto
        {
            TitularID          = t.TitularID,
            Nombre             = t.Nombre,
            CarteraID          = carteraID,
            Cartera            = cartera,
            LiderID            = t.LiderID,
            Lider              = t.Lider ?? $"Líder {t.LiderID}",
            Saldo              = t.Saldo,
            UltimoPerfil       = t.UltimoPerfil,
            FechaUltimaGestion = t.FechaUltimaGestion,
            Fuente             = f.Fuente,
            EtiquetaFuente     = fuente?.EtiquetaCalificador ?? f.Fuente,
            Categoria          = PerfilesSugerencia.Codigo(f.Categoria),
            UltimaConsulta     = t.UltimasConsultas[f.IndiceFuente],
            Prioridad          = f.Prioridad,
            Desglose = new DesglosePrioridadDto
            {
                Perfil            = t.PtsPerfil,
                Saldo             = t.PtsSaldo,
                Gestion           = t.PtsGestion,
                NuncaFuncional    = t.PtsNuncaFuncional,
                NuncaFuente       = f.PtsNuncaFuente,
                NoEncontradoViejo = f.PtsNoEncontradoViejo,
                ContactoFresco    = t.PtsContacto,
            },
            EncontradoEn = GeneradorSqlSugerencias.Funcionales
                .Where((_, i) => (t.EncMask & GeneradorSqlSugerencias.Bit(i)) != 0)
                .Select(x => x.EtiquetaCalificador)
                .ToList(),
        };
    }

    /// <summary>Los puntos que calculó el SQL son los de <see cref="PrioridadSugerencia"/>.</summary>
    private static bool CoincideConCalculoCSharp(
        FilaTop f, decimal? p90, bool incluirLocalizados, SugerenciasLocalizacionSettings pesos, DateOnly hoy)
    {
        var t = f.Titular;
        var esperado = PrioridadSugerencia.Calcular(new EntradaPrioridad(
            PerfilesSugerencia.Clasificar(t.UltimoPerfil).Grupo,
            t.Saldo,
            p90,
            t.FechaUltimaGestion is { } g ? DateOnly.FromDateTime(g) : null,
            t.BuscadoFuncional,
            f.Categoria,
            t.ConFresco,
            incluirLocalizados), pesos, hoy);
        return esperado.Total == f.Prioridad;
    }

    private static void ArmarKpis(
        SugerenciasLocalizacionDto dto, ParametrosSugerenciasLocalizacion p, List<FuenteLocalizacion> fuentes,
        List<Conteo> conteos, Dictionary<MotivoExclusionSugerencia, int> titularesPorMotivo)
    {
        var k = dto.Kpis;
        k.CandidatosAntes = conteos.Sum(c => c.Filas);
        k.Candidatos = conteos.Where(c => c.Motivo is null).Sum(c => c.Filas);
        k.ConContactoFresco = conteos.Sum(c => c.ConFresco);

        var categorias = new List<CategoriaSugerencia> { CategoriaSugerencia.NuncaBuscado, CategoriaSugerencia.NoEncontradoViejo };
        if (p.UmbralRefrescoDias is not null) categorias.Add(CategoriaSugerencia.EncontradoViejo);
        k.PorCategoria = categorias.Select(cat => new ConteoCategoriaSugerenciaDto
        {
            Categoria  = PerfilesSugerencia.Codigo(cat),
            Etiqueta   = PerfilesSugerencia.Etiqueta(cat),
            Antes      = conteos.Where(c => c.Categoria == cat).Sum(c => c.Filas),
            Candidatos = conteos.Where(c => c.Categoria == cat && c.Motivo is null).Sum(c => c.Filas),
        }).ToList();

        k.PorFuente = fuentes.Select(f =>
        {
            var de = conteos.Where(c => c.Fuente.Equals(f.Clave, StringComparison.OrdinalIgnoreCase)).ToList();
            int Suma(CategoriaSugerencia cat, bool soloCandidatos) =>
                de.Where(c => c.Categoria == cat && (!soloCandidatos || c.Motivo is null)).Sum(c => c.Filas);
            var candidatas = de.Where(c => c.Motivo is null).ToList();
            return new ConteoFuenteSugerenciaDto
            {
                Fuente                 = f.Clave,
                Etiqueta               = f.EtiquetaCalificador,
                Estado                 = f.Estado,
                NuncaAntes             = Suma(CategoriaSugerencia.NuncaBuscado, false),
                NoEncontradoViejoAntes = Suma(CategoriaSugerencia.NoEncontradoViejo, false),
                EncontradoViejoAntes   = Suma(CategoriaSugerencia.EncontradoViejo, false),
                Excluidos              = de.Where(c => c.Motivo is not null).Sum(c => c.Filas),
                Nunca                  = Suma(CategoriaSugerencia.NuncaBuscado, true),
                NoEncontradoViejo      = Suma(CategoriaSugerencia.NoEncontradoViejo, true),
                EncontradoViejo        = Suma(CategoriaSugerencia.EncontradoViejo, true),
                Candidatos             = candidatas.Sum(c => c.Filas),
                ConContactoFresco      = de.Sum(c => c.ConFresco),
                SaldoCandidatos        = candidatas.Sum(c => c.Saldo),
            };
        }).ToList();

        k.Exclusiones = Enum.GetValues<MotivoExclusionSugerencia>()
            .Select(m => new ConteoExclusionSugerenciaDto
            {
                Motivo    = PerfilesSugerencia.Codigo(m),
                Etiqueta  = PerfilesSugerencia.Etiqueta(m),
                Filas     = conteos.Where(c => c.Motivo == m).Sum(c => c.Filas),
                Titulares = titularesPorMotivo.GetValueOrDefault(m),
            })
            .Where(e => e.Filas > 0)
            .ToList();
    }

    private static DateTime Dia(DateOnly hoy, int diasAtras) => hoy.AddDays(-diasAtras).ToDateTime(TimeOnly.MinValue);
}
