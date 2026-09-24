using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// MutualSer (sub-rango 999000000200..299): reubicación de teléfonos entre celular, celular 2
/// y fijo; el 60x con el fijo ocupado se descarta SIN concatenar; el flag ignora régimen,
/// tipo de afiliado y localización.
/// </summary>
internal static class CasoMutualSer
{
    private const long Base = 999000000200;
    private static long T(int n) => Base + n;

    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.MutualSer;

        string[] encabezado =
        [
            "Numero Documento", "Nombre", "Celular", "Celular 2", "Fijo", "Email", "Regimen", "Tipo Afiliado",
            "Departamento", "Municipio",
        ];
        string[] Fila(long doc, string nombre, string cel = "", string cel2 = "", string fijo = "", string email = "",
                      string regimen = "", string tipo = "", string depto = "", string municipio = "") =>
            [doc.ToString(), nombre, cel, cel2, fijo, email, regimen, tipo, depto, municipio];

        var filas = new List<string[]>
        {
            Fila(T(1), "MUTUAL COMPLETO", "3001234567", "3007654321", "6041234567", "mutual1@gmail.com",
                 "SUBSIDIADO", "COTIZANTE", "ANTIOQUIA", "MEDELLIN"),
            Fila(T(2), "CELULAR 60 A FIJO", "6012345678"),
            Fila(T(3), "CELULAR 60 CON FIJO OCUPADO", "6012345678", fijo: "6049876543"),
            Fila(T(4), "CELULAR 2 REPETIDO", "3001112233", "57 300 111 2233"),
            Fila(T(5), "FIJO QUE ES CELULAR", "3002223344", fijo: "3009998877"),
            Fila(T(6), "SOLO AFILIACION", regimen: "CONTRIBUTIVO", tipo: "BENEFICIARIO", depto: "CUNDINAMARCA",
                 municipio: "BOGOTA D.C."),
            Fila(T(7), "INTERCAMBIO", "6023456789", fijo: "3004445566"),
            Fila(T(8), "FIJO DE 7", "3005556677", fijo: "2345678"),
        };
        var archivo = h.EscribirXlsx("mutualser.xlsx", encabezado, filas);

        Task<string?> Telefonos(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefonoCelular,'|',NumeroTelefonoCelular2,'|',NumeroTelefonoFijo,'|',CAST(EstadoEnMutualSer AS int))
              FROM dbo.MutualSer WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("MutualSer", def, archivo,
            new Harness.Esperado(Leidas: 8, Excluidas: 0, Duplicadas: 0, Candidatas: 8, Insertadas: 8, TitularesNuevos: 8),
            Base + 1, Base + 99,
            trasValidar: p =>
            {
                h.Igual("MutualSer 1: aviso 60x descartado por fijo ocupado", 1, Harness.Aviso(p, "fijo en la columna de celular descartado"));
                h.Igual("MutualSer 1: aviso fijo de 7 inválido (genérico)", 1, Harness.Aviso(p, "fijo inválido anulado"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("MutualSer 3: T1 completo", "3001234567|3007654321|6041234567|1", await Telefonos(1));
                h.Igual("MutualSer 3: celular 60x pasa al fijo (T2)", "||6012345678|1", await Telefonos(2));
                h.Igual("MutualSer 3: 60x con fijo ocupado se descarta, fijo intacto de 10 dígitos (T3)", "||6049876543|1",
                    await Telefonos(3));
                h.Igual("MutualSer 3: celular 2 repetido anulado (T4)", "3001112233|||1", await Telefonos(4));
                h.Igual("MutualSer 3: fijo que es celular pasa a celular 2 (T5)", "3002223344|3009998877||1", await Telefonos(5));
                h.Igual("MutualSer 3: solo afiliación → EstadoEnMutualSer = 0 (T6)", "|||0", await Telefonos(6));
                h.Igual("MutualSer 3: régimen de T6 guardado", "CONTRIBUTIVO",
                    await h.Texto("SELECT Regimen FROM dbo.MutualSer WHERE TitularID = @t", ("@t", T(6))));
                h.Igual("MutualSer 3: intercambio sin perder números (T7)", "|3004445566|6023456789|1", await Telefonos(7));
                h.Igual("MutualSer 3: fijo de 7 anulado (T8)", "3005556677|||1", await Telefonos(8));
                h.Igual("MutualSer 3: ningún fijo con más de 10 caracteres", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.MutualSer WHERE TitularID BETWEEN @d AND @h AND LEN(NumeroTelefonoFijo) <> 10",
                        ("@d", Base + 1), ("@h", Base + 99)));
            });
    }
}
