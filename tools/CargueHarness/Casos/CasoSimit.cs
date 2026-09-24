using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// SIMIT (sub-rango 999000000400..499): 1 fila del archivo → N filas destino (una por placa
/// distinta de Placa1..Placa15), 0 placas → 1 fila Placa NULL con EstadoEnSIMIT = 0, placa de
/// más de 10 caracteres y marcas de texto ('PAZ Y SALVO', 'NO CARGO'...) descartadas con un solo
/// aviso agrupado, espacio y guion quitados, y la invariante leídas + expansión = excluidas + duplicadas
/// + candidatas.
/// </summary>
internal static class CasoSimit
{
    private const long Base = 999000000400;
    private static long T(int n) => Base + n;

    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Simit;

        // "Placa" (sin número) resuelve Placa1; luego Placa2..Placa15.
        var encabezado = new[] { "Cedula", "Nombre", "Placa" }
            .Concat(Enumerable.Range(2, 14).Select(i => $"Placa{i}")).ToArray();
        string[] Fila(string doc, string nombre, params (int N, string Placa)[] placas)
        {
            var f = new string[encabezado.Length];
            f[0] = doc;
            f[1] = nombre;
            for (var i = 2; i < f.Length; i++) f[i] = "";
            foreach (var (n, placa) in placas) f[1 + n] = placa;
            return f;
        }

        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "DOS PLACAS Y UNA REPETIDA", (1, "ABC123"), (2, "xyz 789"), (3, "ABC123")),   // → 2 filas
            Fila(T(2).ToString(), "TRES PLACAS", (1, "DEF111"), (5, "DEF222"), (15, "DEF333")),                  // → 3 filas
            Fila(T(3).ToString(), "SIN PLACAS"),                                                                // → 1 fila NULL, Estado 0
            Fila(T(4).ToString(), "PLACA LARGA", (1, "ABCDEFGHIJK"), (2, "GHI456")),                            // → 1 fila + aviso
            Fila(T(5).ToString(), "CON PLACA", (1, "JKL789")),
            Fila(T(5).ToString(), "CON PLACA"),                                                                 // vacía de un titular con placa
            Fila(T(6).ToString(), "REPETIDA EXACTA", (1, "MNO012")),
            Fila(T(6).ToString(), "REPETIDA EXACTA", (2, "mno012")),                                            // misma placa → duplicada
            Fila("ABC", "DOCUMENTO MALO", (1, "PQR345")),
            // Marcas reales de dbo.SIMIT (el SP las guardaba como placa con EstadoEnSIMIT = 1).
            // "N/A" y "0" ya son vacíos genéricos (LimpiarTexto): no suman aviso; las otras 5 sí.
            Fila(T(7).ToString(), "SOLO MARCAS", (1, "PAZ Y SALVO"), (2, "NO CARGO"), (3, "SIN"), (4, "N/A"),
                 (5, "XXXXXX"), (6, "99999"), (7, "0")),                                                         // → 1 fila NULL, Estado 0
            Fila(T(8).ToString(), "MARCA Y PLACAS CON ESPACIO Y GUION", (1, "NO CARGO"), (2, "QUR 25"), (3, "CKH-13")), // → 2 filas + aviso
        };
        var archivo = h.EscribirXlsx("simit.xlsx", encabezado, filas);

        Task<string?> Placas(int n) => h.Texto("""
            SELECT STRING_AGG(CONCAT(ISNULL(Placa, 'NULL'), ':', CAST(EstadoEnSIMIT AS int)), ',') WITHIN GROUP (ORDER BY Placa)
              FROM dbo.SIMIT WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("SIMIT", def, archivo,
            new Harness.Esperado(Leidas: 11, Excluidas: 1, Duplicadas: 1, Candidatas: 13, Insertadas: 12,
                                 TitularesNuevos: 8, DescartadasVacias: 1, AgregadasPorExpansion: 4),
            Base + 1, Base + 99,
            trasValidar: p =>
            {
                h.Igual("SIMIT 1: agregadas por expansión en el preview", 4, p.AgregadasPorExpansion);
                h.Igual("SIMIT 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                // 1 placa larga (T4) + 5 marcas (T7) + 1 marca (T8), en UN solo grupo.
                h.Igual("SIMIT 1: aviso 'no es una placa' (larga + marcas)", 7, Harness.Aviso(p, "valor que no es una placa descartado"));
                h.Igual("SIMIT 1: las placas descartadas son UN grupo de advertencia", 1,
                    p.Advertencias.Count(a => a.Mensaje.Contains("placa", StringComparison.OrdinalIgnoreCase)));
                h.Check("SIMIT 1: la columna Placa no se pide en el archivo",
                    !p.ColumnasOpcionalesFaltantes.Contains("Placa") && p.ColumnasEncontradas.Contains("Placa1")
                    && p.ColumnasEncontradas.Contains("Placa15"),
                    $"encontradas={string.Join(",", p.ColumnasEncontradas)} opcionales={string.Join(",", p.ColumnasOpcionalesFaltantes)}");
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("SIMIT 3: 1 fila → 2 placas distintas, normalizadas (T1)", "ABC123:1,XYZ789:1", await Placas(1));
                h.Igual("SIMIT 3: Placa1, Placa5 y Placa15 → 3 filas (T2)", "DEF111:1,DEF222:1,DEF333:1", await Placas(2));
                h.Igual("SIMIT 3: 0 placas → 1 fila Placa NULL, Estado 0 (T3)", "NULL:0", await Placas(3));
                h.Igual("SIMIT 3: placa larga descartada, la otra entra (T4)", "GHI456:1", await Placas(4));
                h.Igual("SIMIT 3: la fila vacía del titular con placa no entra (T5)", "JKL789:1", await Placas(5));
                h.Igual("SIMIT 3: misma placa en dos filas = una (T6)", "MNO012:1", await Placas(6));
                h.Igual("SIMIT 3: solo marcas → 1 fila Placa NULL, Estado 0 (T7)", "NULL:0", await Placas(7));
                h.Igual("SIMIT 3: marca descartada, espacio y guion quitados (T8)", "CKH13:1,QUR25:1", await Placas(8));
                h.Igual("SIMIT 3: ninguna marca entró como placa", 0, await h.Contar("""
                    SELECT COUNT(*) FROM dbo.SIMIT
                     WHERE TitularID BETWEEN @d AND @h
                       AND Placa IN ('PAZYSALVO', 'PAZ Y SALVO', 'PAZYSALV', 'NOCARGO', 'NO CARGO', 'SIN', 'XXXXXX', '99999', 'N/A', '0')
                    """, ("@d", Base + 1), ("@h", Base + 99)));
            });
    }
}
