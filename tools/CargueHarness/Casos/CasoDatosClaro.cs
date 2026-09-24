using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// Datos Claro (sub-rango 999000000500..599): N:1 (dos teléfonos del mismo titular = dos
/// filas), teléfono presente en la ListaNegraTelefonos REAL → anulado (y la fila excluida si
/// se queda sin datos), titular inexistente → advertencia (no se crea), sin Titular nuevo, y
/// teléfono de 10 dígitos que no es celular (3) ni fijo (6) → anulado (hallazgo B1 de la paridad).
/// </summary>
internal static class CasoDatosClaro
{
    private const long Base = 999000000500;
    private static long T(int n) => Base + n;

    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.DatosClaro;

        // Un celular real de la lista negra vigente (solo lectura).
        var vetado = await h.Texto("""
            SELECT TOP 1 NumeroTelefono FROM dbo.ListaNegraTelefonos
             WHERE NumeroTelefono LIKE '3%' AND LEN(NumeroTelefono) = 10 ORDER BY NumeroTelefono
            """) ?? throw new InvalidOperationException("ListaNegraTelefonos no tiene celulares para la prueba.");
        Console.WriteLine($"\nDatosClaro: teléfono vetado de prueba {vetado}.");

        // Los números "limpios" del caso no deben estar vetados (3001112233, por ejemplo, sí lo está).
        h.Igual("DatosClaro 0: los teléfonos limpios del caso no están en ListaNegraTelefonos", 0,
            await h.Contar("""
                SELECT COUNT(*) FROM dbo.ListaNegraTelefonos
                 WHERE NumeroTelefono IN ('3001234567', '3007654321', '3219876543', '6041234567')
                """));

        // T1..T3, T6 y T7 existen en Titular (DatosClaro no crea titulares); T4 y T5 no.
        foreach (var n in new[] { 1, 2, 3, 6, 7 })
            await h.Escalar("INSERT INTO dbo.Titular (NumeroDocumento, Nombre) VALUES (@t, @n)", ("@t", T(n)), ("@n", $"CLARO {n}"));

        string[] encabezado = ["Documento", "Telefono", "Correo"];
        var filas = new List<string[]>
        {
            new[] { T(1).ToString(), "3001234567", "claro1@gmail.com" },
            new[] { T(1).ToString(), "3007654321", "" },                      // mismo titular, otro teléfono → 2.ª fila
            new[] { T(2).ToString(), vetado, "claro2@gmail.com" },           // vetado → entra solo con email
            new[] { T(3).ToString(), vetado, "" },                           // vetado y sin email → excluida
            new[] { T(4).ToString(), "3219876543", "" },                     // titular inexistente → aviso, entra
            new[] { T(5).ToString(), "", "correo-malo" },                    // sin teléfono ni email válido → excluida
            new[] { T(1).ToString(), "3001234567", "CLARO1@gmail.com" },     // repetida exacta de la 1.ª
            new[] { "ABC", "3001230000", "" },                               // documento inválido
            new[] { T(2).ToString(), "604 123 4567", "" },                   // fijo: la columna acepta fijo
            new[] { T(6).ToString(), "5551234567", "claro6@gmail.com" },     // ni 3 ni 6 → teléfono anulado, entra con email
            new[] { T(7).ToString(), "5551234567", "" },                     // ni 3 ni 6 y sin email → excluida
        };
        var archivo = h.EscribirXlsx("datosclaro.xlsx", encabezado, filas);

        Task<string?> Filas(int n) => h.Texto("""
            SELECT STRING_AGG(CONCAT(ISNULL(NumeroTelefono, 'NULL'), '/', ISNULL(Email, 'NULL')), ',')
                   WITHIN GROUP (ORDER BY NumeroTelefono, Email)
              FROM dbo.DatosClaro WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("DatosClaro", def, archivo,
            new Harness.Esperado(Leidas: 11, Excluidas: 4, Duplicadas: 1, Candidatas: 6, Insertadas: 6, TitularesNuevos: 0),
            Base + 1, Base + 99,
            trasValidar: p =>
            {
                h.Igual("DatosClaro 1: aviso teléfono en ListaNegraTelefonos", 2, Harness.Aviso(p, "teléfono en ListaNegraTelefonos anulado"));
                h.Igual("DatosClaro 1: error sin teléfono ni email (1 de entrada + 1 tras la lista negra + 1 tras anular el 555)", 3,
                    Harness.Error(p, "Sin teléfono ni email"));
                h.Igual("DatosClaro 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("DatosClaro 1: aviso teléfono que no es celular ni fijo (T6 y T7)", 2, Harness.Aviso(p, "NumeroTelefono: teléfono inválido anulado"));
                h.Igual("DatosClaro 1: aviso titular inexistente", 1, Harness.Aviso(p, "Titular inexistente"));
                h.Check("DatosClaro 1: no pide Nombre ni cartera",
                    !p.ColumnasEncontradas.Contains("Nombre") && p.ColumnasOpcionalesFaltantes.Count == 0,
                    $"opcionales={string.Join(",", p.ColumnasOpcionalesFaltantes)}");
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("DatosClaro 3: dos teléfonos del mismo titular = dos filas (T1)",
                    "3001234567/claro1@gmail.com,3007654321/NULL", await Filas(1));
                h.Igual("DatosClaro 3: teléfono vetado anulado, entra con email; fijo aceptado (T2)",
                    "NULL/claro2@gmail.com,6041234567/NULL", await Filas(2));
                h.Igual("DatosClaro 3: vetado y sin email no entra (T3)", null, await Filas(3));
                h.Igual("DatosClaro 3: teléfono 555 anulado, entra con email (T6)", "NULL/claro6@gmail.com", await Filas(6));
                h.Igual("DatosClaro 3: teléfono 555 y sin email no entra (T7)", null, await Filas(7));
                h.Igual("DatosClaro 3: titular inexistente entra (T4)", "3219876543/NULL", await Filas(4));
                h.Igual("DatosClaro 3: titular inexistente NO se crea en Titular", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular WHERE NumeroDocumento IN (@a, @b)", ("@a", T(4)), ("@b", T(5))));
                h.Igual("DatosClaro 3: ningún teléfono vetado en el rango", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.DatosClaro WHERE TitularID BETWEEN @d AND @h AND NumeroTelefono = @v",
                        ("@d", Base + 1), ("@h", Base + 99), ("@v", vetado)));
                h.Igual("DatosClaro 3: FechaInsercion = sello de hoy del servidor", 6,
                    await h.Contar("SELECT COUNT(*) FROM dbo.DatosClaro WHERE TitularID BETWEEN @d AND @h AND FechaInsercion = CAST(GETDATE() AS date)",
                        ("@d", Base + 1), ("@h", Base + 99)));
            });
    }
}
