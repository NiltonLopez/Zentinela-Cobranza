using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Sura (sub-rango 999000000001..099 del grupo fase1): validar → simular → cargar → segunda
/// carga, política de vacías (purga y descarte), reparación de mojibake y el orquestador en
/// segundo plano con su job store. Antes era el cuerpo de Program.cs; vive aquí para poder
/// registrarse en <see cref="Grupos"/> como cualquier otro caso.
/// </summary>
internal static class CasoSura
{
    private const long Desde = 999000000001, Hasta = 999000000099;   // sub-rango de Sura
    private static long T(int n) => Desde - 1 + n;                   // T(1) = 999000000001

    public static async Task Correr(Harness h)
    {
        var cs = h.Cs;
        var carpeta = h.Carpeta;
        var UsuarioHarness = h.Usuario;
        var ejecuciones = h.Ejecuciones;
        void Check(string que, bool ok, string detalle = "") => h.Check(que, ok, detalle);
        void Igual<TV>(string que, TV esperado, TV real) => h.Igual(que, esperado, real);

        // Mojibake real, armado por código de carácter para que ningún editor lo "arregle":
        //   NombreRotoT15 = "PIÃ‘A RUIZ"  (U+00C3 U+2018: UTF-8 de Ñ leído como cp1252, lo que hay en Titular)
        //   NombreMojibakeT6 = "PI" U+00C3 U+0091 "A LOPEZ"  (bytes 50 49 C3 83 C2 91 41: el caso de la tarea)
        var NombreRotoT15 = string.Concat("PI", (char)0x00C3, (char)0x2018, "A RUIZ");
        var NombreMojibakeT6 = string.Concat("PI", (char)0x00C3, (char)0x0091, "A LOPEZ");

        var def = CatalogoCargueFuentes.Sura;
        var regla = CatalogoReglasFuente.Obtener(def.Fuente);
        var dbOpciones = h.DbOpciones;

        CargueFuentesMotor NuevoMotor() => h.NuevoMotor();
        var sinProgreso = h.SinProgreso;

        Task<object?> Escalar(string sql, params (string, object)[] ps) => h.Escalar(sql, ps);
        Task<int> Contar(string sql, params (string, object)[] ps) => h.Contar(sql, ps);
        Task<int> EnRango(string tabla, string columna) => h.EnRango(tabla, Desde, Hasta);   // sub-rango de Sura
        Task<int> ContarSura() => h.ContarTabla("Sura");

        // ── Datos de referencia reales ──────────────────────────────────────────
        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(dbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);

        // Par activo resoluble también por NOMBRE (los índices conservan el primer homónimo).
        var carteraPorId = indices.CarteraPorNombre.Values.GroupBy(v => v.ID).ToDictionary(g => g.Key, g => g.First().Nombre);
        var liderPorId = indices.LiderPorNombre.Values.GroupBy(v => v.ID).ToDictionary(g => g.Key, g => g.First().Nombre);
        var paresOrdenados = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).ToList();
        var parNombre = paresOrdenados.First(p =>
            carteraPorId.TryGetValue(p.CarteraID, out var nc) && liderPorId.TryGetValue(p.LiderID, out var nl)
            && indices.Resolver(nc, nl).Error is null
            && indices.Resolver(nc, nl) is var r && r.CarteraID == p.CarteraID && r.LiderID == p.LiderID);
        var parId = paresOrdenados.FirstOrDefault(p => p != parNombre);
        if (parId == default) parId = parNombre;
        var nombreCartera = carteraPorId[parNombre.CarteraID];
        var nombreLider = liderPorId[parNombre.LiderID];

        (int LiderID, int CarteraID) parInactivo;
        await using (var conn = new SqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("""
                SELECT TOP 1 lc.LiderID, lc.CarteraID FROM dbo.Lider_Cartera lc
                 WHERE lc.Activo = 0
                   AND NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera x
                                    WHERE x.LiderID = lc.LiderID AND x.CarteraID = lc.CarteraID AND x.Activo = 1)
                 ORDER BY lc.LiderID, lc.CarteraID;
                """, conn);
            await using var rd = await cmd.ExecuteReaderAsync();
            parInactivo = await rd.ReadAsync() ? (rd.GetInt32(0), rd.GetInt32(1)) : (parNombre.LiderID, -1);
        }
        var carteraInexistente = await Contar("SELECT ISNULL(MAX(ID), 0) + 1000 FROM dbo.Cartera");

        string emailNegro;
        {
            var entradas = new List<string>();
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT CorreoErroneo FROM dbo.ListaNegraCorreos", conn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) entradas.Add(rd.GetString(0));
            var lista = ListaNegraCorreos.Cargar(entradas);
            emailNegro = entradas.Select(e => e.Trim().ToLowerInvariant())
                .First(e => EmailColombiano.Normalizar(e, 255) == e && lista.Contiene(e));
        }
        Console.WriteLine($"Par por ID {parId}, par por nombre {parNombre} ('{nombreCartera}' / '{nombreLider}'), "
                          + $"par inactivo {parInactivo}, cartera inexistente {carteraInexistente}, email negro '{emailNegro}'.");

        // Titular preexistente con nombre ROTO (mojibake real 'Ã‘'): el cargue debe repararlo.
        await Escalar("INSERT INTO dbo.Titular (NumeroDocumento, Nombre) VALUES (@t, @n)",
            ("@t", T(15)), ("@n", NombreRotoT15));

        // ── Archivos sintéticos ─────────────────────────────────────────────────
        string[] encabezado =
        [
            "TitularID", "Nombre", "CarteraID", "LiderID", "Cartera", "Lider", "Celular", "Correo",
            "NIT", "Empresa", "Tipo Afiliado", "Estado Afiliación", "UltimaFechaConsulta",
        ];
        string[] Fila(object doc, string nombre, string cel = "", string email = "", string nit = "", string empresa = "",
                      string tipo = "", string estado = "", string fecha = "", string carteraId = "", string liderId = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, carteraId, liderId, cartera, lider, cel, email, nit, empresa, tipo, estado, fecha];

        var filasA = new List<string[]>
        {
            Fila(T(1), "Juan Pérez", "3001234567", "Juan.Perez@Gmail.com", "900123456", "ACME; SAS", "COTIZANTE", "ACTIVO",
                 "2026-08-01", carteraId: parId.CarteraID.ToString(), liderId: parId.LiderID.ToString()),
            Fila("ABC123", "DOCUMENTO MALO", "3001234568"),                              // excluida: documento inválido
            Fila(T(2), "MARIA GOMEZ", "573001112233"),                                   // 57 + celular
            Fila(T(3), "PEDRO FIJO", "6012345678", "pedro@hotmail.com"),                 // fijo en columna celular
            Fila(T(4), "ANA NEGRA", "3009998877", emailNegro),                           // email en lista negra
            Fila(T(5), "LUIS CORREO", "3005556677", "correo-sin-arroba.com"),            // email inválido
            Fila(T(6), NombreMojibakeT6, "3001230000"),                                  // mojibake reparable
            Fila(T(7), "PI?A GOMEZ", "3001230001"),                                      // irrecuperable
            Fila(T(8), "CARLOS REPETIDO", "3001230002"),                                 // repetido (menos completa)
            Fila(T(8), "CARLOS REPETIDO", "3001230002", "carlos@gmail.com"),             // repetido (se conserva)
            Fila(T(9), "SOFIA", "3001230003"),                                           // con datos
            Fila(T(9), "SOFIA"),                                                         // vacía del mismo titular
            Fila(T(10), "TITULAR SIN DATOS"),                                            // vacía, sin datos en ningún lado
            Fila(T(11), "RELACION POR NOMBRE", "3001230004", cartera: nombreCartera, lider: nombreLider),
            Fila(T(12), "PAR INACTIVO", "3001230005",
                 carteraId: parInactivo.CarteraID.ToString(), liderId: parInactivo.LiderID.ToString()),
            Fila(T(13), "PAR INEXISTENTE", "3001230006",
                 carteraId: carteraInexistente.ToString(), liderId: parNombre.LiderID.ToString()),
            Fila(T(14), "", "3001230007"),                                               // excluida: nuevo sin nombre
            Fila(T(15), "Piña Ruiz", "3001230008", nit: "900.123.456",
                 estado: new string('X', 60)),                                           // repara nombre + trunca
            Fila(T(16), "FECHA MALA", "3001230009", nit: "12AB", fecha: "no-es-fecha"),
        };

        string EscribirXlsx(string nombre, List<string[]> filas)
        {
            var ruta = Path.Combine(carpeta, nombre);
            using var wb = new XLWorkbook();
            var hoja = wb.AddWorksheet("BASE");
            for (var c = 0; c < encabezado.Length; c++) hoja.Cell(1, c + 1).Value = encabezado[c];
            for (var f = 0; f < filas.Count; f++)
                for (var c = 0; c < filas[f].Length; c++)
                    hoja.Cell(f + 2, c + 1).SetValue(filas[f][c]);   // todo como texto, como llega de un proveedor
            wb.SaveAs(ruta);
            return ruta;
        }
        string EscribirCsv(string nombre, List<string[]> filas)
        {
            var ruta = Path.Combine(carpeta, nombre);
            static string Campo(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
            var sb = new StringBuilder();
            foreach (var f in filas.Prepend(encabezado)) sb.Append(string.Join(';', f.Select(Campo))).Append("\r\n");
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
            return ruta;
        }

        var archivoA = EscribirXlsx("sura_a.xlsx", filasA);
        var archivoCsv = EscribirCsv("sura_a.csv", filasA);
        var archivoB = EscribirXlsx("sura_b.xlsx", [Fila(T(10), "TITULAR SIN DATOS", "3001239999")]);

        TrabajoCargue Trabajo(string ruta) =>
            new(Guid.NewGuid(), def, regla, ruta, Path.GetFileName(ruta), UsuarioHarness);

        void Invariantes(string paso, PreviewCargueDto p)
        {
            Check($"{paso}: leídas = excluidas + duplicadas + candidatas",
                p.TotalLeidas == p.Excluidas + p.DuplicadasEnArchivo + p.Candidatas,
                $"{p.TotalLeidas} = {p.Excluidas} + {p.DuplicadasEnArchivo} + {p.Candidatas}");
            Check($"{paso}: candidatas = insertadas + refrescadas + vacías",
                p.Candidatas == p.Insertadas + p.Refrescadas + p.DescartadasVacias,
                $"{p.Candidatas} = {p.Insertadas} + {p.Refrescadas} + {p.DescartadasVacias}");
            h.Desglose(paso, p);
        }

        void Mostrar(PreviewCargueDto p)
        {
            foreach (var e in p.Errores) Console.WriteLine($"        error  x{e.Cantidad}: {e.Mensaje}");
            foreach (var e in p.Advertencias) Console.WriteLine($"        aviso  x{e.Cantidad}: {e.Mensaje}");
            foreach (var e in p.Info) Console.WriteLine($"        info   x{e.Cantidad}: {e.Mensaje}");
        }

        async Task<(ResultadoCargueDto R, long Id)> CargaReal(string paso, string ruta, PreviewCargueDto preview)
        {
            var t = Trabajo(ruta);
            var id = await NuevoMotor().RegistrarInicioAsync(t, preview.HuellaArchivo, UsuarioHarness, CancellationToken.None);
            ejecuciones.Add(id);
            var r = await NuevoMotor().ConfirmarAsync(t, id, preview.HuellaArchivo, simular: false, sinProgreso, CancellationToken.None);
            var estado = (string?)await Escalar("SELECT Estado FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @e", ("@e", id));
            Igual($"{paso}: auditoría cerrada en OK", "OK", estado);
            Igual($"{paso}: auditoría con las insertadas", r.Insertadas,
                await Contar("SELECT ISNULL(Insertadas, -1) FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @e", ("@e", id)));
            h.Desglose(paso, r);
            return (r, id);
        }

        // ── 1. Validar ──────────────────────────────────────────────────────────
        Console.WriteLine("\n== 1. ValidarAsync (archivo A, XLSX) ==");
        var p1 = await NuevoMotor().ValidarAsync(Trabajo(archivoA), sinProgreso, CancellationToken.None);
        Mostrar(p1);
        Igual("1: total leídas", 19, p1.TotalLeidas);
        Igual("1: excluidas (documento inválido + nuevo sin nombre)", 2, p1.Excluidas);
        Igual("1: duplicadas en archivo", 2, p1.DuplicadasEnArchivo);
        Igual("1: candidatas", 15, p1.Candidatas);
        Igual("1: insertadas", 15, p1.Insertadas);
        Igual("1: refrescadas", 0, p1.Refrescadas);
        Igual("1: titulares nuevos", 14, p1.TitularesNuevos);
        Igual("1: nombres reparados (T15 con mojibake en BD)", 1, p1.NombresReparados);
        Igual("1: nombres irrecuperables", 1, p1.NombresIrrecuperables);
        Igual("1: relaciones nuevas (por ID y por nombre)", 2, p1.RelacionesNuevas);
        Igual("1: filas sin cartera/líder", 13, p1.FilasSinCarteraLider);
        Invariantes("1", p1);
        int Aviso(PreviewCargueDto p, string texto) => p.Advertencias.Where(a => a.Mensaje.Contains(texto)).Sum(a => a.Cantidad);
        int Error(PreviewCargueDto p, string texto) => p.Errores.Where(a => a.Mensaje.Contains(texto)).Sum(a => a.Cantidad);
        Igual("1: error documento inválido", 1, Error(p1, "Documento inválido"));
        Igual("1: error titular nuevo sin nombre", 1, Error(p1, "sin nombre"));
        Igual("1: aviso fijo en columna celular", 1, Aviso(p1, "fijo en la columna de celular"));
        Igual("1: aviso email en lista negra", 1, Aviso(p1, "lista negra"));
        Igual("1: aviso email inválido", 1, Aviso(p1, "formato inválido"));
        Igual("1: aviso mojibake reparado", 1, Aviso(p1, "mojibake"));
        Igual("1: aviso nombre irrecuperable", 1, Aviso(p1, "irrecuperables"));
        Igual("1: aviso titular repetido", 2, Aviso(p1, "repetido"));
        Igual("1: aviso par inactivo/inexistente", 2, Aviso(p1, "no son un par activo"));
        Igual("1: aviso truncado", 1, Aviso(p1, "truncado"));
        Igual("1: aviso fecha no reconocida", 1, Aviso(p1, "fecha no reconocida"));
        Igual("1: aviso NIT no numérico", 1, Aviso(p1, "NIT: valor no numérico"));
        Igual("1: sello previsto = hoy del servidor SQL",
            DateOnly.FromDateTime((DateTime)(await Escalar("SELECT CAST(GETDATE() AS date)"))!), p1.SelloPrevisto);
        Check("1: muestra con filas y columnas dinámicas", p1.Muestra.Count == 10 && p1.ColumnasMuestra.Contains("NumeroCelular"),
            $"filas={p1.Muestra.Count} columnas={string.Join(",", p1.ColumnasMuestra)}");

        // ── 1c. Desglose de los chips (qué compone cada número, con ejemplos) ───
        var d1 = p1.Desglose;
        string Ej(ChipCargue chip, string grupo = "") => string.Join(" | ", Harness.Ejemplos(d1, chip, grupo));
        Igual("1c: leídas = pasaron a la carga + excluidas + duplicadas (sin expansión)", 19,
            Harness.EnGrupo(d1, ChipCargue.Leidas, "Pasaron a la carga") + Harness.EnGrupo(d1, ChipCargue.Leidas, "Excluidas")
            + Harness.EnGrupo(d1, ChipCargue.Leidas, "Duplicadas"));
        Check("1c: excluida por documento inválido con fila y valor", Ej(ChipCargue.Excluidas, "Documento inválido") == "Fila 3 · documento ABC123",
            Ej(ChipCargue.Excluidas, "Documento inválido"));
        Check("1c: excluida por titular nuevo sin nombre con su documento", Ej(ChipCargue.Excluidas, "sin nombre") == $"Fila 18 · {T(14)}",
            Ej(ChipCargue.Excluidas, "sin nombre"));
        Igual("1c: duplicadas = titular repetido", 2, Harness.EnGrupo(d1, ChipCargue.Duplicadas, "Titular repetido"));
        Check("1c: ejemplo de repetido con la fila conservada", Ej(ChipCargue.Duplicadas).Contains($"{T(8)} × 2 filas (se conservó la fila 11)"),
            Ej(ChipCargue.Duplicadas));
        Igual("1c: insertadas = titular sin datos previos en Sura", 15, Harness.EnGrupo(d1, ChipCargue.Insertadas, "sin datos previos en Sura"));
        Check("1c: ejemplo de insertada con documento, celular y email",
            Harness.Ejemplos(d1, ChipCargue.Insertadas).FirstOrDefault() == $"{T(1)} · 3001234567 · juan.perez@gmail.com", Ej(ChipCargue.Insertadas));
        Check("1c: titulares nuevos con documento y nombre (máx. 10 de 14)",
            Harness.Ejemplos(d1, ChipCargue.TitularesNuevos).Count == 10 && Ej(ChipCargue.TitularesNuevos).Contains($"{T(2)} · MARIA GOMEZ"),
            Ej(ChipCargue.TitularesNuevos));
        Check("1c: nombre reparado con antes → después", Ej(ChipCargue.NombresReparados, "Reparado") == $"{T(15)} · {NombreRotoT15} → PIÑA RUIZ",
            Ej(ChipCargue.NombresReparados, "Reparado"));
        Check("1c: nombre irrecuperable con su valor", Ej(ChipCargue.NombresReparados, "Irrecuperable") == $"Fila 9 · {T(7)} · PI?A GOMEZ",
            Ej(ChipCargue.NombresReparados, "Irrecuperable"));
        Check("1c: relaciones agrupadas por 'Cartera — Líder'",
            Harness.Chip(d1, ChipCargue.RelacionesNuevas).Grupos.All(g => g.Titulo.Contains(" — ")) && Ej(ChipCargue.RelacionesNuevas).Contains(T(11).ToString()),
            string.Join(" | ", Harness.Chip(d1, ChipCargue.RelacionesNuevas).Grupos.Select(g => $"{g.Titulo} ({g.Cantidad})")));
        Igual("1c: sin relación por par inactivo / inexistente", 2, Harness.EnGrupo(d1, ChipCargue.SinCarteraLider, "no son un par activo"));
        Igual("1c: sin relación porque la fila no traía cartera ni líder", 11,
            Harness.EnGrupo(d1, ChipCargue.SinCarteraLider, DesgloseCargue.TituloSinColumnasRelacion));
        Check("1c: advertencia con fila, documento y valor anulado",
            Ej(ChipCargue.Advertencias, "formato inválido") == $"Fila 7 · {T(5)} · correo-sin-arroba.com", Ej(ChipCargue.Advertencias, "formato inválido"));
        Check("1c: todas las advertencias traen fila y documento",
            Harness.Ejemplos(d1, ChipCargue.Advertencias).All(e => e.StartsWith("Fila ") && e.Contains(" · 9990000000")),
            Ej(ChipCargue.Advertencias));

        // Mismo contenido en CSV (con ';' dentro de comillas en Empresa): mismos conteos.
        var pCsv = await NuevoMotor().ValidarAsync(Trabajo(archivoCsv), sinProgreso, CancellationToken.None);
        Check("1b: CSV con comillas da los mismos conteos que el XLSX",
            (pCsv.TotalLeidas, pCsv.Excluidas, pCsv.DuplicadasEnArchivo, pCsv.Candidatas, pCsv.Insertadas, pCsv.TitularesNuevos)
            == (p1.TotalLeidas, p1.Excluidas, p1.DuplicadasEnArchivo, p1.Candidatas, p1.Insertadas, p1.TitularesNuevos),
            $"csv leídas={pCsv.TotalLeidas} excl={pCsv.Excluidas} cand={pCsv.Candidatas}");

        // ── 2. Confirmar simulado ───────────────────────────────────────────────
        Console.WriteLine("\n== 2. ConfirmarAsync(simular: true) ==");
        var suraAntes = await ContarSura();
        var sim = await NuevoMotor().ConfirmarAsync(Trabajo(archivoA), 0, p1.HuellaArchivo, simular: true, sinProgreso, CancellationToken.None);
        Console.WriteLine($"        {sim.Mensaje}");
        Igual("2: insertadas simuladas", 15, sim.Insertadas);
        Igual("2: titulares nuevos simulados", 14, sim.TitularesNuevos);
        Igual("2: nombres reparados simulados", 1, sim.NombresReparados);
        Igual("2: relaciones simuladas", 2, sim.RelacionesNuevas);
        h.Desglose("2", sim);
        Igual("2: Sura intacta tras rollback", suraAntes, await ContarSura());
        Igual("2: Titular del rango intacto (solo T15 sembrado)", 1, await EnRango("Titular", "NumeroDocumento"));
        Igual("2: Titular_Cartera del rango intacto", 0, await EnRango("Titular_Cartera", "TitularID"));
        Igual("2: nombre roto de T15 intacto", NombreRotoT15,
            (string?)await Escalar("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(15))));
        Igual("2: reversión sin filas de la simulación", 0,
            await Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = 0"));

        // ── 3. Confirmar real ───────────────────────────────────────────────────
        Console.WriteLine("\n== 3. ConfirmarAsync(simular: false) ==");
        var (r3, e3) = await CargaReal("3", archivoA, p1);
        Console.WriteLine($"        {r3.Mensaje} (ejecución {e3}, sello {r3.SelloFechaConsulta:yyyy-MM-dd})");
        Igual("3: insertadas", 15, r3.Insertadas);
        Igual("3: Sura creció en las insertadas", suraAntes + 15, await ContarSura());
        Igual("3: titulares del rango (14 nuevos + T15)", 15, await EnRango("Titular", "NumeroDocumento"));
        Igual("3: relaciones del rango", 2, await EnRango("Titular_Cartera", "TitularID"));
        Igual("3: reversión 'I' por insertada", 15,
            await Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = @e AND Accion = 'I'", ("@e", e3)));
        Igual("3: nombre de T15 reparado", "PIÑA RUIZ",
            (string?)await Escalar("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(15))));
        Igual("3: nombre mojibake de T6 insertado limpio", "PIÑA LOPEZ",
            (string?)await Escalar("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(6))));
        Igual("3: nombre irrecuperable de T7 insertado tal cual", "PI?A GOMEZ",
            (string?)await Escalar("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(7))));
        Igual("3: celular con 57 normalizado (T2)", "3001112233",
            (string?)await Escalar("SELECT NumeroCelular FROM dbo.Sura WHERE TitularID = @t", ("@t", T(2))));
        Igual("3: fijo en celular anulado, email conservado (T3)", "NULL|pedro@hotmail.com|1",
            (string?)await Escalar("SELECT CONCAT(ISNULL(NumeroCelular,'NULL'),'|',Email,'|',CAST(EstadoEnSura AS int)) FROM dbo.Sura WHERE TitularID = @t", ("@t", T(3))));
        Igual("3: email en lista negra anulado (T4)", 1,
            await Contar("SELECT COUNT(*) FROM dbo.Sura WHERE TitularID = @t AND Email IS NULL", ("@t", T(4))));
        Igual("3: repetido conserva la fila más completa (T8)", "carlos@gmail.com",
            (string?)await Escalar("SELECT Email FROM dbo.Sura WHERE TitularID = @t", ("@t", T(8))));
        Igual("3: titular sin datos entra con EstadoEnSura = 0 (T10)", 0,
            await Contar("SELECT CAST(EstadoEnSura AS int) FROM dbo.Sura WHERE TitularID = @t", ("@t", T(10))));
        Igual("3: NIT con puntos normalizado (T15)", 900123456L,
            Convert.ToInt64(await Escalar("SELECT NIT FROM dbo.Sura WHERE TitularID = @t", ("@t", T(15)))));
        Igual("3: relación por nombre creada (T11)", 1,
            await Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                ("@t", T(11)), ("@l", parNombre.LiderID), ("@c", parNombre.CarteraID)));
        Igual("3: par inactivo sin relación (T12)", 0,
            await Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t", ("@t", T(12))));

        // ── 4. Segunda carga del mismo archivo ──────────────────────────────────
        Console.WriteLine("\n== 4. Segunda carga del mismo archivo ==");
        var p4 = await NuevoMotor().ValidarAsync(Trabajo(archivoA), sinProgreso, CancellationToken.None);
        Igual("4: preview insertadas", 0, p4.Insertadas);
        Igual("4: preview refrescadas", 15, p4.Refrescadas);
        Igual("4: preview titulares nuevos", 0, p4.TitularesNuevos);
        Igual("4: preview nombres reparados", 0, p4.NombresReparados);
        Igual("4: preview relaciones nuevas", 0, p4.RelacionesNuevas);
        Invariantes("4", p4);
        var suraAntes4 = await ContarSura();
        var (r4, e4) = await CargaReal("4", archivoA, p4);
        Console.WriteLine($"        {r4.Mensaje} (ejecución {e4})");
        Igual("4: insertadas", 0, r4.Insertadas);
        Igual("4: refrescadas", 15, r4.Refrescadas);
        Igual("4: COUNT(*) de Sura igual antes y después", suraAntes4, await ContarSura());
        Igual("4: reversión 'U' con la fecha anterior", 15,
            await Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = @e AND Accion = 'U' AND FechaAnterior IS NOT NULL", ("@e", e4)));

        // ── 5. El titular vacío ahora trae datos: se purga su vacía vieja ────────
        Console.WriteLine("\n== 5. Carga que purga una vacía vieja (archivo B) ==");
        var p5 = await NuevoMotor().ValidarAsync(Trabajo(archivoB), sinProgreso, CancellationToken.None);
        Igual("5: preview vacías que purgará", 1, p5.VaciasQuePurgara);
        Invariantes("5", p5);
        Igual("5: desglose — insertada con contenido distinto al vigente (T10 tenía su vacía)", 1,
            Harness.EnGrupo(p5.Desglose, ChipCargue.Insertadas, DesgloseCargue.TituloContenidoDistinto));
        Igual("5: desglose — la vacía que se purgará es la de T10", T(10).ToString(),
            string.Join(",", Harness.Ejemplos(p5.Desglose, ChipCargue.VaciasPurgadas)));
        var (r5, e5) = await CargaReal("5", archivoB, p5);
        Igual("5: insertadas", 1, r5.Insertadas);
        Igual("5: vacías purgadas", 1, r5.VaciasPurgadas);
        Igual("5: T10 queda solo con la fila con datos", "1:1",
            (string?)await Escalar("SELECT CONCAT(COUNT(*), ':', SUM(CAST(EstadoEnSura AS int))) FROM dbo.Sura WHERE TitularID = @t", ("@t", T(10))));
        Igual("5: reversión 'D' de la purga", 1,
            await Contar("SELECT COUNT(*) FROM dbo.CargueFuenteReversion WHERE EjecucionID = @e AND Accion = 'D'", ("@e", e5)));

        // ── 6. Archivo A otra vez: la vacía de T10 ya no entra ──────────────────
        Console.WriteLine("\n== 6. Archivo A con T10 vacío y datos en destino ==");
        var p6 = await NuevoMotor().ValidarAsync(Trabajo(archivoA), sinProgreso, CancellationToken.None);
        Igual("6: preview vacías descartadas", 1, p6.DescartadasVacias);
        Invariantes("6", p6);
        Igual("6: desglose — la vacía descartada es la de T10", T(10).ToString(),
            string.Join(",", Harness.Ejemplos(p6.Desglose, ChipCargue.VaciasDescartadas)));
        Igual("6: desglose — refrescadas = contenido idéntico al vigente", 14,
            Harness.EnGrupo(p6.Desglose, ChipCargue.Refrescadas, "Contenido idéntico"));
        var suraAntes6 = await ContarSura();
        var (r6, _) = await CargaReal("6", archivoA, p6);
        Igual("6: insertadas", 0, r6.Insertadas);
        Igual("6: refrescadas", 14, r6.Refrescadas);
        Igual("6: vacías descartadas", 1, r6.DescartadasVacias);
        Igual("6: COUNT(*) de Sura igual", suraAntes6, await ContarSura());

        // ── 7. Orquestador + job store (el camino de la API, sin levantarla) ────
        Console.WriteLine("\n== 7. Orquestador en segundo plano ==");
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddMemoryCache();
        servicios.AddDbContext<CallCenterDbContext>(o => o.UseSqlServer(cs));
        servicios.AddSingleton<ICalificadorCarteraCache, CalificadorCarteraCache>();
        servicios.Configure<CargueFuentesSettings>(o => o.CarpetaTemporal = Path.Combine(carpeta, "jobs"));
        servicios.AddSingleton<CargueFuentesJobStore>();
        servicios.AddSingleton<ICargueFuentesService, CargueFuentesOrquestador>();
        servicios.AddScoped<CargueFuentesMotor>();
        await using var proveedor = servicios.BuildServiceProvider();
        var orq = proveedor.GetRequiredService<ICargueFuentesService>();

        async Task<CargueJobDto> Esperar(Guid id, params EstadoCargueJob[] mientras)
        {
            var limite = DateTime.Now.AddSeconds(120);
            CargueJobDto job;
            while (mientras.Contains((job = orq.Obtener(id)!).Estado) && DateTime.Now < limite) await Task.Delay(200);
            return job;
        }
        async Task<CargueJobDto> Subir(string ruta)
        {
            await using var fs = File.OpenRead(ruta);
            return await orq.IniciarValidacionAsync("sura", fs, Path.GetFileName(ruta), fs.Length, UsuarioHarness, CancellationToken.None);
        }

        Check("7: la fuente Sura sale habilitada con sus columnas",
            orq.ListarFuentes().SingleOrDefault(f => f.Fuente == "Sura") is { Habilitada: true } fs7
            && fs7.ColumnasEsperadas.Any(c => c.Nombre == "NumeroCelular")
            && !fs7.ColumnasEsperadas.Single(c => c.Nombre == "TitularID").Alias.Contains("nit"));

        var j7 = await Subir(archivoA);
        Igual("7: validar arranca en Validando", EstadoCargueJob.Validando, j7.Estado);
        var conflicto = false;
        try { await Subir(archivoA); } catch (CargueFuentesConflictoException) { conflicto = true; }
        Check("7: segundo validar de la misma fuente → conflicto (409)", conflicto || orq.Obtener(j7.JobId)!.Estado != EstadoCargueJob.Validando,
            conflicto ? "rechazado" : "el primero ya había terminado");
        j7 = await Esperar(j7.JobId, EstadoCargueJob.Validando);
        Igual("7: preview listo", EstadoCargueJob.PreviewListo, j7.Estado);
        Check("7: puede confirmar", j7.PuedeConfirmar, $"candidatas={j7.Preview?.Candidatas}");

        var confirmado = await orq.ConfirmarAsync(j7.JobId, UsuarioHarness, CancellationToken.None);
        Igual("7: confirmar pasa a Cargando", EstadoCargueJob.Cargando, confirmado!.Estado);
        j7 = await Esperar(j7.JobId, EstadoCargueJob.Cargando);
        Igual("7: carga completada", EstadoCargueJob.Completado, j7.Estado);
        Igual("7: segunda vuelta del archivo A: 0 insertadas", 0, j7.Resultado?.Insertadas);
        Igual("7: auditoría de la carga en OK", "OK",
            (string?)await Escalar("SELECT Estado FROM dbo.CargueFuenteEjecucion WHERE EjecucionID = @e", ("@e", j7.Resultado!.EjecucionID)));
        Check("7: carpeta del job borrada al terminar",
            !Directory.Exists(Path.Combine(carpeta, "jobs", j7.JobId.ToString("N"))));
        // La lista "Cargues en curso y recientes" oculta todo lo del harness (usuarios harness-cargue*,
        // ver UsuariosPruebaCargue): ni el job en memoria ni sus ejecuciones en BD. El job sigue
        // accesible por su id.
        var listaJobs = await orq.ListarAsync(CancellationToken.None);
        Check("7: la lista NO muestra el job en memoria del harness",
            !listaJobs.Any(r => r.JobId == j7.JobId));
        var ejecucionesHarnessBD = await Contar(
            "SELECT COUNT(*) FROM dbo.CargueFuenteEjecucion WHERE Usuario = @u AND Estado = 'OK'", ("@u", UsuarioHarness));
        Check("7: la lista NO incluye ejecuciones de usuarios harness-cargue* (aunque las haya en BD)",
            ejecucionesHarnessBD >= 4 && !listaJobs.Any(r => UsuariosPruebaCargue.EsDePrueba(r.Usuario)),
            $"en BD={ejecucionesHarnessBD}, en la lista={listaJobs.Count(r => UsuariosPruebaCargue.EsDePrueba(r.Usuario))}");
        Check("7: el job sigue accesible por Obtener(id)",
            orq.Obtener(j7.JobId) is { Estado: EstadoCargueJob.Completado } oj7 && oj7.Resultado?.EjecucionID == j7.Resultado.EjecucionID);

        // Cancelar: si la validación ya terminó, cancelar el preview también deja Cancelado.
        var j8 = await Subir(archivoA);
        orq.Cancelar(j8.JobId, UsuarioHarness);
        j8 = await Esperar(j8.JobId, EstadoCargueJob.Validando);
        if (j8.Estado == EstadoCargueJob.PreviewListo) j8 = orq.Cancelar(j8.JobId, UsuarioHarness)!;
        Igual("7: cancelar deja el job Cancelado", EstadoCargueJob.Cancelado, j8.Estado);
        Check("7: descartar borra el job", orq.Descartar(j8.JobId) && orq.Obtener(j8.JobId) is null);
    }
}
