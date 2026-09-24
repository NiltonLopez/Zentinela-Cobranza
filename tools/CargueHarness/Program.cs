// Harness del Cargue de Fuentes de Datos contra la BD REAL.
//
// Uso:  set ZENTINELA_CARGUE_CS=<cadena de conexión a DBCallCenter>   (o la cadena como primer argumento)
//       dotnet run --project tools/CargueHarness -- [--grupo <grupo>] [--fuente <Fuente>]
//
//   --grupo   fase1 (por defecto) | salud | empresas | otras | desglose  — ver Grupos.cs
//   --fuente  corre solo ese caso del grupo (p. ej. --fuente SIMIT)
//
// Cada grupo tiene su rango RESERVADO de TitularIDs y su usuario de auditoría, para que varios
// harness corran en paralelo desde distintos worktrees sin pisarse. Genera archivos sintéticos
// con TitularIDs de ese rango y corre por fuente: validar → confirmar simulado (rollback) →
// confirmar real → segunda carga del mismo archivo (0 insertadas). La limpieza del rango y de
// la auditoría del grupo va en finally y se verifica al final, junto con el COUNT(*) de cada
// tabla destino de los casos corridos.
// Exit code: 0 = todo OK; 1 = alguna FALLA; 2 = sin cadena de conexión o argumentos inválidos;
//            3 = rango ocupado.

using System.Text;
using CargueHarness;

Console.OutputEncoding = Encoding.UTF8;

// ── Argumentos: --grupo, --fuente y la cadena como posicional (compatibilidad) ──
string? cs = null, nombreGrupo = null, soloFuente = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--grupo" when i + 1 < args.Length: nombreGrupo = args[++i]; break;
        case "--fuente" when i + 1 < args.Length: soloFuente = args[++i]; break;
        case var a when a.StartsWith("--"):
            Console.Error.WriteLine($"Argumento inválido o sin valor: {a}. Uso: [cadena] [--grupo <grupo>] [--fuente <Fuente>]");
            return 2;
        default: cs ??= args[i]; break;
    }
}
cs ??= Environment.GetEnvironmentVariable("ZENTINELA_CARGUE_CS");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("Falta la cadena de conexión: variable ZENTINELA_CARGUE_CS o primer argumento.");
    return 2;
}

var grupo = Grupos.Buscar(nombreGrupo ?? Grupos.PorDefecto);
if (grupo is null)
{
    Console.Error.WriteLine($"Grupo desconocido '{nombreGrupo}'. Grupos: {string.Join(", ", Grupos.Todos.Select(g => g.Nombre))}.");
    return 2;
}
var casos = grupo.Casos
    .Where(c => soloFuente is null || string.Equals(c.Fuente, soloFuente, StringComparison.OrdinalIgnoreCase))
    .ToList();
if (soloFuente is not null && casos.Count == 0)
{
    Console.Error.WriteLine($"El grupo '{grupo.Nombre}' no tiene la fuente '{soloFuente}'. "
                            + $"Casos: {(grupo.Casos.Count == 0 ? "(ninguno)" : string.Join(", ", grupo.Casos.Select(c => c.Fuente)))}.");
    return 2;
}

Console.WriteLine($"Grupo '{grupo.Nombre}': rango {grupo.Desde}..{grupo.Hasta}, usuario '{grupo.Usuario}', "
                  + $"casos: {(casos.Count == 0 ? "(ninguno)" : string.Join(", ", casos.Select(c => c.Fuente)))}.");

var carpeta = Path.Combine(Path.GetTempPath(), "cargue-harness-" + grupo.Nombre + "-" + Guid.NewGuid().ToString("N"));
var h = new Harness(cs, carpeta, grupo);
Directory.CreateDirectory(carpeta);

// ── Precondición: el rango COMPLETO del grupo está libre en todas las tablas ──
var ocupado = 0;
foreach (var (tabla, _) in Harness.TablasDelRango) ocupado += await h.EnRango(tabla);
if (ocupado > 0)
{
    Console.Error.WriteLine($"El rango del grupo '{grupo.Nombre}' {h.Desde}..{h.Hasta} tiene {ocupado} filas: no se toca nada.");
    try { Directory.Delete(carpeta, recursive: true); } catch { /* temp del harness */ }
    return 3;
}
// COUNT(*) de las tablas destino de los casos que se van a correr (no de las de otros grupos,
// que pueden estar cargándose en paralelo).
var conteosIniciales = new Dictionary<string, int>();
foreach (var tabla in casos.Select(c => c.TablaDestino).Distinct()) conteosIniciales[tabla] = await h.ContarTabla(tabla);
Console.WriteLine($"Rango {h.Desde}..{h.Hasta} libre."
                  + string.Concat(conteosIniciales.Select(kv => $" {kv.Key} tiene {kv.Value} filas.")));

try
{
    foreach (var caso in casos) await caso.Correr(h);
}
catch (Exception ex)
{
    h.Fallas++;
    Console.WriteLine($"FALLA | excepción no esperada | {ex}");
}
finally
{
    // ── Limpieza obligatoria (solo el rango y el usuario del grupo) ─────────
    Console.WriteLine("\n== Limpieza ==");
    try
    {
        await h.Limpiar();
    }
    catch (Exception ex)
    {
        h.Fallas++;
        Console.WriteLine($"FALLA | limpieza | {ex.Message}");
    }

    foreach (var (tabla, _) in Harness.TablasDelRango)
        h.Igual($"limpieza: {tabla} del rango reservado", 0, await h.EnRango(tabla));
    h.Igual("limpieza: ejecuciones del harness", 0,
        await h.Contar("SELECT COUNT(*) FROM dbo.CargueFuenteEjecucion WHERE Usuario = @u", ("@u", h.Usuario)));
    h.Igual("limpieza: reversiones del harness", 0,
        h.Ejecuciones.Count == 0 ? 0 : await h.Contar(
            $"SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID IN ({string.Join(",", h.Ejecuciones)})"));
    foreach (var (tabla, inicial) in conteosIniciales)
        h.Igual($"limpieza: COUNT(*) de {tabla} igual al inicial", inicial, await h.ContarTabla(tabla));
    try { Directory.Delete(carpeta, recursive: true); } catch { /* temp del harness */ }
}

Console.WriteLine(h.Fallas == 0 ? "\nRESULTADO: TODO OK" : $"\nRESULTADO: {h.Fallas} FALLA(S)");
return h.Fallas == 0 ? 0 : 1;
