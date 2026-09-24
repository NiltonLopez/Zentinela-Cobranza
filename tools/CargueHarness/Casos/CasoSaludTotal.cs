using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Salud Total (grupo salud, Bloque(1): 999000001100..199). Sin regla propia: flag por teléfono,
/// email o NIT (NIT 0 es vacío), lista negra de correos (el SP no la cruzaba), fecha 1900 → NULL,
/// texto truncado a su largo con advertencia y fijo en la columna de celular anulado.
/// </summary>
internal static class CasoSaludTotal
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.SaludTotal;
        var b = h.Grupo.Bloque(1);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Cedula", "Nombre", "CarteraID", "LiderID", "Numero Telefono", "Email", "Fecha Nacimiento", "Estado Afiliacion",
            "Tipo Afiliado", "Ciudad", "NIT", "Empresa", "Cargo", "Ultima Fecha Pago", "Ingreso Base",
        ];
        string[] Fila(object doc, string nombre, string tel = "", string email = "", string fecha = "", string estado = "",
                      string tipo = "", string ciudad = "", string nit = "", string empresa = "", string cargo = "",
                      string ultimoPago = "", string ingreso = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, tel, email, fecha, estado, tipo, ciudad, nit, empresa, cargo, ultimoPago, ingreso];

        var empresaLarga = "EMPRESA " + new string('X', 110);
        var filas = new List<string[]>
        {
            Fila(T(1), "SALUD TOTAL COMPLETO", "3001234567", "saludtotal1@gmail.com", "1980-05-01", "ACTIVO", "COTIZANTE", "CALI",
                 "900123456", "ACME SAS", "ANALISTA", "2026-08-01", "1423500", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "SOLO NIT", nit: "800123456", empresa: "EMPLEADOR SA"),
            Fila(T(3), "NIT CERO", nit: "0", empresa: "SIN NIT SA", cargo: "OPERARIO"),
            Fila(T(4), "CORREO VETADO", "3001234564", "no@gmail.com"),
            Fila(T(5), "FIJO EN CELULAR", "6012345678", ciudad: "BOGOTA"),
            Fila(T(6), "FECHA VACIA EXCEL", "3001234566", ultimoPago: "1900-01-01", ingreso: "1300000"),
            Fila(T(7), "EMPRESA LARGA", "3001234560", empresa: empresaLarga),
            Fila("ABC", "DOCUMENTO MALO", "3001234569"),
            Fila(T(8), "REPETIDO", "3001234561"),
            Fila(T(8), "REPETIDO", "3001234561", "repetido.saludtotal@gmail.com"),
        };
        var archivo = h.EscribirXlsx("saludtotal.xlsx", encabezado, filas);

        Task<string?> Resumen(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefono, '|', Email, '|', NIT, '|', Empresa, '|', CAST(EstadoEnSaludTotal AS int))
              FROM dbo.SaludTotal WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("SaludTotal", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8,
                                 TitularesNuevos: 8, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("SaludTotal 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("SaludTotal 1: aviso correo en lista negra", 1, Harness.Aviso(p, "email en lista negra"));
                h.Igual("SaludTotal 1: aviso fijo en la columna de celular", 1, Harness.Aviso(p, "NumeroTelefono: número fijo en la columna de celular"));
                h.Igual("SaludTotal 1: aviso Empresa truncada", 1, Harness.Aviso(p, "Empresa: valor truncado a 100"));
                h.Igual("SaludTotal 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("SaludTotal 3: T1 completo",
                    "3001234567|saludtotal1@gmail.com|1980-05-01|ACTIVO|COTIZANTE|CALI|900123456|ACME SAS|ANALISTA|2026-08-01|1423500|1",
                    await h.Texto("""
                        SELECT CONCAT(NumeroTelefono,'|',Email,'|',CONVERT(varchar(10), FechaNacimiento, 23),'|',EstadoAfiliacion,'|',
                                      TipoAfiliado,'|',Ciudad,'|',NIT,'|',Empresa,'|',Cargo,'|',CONVERT(varchar(10), UltimaFechaPago, 23),
                                      '|',IngresoBase,'|',CAST(EstadoEnSaludTotal AS int))
                          FROM dbo.SaludTotal WHERE TitularID = @t
                        """, ("@t", T(1))));
                h.Igual("SaludTotal 3: solo NIT → flag 1 (T2)", "||800123456|EMPLEADOR SA|1", await Resumen(2));
                h.Igual("SaludTotal 3: NIT 0 → NULL; empresa y cargo no cuentan (T3)", "|||SIN NIT SA|0", await Resumen(3));
                h.Igual("SaludTotal 3: correo vetado anulado (T4)", "3001234564||||1", await Resumen(4));
                h.Igual("SaludTotal 3: fijo en celular anulado → flag 0 (T5)", "||||0", await Resumen(5));
                h.Igual("SaludTotal 3: UltimaFechaPago 1900 → NULL (T6)", "|1300000",
                    await h.Texto("SELECT CONCAT(UltimaFechaPago, '|', IngresoBase) FROM dbo.SaludTotal WHERE TitularID = @t", ("@t", T(6))));
                h.Igual("SaludTotal 3: Empresa truncada a 100 (T7)", 100,
                    await h.Contar("SELECT LEN(Empresa) FROM dbo.SaludTotal WHERE TitularID = @t", ("@t", T(7))));
                h.Igual("SaludTotal 3: repetido conserva la fila más completa (T8)", "3001234561|repetido.saludtotal@gmail.com|||1",
                    await Resumen(8));
                h.Igual("SaludTotal 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
