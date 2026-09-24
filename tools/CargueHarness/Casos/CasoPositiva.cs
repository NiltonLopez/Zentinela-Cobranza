using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// Positiva (grupo salud, Bloque(0): 999000001000..099): el fijo que llega en la columna de
/// celular pasa al fijo sin concatenar (paso 4 del SP), columnas intercambiadas, fijo basura
/// reemplazado, correo vetado por igualdad exacta y flag que solo mira celular/fijo/email.
/// </summary>
internal static class CasoPositiva
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Positiva;
        var b = h.Grupo.Bloque(0);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Numero Documento", "Nombre", "CarteraID", "LiderID", "Numero Telefono Celular", "Numero Telefono Fijo",
            "Email", "Fecha Nacimiento", "Departamento", "Municipio",
        ];
        string[] Fila(object doc, string nombre, string cel = "", string fijo = "", string email = "", string fecha = "",
                      string depto = "", string municipio = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, cel, fijo, email, fecha, depto, municipio];

        var filas = new List<string[]>
        {
            Fila(T(1), "POSITIVA COMPLETO", "3001234567", "6051234567", "positiva1@gmail.com", "1980-05-01", "ATLANTICO",
                 "BARRANQUILLA", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "FIJO EN CELULAR", "6012345678"),
            Fila(T(3), "DOS FIJOS", "6012345678", "6049876543"),
            Fila(T(4), "INTERCAMBIADOS", "6012345670", "3001234560"),
            Fila(T(5), "FIJO BASURA", "6012345671", "SIN DATO"),
            Fila(T(6), "SOLO CORREO VETADO", email: "no@gmail.com"),
            Fila(T(7), "CELULAR CERO", "0", depto: "CESAR", fecha: "1990-01-01"),
            Fila("ABC", "DOCUMENTO MALO", "3001234569"),
            Fila(T(8), "REPETIDO", "3001234561"),
            Fila(T(8), "REPETIDO", "3001234561", email: "repetido.positiva@gmail.com"),
        };
        var archivo = h.EscribirXlsx("positiva.xlsx", encabezado, filas);

        Task<string?> Tels(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefonoCelular, '|', NumeroTelefonoFijo, '|', Email, '|', CAST(EstadoEnPositiva AS int))
              FROM dbo.Positiva WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Positiva", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8,
                                 TitularesNuevos: 8, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("Positiva 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Positiva 1: aviso fijo en celular descartado (sin concatenar)", 1,
                    Harness.Aviso(p, "fijo en la columna de celular descartado"));
                h.Igual("Positiva 1: aviso fijo basura reemplazado", 1, Harness.Aviso(p, "valor inválido reemplazado"));
                h.Igual("Positiva 1: aviso correo en lista negra", 1, Harness.Aviso(p, "email en lista negra"));
                h.Igual("Positiva 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Positiva 3: T1 completo", "3001234567|6051234567|positiva1@gmail.com|1980-05-01|ATLANTICO|BARRANQUILLA|1",
                    await h.Texto("""
                        SELECT CONCAT(NumeroTelefonoCelular,'|',NumeroTelefonoFijo,'|',Email,'|',CONVERT(varchar(10), FechaNacimiento, 23),
                                      '|',Departamento,'|',Municipio,'|',CAST(EstadoEnPositiva AS int))
                          FROM dbo.Positiva WHERE TitularID = @t
                        """, ("@t", T(1))));
                h.Igual("Positiva 3: fijo en celular → fijo (T2)", "|6012345678||1", await Tels(2));
                h.Igual("Positiva 3: dos fijos → gana el de su columna, sin concatenar (T3)", "|6049876543||1", await Tels(3));
                h.Igual("Positiva 3: columnas intercambiadas → cada uno a la suya (T4)", "3001234560|6012345670||1", await Tels(4));
                h.Igual("Positiva 3: fijo basura reemplazado (T5)", "|6012345671||1", await Tels(5));
                h.Igual("Positiva 3: correo vetado anulado → flag 0, no '' (T6)", "|||0", await Tels(6));
                h.Igual("Positiva 3: celular '0' → NULL; depto y fecha no cuentan para el flag (T7)", "|||0", await Tels(7));
                h.Igual("Positiva 3: repetido conserva la fila más completa (T8)", "3001234561||repetido.positiva@gmail.com|1", await Tels(8));
                h.Igual("Positiva 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
                h.Igual("Positiva 3: ningún fijo concatenado en el rango", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Positiva WHERE TitularID BETWEEN @d AND @h AND NumeroTelefonoFijo LIKE '%,%'",
                        ("@d", b), ("@h", b + 99)));
            });
    }
}
