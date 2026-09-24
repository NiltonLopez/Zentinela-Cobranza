using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Cámara (grupo empresas, Bloque(1) = 999000002100..199): fijo de 7 dígitos con indicativo
/// por departamento (incluido 'NORTE DE SANTANDER', que solo está en PrefijosDepartamento) y
/// 'BOGOTÁ D.C.'; relleno del formulario ('Seleccione un departamento') anulado; teléfono y email
/// validados y lista negra de correos exacta (el SP de Cámara no validaba ni la cruzaba).
/// </summary>
internal static class CasoCamara
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Camara;
        var @base = h.Grupo.Bloque(1);
        long T(int n) => @base + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado = ["Documento", "Nombre", "CarteraID", "LiderID", "Telefono", "Correo", "Municipio", "Departamento"];
        string[] Fila(object doc, string nombre, string tel = "", string email = "", string municipio = "", string depto = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, tel, email, municipio, depto];

        var filas = new List<string[]>
        {
            Fila(T(1), "CAMARA COMPLETO", "3001234567", "camara1@gmail.com", "MEDELLIN", "ANTIOQUIA",
                 par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "FIJO NORTE SANTANDER", "2345678", depto: "NORTE DE SANTANDER"),
            Fila(T(3), "FIJO BOGOTA TILDE", "2345679", municipio: "BOGOTÁ D.C.", depto: "BOGOTÁ D.C."),
            Fila(T(4), "RELLENO FORMULARIO", "2345670", depto: "Seleccione un departamento"),
            Fila(T(5), "EMAIL NO TIENE", "3001234568", "NO TIENE"),
            Fila(T(6), "EMAIL MAYUSCULAS", email: "JUAN.CAMARA@GMAIL.COM"),
            Fila(T(7), "EMAIL VETADO", "3001234569", "no@gmail.com"),
            Fila(T(8), "TELEFONO RARO", "5551234567", "raro@gmail.com"),
            Fila(T(9), "REPETIDO", "3001234560"),
            Fila(T(9), "REPETIDO", "3001234560", "repetido@gmail.com", "CALI", "VALLE DEL CAUCA"),
            Fila("ABC", "DOCUMENTO MALO", "3001234561"),
            Fila(T(10), "MUNICIPIO PUNTO", "3001234562", municipio: ".", depto: "CESAR"),
        };
        var archivo = h.EscribirXlsx("camara.xlsx", encabezado, filas);

        Task<string?> Fila5(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefono,'|',Email,'|',Municipio,'|',Departamento,'|',CAST(EstadoEnCamara AS int))
              FROM dbo.Camara WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Camara", def, archivo,
            new Harness.Esperado(Leidas: 12, Excluidas: 1, Duplicadas: 1, Candidatas: 10, Insertadas: 10,
                                 TitularesNuevos: 10, Relaciones: 1),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("Camara 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Camara 1: aviso fijo sin prefijo resoluble", 1, Harness.Aviso(p, "sin prefijo resoluble"));
                h.Igual("Camara 1: aviso relleno del formulario", 1, Harness.Aviso(p, "texto de relleno del formulario"));
                h.Igual("Camara 1: aviso email con formato inválido", 1, Harness.Aviso(p, "Email: email con formato inválido"));
                h.Igual("Camara 1: aviso email en lista negra", 1, Harness.Aviso(p, "Email: email en lista negra"));
                h.Igual("Camara 1: aviso teléfono inválido", 1, Harness.Aviso(p, "NumeroTelefono: teléfono inválido anulado"));
                h.Igual("Camara 1: aviso municipio sin letras", 1, Harness.Aviso(p, "Municipio: valor sin letras"));
                h.Igual("Camara 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Camara 3: T1 completo", "3001234567|camara1@gmail.com|MEDELLIN|ANTIOQUIA|1", await Fila5(1));
                h.Igual("Camara 3: fijo de 7 + 'NORTE DE SANTANDER' → 607 (PrefijosDepartamento) (T2)", "6072345678|||NORTE DE SANTANDER|1",
                    await Fila5(2));
                h.Igual("Camara 3: fijo de 7 + 'BOGOTÁ D.C.' → 601 (T3)", "6012345679||BOGOTÁ D.C.|BOGOTÁ D.C.|1", await Fila5(3));
                h.Igual("Camara 3: relleno anulado, fijo sin prefijo anulado → EstadoEnCamara = 0 (T4)", "||||0", await Fila5(4));
                h.Igual("Camara 3: 'NO TIENE' anulado, el celular da el flag (T5)", "3001234568||||1", await Fila5(5));
                h.Igual("Camara 3: email en minúsculas (T6)", "|juan.camara@gmail.com|||1", await Fila5(6));
                h.Igual("Camara 3: email vetado anulado (T7)", "3001234569||||1", await Fila5(7));
                h.Igual("Camara 3: teléfono que no es celular ni fijo anulado (T8)", "|raro@gmail.com|||1", await Fila5(8));
                h.Igual("Camara 3: repetido conserva la fila más completa (T9)", "3001234560|repetido@gmail.com|CALI|VALLE DEL CAUCA|1",
                    await Fila5(9));
                h.Igual("Camara 3: municipio '.' anulado (T10)", "3001234562|||CESAR|1", await Fila5(10));
                h.Igual("Camara 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
