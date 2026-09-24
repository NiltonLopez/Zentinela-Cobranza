using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>Avance que el motor informa al job (fase, % global y texto para la barra).</summary>
internal sealed record ProgresoCargue(FaseCargueJob Fase, int Porcentaje, string Mensaje);

/// <summary>Lo que el motor necesita para procesar un archivo ya guardado en disco.</summary>
internal sealed record TrabajoCargue(
    Guid JobId, DefinicionFuenteCargue Definicion, IReglaFuente Regla,
    string RutaArchivo, string NombreArchivo, string Usuario);

/// <summary>
/// Motor genérico del Cargue de Fuentes de Datos (ADR-039). Scoped: usa el
/// <see cref="CallCenterDbContext"/> de su scope (el orquestador crea uno por trabajo).
///
/// Validar y confirmar corren las MISMAS fases 1–6 (leer, columnas, apoyo, limpieza, dedup,
/// cruces). Confirmar las repite sobre el archivo en disco en vez de reusar lo validado: el
/// job no retiene cientos de miles de filas en memoria entre pasos, y lo que se escribe es
/// exactamente lo re-parseado contra la BD de ese momento (patrón de la carga IVR).
///
/// Nada aquí es de una fuente concreta: el motor interpreta la definición y llama los
/// hooks de su <see cref="IReglaFuente"/>.
/// </summary>
internal sealed class CargueFuentesMotor
{
    private const int TimeoutSegundos = 600;
    private const int CadaNFilasProgreso = 5_000;

    // Celdas que en los archivos de las fuentes significan "sin dato".
    private static readonly HashSet<string> Vacios = new(StringComparer.OrdinalIgnoreCase) { "", "0", "NULL", "N/A" };

    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    private readonly CallCenterDbContext _db;
    private readonly ICalificadorCarteraCache _cache;
    private readonly ILogger<CargueFuentesMotor> _log;
    private readonly CargueFuentesSettings _opciones;

    public CargueFuentesMotor(
        CallCenterDbContext db, ICalificadorCarteraCache cache,
        ILogger<CargueFuentesMotor> log, IOptions<CargueFuentesSettings> opciones)
    {
        _db = db;
        _cache = cache;
        _log = log;
        _opciones = opciones.Value;
    }

    /// <summary>Tramos de % por fase: validar reparte 0–100 en la preparación; confirmar la deja en 0–50.</summary>
    private sealed record Escala(int LeyendoFin, int ApoyoFin, int LimpiezaFin, int DedupFin, int CrucesFin);

    private static readonly Escala EscalaValidar   = new(15, 20, 60, 65, 90);
    private static readonly Escala EscalaConfirmar = new(8, 10, 30, 33, 50);

    // ════════════════════════════════════════════════════════════════════════
    //  API del motor
    // ════════════════════════════════════════════════════════════════════════

    public async Task<PreviewCargueDto> ValidarAsync(
        TrabajoCargue t, IProgress<ProgresoCargue> progreso, CancellationToken ct)
    {
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        // Solo cerramos si la abrimos nosotros: cerrarla tiraría las #temp de otro código del scope.
        var laAbriYo = conn.State != ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var prep = await PrepararAsync(t, conn, EscalaValidar, progreso, ct);
            progreso.Report(new(FaseCargueJob.Preview, 92, "Armando el preview"));
            var preview = await ArmarPreviewAsync(prep, conn, ct);
            progreso.Report(new(FaseCargueJob.Preview, 100, "Preview listo"));
            return preview;
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Registra la ejecución EN CURSO en <c>CargueFuenteEjecucion</c>. El orquestador la llama
    /// ANTES de lanzar la carga: si el proceso muere a mitad, la fila queda como evidencia.
    /// Conexión propia: no depende del estado de la conexión del DbContext.
    /// </summary>
    public async Task<long> RegistrarInicioAsync(TrabajoCargue t, string huella, string usuario, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(GeneradorSqlCargue.RegistrarInicio, conn);
        cmd.Parameters.Add("@JobID", SqlDbType.UniqueIdentifier).Value = t.JobId;
        cmd.Parameters.Add("@Fuente", SqlDbType.VarChar, 30).Value = Recortar(t.Definicion.Fuente, 30);
        cmd.Parameters.Add("@Usuario", SqlDbType.VarChar, 100).Value = Recortar(usuario, 100);
        cmd.Parameters.Add("@NombreArchivo", SqlDbType.NVarChar, 260).Value = Recortar(t.NombreArchivo, 260);
        cmd.Parameters.Add("@Huella", SqlDbType.Char, 64).Value = huella;
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Re-procesa el archivo y escribe en UNA transacción. <paramref name="simular"/> = true
    /// hace todo y revierte (harness, comparación con los SP legacy): no toca la auditoría
    /// ni la caché. Si falla o se cancela, la transacción se revierte y la ejecución queda
    /// ERROR / CANCELADA.
    /// </summary>
    public async Task<ResultadoCargueDto> ConfirmarAsync(
        TrabajoCargue t, long ejecucionID, string? huellaEsperada, bool simular,
        IProgress<ProgresoCargue> progreso, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        var laAbriYo = conn.State != ConnectionState.Open;
        try
        {
            if (laAbriYo) await _db.Database.OpenConnectionAsync(ct);

            var prep = await PrepararAsync(t, conn, EscalaConfirmar, progreso, ct);
            if (huellaEsperada is not null && !string.Equals(prep.Huella, huellaEsperada, StringComparison.OrdinalIgnoreCase))
                throw new CargueArchivoRechazadoException(
                    "El archivo en disco no coincide con el que se validó. Vuelve a subirlo.");

            var escritura = await EscribirAsync(t.Definicion, prep, conn, ejecucionID, simular, progreso, ct);

            var resultado = new ResultadoCargueDto
            {
                EjecucionID        = ejecucionID,
                SelloFechaConsulta = escritura.Sello,
                Insertadas         = escritura.Insertadas,
                Refrescadas        = escritura.Refrescadas,
                DescartadasVacias  = escritura.DescartadasVacias,
                VaciasPurgadas     = escritura.VaciasPurgadas,
                TitularesNuevos    = escritura.TitularesNuevos,
                NombresReparados   = escritura.NombresReparados,
                NombresIrrecuperables = prep.NombresIrrecuperables,
                RelacionesNuevas   = escritura.RelacionesNuevas,
                Advertencias       = prep.Ctx.TotalAdvertencias,
                Mensaje = (simular ? "Simulación (se revirtió, sin cambios): " : $"Cargue de {t.Definicion.Etiqueta} completado: ")
                          + $"{escritura.Insertadas.ToString("N0", Co)} insertadas, "
                          + $"{escritura.Refrescadas.ToString("N0", Co)} refrescadas.",
            };
            resultado.Desglose = prep.Ctx.Desglose.ParaResultado(resultado);   // desglose (DesgloseCargue.cs)

            if (!simular)
            {
                progreso.Report(new(FaseCargueJob.Auditoria, 94, "Registrando la auditoría"));
                await RegistrarFinOkAsync(ejecucionID, prep, escritura);
                // El Calificador cachea hasta 24 h cifras que salen de estas tablas (vía
                // fn_BaseMadreParaTitulares) y solo se limpia al corte de la 1 p. m.
                _cache.InvalidarTodo();
            }

            resultado.DuracionSegundos = Math.Round(reloj.Elapsed.TotalSeconds, 1);
            progreso.Report(new(FaseCargueJob.Terminado, 100, resultado.Mensaje));
            return resultado;
        }
        catch (Exception ex) when (!simular && ejecucionID > 0)
        {
            var estado = ct.IsCancellationRequested ? "CANCELADA" : "ERROR";
            await RegistrarFinFalloAsync(ejecucionID, estado, estado == "CANCELADA" ? "Cancelada por el usuario." : ex.Message);
            throw;
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Fases 1–6
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Resultado de las fases 1–6; #Carga y #Relaciones quedan pobladas en la conexión.</summary>
    private sealed class Preparacion
    {
        public required ContextoCargue Ctx { get; init; }
        public required string Huella { get; init; }
        public required List<string> ColumnasEncontradas { get; init; }
        public required List<string> ColumnasOpcionalesFaltantes { get; init; }
        public int TotalLeidas { get; set; }
        public int Excluidas { get; set; }
        public int DuplicadasEnArchivo { get; set; }
        public int AgregadasPorExpansion { get; set; }
        public int Candidatas { get; set; }
        public int Insertadas { get; set; }
        public int Refrescadas { get; set; }
        public int DescartadasVacias { get; set; }
        public int VaciasQuePurgara { get; set; }
        public int TitularesNuevos { get; set; }
        public int NombresReparados { get; set; }
        public int NombresIrrecuperables { get; set; }
        public int RelacionesNuevas { get; set; }
        public int FilasSinCarteraLider { get; set; }
        public DateOnly SelloPrevisto { get; set; }
    }

    /// <summary>Índices (0-based) de cada columna en el archivo; -1 = no vino.</summary>
    private sealed class MapaColumnas
    {
        public int TitularID, Nombre, CarteraID, Cartera, LiderID, Lider;
        public required int[] Datos { get; init; }
        public required int[] Extra { get; init; }
        public bool HayCartera => CarteraID >= 0 || Cartera >= 0;
        public bool HayLider   => LiderID >= 0 || Lider >= 0;
    }

    private async Task<Preparacion> PrepararAsync(
        TrabajoCargue t, SqlConnection conn, Escala esc, IProgress<ProgresoCargue> progreso, CancellationToken ct)
    {
        var def = t.Definicion;
        GeneradorSqlCargue.ValidarDefinicion(def);
        var ctx = new ContextoCargue(def);

        // ── 1. Leyendo ──────────────────────────────────────────────────────
        progreso.Report(new(FaseCargueJob.Leyendo, 0, "Calculando la huella del archivo"));
        var (huella, saltosDeLinea) = await HuellaYLineasAsync(t.RutaArchivo, ct);
        var esXlsx = Path.GetExtension(t.RutaArchivo).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

        int totalEstimado;
        if (esXlsx)
        {
            progreso.Report(new(FaseCargueJob.Leyendo, esc.LeyendoFin / 2, "Contando filas del Excel"));
            await using var conteo = AbrirArchivo(t.RutaArchivo);
            totalEstimado = Math.Max(ArchivoTabular.ContarFilasXlsx(conteo) - 1, 0);
            if (totalEstimado > _opciones.LimiteFilasXlsx)
                throw new CargueArchivoRechazadoException(
                    $"El Excel tiene {totalEstimado.ToString("N0", Co)} filas y el máximo para XLSX es "
                    + $"{_opciones.LimiteFilasXlsx.ToString("N0", Co)}. Guárdalo como CSV y vuelve a subirlo: "
                    + "el CSV se lee por partes y no tiene tope.");
        }
        else
        {
            // Estimación para la barra: un campo entre comillas con saltos de línea la infla un poco.
            totalEstimado = Math.Max(saltosDeLinea, 1);
        }

        await using var archivo = AbrirArchivo(t.RutaArchivo);
        var filas = esXlsx ? ComoAsync(ArchivoTabular.LeerXlsxPorFilas(archivo), ct) : ArchivoTabular.LeerCsvPorFilasAsync(archivo, ct);
        await using var cursor = filas.GetAsyncEnumerator(ct);
        if (!await cursor.MoveNextAsync())
            throw new CargueArchivoRechazadoException("El archivo está vacío.");
        var encabezado = cursor.Current;

        // ── 2. Columnas ─────────────────────────────────────────────────────
        progreso.Report(new(FaseCargueJob.Columnas, esc.LeyendoFin, "Resolviendo columnas"));
        var (mapa, encontradas, faltantes, opcionalesFaltantes, sinUso) = ResolverColumnas(def, encabezado);
        foreach (var c in sinUso) ctx.Informar($"Columna del archivo que no se usa: {c}");
        // "Ninguna columna de datos" cuenta también las extra: en SIMIT el dato (Placa) no viene
        // del archivo, lo producen Placa1..15.
        if (faltantes.Count > 0 || (mapa.Datos.All(i => i < 0) && mapa.Extra.All(i => i < 0)))
        {
            var mensaje = faltantes.Count > 0
                ? $"Faltan columnas obligatorias: {string.Join(", ", faltantes)}."
                : "El archivo no trae ninguna columna de datos de la fuente.";
            throw new CargueArchivoRechazadoException(mensaje, new PreviewCargueDto
            {
                HuellaArchivo = huella,
                ColumnasEncontradas = encontradas,
                ColumnasFaltantes = faltantes.Count > 0
                    ? faltantes
                    : def.ColumnasDato.Where(c => c.DesdeArchivo).Concat(def.ColumnasArchivoExtra).Select(c => c.Nombre).ToList(),
                ColumnasOpcionalesFaltantes = opcionalesFaltantes,
                Info = ctx.Info(),
            });
        }

        // ── 3. Apoyo ────────────────────────────────────────────────────────
        progreso.Report(new(FaseCargueJob.Apoyo, esc.LeyendoFin, "Cargando lista negra y catálogos"));
        await CargarApoyoAsync(t, ctx, mapa, conn, ct);

        var prep = new Preparacion
        {
            Ctx = ctx,
            Huella = huella,
            ColumnasEncontradas = encontradas,
            ColumnasOpcionalesFaltantes = opcionalesFaltantes,
        };

        // ── 4. Limpieza ─────────────────────────────────────────────────────
        var validas = new List<FilaCargue>();
        while (await cursor.MoveNextAsync())
        {
            prep.TotalLeidas++;
            if (prep.TotalLeidas % 1_000 == 0) ct.ThrowIfCancellationRequested();
            if (prep.TotalLeidas % CadaNFilasProgreso == 0)
            {
                var fraccion = Math.Min(1.0, prep.TotalLeidas / (double)totalEstimado);
                progreso.Report(new(FaseCargueJob.Limpieza,
                    esc.ApoyoFin + (int)((esc.LimpiezaFin - esc.ApoyoFin) * fraccion),
                    $"Limpiando filas {prep.TotalLeidas.ToString("N0", Co)} / {totalEstimado.ToString("N0", Co)}"));
            }

            var fila = LimpiarFila(cursor.Current, prep.TotalLeidas + 1, encabezado.Length, mapa, t.Regla, ctx);
            if (fila is null) { prep.Excluidas++; continue; }

            var expandidas = t.Regla.Expandir(fila, ctx).ToList();
            if (expandidas.Count == 0)
            {
                ctx.Excluir(fila, "La regla de la fuente no produjo filas");
                prep.Excluidas++;
                continue;
            }
            prep.AgregadasPorExpansion += expandidas.Count - 1;
            ctx.Desglose.Expansion(expandidas);
            foreach (var f in expandidas)
            {
                CalcularFlag(f, def);
                validas.Add(f);
            }
        }

        // ── 4b. Lista negra de teléfonos (si la regla la pide) ─────────────
        // Antes de deduplicar: dos filas que solo difieren en un teléfono vetado quedan iguales.
        if (t.Regla.NecesitaListaNegraTelefonos)
        {
            progreso.Report(new(FaseCargueJob.Limpieza, esc.LimpiezaFin, "Cruzando teléfonos con ListaNegraTelefonos"));
            validas = await AplicarListaNegraTelefonosAsync(validas, t.Regla, ctx, prep, conn, ct);
        }

        // ── 5. Dedup ────────────────────────────────────────────────────────
        progreso.Report(new(FaseCargueJob.Dedup, esc.LimpiezaFin, "Quitando repetidos del archivo"));
        // Las relaciones se toman de TODAS las filas válidas, antes de deduplicar: el mismo
        // titular en dos carteras son dos relaciones aunque su dato de contacto sea uno.
        var relaciones = def.CreaTitularCartera
            ? validas.Where(f => f.CarteraID is not null && f.LiderID is not null)
                     .Select(f => (f.TitularID, LiderID: f.LiderID!.Value, CarteraID: f.CarteraID!.Value))
                     .ToHashSet()
            : [];
        var candidatas = Deduplicar(validas, def, ctx, out var duplicadas);
        prep.DuplicadasEnArchivo = duplicadas;
        validas = null!;   // liberar: con 1M de filas la lista pesa

        // ── 6. Cruces (solo lectura sobre las tablas reales) ────────────────
        progreso.Report(new(FaseCargueJob.Cruces, esc.DedupFin, "Cruzando con Titular"));
        candidatas = await CruzarTitularesAsync(candidatas, def, prep, conn, ct);

        var titularesVivos = candidatas.Select(f => f.TitularID).ToHashSet();
        relaciones.RemoveWhere(r => !titularesVivos.Contains(r.TitularID));
        if (def.CreaTitularCartera)
        {
            var conRelacion = relaciones.Select(r => r.TitularID).ToHashSet();
            prep.FilasSinCarteraLider = candidatas.Count(f => !conRelacion.Contains(f.TitularID));
            ctx.Desglose.SinRelacion(candidatas.Where(f => !conRelacion.Contains(f.TitularID)));
        }

        progreso.Report(new(FaseCargueJob.Cruces, esc.DedupFin + (esc.CrucesFin - esc.DedupFin) / 3, "Subiendo filas a la tabla de trabajo"));
        await EjecutarAsync(conn, null, GeneradorSqlCargue.CrearTablaCarga(def), ct);
        await BulkAsync(conn, "#Carga", TablaCarga(def, candidatas), ct);
        await BulkAsync(conn, "#Relaciones", TablaRelaciones(relaciones), ct);
        prep.Candidatas = candidatas.Count;
        ctx.Desglose.Candidatas(candidatas);
        candidatas = null!;

        if (def.CreaTitularCartera)
            prep.RelacionesNuevas = await EscalarIntAsync(conn, null, GeneradorSqlCargue.ContarRelacionesNuevas, ct);

        progreso.Report(new(FaseCargueJob.Cruces, esc.DedupFin + 2 * (esc.CrucesFin - esc.DedupFin) / 3, $"Comparando contra {def.TablaDestino}"));
        await EjecutarAsync(conn, null, GeneradorSqlCargue.MarcarImpacto(def), ct);
        await using (var cmd = Comando(conn, null, GeneradorSqlCargue.ConsultarImpacto(def)))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            await rd.ReadAsync(ct);
            prep.Refrescadas       = rd.GetInt32(1);
            prep.DescartadasVacias = rd.GetInt32(2);
            prep.Insertadas        = rd.GetInt32(3);
            prep.VaciasQuePurgara  = rd.GetInt32(4);
            if (rd.GetInt32(0) != prep.Candidatas)
                throw new InvalidOperationException(
                    $"#Carga tiene {rd.GetInt32(0)} filas y se subieron {prep.Candidatas}: carga de trabajo incompleta.");
        }

        var sello = await EscalarAsync(conn, null, GeneradorSqlCargue.LeerSello(def), ct);
        prep.SelloPrevisto = DateOnly.FromDateTime((DateTime)sello!);

        // Ejemplos del desglose que dependen de la BD: TOP (10) por grupo sobre #Carga / #Relaciones.
        await ctx.Desglose.ConsultarBdAsync(conn, prep.Refrescadas, prep.DescartadasVacias, prep.VaciasQuePurgara,
                                            prep.RelacionesNuevas, TimeoutSegundos, ct);

        ctx.Informar("Titulares nuevos que se crearán", prep.TitularesNuevos);
        ctx.Informar("Nombres de titular rotos en BD que se repararán", prep.NombresReparados);
        ctx.Informar("Relaciones titular-cartera nuevas", prep.RelacionesNuevas);
        ctx.Informar("Filas vacías que no entran porque el titular ya tiene datos", prep.DescartadasVacias);
        ctx.Informar($"Filas vacías viejas que se eliminarán de {def.TablaDestino}", prep.VaciasQuePurgara);

        // Invariante 1 (el "éxito falso" de ADR-038: si los conteos no cuadran, algo se perdió
        // en silencio). Con expansión 1→N, cada fila agregada cuenta como una leída más.
        if (prep.TotalLeidas + prep.AgregadasPorExpansion != prep.Excluidas + prep.DuplicadasEnArchivo + prep.Candidatas)
            throw new InvalidOperationException(
                $"Conteos incoherentes: leídas {prep.TotalLeidas} (+{prep.AgregadasPorExpansion} por expansión) ≠ "
                + $"excluidas {prep.Excluidas} + duplicadas {prep.DuplicadasEnArchivo} + candidatas {prep.Candidatas}.");
        if (prep.Candidatas != prep.Insertadas + prep.Refrescadas + prep.DescartadasVacias)
            throw new InvalidOperationException(
                $"Conteos incoherentes: candidatas {prep.Candidatas} ≠ insertadas {prep.Insertadas} + "
                + $"refrescadas {prep.Refrescadas} + vacías {prep.DescartadasVacias}.");

        progreso.Report(new(FaseCargueJob.Cruces, esc.CrucesFin, "Cruces terminados"));
        return prep;
    }

    // ── 2. Columnas ─────────────────────────────────────────────────────────

    private static (MapaColumnas Mapa, List<string> Encontradas, List<string> Faltantes,
                    List<string> OpcionalesFaltantes, List<string> SinUso)
        ResolverColumnas(DefinicionFuenteCargue def, string[] encabezado)
    {
        var headers = ArchivoTabular.LeerHeaders(encabezado);

        // Un alias que la fuente usa para una columna de dato no sirve para las comunes:
        // en Sura "NIT" es el del empleador, no el documento del titular.
        var aliasDeDatos = def.ColumnasDato.Concat(def.ColumnasArchivoExtra)
            .SelectMany(c => c.Alias).ToHashSet(StringComparer.Ordinal);
        int Comun(string[] alias) =>
            ArchivoTabular.ResolverColumna(headers, alias.Where(a => !aliasDeDatos.Contains(a)).ToArray());

        var mapa = new MapaColumnas
        {
            TitularID = Comun(CatalogoCargueFuentes.AliasTitularId),
            Nombre    = def.CreaTitular ? Comun(CatalogoCargueFuentes.AliasNombre) : -1,
            CarteraID = def.CreaTitularCartera ? Comun(CatalogoCargueFuentes.AliasCarteraId) : -1,
            Cartera   = def.CreaTitularCartera ? Comun(CatalogoCargueFuentes.AliasCartera) : -1,
            LiderID   = def.CreaTitularCartera ? Comun(CatalogoCargueFuentes.AliasLiderId) : -1,
            Lider     = def.CreaTitularCartera ? Comun(CatalogoCargueFuentes.AliasLider) : -1,
            // Una columna que produce la regla (DesdeArchivo = false) no se busca en el archivo.
            Datos     = def.ColumnasDato.Select(c => c.DesdeArchivo ? ArchivoTabular.ResolverColumna(headers, c.Alias) : -1).ToArray(),
            Extra     = def.ColumnasArchivoExtra.Select(c => ArchivoTabular.ResolverColumna(headers, c.Alias)).ToArray(),
        };

        var encontradas = new List<string>();
        var faltantes = new List<string>();
        var opcionales = new List<string>();
        void Anotar(string nombre, int indice, bool obligatoria)
        {
            if (indice >= 0) encontradas.Add(nombre);
            else if (obligatoria) faltantes.Add(nombre);
            else opcionales.Add(nombre);
        }

        Anotar("TitularID", mapa.TitularID, true);
        if (def.CreaTitular) Anotar("Nombre", mapa.Nombre, true);
        if (def.CreaTitularCartera)
        {
            if (mapa.CarteraID >= 0) encontradas.Add("CarteraID");
            if (mapa.Cartera >= 0) encontradas.Add("Cartera");
            if (!mapa.HayCartera) opcionales.Add("CarteraID o Cartera");
            if (mapa.LiderID >= 0) encontradas.Add("LiderID");
            if (mapa.Lider >= 0) encontradas.Add("Lider");
            if (!mapa.HayLider) opcionales.Add("LiderID o Lider");
        }
        for (var i = 0; i < def.ColumnasDato.Count; i++)
            if (def.ColumnasDato[i].DesdeArchivo)
                Anotar(def.ColumnasDato[i].Nombre, mapa.Datos[i], def.ColumnasDato[i].Obligatoria);
        for (var i = 0; i < def.ColumnasArchivoExtra.Count; i++)
            Anotar(def.ColumnasArchivoExtra[i].Nombre, mapa.Extra[i], def.ColumnasArchivoExtra[i].Obligatoria);

        var usadas = new HashSet<int>(new[] { mapa.TitularID, mapa.Nombre, mapa.CarteraID, mapa.Cartera, mapa.LiderID, mapa.Lider }
            .Concat(mapa.Datos).Concat(mapa.Extra).Where(i => i >= 0));
        var sinUso = encabezado
            .Select((h, i) => (h, i))
            .Where(x => !usadas.Contains(x.i) && !string.IsNullOrWhiteSpace(x.h))
            .Select(x => x.h.Trim())
            .ToList();

        return (mapa, encontradas, faltantes, opcionales, sinUso);
    }

    // ── 3. Apoyo ────────────────────────────────────────────────────────────

    private async Task CargarApoyoAsync(
        TrabajoCargue t, ContextoCargue ctx, MapaColumnas mapa, SqlConnection conn, CancellationToken ct)
    {
        var def = t.Definicion;

        if (def.ColumnasDato.Any(c => c.Tipo == TipoColumnaCargue.Email))
        {
            var entradas = new List<string>();
            await using (var cmd = Comando(conn, null, "SELECT CorreoErroneo FROM dbo.ListaNegraCorreos;"))
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
                while (await rd.ReadAsync(ct))
                    if (!rd.IsDBNull(0)) entradas.Add(rd.GetString(0));
            ctx.ListaNegraCorreos = ListaNegraCorreos.Cargar(entradas);
            ctx.Informar("Entradas de ListaNegraCorreos ignoradas (no son correo ni dominio)", ctx.ListaNegraCorreos.Ignoradas);
        }

        if (t.Regla.NecesitaPrefijos)
        {
            var filas = new List<(string, string, string)>();
            await using (var cmd = Comando(conn, null, "SELECT Departamento, Municipio, Prefijo FROM dbo.PrefijoMunicipio;"))
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
                while (await rd.ReadAsync(ct))
                    filas.Add((rd.IsDBNull(0) ? "" : rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1),
                               rd.IsDBNull(2) ? "" : rd.GetString(2)));
            ctx.Prefijos = PrefijosFijo.Cargar(filas);
        }

        if (def.CreaTitularCartera && (mapa.HayCartera || mapa.HayLider))
            ctx.Indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(_db, ct);
    }

    // ── 4. Limpieza ─────────────────────────────────────────────────────────

    /// <summary>
    /// Limpia una fila. Devuelve null si se excluye (con su motivo ya contado). Orden fijo de
    /// exclusión, un solo motivo por fila: fila vacía → fila corrida → documento inválido →
    /// regla de la fuente. Todo lo demás es advertencia: la fila entra con el dato anulado.
    /// </summary>
    private static FilaCargue? LimpiarFila(
        string[] cruda, int linea, int anchoEncabezado, MapaColumnas mapa, IReglaFuente regla, ContextoCargue ctx)
    {
        var def = ctx.Definicion;

        ctx.EnFila(linea, null);
        if (cruda.All(string.IsNullOrWhiteSpace))
        {
            ctx.ExcluirCruda("Fila vacía", linea);
            return null;
        }
        if (cruda.Length < anchoEncabezado)
        {
            ctx.ExcluirCruda("La fila tiene menos columnas que el encabezado (fila corrida)", linea,
                $"{DesgloseCargue.Valor(Celda(cruda, mapa.TitularID))} · {cruda.Length} de {anchoEncabezado} columnas");
            return null;
        }
        if (!TryDocumento(Celda(cruda, mapa.TitularID), out var titularId))
        {
            ctx.ExcluirCruda("Documento inválido o vacío (se esperan de 5 a 15 dígitos)", linea,
                $"documento {DesgloseCargue.Valor(Celda(cruda, mapa.TitularID))}");
            return null;
        }
        ctx.EnFila(linea, titularId);

        var fila = new FilaCargue
        {
            Linea     = linea,
            TitularID = titularId,
            Datos     = new object?[def.ColumnasDato.Count],
            Extra     = mapa.Extra.Select(i => Celda(cruda, i)).ToArray(),
        };

        if (mapa.Nombre >= 0) AsignarNombre(fila, Celda(cruda, mapa.Nombre), ctx);

        if (def.CreaTitularCartera) ResolverRelacion(cruda, mapa, fila, ctx);

        // Tipado genérico (antes de la regla).
        for (var i = 0; i < def.ColumnasDato.Count; i++)
            fila.Datos[i] = Tipar(def.ColumnasDato[i], Celda(cruda, mapa.Datos[i]), ctx);

        regla.Transformar(fila, ctx);

        // Validación de contacto (después de la regla: lo que ella no arregló, se anula).
        for (var i = 0; i < def.ColumnasDato.Count; i++)
            fila.Datos[i] = ValidarContacto(def.ColumnasDato[i], fila.Datos[i], ctx);

        regla.Validar(fila, ctx);
        return fila.MotivoExclusion is null ? fila : null;
    }

    /// <summary>
    /// Nombre del titular limpio (ver <see cref="NormalizadorTitular"/>), con un aviso agrupado
    /// por cada cosa que se le hizo: reparar mojibake, marcarlo irrecuperable o recortarlo.
    /// </summary>
    internal static void AsignarNombre(FilaCargue fila, string? bruto, ContextoCargue ctx)
    {
        var n = NormalizadorTitular.LimpiarNombre(bruto);
        fila.Nombre = n.Nombre.Length == 0 ? null : n.Nombre;
        fila.NombreReparado = n.Reparado;
        fila.NombreIrrecuperable = n.Irrecuperable;
        if (n.Reparado) ctx.Advertir("Nombre con mojibake reparado (p. ej. 'Ã‘' → 'Ñ')", $"{bruto?.Trim()} → {n.Nombre}");
        if (n.Irrecuperable) ctx.Advertir("Nombre con caracteres irrecuperables ('?'): no se usa para reparar Titular", n.Nombre);
        if (n.Truncado) ctx.Advertir($"Nombre: truncado a {NormalizadorTitular.LargoMaximo} caracteres", bruto?.Trim());
    }

    private static string? Celda(string[] cruda, int indice) =>
        indice >= 0 && indice < cruda.Length ? cruda[indice] : null;

    /// <summary>Documento: solo dígitos (se toleran puntos y el ".0" de Excel), 5 a 15 sin ceros a la izquierda.</summary>
    private static bool TryDocumento(string? bruto, out long documento)
    {
        documento = 0;
        if (string.IsNullOrWhiteSpace(bruto)) return false;
        var t = QuitarSufijoDecimal(bruto);
        if (t.Any(char.IsLetter)) return false;
        var digitos = new string(t.Where(char.IsDigit).ToArray()).TrimStart('0');
        return digitos.Length is >= 5 and <= 15 && long.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out documento);
    }

    private static string QuitarSufijoDecimal(string s)
    {
        var t = s.Trim();
        return t.EndsWith(".0", StringComparison.Ordinal) ? t[..^2] : t;
    }

    /// <summary>
    /// Trim + espacios colapsados; los vacíos convencionales ('', '0', 'NULL', 'N/A') a null.
    /// Interno: las reglas lo usan sobre <c>FilaCargue.Extra</c>, que llega crudo.
    /// </summary>
    internal static string? LimpiarTexto(string? bruto)
    {
        if (bruto is null) return null;
        var t = string.Join(' ', bruto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Vacios.Contains(t) ? null : t;
    }

    private static void ResolverRelacion(string[] cruda, MapaColumnas mapa, FilaCargue fila, ContextoCargue ctx)
    {
        var carteraId = LimpiarTexto(Celda(cruda, mapa.CarteraID));
        var cartera   = LimpiarTexto(Celda(cruda, mapa.Cartera));
        var liderId   = LimpiarTexto(Celda(cruda, mapa.LiderID));
        var lider     = LimpiarTexto(Celda(cruda, mapa.Lider));
        var hayCartera = carteraId is not null || cartera is not null;
        var hayLider   = liderId is not null || lider is not null;

        // ctx.SinRelacion = advertencia + motivo en la fila (desglose del chip "sin cartera/líder").
        if (!hayCartera && !hayLider) return;   // opcionales: sin advertencia
        if (!hayLider)   { ctx.SinRelacion(fila, "Cartera sin líder: la fila entra sin relación", carteraId ?? cartera); return; }
        if (!hayCartera) { ctx.SinRelacion(fila, "Líder sin cartera: la fila entra sin relación", liderId ?? lider); return; }

        var indices = ctx.Indices!;
        int idCartera, idLider;

        if (carteraId is not null)
        {
            if (!int.TryParse(QuitarSufijoDecimal(carteraId), NumberStyles.None, CultureInfo.InvariantCulture, out idCartera))
            { ctx.SinRelacion(fila, "CarteraID no numérico: la fila entra sin relación", carteraId); return; }
        }
        else if (indices.CarteraPorNombre.TryGetValue(ArchivoTabular.Normalizar(cartera!), out var c)) idCartera = c.ID;
        else { ctx.SinRelacion(fila, $"Cartera '{cartera}' no encontrada (o inactiva): la fila entra sin relación"); return; }

        if (liderId is not null)
        {
            if (!int.TryParse(QuitarSufijoDecimal(liderId), NumberStyles.None, CultureInfo.InvariantCulture, out idLider))
            { ctx.SinRelacion(fila, "LiderID no numérico: la fila entra sin relación", liderId); return; }
        }
        else if (indices.LiderPorNombre.TryGetValue(ArchivoTabular.Normalizar(lider!), out var l)) idLider = l.ID;
        else { ctx.SinRelacion(fila, $"Líder '{lider}' no encontrado: la fila entra sin relación"); return; }

        if (!indices.ParesActivos.Contains((idLider, idCartera)))
        {
            ctx.SinRelacion(fila, $"Líder {idLider} y cartera {idCartera} no son un par activo de Lider_Cartera: la fila entra sin relación");
            return;
        }
        fila.CarteraID = idCartera;
        fila.LiderID = idLider;
    }

    /// <summary>Tipado genérico por columna, antes de la regla. Contacto: solo limpieza superficial.</summary>
    private static object? Tipar(ColumnaFuenteCargue col, string? bruto, ContextoCargue ctx)
    {
        var texto = LimpiarTexto(bruto);
        if (texto is null) return null;

        switch (col.Tipo)
        {
            case TipoColumnaCargue.Texto:
                if (col.Largo > 0 && texto.Length > col.Largo)
                {
                    // Nunca dejar que SQL lance "String or binary data would be truncated" a
                    // mitad de un millón de filas: se trunca aquí y se avisa.
                    ctx.Advertir($"{col.Nombre}: valor truncado a {col.Largo} caracteres", texto);
                    return texto[..col.Largo].TrimEnd();
                }
                return texto;

            case TipoColumnaCargue.Entero:
                var numero = QuitarSufijoDecimal(texto).Replace(".", "").Replace(",", "").Replace(" ", "");
                if (numero.All(char.IsDigit) && long.TryParse(numero, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    return n == 0 ? null : n;
                ctx.Advertir($"{col.Nombre}: valor no numérico anulado", texto);
                return null;

            case TipoColumnaCargue.Fecha:
                if (ArchivoTabular.TryParseFecha(texto, out var fecha))
                    return fecha.Year <= 1900 ? null : fecha;   // 1900-01-01 es el "vacío" de Excel
                ctx.Advertir($"{col.Nombre}: fecha no reconocida anulada", texto);
                return null;

            default:
                return texto;
        }
    }

    /// <summary>Validación fuerte de teléfonos, correos y placas; lo inválido se anula con advertencia.</summary>
    internal static object? ValidarContacto(ColumnaFuenteCargue col, object? valor, ContextoCargue ctx)
    {
        if (valor is not string texto) return valor;

        switch (col.Tipo)
        {
            case TipoColumnaCargue.Celular:
            {
                var celular = TelefonoColombiano.NormalizarCelular(texto);
                if (celular is null)
                    ctx.Advertir(TelefonoColombiano.Normalizar(texto).Numero is not null
                        ? $"{col.Nombre}: número fijo en la columna de celular, anulado"
                        : $"{col.Nombre}: celular inválido anulado", texto);
                return celular;
            }
            case TipoColumnaCargue.Fijo:
            {
                var fijo = TelefonoColombiano.NormalizarFijo(texto);
                if (fijo is null)
                    ctx.Advertir(TelefonoColombiano.Normalizar(texto).EsCelular
                        ? $"{col.Nombre}: celular en la columna de fijo, anulado"
                        : $"{col.Nombre}: fijo inválido anulado", texto);
                return fijo;
            }
            case TipoColumnaCargue.Telefono:
            {
                // Celular (empieza en 3) o fijo (empieza en 6). Diez dígitos con otro inicio
                // ('5551234567', '1234567890') no son un teléfono colombiano marcable.
                var numero = TelefonoColombiano.NormalizarCelular(texto) ?? TelefonoColombiano.NormalizarFijo(texto);
                if (numero is null) ctx.Advertir($"{col.Nombre}: teléfono inválido anulado", texto);
                return numero;
            }
            case TipoColumnaCargue.Email:
            {
                // Se normaliza sin tope de largo para distinguir "no es un correo" de "no cabe en
                // la columna": los dos se anulan, pero el aviso debe decir cuál de los dos pasó.
                var largo = col.Largo > 0 ? col.Largo : 255;
                var email = EmailColombiano.Normalizar(texto, int.MaxValue);
                if (email is null)
                {
                    ctx.Advertir($"{col.Nombre}: email con formato inválido anulado", texto);
                    return null;
                }
                if (email.Length > largo)
                {
                    // Un correo truncado es un correo equivocado: se anula, no se recorta.
                    ctx.Advertir($"{col.Nombre}: email de más de {largo} caracteres anulado (no cabe en la columna)", email);
                    return null;
                }
                if (ctx.ListaNegraCorreos?.Contiene(email) == true)
                {
                    ctx.Advertir($"{col.Nombre}: email en lista negra anulado", email);
                    return null;
                }
                return email;
            }
            case TipoColumnaCargue.Placa:
            {
                var placa = PlacaVehiculo.Normalizar(texto);
                if (placa is null) return null;
                // Marcas de texto ('PAZ Y SALVO', 'NO CARGO', 'SIN'...) no son placas: ver
                // PlacaVehiculo.EsValida. Mensaje fijo (sin el valor) para que agrupe.
                if (!PlacaVehiculo.EsValida(placa))
                {
                    ctx.Advertir($"{col.Nombre}: valor que no es una placa anulado", texto);
                    return null;
                }
                if (col.Largo > 0 && placa.Length > col.Largo)
                {
                    ctx.Advertir($"{col.Nombre}: placa de más de {col.Largo} caracteres anulada", placa);
                    return null;
                }
                return placa;
            }
            default:
                return valor;
        }
    }

    /// <summary>EstadoEn&lt;Fuente&gt; = alguna columna que entra al flag tiene valor, ya limpia.</summary>
    private static void CalcularFlag(FilaCargue fila, DefinicionFuenteCargue def)
    {
        if (def.ColumnaFlag is null) return;
        var flag = false;
        for (var i = 0; i < def.ColumnasDato.Count && !flag; i++)
            flag = def.ColumnasDato[i].EntraAlFlag && fila.Datos[i] is not null;
        fila.Flag = flag;
    }

    // ── 4b. Lista negra de teléfonos ────────────────────────────────────────

    /// <summary>
    /// Anula los teléfonos (columnas Celular / Fijo / Telefono) que están en
    /// <c>dbo.ListaNegraTelefonos</c> y vuelve a pasar por <c>regla.Validar</c> las filas tocadas:
    /// una fila que se queda sin datos puede pasar a excluida. El cruce es por número, contra
    /// la tabla REAL y sin mirar cartera (el SP de DatosClaro usaba ListaNegraTelefonosAuxiliar,
    /// un staging de 1,2M filas, no la lista vigente). Los números distintos del archivo suben
    /// a una #temp y se cruzan con HASH JOIN: la lista negra es un heap sin índice (~700k
    /// filas) y un nested loop la recorrería una vez por número.
    /// </summary>
    private static async Task<List<FilaCargue>> AplicarListaNegraTelefonosAsync(
        List<FilaCargue> filas, IReglaFuente regla, ContextoCargue ctx, Preparacion prep, SqlConnection conn, CancellationToken ct)
    {
        var def = ctx.Definicion;
        var columnas = def.ColumnasDato
            .Select((c, i) => (c, i))
            .Where(x => x.c.Tipo is TipoColumnaCargue.Celular or TipoColumnaCargue.Fijo or TipoColumnaCargue.Telefono)
            .ToList();
        if (columnas.Count == 0) return filas;

        var numeros = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in filas)
            foreach (var (_, i) in columnas)
                if (f.Datos[i] is string n) numeros.Add(n);
        if (numeros.Count == 0) return filas;

        await EjecutarAsync(conn, null, GeneradorSqlCargue.CrearTablaTelefonos, ct);
        var dt = new DataTable();
        dt.Columns.Add("NumeroTelefono", typeof(string));
        foreach (var n in numeros) dt.Rows.Add(n);
        await BulkAsync(conn, "#TelefonosArchivo", dt, ct);

        var vetados = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = Comando(conn, null, GeneradorSqlCargue.TelefonosEnListaNegra))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                vetados.Add(rd.GetString(0));
        ctx.Informar("Teléfonos distintos del archivo que están en ListaNegraTelefonos", vetados.Count);
        if (vetados.Count == 0) return filas;

        var resultado = new List<FilaCargue>(filas.Count);
        foreach (var f in filas)
        {
            var tocada = false;
            foreach (var (c, i) in columnas)
            {
                if (f.Datos[i] is string n && vetados.Contains(n))
                {
                    f.Datos[i] = null;
                    ctx.EnFila(f.Linea, f.TitularID);
                    ctx.Advertir($"{c.Nombre}: teléfono en ListaNegraTelefonos anulado", n);
                    tocada = true;
                }
            }
            if (tocada)
            {
                regla.Validar(f, ctx);
                if (f.MotivoExclusion is not null) { prep.Excluidas++; continue; }
                CalcularFlag(f, def);
            }
            resultado.Add(f);
        }
        return resultado;
    }

    // ── 5. Dedup ────────────────────────────────────────────────────────────

    private static List<FilaCargue> Deduplicar(
        List<FilaCargue> filas, DefinicionFuenteCargue def, ContextoCargue ctx, out int duplicadas)
    {
        var resultado = new List<FilaCargue>(filas.Count);
        duplicadas = 0;

        if (def.Cardinalidad == CardinalidadCargue.UnaPorTitular)
        {
            foreach (var grupo in filas.GroupBy(f => f.TitularID))
            {
                if (!grupo.Skip(1).Any()) { resultado.Add(grupo.First()); continue; }

                // La más completa; a igualdad, la última del archivo (la más reciente del proveedor).
                var conservada = grupo.OrderByDescending(f => f.DatosNoNulos).ThenByDescending(f => f.Linea).First();
                if (conservada.Nombre is null && grupo.FirstOrDefault(f => f.Nombre is not null) is { } conNombre)
                {
                    conservada.Nombre = conNombre.Nombre;
                    conservada.NombreReparado = conNombre.NombreReparado;
                    conservada.NombreIrrecuperable = conNombre.NombreIrrecuperable;
                }
                resultado.Add(conservada);
                var descartadas = grupo.Count() - 1;
                duplicadas += descartadas;
                ctx.Desglose.TitularRepetido(grupo.ToList(), conservada);
                foreach (var d in grupo)
                {
                    if (ReferenceEquals(d, conservada)) continue;
                    ctx.EnFila(d.Linea, d.TitularID);
                    ctx.Advertir("Titular repetido en el archivo: se conserva la fila más completa", $"se conservó la fila {conservada.Linea}");
                }
            }
            return resultado;
        }

        // VariasPorTitular: solo se quitan las filas EXACTAMENTE iguales (titular + clave).
        var claves = def.ColumnasDato.Select((c, i) => (c, i)).Where(x => x.c.EntraAClave).Select(x => x.i).ToArray();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in filas)
        {
            var clave = f.TitularID.ToString(CultureInfo.InvariantCulture) + "\u001F"
                + string.Join("\u001F", claves.Select(i => ValorATexto(f.Datos[i]) ?? "\u0000"));
            if (vistas.Add(clave)) resultado.Add(f);
            else
            {
                duplicadas++;
                ctx.Informar("Fila repetida exacta en el archivo");
                ctx.Desglose.FilaIdentica(f, () => string.Join(" · ", claves.Select(i => ValorATexto(f.Datos[i]) ?? "(vacío)")));
            }
        }
        return resultado;
    }

    // ── 6. Cruces ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lee existencia y nombre actual de los titulares; excluye titulares nuevos sin nombre
    /// (Titular.Nombre es NOT NULL) y marca los nombres que se repararán.
    /// </summary>
    private static async Task<List<FilaCargue>> CruzarTitularesAsync(
        List<FilaCargue> candidatas, DefinicionFuenteCargue def, Preparacion prep, SqlConnection conn, CancellationToken ct)
    {
        await EjecutarAsync(conn, null, GeneradorSqlCargue.CrearTablasApoyo, ct);

        var ids = candidatas.Select(f => f.TitularID).Distinct().ToList();
        var titulares = new DataTable();
        titulares.Columns.Add("TitularID", typeof(long));
        foreach (var id in ids) titulares.Rows.Add(id);
        await BulkAsync(conn, "#Titulares", titulares, ct);

        var enBD = new Dictionary<long, string?>(ids.Count);   // presente = existe; valor = nombre actual
        await using (var cmd = Comando(conn, null, GeneradorSqlCargue.ConsultarTitulares))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                if (rd.GetBoolean(1)) enBD[rd.GetInt64(0)] = rd.IsDBNull(2) ? null : rd.GetString(2);

        if (!def.CreaTitular)
        {
            // La fuente no crea titulares (DatosClaro, sin FK a Titular): la fila entra igual,
            // como hacía el SP, pero se avisa; el Calificador la ignora porque cruza por Titular_Cartera.
            foreach (var f in candidatas)
                if (!enBD.ContainsKey(f.TitularID))
                {
                    prep.Ctx.EnFila(f.Linea, f.TitularID);
                    prep.Ctx.Advertir("Titular inexistente en Titular: la fila entra igual (esta fuente no crea titulares)");
                }
            prep.NombresIrrecuperables = candidatas.Count(f => f.NombreIrrecuperable);
            prep.Ctx.Desglose.Irrecuperables(candidatas);
            return candidatas;
        }

        // Nombre por titular: el primero no vacío entre sus filas (con VariasPorTitular hay varias).
        var nombrePorTitular = new Dictionary<long, FilaCargue>();
        foreach (var f in candidatas)
            if (f.Nombre is not null) nombrePorTitular.TryAdd(f.TitularID, f);

        var resultado = new List<FilaCargue>(candidatas.Count);
        var nuevos = new HashSet<long>();
        var reparados = new HashSet<long>();
        foreach (var f in candidatas)
        {
            var existe = enBD.TryGetValue(f.TitularID, out var nombreBD);
            nombrePorTitular.TryGetValue(f.TitularID, out var conNombre);

            if (!existe && conNombre is null)
            {
                prep.Ctx.Excluir(f, "Titular nuevo sin nombre en el archivo");
                prep.Excluidas++;
                continue;
            }
            if (!existe)
            {
                if (nuevos.Add(f.TitularID)) prep.Ctx.Desglose.TitularNuevo(f.TitularID, conNombre!.Nombre);
            }
            else if (NormalizadorTitular.EstaRoto(nombreBD) && conNombre is { NombreIrrecuperable: false })
            {
                f.ReparaNombreEnBD = true;
                if (reparados.Add(f.TitularID)) prep.Ctx.Desglose.NombreReparado(f.TitularID, nombreBD, conNombre.Nombre);
            }
            if (f.Nombre is null && conNombre is not null) f.Nombre = conNombre.Nombre;
            resultado.Add(f);
        }

        prep.TitularesNuevos = nuevos.Count;
        prep.NombresReparados = reparados.Count;
        prep.NombresIrrecuperables = resultado.Count(f => f.NombreIrrecuperable);
        prep.Ctx.Desglose.Irrecuperables(resultado);
        return resultado;
    }

    private static DataTable TablaCarga(DefinicionFuenteCargue def, List<FilaCargue> filas)
    {
        var dt = new DataTable();
        dt.Columns.Add("Fila", typeof(int));
        dt.Columns.Add("TitularID", typeof(long));
        dt.Columns.Add("Nombre", typeof(string));
        dt.Columns.Add("ReparaNombre", typeof(bool));
        foreach (var c in def.ColumnasDato)
            dt.Columns.Add(c.Nombre, c.Tipo switch
            {
                TipoColumnaCargue.Entero => typeof(long),
                // DateOnly no lo soporta DataTable/SqlBulkCopy: va como DateTime a una columna date.
                TipoColumnaCargue.Fecha => typeof(DateTime),
                _ => typeof(string),
            });
        if (def.ColumnaFlag is not null) dt.Columns.Add(def.ColumnaFlag, typeof(bool));

        var fila = 0;
        foreach (var f in filas)
        {
            var valores = new object?[dt.Columns.Count];
            valores[0] = ++fila;   // correlativo propio: con Expandir, varias filas comparten Linea
            valores[1] = f.TitularID;
            valores[2] = (object?)f.Nombre ?? DBNull.Value;
            valores[3] = f.ReparaNombreEnBD;
            for (var i = 0; i < f.Datos.Length; i++)
                valores[4 + i] = f.Datos[i] switch
                {
                    null => DBNull.Value,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    var v => v,
                };
            if (def.ColumnaFlag is not null) valores[^1] = f.Flag;
            dt.Rows.Add(valores);
        }
        return dt;
    }

    private static DataTable TablaRelaciones(HashSet<(long TitularID, int LiderID, int CarteraID)> relaciones)
    {
        var dt = new DataTable();
        dt.Columns.Add("TitularID", typeof(long));
        dt.Columns.Add("LiderID", typeof(int));
        dt.Columns.Add("CarteraID", typeof(int));
        foreach (var r in relaciones) dt.Rows.Add(r.TitularID, r.LiderID, r.CarteraID);
        return dt;
    }

    // ── 7. Preview ──────────────────────────────────────────────────────────

    private static async Task<PreviewCargueDto> ArmarPreviewAsync(Preparacion prep, SqlConnection conn, CancellationToken ct)
    {
        var def = prep.Ctx.Definicion;
        var muestra = new List<List<string?>>();
        await using (var cmd = Comando(conn, null, GeneradorSqlCargue.ConsultarMuestra(def)))
        {
            cmd.Parameters.Add("@Filas", SqlDbType.Int).Value = CatalogoCargueFuentes.FilasMuestra;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var fila = new List<string?>(rd.FieldCount);
                for (var i = 0; i < rd.FieldCount; i++)
                    fila.Add(rd.IsDBNull(i) ? null : ValorATexto(rd.GetValue(i)));
                muestra.Add(fila);
            }
        }

        var preview = new PreviewCargueDto
        {
            TotalLeidas           = prep.TotalLeidas,
            Excluidas             = prep.Excluidas,
            DuplicadasEnArchivo   = prep.DuplicadasEnArchivo,
            AgregadasPorExpansion = prep.AgregadasPorExpansion,
            Candidatas            = prep.Candidatas,
            Insertadas            = prep.Insertadas,
            Refrescadas           = prep.Refrescadas,
            DescartadasVacias     = prep.DescartadasVacias,
            VaciasQuePurgara      = prep.VaciasQuePurgara,
            TitularesNuevos       = prep.TitularesNuevos,
            NombresReparados      = prep.NombresReparados,
            NombresIrrecuperables = prep.NombresIrrecuperables,
            RelacionesNuevas      = prep.RelacionesNuevas,
            FilasSinCarteraLider  = prep.FilasSinCarteraLider,
            ColumnasEncontradas         = prep.ColumnasEncontradas,
            ColumnasFaltantes           = [],
            ColumnasOpcionalesFaltantes = prep.ColumnasOpcionalesFaltantes,
            Errores      = prep.Ctx.Errores(),
            Advertencias = prep.Ctx.Advertencias(),
            Info         = prep.Ctx.Info(),
            ColumnasMuestra = GeneradorSqlCargue.ColumnasMuestra(def),
            Muestra         = muestra,
            HuellaArchivo   = prep.Huella,
            SelloPrevisto   = prep.SelloPrevisto,
        };
        preview.Desglose = prep.Ctx.Desglose.ParaPreview(preview);   // verifica que cada chip cuadre
        return preview;
    }

    private static string? ValorATexto(object? v) => v switch
    {
        null => null,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "Sí" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  Escritura
    // ════════════════════════════════════════════════════════════════════════

    private sealed record Escritura(
        DateTime Sello, int TitularesNuevos, int NombresReparados, int RelacionesNuevas,
        int DescartadasVacias, int Refrescadas, int Insertadas, int VaciasPurgadas);

    private async Task<Escritura> EscribirAsync(
        DefinicionFuenteCargue def, Preparacion prep, SqlConnection conn, long ejecucionID, bool simular,
        IProgress<ProgresoCargue> progreso, CancellationToken ct)
    {
        progreso.Report(new(FaseCargueJob.Escribiendo, 50, "Abriendo la transacción"));
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var sqlTx = (SqlTransaction)tx.GetDbTransaction();
        try
        {
            // Sello del servidor SQL (no del servidor de la API), leído dentro de la transacción.
            var sello = (DateTime)(await EscalarAsync(conn, sqlTx, GeneradorSqlCargue.LeerSello(def), ct))!;

            async Task<int> Paso(string sql, int pct, string mensaje)
            {
                progreso.Report(new(FaseCargueJob.Escribiendo, pct, mensaje));
                await using var cmd = Comando(conn, sqlTx, sql + "\nSELECT @@ROWCOUNT;");
                cmd.Parameters.Add("@Sello", def.TipoFecha == TipoFechaSello.Date ? SqlDbType.Date : SqlDbType.DateTime).Value = sello;
                cmd.Parameters.Add("@EjecucionID", SqlDbType.BigInt).Value = ejecucionID;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            }

            var titularesNuevos = def.CreaTitular
                ? await Paso(GeneradorSqlCargue.InsertarTitulares, 54, "Creando titulares nuevos") : 0;
            var nombresReparados = def.CreaTitular
                ? await Paso(GeneradorSqlCargue.RepararNombres, 58, "Reparando nombres rotos") : 0;
            var relacionesNuevas = def.CreaTitularCartera
                ? await Paso(GeneradorSqlCargue.InsertarRelaciones, 62, "Creando relaciones titular-cartera") : 0;

            // Se vuelve a marcar dentro de la transacción: los conteos que se verifican abajo
            // son los del momento de escribir, no los de la fase de cruces.
            progreso.Report(new(FaseCargueJob.Escribiendo, 66, "Comparando contenido"));
            await EjecutarAsync(conn, sqlTx, GeneradorSqlCargue.MarcarImpacto(def), ct);

            var descartadas = await Paso(GeneradorSqlCargue.DescartarVacias, 70, "Descartando filas vacías");
            await Paso(GeneradorSqlCargue.Refrescar(def), 76, "Refrescando fecha del contenido idéntico");
            var refrescadas = await EscalarIntAsync(conn, sqlTx, GeneradorSqlCargue.ContarRefrescadas, ct);
            var insertadas = await Paso(GeneradorSqlCargue.Insertar(def), 84, $"Insertando en {def.TablaDestino}");
            var purgar = GeneradorSqlCargue.PurgarVacias(def);
            var purgadas = purgar is null ? 0 : await Paso(purgar, 90, "Purgando vacías viejas");

            // Invariante 2: cada candidata terminó en exactamente un destino.
            if (prep.Candidatas != insertadas + refrescadas + descartadas)
                throw new InvalidOperationException(
                    $"Conteos incoherentes al escribir: candidatas {prep.Candidatas} ≠ insertadas {insertadas} + "
                    + $"refrescadas {refrescadas} + vacías {descartadas}. Se revirtió todo.");

            ct.ThrowIfCancellationRequested();
            if (simular) await tx.RollbackAsync(CancellationToken.None);
            else await tx.CommitAsync(CancellationToken.None);

            return new Escritura(sello, titularesNuevos, nombresReparados, relacionesNuevas,
                                 descartadas, refrescadas, insertadas, purgadas);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "Rollback del cargue de {Fuente} falló (la conexión pudo cerrarse)", def.Fuente); }
            throw;
        }
    }

    // ── Auditoría (conexión propia: debe funcionar aunque la del cargue haya muerto) ─

    private async Task RegistrarFinOkAsync(long ejecucionID, Preparacion prep, Escritura e)
    {
        try
        {
            await using var conn = new SqlConnection(_db.Database.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(GeneradorSqlCargue.RegistrarFinOk, conn);
            cmd.Parameters.Add("@EjecucionID", SqlDbType.BigInt).Value = ejecucionID;
            cmd.Parameters.Add("@Sello", SqlDbType.DateTime2).Value = e.Sello;
            cmd.Parameters.Add("@TotalLeidas", SqlDbType.Int).Value = prep.TotalLeidas;
            cmd.Parameters.Add("@Excluidas", SqlDbType.Int).Value = prep.Excluidas;
            cmd.Parameters.Add("@DuplicadasArchivo", SqlDbType.Int).Value = prep.DuplicadasEnArchivo;
            cmd.Parameters.Add("@Candidatas", SqlDbType.Int).Value = prep.Candidatas;
            cmd.Parameters.Add("@Insertadas", SqlDbType.Int).Value = e.Insertadas;
            cmd.Parameters.Add("@Refrescadas", SqlDbType.Int).Value = e.Refrescadas;
            cmd.Parameters.Add("@DescartadasVacias", SqlDbType.Int).Value = e.DescartadasVacias;
            cmd.Parameters.Add("@VaciasPurgadas", SqlDbType.Int).Value = e.VaciasPurgadas;
            cmd.Parameters.Add("@TitularesNuevos", SqlDbType.Int).Value = e.TitularesNuevos;
            cmd.Parameters.Add("@NombresReparados", SqlDbType.Int).Value = e.NombresReparados;
            cmd.Parameters.Add("@RelacionesNuevas", SqlDbType.Int).Value = e.RelacionesNuevas;
            cmd.Parameters.Add("@Advertencias", SqlDbType.Int).Value = prep.Ctx.TotalAdvertencias;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Los datos YA están confirmados: fallar aquí marcaría ERROR un cargue exitoso.
            _log.LogError(ex, "Cargue {EjecucionID} confirmado pero no se pudo cerrar su auditoría", ejecucionID);
        }
    }

    public async Task RegistrarFinFalloAsync(long ejecucionID, string estado, string error)
    {
        try
        {
            await using var conn = new SqlConnection(_db.Database.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(GeneradorSqlCargue.RegistrarFinFallo, conn);
            cmd.Parameters.Add("@EjecucionID", SqlDbType.BigInt).Value = ejecucionID;
            cmd.Parameters.Add("@Estado", SqlDbType.VarChar, 12).Value = estado;
            cmd.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value = Recortar(error, 1000);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo marcar {Estado} la ejecución de cargue {EjecucionID}", estado, ejecucionID);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Utilidades
    // ════════════════════════════════════════════════════════════════════════

    private static FileStream AbrirArchivo(string ruta) =>
        new(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>SHA-256 (hex) y cantidad de '\n' en una sola pasada por el archivo.</summary>
    private static async Task<(string Huella, int Lineas)> HuellaYLineasAsync(string ruta, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = AbrirArchivo(ruta);
        var buffer = new byte[128 * 1024];
        var lineas = 0;
        int n;
        while ((n = await fs.ReadAsync(buffer, ct)) > 0)
        {
            sha.AppendData(buffer, 0, n);
            lineas += buffer.AsSpan(0, n).Count((byte)'\n');
        }
        return (Convert.ToHexString(sha.GetHashAndReset()), lineas);
    }

    private static async IAsyncEnumerable<string[]> ComoAsync(
        IEnumerable<string[]> filas, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        foreach (var f in filas)
        {
            ct.ThrowIfCancellationRequested();
            yield return f;
        }
    }

    private static string Recortar(string s, int largo) => s.Length <= largo ? s : s[..largo];

    private static SqlCommand Comando(SqlConnection conn, SqlTransaction? tx, string sql) =>
        new(sql, conn, tx) { CommandTimeout = TimeoutSegundos };

    private static async Task EjecutarAsync(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = Comando(conn, tx, sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> EscalarAsync(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = Comando(conn, tx, sql);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task<int> EscalarIntAsync(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct) =>
        Convert.ToInt32(await EscalarAsync(conn, tx, sql, ct), CultureInfo.InvariantCulture);

    private static async Task BulkAsync(SqlConnection conn, string tabla, DataTable datos, CancellationToken ct)
    {
        if (datos.Rows.Count == 0) return;
        // CheckConstraints por costumbre del proyecto (aquí el destino es una #temp).
        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, null)
        {
            DestinationTableName = tabla,
            BatchSize            = 10_000,
            BulkCopyTimeout      = TimeoutSegundos,
        };
        // Mapeo por NOMBRE: #Carga tiene columnas con DEFAULT que no van en el DataTable.
        foreach (DataColumn col in datos.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(datos, ct);
    }
}
