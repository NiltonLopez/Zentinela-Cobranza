using System.Data;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.Settings;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace CargueHarness;

/// <summary>
/// Utilidades comunes del harness: aserciones, consultas, motor, archivos sintéticos y el
/// flujo estándar de una fuente (validar → simular → cargar → segunda carga = 0 insertadas).
///
/// Todo va acotado al GRUPO activo (ver <see cref="Grupos"/>): su rango reservado de
/// TitularIDs y su usuario de auditoría. Varios grupos corren en paralelo desde distintos
/// worktrees; ninguno lee, verifica ni borra el rango o las ejecuciones de otro.
/// </summary>
internal sealed class Harness
{
    /// <summary>
    /// Tablas que el harness puede tocar, en orden de borrado (hijas antes que Titular), con
    /// el filtro de rango (@d..@h = rango del grupo activo).
    ///
    /// FUENTE NUEVA → agregar AQUÍ su tabla destino, ANTES de "Titular" (una línea por tabla):
    ///     ("MiFuente",        "TitularID BETWEEN @d AND @h"),
    /// y si su TitularID es varchar (como NuevaEps), filtrar como número con TRY_CAST, que
    /// también atrapa ceros a la izquierda ('0999000000301'):
    ///     ("MiFuente",        "TRY_CAST(TitularID AS bigint) BETWEEN @d AND @h"),
    /// Todas las tablas se verifican (rango libre) y se limpian en TODOS los grupos, pero solo
    /// dentro del rango del grupo activo, así que agregar una no afecta a los demás.
    /// </summary>
    public static readonly (string Tabla, string Filtro)[] TablasDelRango =
    [
        ("Titular_Cartera", "TitularID BETWEEN @d AND @h"),
        ("Sura",            "TitularID BETWEEN @d AND @h"),
        ("Cajacopi",        "TitularID BETWEEN @d AND @h"),
        ("MutualSer",       "TitularID BETWEEN @d AND @h"),
        ("NuevaEps",        "TRY_CAST(TitularID AS bigint) BETWEEN @d AND @h"),
        ("SIMIT",           "TitularID BETWEEN @d AND @h"),
        ("DatosClaro",      "TitularID BETWEEN @d AND @h"),
        ("Positiva",        "TitularID BETWEEN @d AND @h"),
        ("SaludTotal",      "TitularID BETWEEN @d AND @h"),
        ("SOS",             "TitularID BETWEEN @d AND @h"),
        ("Coosalud",        "TitularID BETWEEN @d AND @h"),
        // ← fuentes nuevas aquí
        ("Titular",         "NumeroDocumento BETWEEN @d AND @h"),
    ];

    public Harness(string cs, string carpeta, GrupoHarness grupo)
    {
        Cs = cs;
        Carpeta = carpeta;
        Grupo = grupo;
        DbOpciones = new DbContextOptionsBuilder<CallCenterDbContext>().UseSqlServer(cs).Options;
    }

    public string Cs { get; }
    public string Carpeta { get; }
    public GrupoHarness Grupo { get; }
    /// <summary>Rango reservado del grupo activo.</summary>
    public long Desde => Grupo.Desde;
    public long Hasta => Grupo.Hasta;
    /// <summary>Usuario de auditoría del grupo activo (identifica y limpia sus ejecuciones).</summary>
    public string Usuario => Grupo.Usuario;
    public DbContextOptions<CallCenterDbContext> DbOpciones { get; }
    public int Fallas { get; set; }
    public List<long> Ejecuciones { get; } = [];
    public Progress<ProgresoCargue> SinProgreso { get; } = new(_ => { });

    // ── Aserciones ──────────────────────────────────────────────────────────

    public void Check(string que, bool ok, string detalle = "")
    {
        if (!ok) Fallas++;
        Console.WriteLine($"{(ok ? "OK   " : "FALLA")} | {que}{(detalle.Length > 0 ? " | " + detalle : "")}");
    }

    public void Igual<T>(string que, T esperado, T real) =>
        Check(que, EqualityComparer<T>.Default.Equals(esperado, real), $"esperado={esperado} real={real}");

    // ── Consultas (conexión propia por consulta) ────────────────────────────

    public async Task<object?> Escalar(string sql, params (string, object)[] ps)
    {
        await using var conn = new SqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var r = await cmd.ExecuteScalarAsync();
        return r is DBNull ? null : r;
    }

    public async Task<int> Contar(string sql, params (string, object)[] ps) => Convert.ToInt32(await Escalar(sql, ps));

    public async Task<string?> Texto(string sql, params (string, object)[] ps) => (string?)await Escalar(sql, ps);

    public Task<int> ContarTabla(string tabla) => Contar($"SELECT COUNT(*) FROM dbo.{tabla}");

    /// <summary>
    /// Filas de una tabla dentro de [desde, hasta] (por defecto, todo el rango del grupo
    /// activo). Un sub-rango fuera del grupo es un error del caso: se aborta en vez de leer
    /// (o, más adelante, escribir) el rango de otro grupo.
    /// </summary>
    public Task<int> EnRango(string tabla, long? desde = null, long? hasta = null)
    {
        var (d, hs) = (desde ?? Desde, hasta ?? Hasta);
        ExigirDentroDelGrupo(d, hs);
        var filtro = TablasDelRango.Single(t => t.Tabla == tabla).Filtro;
        return Contar($"SELECT COUNT(*) FROM dbo.{tabla} WHERE {filtro}", ("@d", d), ("@h", hs));
    }

    /// <summary>Lanza si [desde, hasta] no cae completo dentro del rango del grupo activo.</summary>
    public void ExigirDentroDelGrupo(long desde, long hasta)
    {
        if (desde < Desde || hasta > Hasta || desde > hasta)
            throw new InvalidOperationException(
                $"El sub-rango {desde}..{hasta} no está dentro del rango del grupo '{Grupo.Nombre}' ({Desde}..{Hasta}).");
    }

    // ── Motor ───────────────────────────────────────────────────────────────

    public CargueFuentesMotor NuevoMotor() => new(
        new CallCenterDbContext(DbOpciones),
        new CalificadorCarteraCache(new MemoryCache(new MemoryCacheOptions())),
        NullLogger<CargueFuentesMotor>.Instance,
        Options.Create(new CargueFuentesSettings()));

    public TrabajoCargue Trabajo(DefinicionFuenteCargue def, string ruta) =>
        new(Guid.NewGuid(), def, CatalogoReglasFuente.Obtener(def.Fuente), ruta, Path.GetFileName(ruta), Usuario);

    public Task<PreviewCargueDto> Validar(DefinicionFuenteCargue def, string ruta) =>
        NuevoMotor().ValidarAsync(Trabajo(def, ruta), SinProgreso, CancellationToken.None);

    public Task<ResultadoCargueDto> Simular(DefinicionFuenteCargue def, string ruta, PreviewCargueDto preview) =>
        NuevoMotor().ConfirmarAsync(Trabajo(def, ruta), 0, preview.HuellaArchivo, simular: true, SinProgreso, CancellationToken.None);

    public async Task<(ResultadoCargueDto R, long Id)> CargaReal(
        DefinicionFuenteCargue def, string paso, string ruta, PreviewCargueDto preview)
    {
        var t = Trabajo(def, ruta);
        var id = await NuevoMotor().RegistrarInicioAsync(t, preview.HuellaArchivo, Usuario, CancellationToken.None);
        Ejecuciones.Add(id);
        var r = await NuevoMotor().ConfirmarAsync(t, id, preview.HuellaArchivo, simular: false, SinProgreso, CancellationToken.None);
        Igual($"{paso}: auditoría cerrada en OK", "OK",
            await Texto("SELECT Estado FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @e", ("@e", id)));
        Igual($"{paso}: auditoría con las insertadas", r.Insertadas,
            await Contar("SELECT ISNULL(Insertadas, -1) FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @e", ("@e", id)));
        return (r, id);
    }

    public Task<int> Reversiones(long ejecucionId, char accion) =>
        Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = @e AND Accion = @a",
               ("@e", ejecucionId), ("@a", accion.ToString()));

    // ── Preview ─────────────────────────────────────────────────────────────

    public void Invariantes(string paso, PreviewCargueDto p, int agregadasPorExpansion = 0)
    {
        Check($"{paso}: leídas{(agregadasPorExpansion > 0 ? " + expansión" : "")} = excluidas + duplicadas + candidatas",
            p.TotalLeidas + agregadasPorExpansion == p.Excluidas + p.DuplicadasEnArchivo + p.Candidatas,
            $"{p.TotalLeidas} + {agregadasPorExpansion} = {p.Excluidas} + {p.DuplicadasEnArchivo} + {p.Candidatas}");
        Check($"{paso}: candidatas = insertadas + refrescadas + vacías",
            p.Candidatas == p.Insertadas + p.Refrescadas + p.DescartadasVacias,
            $"{p.Candidatas} = {p.Insertadas} + {p.Refrescadas} + {p.DescartadasVacias}");
    }

    public static void Mostrar(PreviewCargueDto p)
    {
        foreach (var e in p.Errores) Console.WriteLine($"        error  x{e.Cantidad}: {e.Mensaje}");
        foreach (var e in p.Advertencias) Console.WriteLine($"        aviso  x{e.Cantidad}: {e.Mensaje}");
        foreach (var e in p.Info) Console.WriteLine($"        info   x{e.Cantidad}: {e.Mensaje}");
    }

    public static int Aviso(PreviewCargueDto p, string texto) =>
        p.Advertencias.Where(a => a.Mensaje.Contains(texto)).Sum(a => a.Cantidad);

    public static int Error(PreviewCargueDto p, string texto) =>
        p.Errores.Where(a => a.Mensaje.Contains(texto)).Sum(a => a.Cantidad);

    // ── Archivos sintéticos ─────────────────────────────────────────────────

    /// <summary>XLSX con todas las celdas como texto, como llega de un proveedor.</summary>
    public string EscribirXlsx(string nombre, string[] encabezado, IReadOnlyList<string[]> filas)
    {
        var ruta = Path.Combine(Carpeta, nombre);
        using var wb = new XLWorkbook();
        var hoja = wb.AddWorksheet("BASE");
        for (var c = 0; c < encabezado.Length; c++) hoja.Cell(1, c + 1).Value = encabezado[c];
        for (var f = 0; f < filas.Count; f++)
            for (var c = 0; c < filas[f].Length; c++)
                hoja.Cell(f + 2, c + 1).SetValue(filas[f][c]);
        wb.SaveAs(ruta);
        return ruta;
    }

    public string EscribirCsv(string nombre, string[] encabezado, IReadOnlyList<string[]> filas)
    {
        var ruta = Path.Combine(Carpeta, nombre);
        static string Campo(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder();
        foreach (var f in filas.Prepend(encabezado)) sb.Append(string.Join(';', f.Select(Campo))).Append("\r\n");
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
        return ruta;
    }

    // ── Flujo estándar de una fuente ────────────────────────────────────────

    /// <summary>Conteos que se esperan del primer preview del archivo.</summary>
    public sealed record Esperado(
        int Leidas, int Excluidas, int Duplicadas, int Candidatas, int Insertadas,
        int TitularesNuevos, int DescartadasVacias = 0, int AgregadasPorExpansion = 0, int Relaciones = 0);

    /// <summary>
    /// validar → confirmar simulado (rollback, destino intacto) → confirmar real → segunda
    /// carga del MISMO archivo (0 insertadas, todo refrescado, COUNT(*) igual). Las
    /// verificaciones propias de la fuente van en los callbacks.
    /// </summary>
    public async Task FlujoEstandar(
        string etiqueta, DefinicionFuenteCargue def, string archivo, Esperado e, long desde, long hasta,
        Func<PreviewCargueDto, Task> trasValidar, Func<ResultadoCargueDto, long, Task> trasCargar)
    {
        ExigirDentroDelGrupo(desde, hasta);
        var tabla = def.TablaDestino.Split('.').Last();

        Console.WriteLine($"\n== {etiqueta} 1. ValidarAsync ==");
        var p1 = await Validar(def, archivo);
        Mostrar(p1);
        Igual($"{etiqueta} 1: total leídas", e.Leidas, p1.TotalLeidas);
        Igual($"{etiqueta} 1: excluidas", e.Excluidas, p1.Excluidas);
        Igual($"{etiqueta} 1: duplicadas en archivo", e.Duplicadas, p1.DuplicadasEnArchivo);
        Igual($"{etiqueta} 1: candidatas", e.Candidatas, p1.Candidatas);
        Igual($"{etiqueta} 1: insertadas", e.Insertadas, p1.Insertadas);
        Igual($"{etiqueta} 1: refrescadas", 0, p1.Refrescadas);
        Igual($"{etiqueta} 1: vacías descartadas", e.DescartadasVacias, p1.DescartadasVacias);
        Igual($"{etiqueta} 1: titulares nuevos", e.TitularesNuevos, p1.TitularesNuevos);
        Igual($"{etiqueta} 1: relaciones nuevas", e.Relaciones, p1.RelacionesNuevas);
        Invariantes($"{etiqueta} 1", p1, e.AgregadasPorExpansion);
        await trasValidar(p1);

        Console.WriteLine($"\n== {etiqueta} 2. ConfirmarAsync(simular: true) ==");
        var destinoAntes = await ContarTabla(tabla);
        var titularesAntes = await EnRango("Titular", desde, hasta);
        var sim = await Simular(def, archivo, p1);
        Console.WriteLine($"        {sim.Mensaje}");
        Igual($"{etiqueta} 2: insertadas simuladas", e.Insertadas, sim.Insertadas);
        Igual($"{etiqueta} 2: titulares nuevos simulados", e.TitularesNuevos, sim.TitularesNuevos);
        Igual($"{etiqueta} 2: {tabla} intacta tras rollback", destinoAntes, await ContarTabla(tabla));
        Igual($"{etiqueta} 2: Titular del rango intacto", titularesAntes, await EnRango("Titular", desde, hasta));
        Igual($"{etiqueta} 2: Titular_Cartera del rango intacto", 0, await EnRango("Titular_Cartera", desde, hasta));

        Console.WriteLine($"\n== {etiqueta} 3. ConfirmarAsync(simular: false) ==");
        var (r3, e3) = await CargaReal(def, $"{etiqueta} 3", archivo, p1);
        Console.WriteLine($"        {r3.Mensaje} (ejecución {e3}, sello {r3.SelloFechaConsulta:yyyy-MM-dd})");
        Igual($"{etiqueta} 3: insertadas", e.Insertadas, r3.Insertadas);
        Igual($"{etiqueta} 3: {tabla} creció en las insertadas", destinoAntes + e.Insertadas, await ContarTabla(tabla));
        Igual($"{etiqueta} 3: {tabla} del rango = insertadas", e.Insertadas, await EnRango(tabla, desde, hasta));
        Igual($"{etiqueta} 3: titulares nuevos", e.TitularesNuevos, r3.TitularesNuevos);
        Igual($"{etiqueta} 3: Titular del rango", titularesAntes + e.TitularesNuevos, await EnRango("Titular", desde, hasta));
        Igual($"{etiqueta} 3: reversión 'I' por insertada", e.Insertadas, await Reversiones(e3, 'I'));
        await trasCargar(r3, e3);

        Console.WriteLine($"\n== {etiqueta} 4. Segunda carga del mismo archivo ==");
        var p4 = await Validar(def, archivo);
        Igual($"{etiqueta} 4: preview insertadas", 0, p4.Insertadas);
        Igual($"{etiqueta} 4: preview refrescadas", e.Insertadas, p4.Refrescadas);
        Igual($"{etiqueta} 4: preview titulares nuevos", 0, p4.TitularesNuevos);
        Igual($"{etiqueta} 4: preview relaciones nuevas", 0, p4.RelacionesNuevas);
        Invariantes($"{etiqueta} 4", p4, e.AgregadasPorExpansion);
        var antes4 = await ContarTabla(tabla);
        var (r4, e4) = await CargaReal(def, $"{etiqueta} 4", archivo, p4);
        Console.WriteLine($"        {r4.Mensaje} (ejecución {e4})");
        Igual($"{etiqueta} 4: insertadas", 0, r4.Insertadas);
        Igual($"{etiqueta} 4: refrescadas", e.Insertadas, r4.Refrescadas);
        Igual($"{etiqueta} 4: COUNT(*) de {tabla} igual antes y después", antes4, await ContarTabla(tabla));
        Check($"{etiqueta} 4: reversión 'U' con la fecha anterior",
            await Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = @e AND Accion = 'U' AND FechaAnterior IS NOT NULL",
                ("@e", e4)) >= e.Insertadas);
    }

    // ── Limpieza ────────────────────────────────────────────────────────────

    /// <summary>
    /// Borra el rango del grupo activo de todas las tablas y la auditoría de SU usuario
    /// (igualdad exacta: 'harness-cargue' no arrastra a 'harness-cargue-salud').
    /// </summary>
    public async Task Limpiar()
    {
        var sb = new StringBuilder();
        foreach (var (tabla, filtro) in TablasDelRango) sb.AppendLine($"DELETE FROM dbo.{tabla} WHERE {filtro};");
        sb.AppendLine("""
            DELETE FROM dbo.CargueFuenteReversion
             WHERE EjecucionID IN (SELECT EjecucionID FROM dbo.CargueFuenteEjecucion WHERE Usuario = @u);
            DELETE FROM dbo.CargueFuenteEjecucion WHERE Usuario = @u;
            """);
        await using var conn = new SqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sb.ToString(), conn) { CommandTimeout = 300 };
        cmd.Parameters.Add("@d", SqlDbType.BigInt).Value = Desde;
        cmd.Parameters.Add("@h", SqlDbType.BigInt).Value = Hasta;
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Usuario;
        await cmd.ExecuteNonQueryAsync();
    }
}
