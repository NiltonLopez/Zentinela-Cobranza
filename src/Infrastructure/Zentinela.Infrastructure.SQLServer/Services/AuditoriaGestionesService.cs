using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Zentinela.Application.DTOs.AuditoriaGestiones;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Orquesta la auditoría de gestiones: consulta HistoricoEstadoTitular por las
/// carteras seleccionadas y el rango de FechaUltimaGestion, evalúa la congruencia
/// perfil↔gestión con IA (por lotes) + reglas determinísticas sobre la promesa,
/// resuelve nombres (cartera, líder, asesor) y arma el informe del dashboard.
/// </summary>
public class AuditoriaGestionesService : IAuditoriaGestionesService
{
    private readonly CallCenterDbContext      _cc;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly IEvaluadorCongruencia    _evaluador;
    private readonly AuditoriaReportesStore   _store;

    private const int TamanoLote = 10;
    // Configurables vía appsettings ("Auditoria"): permiten pruebas pequeñas y respetar el
    // límite de peticiones/minuto de Gemini (free tier) sin recompilar. Defaults = valores de prod.
    private readonly int _maxGestionesIA;     // tope de gestiones que van a IA (default 1800)
    private readonly int _maxPeticionesIA;    // tope de PETICIONES a Gemini (0 = usar _maxGestionesIA)
    private readonly int _concurrenciaIA;     // lotes en paralelo (default 6; free tier: bajar a 1)
    private readonly int _delayEntreLotesMs;  // pausa antes de cada lote para no exceder RPM (default 0)

    // Tope efectivo de gestiones a IA. Si se fija un nº de peticiones, manda ese × 10 (TamanoLote);
    // si no, usa el tope por gestiones. Cada petición = un lote de TamanoLote gestiones.
    private int TopeGestiones => _maxPeticionesIA > 0 ? _maxPeticionesIA * TamanoLote : _maxGestionesIA;

    // ── Precios Gemini en pesos colombianos (COP) ───────────────────────────────
    // Entrada: 859,88 COP / millón → 0,00085988 por token
    // Salida:  5159,31 COP / millón → 0,00515931 por token
    private const decimal PrecioEntradaPorToken = 0.00085988m;
    private const decimal PrecioSalidaPorToken  = 0.00515931m;
    // La salida no se puede contar por adelantado (aún no existe): se estima por registro.
    // Con la justificación limitada a ~12 palabras, el JSON por registro ronda 40-45 tokens.
    private const int     TokensSalidaPorRegistro = 45;

    public AuditoriaGestionesService(
        CallCenterDbContext cc,
        RecursosHumanosDbContext rrhh,
        IEvaluadorCongruencia evaluador,
        AuditoriaReportesStore store,
        IConfiguration config)
    {
        _cc        = cc;
        _rrhh      = rrhh;
        _evaluador = evaluador;
        _store     = store;
        _maxGestionesIA    = config.GetValue("Auditoria:MaxGestionesIA", 1800);
        _maxPeticionesIA   = config.GetValue("Auditoria:MaxPeticionesIA", 0);
        _concurrenciaIA    = config.GetValue("Auditoria:ConcurrenciaIA", 6);
        _delayEntreLotesMs = config.GetValue("Auditoria:DelayEntreLotesMs", 0);
    }

    /// <summary>Lista los reportes ya generados y disponibles (compartidos, expiran a 48 h).</summary>
    public IReadOnlyList<ReporteAuditoriaInfo> ListarReportes() => _store.Listar();

    public async Task<AnalizarAuditoriaResponse> AnalizarAsync(FiltroAuditoria filtro, CancellationToken ct = default)
    {
        var carteraIDs = filtro.Carteras.Select(c => c.CarteraID).Distinct().ToList();
        if (carteraIDs.Count == 0)
            return new AnalizarAuditoriaResponse { Informe = new() };

        // Reporte compartido en memoria: 1 cartera + 1 día se cachea 48 h (no re-auditar ni re-pagar).
        bool cacheable = carteraIDs.Count == 1 && filtro.FechaInicio == filtro.FechaFin;
        if (cacheable && _store.TryObtener(carteraIDs[0], filtro.FechaInicio) is { } previo)
            return previo;

        // Consulta base: una cartera puede tener cientos de miles de gestiones en el rango,
        // así que NO se materializan todas. Se empuja el trabajo a SQL (COUNT + GROUP BY) y
        // solo se traen las filas que realmente se necesitan (reglas + IA).
        var baseQuery = _cc.HistoricoEstadosTitular
            .Where(h => carteraIDs.Contains(h.CarteraID)
                     && h.FechaUltimaGestion >= filtro.FechaInicio
                     && h.FechaUltimaGestion <= filtro.FechaFin);

        // 1. Totales agregados (en SQL, sin transferir filas)
        var total = await baseQuery.CountAsync(ct);
        if (total == 0)
            return new AnalizarAuditoriaResponse
            {
                NombreArchivo = $"{carteraIDs.Count} cartera(s)",
                FilasLeidas   = 0,
                Informe       = new(),
            };

        var conteoPorCartera = await baseQuery
            .GroupBy(h => h.CarteraID)
            .Select(g => new { CarteraID = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var conteoPorPerfil = await baseQuery
            .Where(h => h.UltimoPerfil != null && h.UltimoPerfil != "")
            .GroupBy(h => h.UltimoPerfil!)
            .Select(g => new { Perfil = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        // 2. Filas que disparan reglas determinísticas de promesa (conjunto pequeño):
        //    medio = exactamente uno de (fecha, valor) presente; bajo = valor < 5 dígitos.
        var candidatas = await baseQuery
            .Where(h => (h.FechaPromesa != null && (h.ValorPromesa == null || h.ValorPromesa <= 0))
                     || (h.FechaPromesa == null && h.ValorPromesa > 0)
                     || (h.FechaPromesa != null && h.ValorPromesa > 0 && h.ValorPromesa < 10000))
            .Select(h => new Registro
            {
                TitularID    = h.TitularID,
                CarteraID    = h.CarteraID,
                AgenteID     = h.AgenteID,
                UltimoPerfil = h.UltimoPerfil ?? "",
                Gestion      = h.Gestion ?? "",
                FechaPromesa = h.FechaPromesa,
                ValorPromesa = h.ValorPromesa,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // 3. Filas con texto de gestión → IA. Se acotan a MaxGestionesIA (control de costo),
        //    pero se cuenta el total real para avisar si se descartó parte (no cortar en silencio).
        var conTextoQuery = baseQuery.Where(h => h.Gestion != null && h.Gestion != "");
        var totalConTexto = await conTextoQuery.CountAsync(ct);
        var conGestion = await conTextoQuery
            .Select(h => new Registro
            {
                TitularID    = h.TitularID,
                CarteraID    = h.CarteraID,
                AgenteID     = h.AgenteID,
                UltimoPerfil = h.UltimoPerfil ?? "",
                Gestion      = h.Gestion ?? "",
                FechaPromesa = h.FechaPromesa,
                ValorPromesa = h.ValorPromesa,
            })
            .Take(TopeGestiones)
            .AsNoTracking()
            .ToListAsync(ct);

        // 4. Resolver nombres (cartera, líder, asesor) solo para las filas involucradas
        var nombreCartera = await _cc.Carteras
            .Where(c => carteraIDs.Contains(c.ID))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.ID, c => c.NombreCartera, ct);

        var agenteIDs = candidatas.Concat(conGestion)
            .Where(r => r.AgenteID is > 0)
            .Select(r => (long)r.AgenteID!.Value)
            .Distinct()
            .ToList();

        var carteraAgentes = await _cc.CarteraAgentes
            .Where(ca => agenteIDs.Contains(ca.AgenteID) && carteraIDs.Contains(ca.CarteraID))
            .AsNoTracking()
            .ToListAsync(ct);
        var paresALider = carteraAgentes
            .GroupBy(ca => (ca.AgenteID, ca.CarteraID))
            .ToDictionary(g => g.Key, g => g.First().LiderID);

        // Líderes asignados a cada cartera (Lider_Cartera). Sirve de respaldo cuando el
        // agente de la gestión ya no está en Cartera_Agente: si la cartera tiene un único
        // líder, las gestiones huérfanas se le atribuyen (le pertenecen igual).
        var lideresCartera = await _cc.LiderCarteras
            .Where(lc => carteraIDs.Contains(lc.CarteraID) && !lc.EsExterna && lc.Activo)
            .Select(lc => new { lc.CarteraID, lc.LiderID })
            .AsNoTracking()
            .ToListAsync(ct);
        var lideresPorCartera = lideresCartera
            .GroupBy(x => x.CarteraID)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LiderID).Distinct().ToList());

        var liderIDs = paresALider.Values
            .Concat(lideresPorCartera.Values.SelectMany(v => v))
            .Distinct()
            .ToList();
        var lideres = await _cc.Lideres
            .Where(l => liderIDs.Contains(l.ID))
            .Select(l => new { l.ID, l.Nombre, l.NumeroDocumento })
            .AsNoTracking()
            .ToListAsync(ct);
        // Nombre del líder desde RRHH (por documento) → MISMA fuente que el supervisor, sin duplicados.
        var liderDocs = lideres.Where(l => l.NumeroDocumento != null).Select(l => l.NumeroDocumento!.Value).Distinct().ToList();
        var nombrePorDocLider = await _rrhh.Empleados
            .Where(e => liderDocs.Contains(e.NumeroDocumento))
            .Select(e => new { e.NumeroDocumento, e.Nombre, e.Apellidos })
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => PrimerNombreApellido(e.Nombre, e.Apellidos), ct);
        var nombreLider = lideres.ToDictionary(l => l.ID, l =>
            l.NumeroDocumento is long d && nombrePorDocLider.TryGetValue(d, out var n) ? n : TituloCase(l.Nombre));

        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .Select(e => new { e.NumeroDocumento, e.Nombre, e.Apellidos, e.IDSupervisor })
            .AsNoTracking()
            .ToListAsync(ct);
        var nombreAsesor = empleados.ToDictionary(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        // Supervisor real de cada asesor: RRHH Empleado.IDSupervisor → Empleado.ID (fuente principal del líder).
        var supervisorIDs = empleados.Where(e => e.IDSupervisor is > 0).Select(e => e.IDSupervisor!.Value).Distinct().ToList();
        var nombreSupervisor = await _rrhh.Empleados
            .Where(s => supervisorIDs.Contains(s.ID))
            .Select(s => new { s.ID, s.Nombre, s.Apellidos })
            .AsNoTracking()
            .ToDictionaryAsync(s => s.ID, s => PrimerNombreApellido(s.Nombre, s.Apellidos), ct);
        var supervisorPorAgente = empleados
            .Where(e => e.IDSupervisor is int sid && nombreSupervisor.ContainsKey(sid))
            .ToDictionary(e => e.NumeroDocumento, e => nombreSupervisor[e.IDSupervisor!.Value]);

        string Cartera(Registro r) => nombreCartera.TryGetValue(r.CarteraID, out var n) ? n : $"Cartera {r.CarteraID}";
        string NombreLider(int lid) => nombreLider.TryGetValue(lid, out var ln) ? ln : $"Líder {lid}";
        string Lider(Registro r)
        {
            // 1) Supervisor real del asesor según RRHH (Empleado.IDSupervisor) — fuente principal
            if (r.AgenteID is int a && a > 0 && supervisorPorAgente.TryGetValue(a, out var sup))
                return sup;
            // 2) Respaldo: líder del agente en la cartera (Cartera_Agente)
            if (r.AgenteID is int a2 && a2 > 0 && paresALider.TryGetValue(((long)a2, r.CarteraID), out var lid))
                return NombreLider(lid);
            // 3) Respaldo: si la cartera tiene un único líder asignado, atribuírselo
            if (lideresPorCartera.TryGetValue(r.CarteraID, out var lst) && lst.Count == 1)
                return NombreLider(lst[0]);
            return "Sin asignar";
        }
        string Asesor(Registro r) =>
            r.AgenteID is int a && a > 0
                ? (nombreAsesor.TryGetValue(a, out var an) ? an : "No se encuentra agente")
                : "Gestión sin agente";

        var hallazgos = new List<IncongruenciaFila>();

        IncongruenciaFila Fila(Registro r, string tipo, string severidad, string detalle) => new()
        {
            Identificacion = r.TitularID.ToString(),
            AsesorGestion  = Asesor(r),
            Lider          = Lider(r),
            Cartera        = Cartera(r),
            Tipo           = tipo,
            Severidad      = severidad,
            Detalle        = detalle,
            UltimoPerfil   = r.UltimoPerfil,
            Contacto       = "",
            Gestion        = r.Gestion,
            FechaPromesa   = r.FechaPromesa?.ToString("yyyy-MM-dd"),
            ValorPromesa   = r.ValorPromesa,
        };

        // 5. Evaluación IA perfil↔gestión (solo filas con texto de gestión — ver paso 3).
        //    Los lotes se evalúan EN PARALELO con un tope de concurrencia (Gemini limita las
        //    peticiones por minuto). Cada lote lleva índices globales, así que el orden no importa.
        var lotes = new List<List<GestionParaEvaluar>>();
        for (int i = 0; i < conGestion.Count; i += TamanoLote)
        {
            var slice = conGestion.Skip(i).Take(TamanoLote).ToList();
            lotes.Add(slice.Select((r, k) => new GestionParaEvaluar(i + k, r.UltimoPerfil, r.Gestion)).ToList());
        }

        // Un lote que fracasa tras agotar sus reintentos NO puede tumbar la auditoría entera:
        // antes, con Task.WhenAll propagando, un único lote perdido descartaba el trabajo de
        // los otros ~146 (ya facturados). Ahora se pierde solo ese lote y se cuenta aparte.
        int lotesFallidos = 0;
        using var limitador = new SemaphoreSlim(_concurrenciaIA);
        var tareas = lotes.Select(async lote =>
        {
            await limitador.WaitAsync(ct);
            try
            {
                // Pausa opcional para no exceder el límite de peticiones/minuto (free tier).
                if (_delayEntreLotesMs > 0) await Task.Delay(_delayEntreLotesMs, ct);
                return await _evaluador.EvaluarLoteAsync(lote, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Cancelar sí debe abortar (es el usuario); cualquier otro fallo se absorbe.
                Interlocked.Increment(ref lotesFallidos);
                return (IReadOnlyList<VeredictoGestion>)[];
            }
            finally { limitador.Release(); }
        });
        var resultados = await Task.WhenAll(tareas);

        // Nº de gestiones que se quedaron sin evaluar por lotes caídos, para poder avisarlo
        // en el informe en vez de reportar un resultado incompleto como si fuera completo.
        var gestionesSinEvaluar = lotesFallidos * TamanoLote;

        // El armado de hallazgos se hace AQUÍ (secuencial): la lista no es thread-safe.
        foreach (var v in resultados.SelectMany(r => r))
        {
            if (v.Indice < 0 || v.Indice >= conGestion.Count) continue;
            var res = (v.Resultado ?? "").Trim();
            if (res.StartsWith("Coincide", StringComparison.OrdinalIgnoreCase)) continue;   // sin hallazgo
            // "Ambiguo" → requiere revisión manual (no cuenta como crítico)
            var severidad = res.StartsWith("Ambiguo", StringComparison.OrdinalIgnoreCase) ? "revision" : "critico";
            hallazgos.Add(Fila(conGestion[v.Indice], "perfil-gestion", severidad, v.Justificacion));
        }

        // 6. Reglas determinísticas sobre la promesa (estas columnas SÍ tienen datos)
        foreach (var r in candidatas)
        {
            bool hayFecha = r.FechaPromesa is not null;
            bool hayValor = r.ValorPromesa is decimal v && v > 0;

            // Nivel 2 — medio: promesa incompleta (exactamente uno de fecha/valor presente)
            if (hayFecha && !hayValor)
                hallazgos.Add(Fila(r, "promesa-sin-valor", "medio",
                    "Hay fecha de promesa pero el valor de promesa está vacío."));
            else if (!hayFecha && hayValor)
                hallazgos.Add(Fila(r, "valor-sin-fecha", "medio",
                    "Hay valor de promesa pero la fecha de promesa está vacía."));
            // Nivel 3 — bajo: ambos presentes pero el valor tiene menos de 5 dígitos
            else if (hayFecha && hayValor && r.ValorPromesa is decimal vp && vp < 10000)
                hallazgos.Add(Fila(r, "valor-bajo", "bajo",
                    $"Valor de promesa ${vp:N0} tiene menos de 5 dígitos."));
        }

        // 7. Agregados — conteos por nivel de severidad
        var totalCriticas = hallazgos.Count(h => h.Severidad == "critico");
        var totalMedias   = hallazgos.Count(h => h.Severidad == "medio");
        var totalBajas    = hallazgos.Count(h => h.Severidad == "bajo");
        var totalRevision = hallazgos.Count(h => h.Severidad == "revision");
        // El % cuenta solo incongruencias confirmadas (crítico/medio/bajo), no las "requiere revisión".
        var confirmadas = totalCriticas + totalMedias + totalBajas;
        var pct = total > 0 ? Math.Round((decimal)confirmadas / total * 100, 1) : 0m;

        // Totales por cartera/perfil vienen del GROUP BY en SQL (mapeados a nombre)
        var totalPorCartera = conteoPorCartera.ToDictionary(
            x => nombreCartera.TryGetValue(x.CarteraID, out var n) ? n : $"Cartera {x.CarteraID}",
            x => x.Total);
        var incongPorCartera = hallazgos
            .GroupBy(h => h.Cartera)
            .Select(g => new IncongruenciaPorCartera
            {
                Cartera        = g.Key,
                Incongruencias = g.Count(),
                Total          = totalPorCartera.GetValueOrDefault(g.Key),
            })
            .OrderByDescending(x => x.Incongruencias)
            .ToList();

        var porLider = hallazgos
            .GroupBy(h => h.Lider)
            .Select(g => new GrupoLider { Lider = g.Key, Items = g.ToList() })
            .OrderByDescending(g => g.Items.Count)
            .ToList();

        var totalPorPerfil = conteoPorPerfil.ToDictionary(x => x.Perfil, x => x.Total);
        var tipRanking = hallazgos
            .Where(h => !string.IsNullOrWhiteSpace(h.UltimoPerfil))
            .GroupBy(h => h.UltimoPerfil)
            .Select(g => new TipificacionRanking
            {
                Tipificacion   = g.Key,
                Incongruencias = g.Count(),
                Total          = totalPorPerfil.GetValueOrDefault(g.Key),
            })
            .OrderByDescending(x => x.Incongruencias)
            .ToList();

        var (palabraTop, topPalabras) = CalcularPalabras(conGestion, Cartera);
        // Palabra más repetida SOLO en las gestiones con inconsistencia (los hallazgos).
        var palabraInc = PalabraTop(hallazgos.Select(h => h.Gestion));

        var respuesta = new AnalizarAuditoriaResponse
        {
            NombreArchivo = $"{carteraIDs.Count} cartera(s)",
            FilasLeidas   = total,
            Informe = new InformeAuditoria
            {
                TotalGestiones           = total,
                TotalGestionesConTexto   = totalConTexto,
                GestionesEvaluadasIA     = conGestion.Count,
                GestionesSinEvaluar      = gestionesSinEvaluar,
                TotalCriticas            = totalCriticas,
                TotalMedias              = totalMedias,
                TotalBajas               = totalBajas,
                TotalRevision            = totalRevision,
                PorcentajeIncongruencias = pct,
                DuracionPromedioMin      = 0m,   // la BD no tiene columna de duración
                IncongruenciasPorCartera = incongPorCartera,
                PalabraMasRepetida       = palabraTop,
                PalabraInconsistencias   = palabraInc,
                TopPalabras              = topPalabras,
                IncongruenciasPorLider   = porLider,
                TipificacionesConMasIncongruencias = tipRanking,
            },
        };

        // Guardar en el almacén compartido para que quede disponible a otros usuarios (48 h).
        if (cacheable) _store.Guardar(carteraIDs[0], filtro.FechaInicio, respuesta);
        return respuesta;
    }

    public async Task<EstimacionCostoResponse> EstimarCostoAsync(FiltroAuditoria filtro, CancellationToken ct = default)
    {
        var carteraIDs = filtro.Carteras.Select(c => c.CarteraID).Distinct().ToList();
        if (carteraIDs.Count == 0) return new EstimacionCostoResponse();

        // Solo las gestiones con texto van a la IA (las demás se evalúan con reglas, sin costo).
        var conGestion = await _cc.HistoricoEstadosTitular
            .Where(h => carteraIDs.Contains(h.CarteraID)
                     && h.FechaUltimaGestion >= filtro.FechaInicio
                     && h.FechaUltimaGestion <= filtro.FechaFin
                     && h.Gestion != null && h.Gestion != "")
            .Select(h => new GestionParaEvaluar(0, h.UltimoPerfil ?? "", h.Gestion ?? ""))
            .Take(TopeGestiones)
            .AsNoTracking()
            .ToListAsync(ct);

        var registrosIA = conGestion.Count;
        if (registrosIA == 0) return new EstimacionCostoResponse();

        // Conteo real de tokens de UN lote representativo y extrapolación por nº de lotes
        // (cada lote arrastra el mismo encabezado del prompt → extrapolar por lote es fiel).
        var loteMuestra = conGestion.Take(TamanoLote)
            .Select((g, i) => g with { Indice = i })
            .ToList();
        var tokensLote = await _evaluador.ContarTokensEntradaAsync(loteMuestra, ct);
        var numLotes   = (int)Math.Ceiling(registrosIA / (double)TamanoLote);

        var tokensEntrada = tokensLote * numLotes;
        var tokensSalida  = registrosIA * TokensSalidaPorRegistro;

        var costoEntrada = Math.Round(tokensEntrada * PrecioEntradaPorToken, 2);
        var costoSalida  = Math.Round(tokensSalida  * PrecioSalidaPorToken,  2);

        return new EstimacionCostoResponse
        {
            RegistrosIA   = registrosIA,
            TokensEntrada = tokensEntrada,
            TokensSalida  = tokensSalida,
            CostoEntrada  = costoEntrada,
            CostoSalida   = costoSalida,
            CostoTotal    = costoEntrada + costoSalida,
        };
    }

    // ── Frecuencia de palabras sobre el texto de gestión ────────────────────────
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de","la","el","en","y","a","los","las","un","una","que","con","por","para","se","su",
        "del","al","lo","le","es","no","si","o","mas","más","pero","como","ya","muy","sin","sobre",
        "com","www","co",   // ruido de dominios de correo (.com, www)
    };

    private static (PalabraMasRepetida, List<PalabraConteo>) CalcularPalabras(
        List<Registro> registros, Func<Registro, string> cartera)
    {
        var conteo   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var carteras = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in registros)
        {
            if (string.IsNullOrWhiteSpace(r.Gestion)) continue;
            foreach (var palabra in Tokenizar(r.Gestion))
            {
                conteo[palabra] = conteo.GetValueOrDefault(palabra) + 1;
                if (!carteras.TryGetValue(palabra, out var set)) carteras[palabra] = set = [];
                set.Add(cartera(r));
            }
        }

        var top = conteo.OrderByDescending(kv => kv.Value).Take(5).ToList();
        var topPalabras = top.Select(kv => new PalabraConteo { Palabra = kv.Key, Conteo = kv.Value }).ToList();

        var primera = top.FirstOrDefault();
        var palabraTop = primera.Key is null
            ? new PalabraMasRepetida()
            : new PalabraMasRepetida
            {
                Palabra  = primera.Key,
                Conteo   = primera.Value,
                Carteras = carteras[primera.Key].ToList(),
            };

        return (palabraTop, topPalabras);
    }

    // Primer nombre + primer apellido, en Title Case (ej. "LUISA FERNANDA" + "CASTRO MEDINA" → "Luisa Castro").
    private static string PrimerNombreApellido(string nombre, string apellidos)
    {
        var n = (nombre ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var a = (apellidos ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return TituloCase($"{n} {a}".Trim());
    }

    // Primera letra de cada palabra en mayúscula (RRHH guarda los nombres en MAYÚSCULAS).
    private static string TituloCase(string s) =>
        string.IsNullOrWhiteSpace(s) ? s
        : string.Join(' ', s.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    // Palabra más repetida sobre un conjunto de textos de gestión (sin rastrear carteras).
    private static PalabraMasRepetida PalabraTop(IEnumerable<string> textos)
    {
        var conteo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in textos)
            foreach (var palabra in Tokenizar(t ?? ""))
                conteo[palabra] = conteo.GetValueOrDefault(palabra) + 1;

        var top = conteo.OrderByDescending(kv => kv.Value).FirstOrDefault();
        return top.Key is null
            ? new PalabraMasRepetida()
            : new PalabraMasRepetida { Palabra = top.Key, Conteo = top.Value };
    }

    private static IEnumerable<string> Tokenizar(string texto) =>
        texto.Split(new[] { ' ', ',', '.', ';', ':', '\n', '\r', '\t', '(', ')', '/', '-', '"', '\'', '@', '_', '#', '*', '+', '=' },
                    StringSplitOptions.RemoveEmptyEntries)
             .Select(t => t.Trim().ToLowerInvariant())
             // Solo palabras (solo letras): descarta números, montos, correos y caracteres especiales.
             .Where(t => t.Length > 2 && t.All(char.IsLetter) && !Stopwords.Contains(t));

    // Proyección interna de una fila del histórico.
    private sealed class Registro
    {
        public long     TitularID    { get; init; }
        public int      CarteraID    { get; init; }
        public int?     AgenteID     { get; init; }
        public string   UltimoPerfil { get; init; } = "";
        public string   Gestion      { get; init; } = "";
        public DateOnly? FechaPromesa { get; init; }
        public decimal?  ValorPromesa { get; init; }
    }
}
