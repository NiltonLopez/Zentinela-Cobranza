using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// MiVacuna (grupo salud, Bloque(5): 999000001500..599). Sin regla propia: celular '0' → NULL,
/// correo vetado → NULL (el SP lo dejaba en '' y contaba como dato), flag por celular o email y
/// FechaExpedicion que no cuenta para el flag. El destino no tiene FK a Titular, pero el cargue
/// crea Titular y relaciones como el SP.
/// </summary>
internal static class CasoMiVacuna
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.MiVacuna;
        var b = h.Grupo.Bloque(5);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Documento", "Nombre", "CarteraID", "LiderID", "Fecha Expedicion", "Celular", "Email"];
        string[] Fila(object doc, string nombre, string cel = "", string email = "", string fecha = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, fecha, cel, email];

        var filas = new List<string[]>
        {
            Fila(T(1), "MIVACUNA COMPLETO", "3001234567", "mivacuna1@gmail.com", "2005-06-01", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "CELULAR CERO", "0", fecha: "2010-01-01"),
            Fila(T(3), "SOLO CORREO VETADO", email: "no@gmail.com"),
            Fila(T(4), "CORREO LIKE", "3001234564", "juana@gmail.com"),
            Fila(T(5), "FIJO EN CELULAR", "6015551234", "mivacuna5@gmail.com"),
            Fila("ABC", "DOCUMENTO MALO", "3001234569"),
            Fila(T(8), "REPETIDO", "3001234561"),
            Fila(T(8), "REPETIDO", "3001234561", "repetido.mivacuna@gmail.com"),
        };
        var archivo = h.EscribirXlsx("mivacuna.xlsx", encabezado, filas);

        Task<string?> Resumen(int n) => h.Texto("""
            SELECT CONCAT(CONVERT(varchar(10), FechaExpedicion, 23), '|', Celular, '|', Email, '|', CAST(EstadoEnMiVacuna AS int))
              FROM dbo.MiVacuna WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("MiVacuna", def, archivo,
            new Harness.Esperado(Leidas: 8, Excluidas: 1, Duplicadas: 1, Candidatas: 6, Insertadas: 6,
                                 TitularesNuevos: 6, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("MiVacuna 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("MiVacuna 1: aviso correo en lista negra", 1, Harness.Aviso(p, "email en lista negra"));
                h.Igual("MiVacuna 1: aviso fijo en la columna de celular", 1, Harness.Aviso(p, "Celular: número fijo en la columna de celular"));
                h.Igual("MiVacuna 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("MiVacuna 3: T1 completo", "2005-06-01|3001234567|mivacuna1@gmail.com|1", await Resumen(1));
                h.Igual("MiVacuna 3: celular '0' → NULL; la fecha no cuenta para el flag (T2)", "2010-01-01|||0", await Resumen(2));
                h.Igual("MiVacuna 3: correo vetado → NULL (no ''), flag 0 (T3)", "|||0", await Resumen(3));
                h.Igual("MiVacuna 3: correo que solo caería por LIKE se conserva (T4)", "|3001234564|juana@gmail.com|1", await Resumen(4));
                h.Igual("MiVacuna 3: fijo en celular anulado, el correo cuenta (T5)", "||mivacuna5@gmail.com|1", await Resumen(5));
                h.Igual("MiVacuna 3: repetido conserva la fila más completa (T8)", "|3001234561|repetido.mivacuna@gmail.com|1", await Resumen(8));
                h.Igual("MiVacuna 3: ningún correo vacío ('') en el rango", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.MiVacuna WHERE TitularID BETWEEN @d AND @h AND Email = ''",
                        ("@d", b), ("@h", b + 99)));
                h.Igual("MiVacuna 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
