using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// SOS (grupo salud, Bloque(2): 999000001200..299). Sin regla propia: flag por cualquiera de las
/// cuatro columnas (NitEmpresa 0 es vacío), Telefono como celular (fijo de 7 y fijo 60x anulados),
/// Direccion truncada a 70 con advertencia (en el SP un valor así tumbaba el INSERT) y "Razon Social"
/// que va al dato, no al nombre del titular.
/// </summary>
internal static class CasoSos
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Sos;
        var b = h.Grupo.Bloque(2);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Documento", "Nombre", "CarteraID", "LiderID", "NIT Empresa", "Razon Social", "Telefono", "Dirección"];
        string[] Fila(object doc, string nombre, string nit = "", string razon = "", string tel = "", string dir = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, nit, razon, tel, dir];

        var direccionLarga = "CALLE 5 # 10-20 " + new string('B', 70);
        var filas = new List<string[]>
        {
            Fila(T(1), "SOS COMPLETO", "891300237", "INGENIO DEL CAUCA", "3001234567", "CORREG LA PAILA",
                 par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "SOLO NIT", "900336004"),
            Fila(T(3), "NIT CERO", "0"),
            Fila(T(4), "FIJO DE SIETE", razon: "COVATRANS LTDA", tel: "3301921"),
            Fila(T(5), "FIJO DIEZ", tel: "6021234567"),
            Fila(T(6), "DIRECCION LARGA", tel: "3001234566", dir: direccionLarga),
            Fila("ABC", "DOCUMENTO MALO", tel: "3001234569"),
            Fila(T(8), "REPETIDO", tel: "3001234561"),
            Fila(T(8), "REPETIDO", "800197463", "POLLOS EL BUCANERO S.A.", "3001234561"),
        };
        var archivo = h.EscribirXlsx("sos.xlsx", encabezado, filas);

        Task<string?> Resumen(int n) => h.Texto("""
            SELECT CONCAT(NitEmpresa, '|', RazonSocial, '|', Telefono, '|', Direccion, '|', CAST(EstadoEnSOS AS int))
              FROM dbo.SOS WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("SOS", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7,
                                 TitularesNuevos: 7, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("SOS 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("SOS 1: aviso celular inválido (fijo de 7)", 1, Harness.Aviso(p, "Telefono: celular inválido anulado"));
                h.Igual("SOS 1: aviso fijo en la columna de celular", 1, Harness.Aviso(p, "Telefono: número fijo en la columna de celular"));
                h.Igual("SOS 1: aviso Direccion truncada", 1, Harness.Aviso(p, "Direccion: valor truncado a 70"));
                h.Igual("SOS 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                h.Check("SOS 1: 'Razon Social' se usa como dato, no como nombre",
                    p.ColumnasEncontradas.Contains("RazonSocial") && p.ColumnasEncontradas.Contains("Nombre"),
                    string.Join(",", p.ColumnasEncontradas));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("SOS 3: T1 completo", "891300237|INGENIO DEL CAUCA|3001234567|CORREG LA PAILA|1", await Resumen(1));
                h.Igual("SOS 3: nombre del titular desde 'Nombre' (T1)", "SOS COMPLETO",
                    await h.Texto("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(1))));
                h.Igual("SOS 3: solo NIT → flag 1 (T2)", "900336004||||1", await Resumen(2));
                h.Igual("SOS 3: NIT 0 → NULL, flag 0 (T3)", "||||0", await Resumen(3));
                h.Igual("SOS 3: fijo de 7 anulado; razón social cuenta (T4)", "|COVATRANS LTDA|||1", await Resumen(4));
                h.Igual("SOS 3: fijo 60x anulado → flag 0 (T5)", "||||0", await Resumen(5));
                h.Igual("SOS 3: Direccion truncada a 70 (T6)", 70,
                    await h.Contar("SELECT LEN(Direccion) FROM dbo.SOS WHERE TitularID = @t", ("@t", T(6))));
                h.Igual("SOS 3: repetido conserva la fila más completa (T8)", "800197463|POLLOS EL BUCANERO S.A.|3001234561||1",
                    await Resumen(8));
                h.Igual("SOS 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
