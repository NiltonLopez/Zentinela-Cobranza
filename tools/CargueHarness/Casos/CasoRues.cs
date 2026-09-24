using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// RUES (grupo empresas, Bloque(0) = 999000002000..099): primera fuente con sello
/// <c>datetime</c> (<c>GETDATE()</c> con hora, como el SP); matrícula entera ('0' y texto se
/// anulan), textos truncados a su largo real con aviso, flag sin NombreEmpresa, relación con
/// par activo.
/// </summary>
internal static class CasoRues
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Rues;
        var @base = h.Grupo.Bloque(0);
        long T(int n) => @base + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Documento", "Nombre", "CarteraID", "LiderID", "Matricula", "Estado Empresa", "Actividad Economica", "Nombre Empresa",
        ];
        string[] Fila(object doc, string nombre, string matricula = "", string estado = "", string actividad = "",
                      string empresa = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, matricula, estado, actividad, empresa];

        var actividadLarga = "COMERCIO AL POR MENOR " + new string('X', 260);   // > varchar(250)
        var empresaLarga = "INVERSIONES " + new string('Y', 80);                // > varchar(70)
        var filas = new List<string[]>
        {
            Fila(T(1), "RUES COMPLETO", "4042292", "Activa", "Expendio a la mesa de comidas preparadas", "LA ESQUINA SAS",
                 par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "SOLO NOMBRE EMPRESA", empresa: "TIENDA SIN MATRICULA"),
            Fila(T(3), "MATRICULA CERO", "0", "N/A", "Gestión de instalaciones deportivas"),
            Fila(T(4), "MATRICULA TEXTO", "ABC-12", "Cancelada"),
            Fila(T(5), "ACTIVIDAD LARGA", "189859", "Activa", actividadLarga),
            Fila(T(6), "REPETIDO", "96168"),
            Fila(T(6), "REPETIDO", "96168", "Activa", "Gestión de instalaciones deportivas"),
            Fila("12", "DOCUMENTO MALO", "121046"),
            Fila(T(7), "SIN NADA"),
            Fila(T(8), "EMPRESA LARGA", "207114", empresa: empresaLarga),
        };
        var archivo = h.EscribirXlsx("rues.xlsx", encabezado, filas);

        Task<string?> Fila3(int n) => h.Texto("""
            SELECT CONCAT(NumeroMatricula,'|',EstadoEmpresa,'|',LEFT(ActividadEconomica, 30),'|',NombreEmpresa,'|',CAST(EstadoEnRUES AS int))
              FROM dbo.RUES WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("RUES", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8,
                                 TitularesNuevos: 8, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("RUES 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("RUES 1: aviso matrícula no numérica", 1, Harness.Aviso(p, "NumeroMatricula: valor no numérico anulado"));
                h.Igual("RUES 1: aviso actividad truncada a 250", 1, Harness.Aviso(p, "ActividadEconomica: valor truncado a 250"));
                h.Igual("RUES 1: aviso nombre de empresa truncado a 70", 1, Harness.Aviso(p, "NombreEmpresa: valor truncado a 70"));
                h.Igual("RUES 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (r, ejecucion) =>
            {
                h.Igual("RUES 3: T1 completo", "4042292|Activa|Expendio a la mesa de comidas |LA ESQUINA SAS|1", await Fila3(1));
                h.Igual("RUES 3: solo NombreEmpresa → EstadoEnRUES = 0, empresa guardada (T2)", "|||TIENDA SIN MATRICULA|0", await Fila3(2));
                h.Igual("RUES 3: matrícula '0' y estado 'N/A' → NULL, la actividad da el flag (T3)",
                    "||Gestión de instalaciones depor||1", await Fila3(3));
                h.Igual("RUES 3: matrícula no numérica anulada, el estado da el flag (T4)", "|Cancelada|||1", await Fila3(4));
                h.Igual("RUES 3: actividad truncada a 250 caracteres (T5)", 250,
                    await h.Contar("SELECT LEN(ActividadEconomica) FROM dbo.RUES WHERE TitularID = @t", ("@t", T(5))));
                h.Igual("RUES 3: repetido conserva la fila más completa (T6)", "96168|Activa|Gestión de instalaciones depor||1",
                    await Fila3(6));
                h.Igual("RUES 3: fila sin datos → EstadoEnRUES = 0 (T7)", "||||0", await Fila3(7));
                h.Igual("RUES 3: nombre de empresa truncado a 70 (T8)", 70,
                    await h.Contar("SELECT LEN(NombreEmpresa) FROM dbo.RUES WHERE TitularID = @t", ("@t", T(8))));
                h.Igual("RUES 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
                // Sello datetime: FechaConsulta es exactamente el GETDATE() leído en la transacción
                // (CargueFuenteEjecucion lo guarda como datetime2; se compara convertido a datetime).
                // El sello va truncado al segundo porque SelloFechaConsulta es datetime2(0): así el
                // script de reversión puede ubicar las filas 'I' por igualdad exacta.
                h.Igual("RUES 3: FechaConsulta = sello datetime de la ejecución (con hora, al segundo)", 8,
                    await h.Contar("""
                        SELECT COUNT(*) FROM dbo.RUES s
                          JOIN dbo.CargueFuenteEjecucion e ON e.EjecucionID = @e
                         WHERE s.TitularID BETWEEN @d AND @h AND s.FechaConsulta = CONVERT(datetime, e.SelloFechaConsulta)
                        """, ("@e", ejecucion), ("@d", T(1)), ("@h", T(99))));
                h.Check("RUES 3: el sello conserva la hora (no es medianoche) y no tiene milisegundos",
                    r.SelloFechaConsulta.TimeOfDay != TimeSpan.Zero && r.SelloFechaConsulta.Millisecond == 0,
                    r.SelloFechaConsulta.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            });
    }
}
