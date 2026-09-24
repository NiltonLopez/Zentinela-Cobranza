using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Coosalud (grupo salud, Bloque(3): 999000001300..399). Sin regla propia: celular de 10 dígitos
/// (el SP solo miraba el largo), lista negra de correos exacta y por dominio (el SP usaba LIKE),
/// flag por teléfono o email, y la localización de origen que no cuenta para el flag. El destino
/// no tiene FK a Titular, pero el cargue crea Titular y relaciones como el SP.
/// </summary>
internal static class CasoCoosalud
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Coosalud;
        var b = h.Grupo.Bloque(3);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Identificacion", "Nombre", "CarteraID", "LiderID", "Numero Telefono", "Email", "Municipio Origen",
            "Departamento Origen", "Regimen", "Tipo Afiliado", "Estado Afiliacion", "Nivel Sisben", "Fecha Nacimiento",
        ];
        string[] Fila(object doc, string nombre, string tel = "", string email = "", string muni = "", string depto = "",
                      string regimen = "", string tipo = "", string estado = "", string sisben = "", string fecha = "",
                      string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, tel, email, muni, depto, regimen, tipo, estado, sisben, fecha];

        var filas = new List<string[]>
        {
            Fila(T(1), "COOSALUD COMPLETO", "3001234567", "coosalud1@gmail.com", "MONTERIA", "CORDOBA", "SUBSIDIADO", "CABEZA DE FAMILIA",
                 "ACTIVO", "1", "1975-03-02", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "CELULAR NUEVE", "300123456", "coosalud2@gmail.com"),
            Fila(T(3), "SOLO CORREO VETADO", email: "no@gmail.com", muni: "SINCELEJO"),
            Fila(T(4), "DOMINIO VETADO", "3001234564", "x@uniquindio.edu.co"),
            Fila(T(5), "FIJO EN CELULAR", "6041234567", depto: "SUCRE"),
            Fila(T(6), "SISBEN N", "3001234566", sisben: "N", regimen: "CONTRIBUTIVO"),
            Fila("ABC", "DOCUMENTO MALO", "3001234569"),
            Fila(T(8), "REPETIDO", "3001234561"),
            Fila(T(8), "REPETIDO", "3001234561", "repetido.coosalud@gmail.com"),
        };
        var archivo = h.EscribirXlsx("coosalud.xlsx", encabezado, filas);

        Task<string?> Resumen(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefono, '|', Email, '|', MunicipioOrigen, '|', DepartamentoOrigen, '|', CAST(EstadoEnCoosalud AS int))
              FROM dbo.Coosalud WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Coosalud", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7,
                                 TitularesNuevos: 7, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("Coosalud 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Coosalud 1: aviso celular inválido (9 dígitos)", 1, Harness.Aviso(p, "NumeroTelefono: celular inválido anulado"));
                h.Igual("Coosalud 1: aviso correo en lista negra (exacto y dominio)", 2, Harness.Aviso(p, "email en lista negra"));
                h.Igual("Coosalud 1: aviso fijo en la columna de celular", 1, Harness.Aviso(p, "NumeroTelefono: número fijo en la columna de celular"));
                h.Igual("Coosalud 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Coosalud 3: T1 completo",
                    "3001234567|coosalud1@gmail.com|MONTERIA|CORDOBA|SUBSIDIADO|CABEZA DE FAMILIA|ACTIVO|1|1975-03-02|1",
                    await h.Texto("""
                        SELECT CONCAT(NumeroTelefono,'|',Email,'|',MunicipioOrigen,'|',DepartamentoOrigen,'|',Regimen,'|',TipoAfiliado,'|',
                                      EstadoAfiliacion,'|',NivelSisben,'|',CONVERT(varchar(10), FechaNacimiento, 23),'|',CAST(EstadoEnCoosalud AS int))
                          FROM dbo.Coosalud WHERE TitularID = @t
                        """, ("@t", T(1))));
                h.Igual("Coosalud 3: celular de 9 anulado, el email cuenta (T2)", "|coosalud2@gmail.com|||1", await Resumen(2));
                h.Igual("Coosalud 3: correo vetado → NULL, municipio no cuenta (T3)", "||SINCELEJO||0", await Resumen(3));
                h.Igual("Coosalud 3: dominio vetado → NULL (T4)", "3001234564||||1", await Resumen(4));
                h.Igual("Coosalud 3: fijo en celular anulado → flag 0 (T5)", "|||SUCRE|0", await Resumen(5));
                h.Igual("Coosalud 3: NivelSisben 'N' se guarda (T6)", "N",
                    await h.Texto("SELECT NivelSisben FROM dbo.Coosalud WHERE TitularID = @t", ("@t", T(6))));
                h.Igual("Coosalud 3: repetido conserva la fila más completa (T8)", "3001234561|repetido.coosalud@gmail.com|||1",
                    await Resumen(8));
                h.Igual("Coosalud 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
