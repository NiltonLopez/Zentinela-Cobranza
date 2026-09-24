using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Compensados (grupo empresas, Bloque(5) = 999000002500..599): EstadoEnCompensados es INT (el
/// motor escribe 0/1); el flag sale de EPS y período, NO de la columna Estado del archivo (que se
/// informa como "no se usa"); marca 'NO SE ENCUENTRA' anulada; período '2026-08' → 2026-08-01.
/// </summary>
internal static class CasoCompensados
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Compensados;
        var @base = h.Grupo.Bloque(5);
        long T(int n) => @base + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Documento", "Nombre", "CarteraID", "LiderID", "EPS", "Ultimo Periodo Compensado", "Tipo Afiliado", "Estado"];
        string[] Fila(object doc, string nombre, string eps = "", string periodo = "", string tipo = "", string estado = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, eps, periodo, tipo, estado];

        var filas = new List<string[]>
        {
            Fila(T(1), "COMPENSADO COMPLETO", "Nueva Eps", "2026-08-01", "COTIZANTE", "1", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "SOLO ESTADO DEL ARCHIVO", estado: "1"),
            Fila(T(3), "NO SE ENCUENTRA", tipo: "NO SE ENCUENTRA", estado: "0"),
            Fila(T(4), "PERIODO MES", "Salud Total", "2026-08", "BENEFICIARIO"),
            Fila(T(5), "PERIODO MALO", "Sanitas", "AGOSTO", "COTIZANTE"),
            Fila(T(6), "SOLO TIPO", tipo: "ADICIONAL"),
            Fila(T(7), "REPETIDO", "Compensar"),
            Fila(T(7), "REPETIDO", "Compensar", "2026-07-01", "COTIZANTE"),
            Fila("99", "DOCUMENTO MALO", "Famisanar"),
        };
        var archivo = h.EscribirXlsx("compensados.xlsx", encabezado, filas);

        Task<string?> Fila4(int n) => h.Texto("""
            SELECT CONCAT(EPS,'|',CONVERT(varchar(10), UltimoPeriodoCompensado, 23),'|',TipoAfiliado,'|',EstadoEnCompensados)
              FROM dbo.Compensados WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Compensados", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7,
                                 TitularesNuevos: 7, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("Compensados 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Compensados 1: aviso marca 'NO SE ENCUENTRA'", 1, Harness.Aviso(p, "TipoAfiliado: marca 'NO SE ENCUENTRA'"));
                h.Igual("Compensados 1: aviso período no reconocido", 1, Harness.Aviso(p, "UltimoPeriodoCompensado: fecha no reconocida"));
                h.Igual("Compensados 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                h.Check("Compensados 1: la columna Estado del archivo se informa como no usada",
                    p.Info.Any(i => i.Mensaje == "Columna del archivo que no se usa: Estado"), "");
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Compensados 3: T1 completo, EstadoEnCompensados = 1 (int)", "Nueva Eps|2026-08-01|COTIZANTE|1", await Fila4(1));
                h.Igual("Compensados 3: solo Estado del archivo → flag 0 (el SP daba 1) (T2)", "|||0", await Fila4(2));
                h.Igual("Compensados 3: 'NO SE ENCUENTRA' anulado, flag 0 (T3)", "|||0", await Fila4(3));
                h.Igual("Compensados 3: período '2026-08' → 2026-08-01 (T4)", "Salud Total|2026-08-01|BENEFICIARIO|1", await Fila4(4));
                h.Igual("Compensados 3: período no reconocido anulado, la EPS da el flag (T5)", "Sanitas||COTIZANTE|1", await Fila4(5));
                h.Igual("Compensados 3: solo TipoAfiliado → flag 0, tipo guardado (T6)", "||ADICIONAL|0", await Fila4(6));
                h.Igual("Compensados 3: repetido conserva la fila más completa (T7)", "Compensar|2026-07-01|COTIZANTE|1", await Fila4(7));
                h.Igual("Compensados 3: EstadoEnCompensados solo 0/1", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Compensados WHERE TitularID BETWEEN @d AND @h AND EstadoEnCompensados NOT IN (0, 1)",
                        ("@d", T(1)), ("@h", T(99))));
                h.Igual("Compensados 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
