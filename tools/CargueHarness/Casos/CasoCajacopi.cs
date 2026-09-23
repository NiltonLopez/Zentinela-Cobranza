using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Cajacopi (sub-rango 999000000100..199): fijo de 7 dígitos con indicativo por departamento
/// y municipio (Bogotá con y sin tilde), sin prefijo resoluble, relleno '11111', correo
/// "NO TIENE" y flag calculado DESPUÉS de limpiar.
/// </summary>
internal static class CasoCajacopi
{
    private const long Base = 999000000100;
    private static long T(int n) => Base + n;

    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Cajacopi;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Documento", "Nombre", "CarteraID", "LiderID", "Celular", "Telefono Fijo", "Correo",
            "Departamento", "Municipio", "Fecha Nacimiento",
        ];
        string[] Fila(object doc, string nombre, string cel = "", string fijo = "", string email = "",
                      string depto = "", string municipio = "", string fecha = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, cel, fijo, email, depto, municipio, fecha];

        var filas = new List<string[]>
        {
            Fila(T(1), "CAJACOPI COMPLETO", "3001234567", "6051234567", "cajacopi1@gmail.com", "ATLANTICO", "BARRANQUILLA",
                 "1980-05-01", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "BOGOTA SIN TILDE", fijo: "2345678", depto: "BOGOTA D.C.", municipio: "BOGOTA D.C."),
            Fila(T(3), "BOGOTA CON TILDE", fijo: "2345679", depto: "Bogotá D. C.", municipio: "BOGOTÁ, D.C."),
            Fila(T(4), "SIN PREFIJO", "3001234568", "2345670", depto: "NO EXISTE", municipio: "NI ESTE"),
            Fila(T(5), "SOLO BASURA", fijo: "1111111", email: "NO TIENE", depto: "CESAR", municipio: "VALLEDUPAR"),
            Fila(T(6), "NOTIENE EN CORREO", "3001234569", email: "notiene@gmail.com"),
            Fila(T(7), "FIJO EN CELULAR", "6051234567", fecha: "1990-01-01"),
            Fila("ABC", "DOCUMENTO MALO", "3001234560"),
            Fila(T(8), "REPETIDO", "3001234561"),
            Fila(T(8), "REPETIDO", "3001234561", email: "repetido@gmail.com"),
        };
        var archivo = h.EscribirXlsx("cajacopi.xlsx", encabezado, filas);

        async Task<string?> Valor(string columna, int n) =>
            await h.Texto($"SELECT CONVERT(varchar(100), {columna}) FROM dbo.Cajacopi WHERE TitularID = @t", ("@t", T(n)));

        await h.FlujoEstandar("Cajacopi", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8,
                                 TitularesNuevos: 8, Relaciones: 1),
            Base + 1, Base + 99,
            trasValidar: p =>
            {
                h.Igual("Cajacopi 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Cajacopi 1: aviso fijo sin prefijo resoluble", 1, Harness.Aviso(p, "sin prefijo resoluble"));
                h.Igual("Cajacopi 1: aviso fijo de relleno '11111'", 1, Harness.Aviso(p, "'11111'"));
                h.Igual("Cajacopi 1: aviso correo 'NO TIENE'", 2, Harness.Aviso(p, "'NO TIENE'"));
                h.Igual("Cajacopi 1: aviso fijo en columna de celular", 1, Harness.Aviso(p, "fijo en la columna de celular"));
                h.Igual("Cajacopi 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Cajacopi 3: T1 completo", "3001234567|6051234567|cajacopi1@gmail.com|ATLANTICO|BARRANQUILLA|1980-05-01|1",
                    await h.Texto("""
                        SELECT CONCAT(NumeroTelefonoCelular,'|',NumeroTelefonoFijo,'|',Email,'|',Departamento,'|',Municipio,'|',
                                      CONVERT(varchar(10), FechaNacimiento, 23),'|',CAST(EstadoEnCajacopi AS int))
                          FROM dbo.Cajacopi WHERE TitularID = @t
                        """, ("@t", T(1))));
                h.Igual("Cajacopi 3: 'BOGOTA D.C.' sin tilde + fijo de 7 → 601", "6012345678", await Valor("NumeroTelefonoFijo", 2));
                h.Igual("Cajacopi 3: 'Bogotá D. C.' con tilde + fijo de 7 → 601", "6012345679", await Valor("NumeroTelefonoFijo", 3));
                h.Igual("Cajacopi 3: fijo sin prefijo anulado (T4)", null, await Valor("NumeroTelefonoFijo", 4));
                h.Igual("Cajacopi 3: flag DESPUÉS de limpiar: solo basura → EstadoEnCajacopi = 0 (T5)", "0|||CESAR",
                    await h.Texto("""
                        SELECT CONCAT(CAST(EstadoEnCajacopi AS int),'|',NumeroTelefonoFijo,'|',Email,'|',Departamento)
                          FROM dbo.Cajacopi WHERE TitularID = @t
                        """, ("@t", T(5))));
                h.Igual("Cajacopi 3: correo con NOTIEN anulado (T6)", null, await Valor("Email", 6));
                h.Igual("Cajacopi 3: fijo en celular anulado, FechaNacimiento cuenta para el flag (T7)", "|1",
                    await h.Texto("SELECT CONCAT(NumeroTelefonoCelular,'|',CAST(EstadoEnCajacopi AS int)) FROM dbo.Cajacopi WHERE TitularID = @t",
                        ("@t", T(7))));
                h.Igual("Cajacopi 3: repetido conserva la fila más completa (T8)", "repetido@gmail.com", await Valor("Email", 8));
                h.Igual("Cajacopi 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
