using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Compensados (grupo empresas, Bloque(5) = 999000002500..599): EstadoEnCompensados es INT (el
/// motor escribe 0/1); el flag sale de EPS, período o la columna Estado del archivo
/// (<c>FlagDesdeArchivo</c>: "encontrado sin datos", que no se guarda); '0' / 'NO SE ENCUENTRA' en
/// Estado no lo encienden; marca 'NO SE ENCUENTRA' anulada; período '2026-08' → 2026-08-01. Paso 5:
/// el flag está en la clave de contenido (un "encontrado sin datos" no refresca la vacía vieja: se
/// inserta y la purga), y un "no encontrado" de un titular con flag 1 es vacía descartada.
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
            Fila(T(8), "ESTADO NO SE ENCUENTRA", estado: "NO SE ENCUENTRA"),
            Fila(T(9), "ESTADO RARO", estado: "PENDIENTE"),
            Fila("99", "DOCUMENTO MALO", "Famisanar"),
        };
        var archivo = h.EscribirXlsx("compensados.xlsx", encabezado, filas);

        Task<string?> Fila4(int n) => h.Texto("""
            SELECT CONCAT(EPS,'|',CONVERT(varchar(10), UltimoPeriodoCompensado, 23),'|',TipoAfiliado,'|',EstadoEnCompensados)
              FROM dbo.Compensados WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Compensados", def, archivo,
            new Harness.Esperado(Leidas: 11, Excluidas: 1, Duplicadas: 1, Candidatas: 9, Insertadas: 9,
                                 TitularesNuevos: 9, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("Compensados 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Compensados 1: aviso marca 'NO SE ENCUENTRA'", 1, Harness.Aviso(p, "TipoAfiliado: marca 'NO SE ENCUENTRA'"));
                h.Igual("Compensados 1: aviso período no reconocido", 1, Harness.Aviso(p, "UltimoPeriodoCompensado: fecha no reconocida"));
                h.Igual("Compensados 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                h.Check("Compensados 1: la columna Estado del archivo se usa (FlagDesdeArchivo)",
                    p.ColumnasEncontradas.Contains("Estado") && !p.Info.Any(i => i.Mensaje == "Columna del archivo que no se usa: Estado"), "");
                h.Igual("Compensados 1: info 'encontrado sin datos' solo T2 (T1 tiene EPS)", 1,
                    p.Info.Where(i => i.Mensaje.StartsWith("Estado: encontrado sin datos")).Sum(i => i.Cantidad));
                h.Igual("Compensados 1: aviso Estado no reconocido (T9)", 1, Harness.Aviso(p, "Estado: valor no reconocido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Compensados 3: T1 completo, EstadoEnCompensados = 1 (int)", "Nueva Eps|2026-08-01|COTIZANTE|1", await Fila4(1));
                h.Igual("Compensados 3: solo Estado '1' → encontrado sin datos, flag 1 (T2)", "|||1", await Fila4(2));
                h.Igual("Compensados 3: 'NO SE ENCUENTRA' anulado + Estado '0', flag 0 (T3)", "|||0", await Fila4(3));
                h.Igual("Compensados 3: Estado 'NO SE ENCUENTRA' → flag 0 (T8)", "|||0", await Fila4(8));
                h.Igual("Compensados 3: Estado no reconocido → flag 0 (T9)", "|||0", await Fila4(9));
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

        // ── 5. El flag del archivo está en la clave de contenido ────────────────
        // T3 tiene en el destino una fila vacía con flag 0 ("no encontrado") y ahora llega "encontrado
        // sin datos": mismo contenido (todo NULL) pero otro flag → se inserta y la vieja se purga (sin
        // el flag en la clave solo se refrescaría la de flag 0 y la purga la borraría: T3 sin fila).
        // T2 tiene flag 1 sin datos y ahora llega "no encontrado": es vacía y no entra (política).
        Console.WriteLine("\n== Compensados 5. El Estado cambia (flag en la clave) ==");
        var archivoB = h.EscribirXlsx("compensados-b.xlsx", encabezado,
        [
            Fila(T(3), "NO SE ENCUENTRA", estado: "1"),
            Fila(T(2), "SOLO ESTADO DEL ARCHIVO", estado: "0"),
        ]);
        var p5 = await h.Validar(def, archivoB);
        Harness.Mostrar(p5);
        h.Igual("Compensados 5: preview insertadas (T3 encontrado)", 1, p5.Insertadas);
        h.Igual("Compensados 5: preview refrescadas", 0, p5.Refrescadas);
        h.Igual("Compensados 5: preview vacías descartadas (T2 no encontrado)", 1, p5.DescartadasVacias);
        h.Igual("Compensados 5: preview vacías a purgar (la de T3)", 1, p5.VaciasQuePurgara);
        h.Invariantes("Compensados 5", p5);
        var (r5, e5) = await h.CargaReal(def, "Compensados 5", archivoB, p5);
        h.Desglose("Compensados 5", r5);
        h.Igual("Compensados 5: insertadas", 1, r5.Insertadas);
        h.Igual("Compensados 5: purgadas", 1, r5.VaciasPurgadas);
        h.Igual("Compensados 5: T3 queda con una sola fila, flag 1", "1:1",
            await h.Texto("SELECT CONCAT(COUNT(*), ':', SUM(EstadoEnCompensados)) FROM dbo.Compensados WHERE TitularID = @t", ("@t", T(3))));
        h.Igual("Compensados 5: T2 sigue con una sola fila, flag 1", "1:1",
            await h.Texto("SELECT CONCAT(COUNT(*), ':', SUM(EstadoEnCompensados)) FROM dbo.Compensados WHERE TitularID = @t", ("@t", T(2))));
        h.Igual("Compensados 5: reversión 'D' de la purga", 1, await h.Reversiones(e5, 'D'));

        // Segunda carga del archivo B: 0 insertadas (T3 refresca con su flag 1; T2 sigue descartada).
        var p6 = await h.Validar(def, archivoB);
        h.Igual("Compensados 6: preview insertadas", 0, p6.Insertadas);
        h.Igual("Compensados 6: preview refrescadas", 1, p6.Refrescadas);
        h.Igual("Compensados 6: preview vacías descartadas", 1, p6.DescartadasVacias);
        h.Igual("Compensados 6: preview vacías a purgar", 0, p6.VaciasQuePurgara);
        var antes6 = await h.ContarTabla("Compensados");
        var (r6, _) = await h.CargaReal(def, "Compensados 6", archivoB, p6);
        h.Igual("Compensados 6: insertadas", 0, r6.Insertadas);
        h.Igual("Compensados 6: COUNT(*) igual antes y después", antes6, await h.ContarTabla("Compensados"));
    }
}
