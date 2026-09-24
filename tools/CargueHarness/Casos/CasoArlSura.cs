using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// ARLSura (grupo empresas, Bloque(4) = 999000002400..499): empleador por NIT y empresa. Fuente
/// distinta de Sura (EPS): cargar ARLSura escribe dbo.ARLSura y deja dbo.Sura intacta (mismo
/// nombre de flag, EstadoEnSura, en tablas distintas). NIT '0' → NULL; solo empresa da flag.
/// </summary>
internal static class CasoArlSura
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.ArlSura;
        var @base = h.Grupo.Bloque(4);
        long T(int n) => @base + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Cedula", "Nombre", "CarteraID", "LiderID", "NIT Empleador", "Empresa"];
        string[] Fila(object doc, string nombre, string nit = "", string empresa = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, nit, empresa];

        var filas = new List<string[]>
        {
            Fila(T(1), "ARL COMPLETO", "890900608", "GRUPO LA VIRGINIA S.A.S.", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "SOLO EMPRESA", empresa: "MERCA VILLA LA ECONOMICA S.A.S."),
            Fila(T(3), "NIT CERO", "0"),
            Fila(T(4), "EMPRESA LARGA", "900000002", "EMPRESA " + new string('Q', 110)),
            Fila(T(5), "REPETIDO", "800222333"),
            Fila(T(5), "REPETIDO", "800222333", "REPETIDA S.A.S."),
            Fila("12AB", "DOCUMENTO MALO", "800222444"),
        };
        var archivo = h.EscribirXlsx("arlsura.xlsx", encabezado, filas);

        Task<string?> Fila2(int n) => h.Texto("""
            SELECT CONCAT(NIT,'|',LEFT(Empresa, 40),'|',CAST(EstadoEnSura AS int)) FROM dbo.ARLSura WHERE TitularID = @t
            """, ("@t", T(n)));

        var suraAntes = await h.ContarTabla("Sura");
        await h.FlujoEstandar("ARLSura", def, archivo,
            new Harness.Esperado(Leidas: 7, Excluidas: 1, Duplicadas: 1, Candidatas: 5, Insertadas: 5,
                                 TitularesNuevos: 5, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("ARLSura 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("ARLSura 1: aviso empresa truncada a 100", 1, Harness.Aviso(p, "Empresa: valor truncado a 100"));
                h.Igual("ARLSura 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                h.Check("ARLSura 1: la columna 'NIT Empleador' es el NIT de la empresa, no el documento",
                    p.ColumnasEncontradas.Contains("NIT") && p.Muestra.Any(f => f[0] == T(1).ToString()), string.Join(",", p.ColumnasEncontradas));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("ARLSura 3: T1 completo", "890900608|GRUPO LA VIRGINIA S.A.S.|1", await Fila2(1));
                h.Igual("ARLSura 3: solo empresa → flag 1 (T2)", "|MERCA VILLA LA ECONOMICA S.A.S.|1", await Fila2(2));
                h.Igual("ARLSura 3: NIT '0' → NULL, sin datos → EstadoEnSura = 0 (T3)", "||0", await Fila2(3));
                h.Igual("ARLSura 3: empresa truncada a 100 (T4)", 100,
                    await h.Contar("SELECT LEN(Empresa) FROM dbo.ARLSura WHERE TitularID = @t", ("@t", T(4))));
                h.Igual("ARLSura 3: repetido conserva la fila más completa (T5)", "800222333|REPETIDA S.A.S.|1", await Fila2(5));
                h.Igual("ARLSura 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
                h.Igual("ARLSura 3: dbo.Sura (la EPS) intacta", suraAntes, await h.ContarTabla("Sura"));
                h.Igual("ARLSura 3: dbo.Sura sin filas del sub-rango", 0, await h.EnRango("Sura", T(1), T(99)));
            });
    }
}
