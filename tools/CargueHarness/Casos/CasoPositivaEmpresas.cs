using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// PositivaEmpresas (grupo empresas, Bloque(3) = 999000002300..399): empleador por NIT y
/// razón social; razón social partida en líneas (PDF) colapsada a una; NIT con puntos; NIT '0'
/// → NULL; 'NIT-DV' → NIT sin el DV; solo EstadoAfiliacion → flag 0; relación con par activo.
/// </summary>
internal static class CasoPositivaEmpresas
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.PositivaEmpresas;
        var @base = h.Grupo.Bloque(3);
        long T(int n) => @base + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Identificacion", "Nombre", "CarteraID", "LiderID", "NIT", "Empresa", "Estado Afiliacion"];
        string[] Fila(object doc, string nombre, string nit = "", string empresa = "", string estado = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, nit, empresa, estado];

        var filas = new List<string[]>
        {
            Fila(T(1), "POSITIVA COMPLETO", "899999103", "SENADO DE LA REPUBLICA", "Activa", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "EMPRESA EN LINEAS", "900691735", "COMERCIALIZADORA ANDINA\nFRIO SAS", "Activa"),
            Fila(T(3), "NIT CON PUNTOS", "899.999.034", "SERVICIO NACIONAL DE APRENDIZAJE SENA"),
            Fila(T(4), "SOLO ESTADO", "0", estado: "Retirado"),
            Fila(T(5), "NIT CON DV", "900123456-7", "ACME SAS"),
            Fila(T(6), "EMPRESA LARGA", "900000001", "RAZON " + new string('Z', 240)),
            Fila(T(7), "REPETIDO", "800111222"),
            Fila(T(7), "REPETIDO", "800111222", "REPETIDA SAS", "Inactiva"),
            Fila("1234", "DOCUMENTO MALO", "800111333"),
        };
        var archivo = h.EscribirXlsx("positivaempresas.xlsx", encabezado, filas);

        Task<string?> Fila4(int n) => h.Texto("""
            SELECT CONCAT(NIT,'|',LEFT(Empresa, 40),'|',EstadoAfiliacion,'|',CAST(EstadoEnPositivaEmpresa AS int))
              FROM dbo.PositivaEmpresas WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("PositivaEmpresas", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7,
                                 TitularesNuevos: 7, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("PositivaEmpresas 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                // H4 de PARIDAD-empresas.md: 'NIT-DV' pierde el DV (informativo), ya no se anula.
                h.Igual("PositivaEmpresas 1: sin aviso NIT no numérico", 0, Harness.Aviso(p, "NIT: valor no numérico anulado"));
                h.Igual("PositivaEmpresas 1: info NIT con DV (T5)", 1,
                    p.Info.Where(i => i.Mensaje == "NIT: dígito de verificación quitado").Sum(i => i.Cantidad));
                h.Igual("PositivaEmpresas 1: aviso empresa truncada a 230", 1, Harness.Aviso(p, "Empresa: valor truncado a 230"));
                h.Igual("PositivaEmpresas 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("PositivaEmpresas 3: T1 completo", "899999103|SENADO DE LA REPUBLICA|Activa|1", await Fila4(1));
                h.Igual("PositivaEmpresas 3: razón social en dos líneas → una (T2)", "900691735|COMERCIALIZADORA ANDINA FRIO SAS|Activa|1", await Fila4(2));
                h.Igual("PositivaEmpresas 3: NIT con puntos (T3)", "899999034|SERVICIO NACIONAL DE APRENDIZAJE SENA||1", await Fila4(3));
                h.Igual("PositivaEmpresas 3: NIT '0' → NULL, solo estado → flag 0 (T4)", "||Retirado|0", await Fila4(4));
                h.Igual("PositivaEmpresas 3: NIT con DV guardado sin el DV (T5)", "900123456|ACME SAS||1", await Fila4(5));
                h.Igual("PositivaEmpresas 3: empresa truncada a 230 (T6)", 230,
                    await h.Contar("SELECT LEN(Empresa) FROM dbo.PositivaEmpresas WHERE TitularID = @t", ("@t", T(6))));
                h.Igual("PositivaEmpresas 3: repetido conserva la fila más completa (T7)", "800111222|REPETIDA SAS|Inactiva|1", await Fila4(7));
                h.Igual("PositivaEmpresas 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
