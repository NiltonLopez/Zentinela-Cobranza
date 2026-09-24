using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// Nueva EPS (sub-rango 999000000300..399): el destino guarda TitularID como varchar(30).
/// Un documento con ceros a la izquierda ('000999000000301') se guarda '999000000301' en
/// NuevaEps y crea el Titular 999000000301; la segunda carga debe casar el texto (0 insertadas).
/// </summary>
internal static class CasoNuevaEps
{
    private const long Base = 999000000300;
    private static long T(int n) => Base + n;

    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.NuevaEps;

        string[] encabezado =
        [
            "Identificacion", "Nombre", "Telefono", "Estado Afiliacion", "Tipo Afiliado", "Fecha Nacimiento",
            "Departamento", "Municipio",
        ];
        string[] Fila(string doc, string nombre, string tel = "", string estado = "", string tipo = "", string fecha = "",
                      string depto = "", string municipio = "") =>
            [doc, nombre, tel, estado, tipo, fecha, depto, municipio];

        var filas = new List<string[]>
        {
            Fila("000" + T(1), "CEROS A LA IZQUIERDA", "3001234567", "ACTIVO", "COTIZANTE", "1975-03-02", "ANTIOQUIA", "MEDELLIN"),
            Fila(T(2).ToString(), "CELULAR CON 57", "573001112233", "ACTIVO"),
            Fila(T(3).ToString(), "FIJO SIN PREFIJO", "2345678", "RETIRADO"),
            Fila(T(4).ToString(), "SOLO LOCALIZACION", fecha: "1980-01-01", depto: "CESAR", municipio: "VALLEDUPAR"),
            Fila(T(5).ToString(), "REPETIDO", "3005556677"),
            Fila(T(5).ToString(), "REPETIDO", "3005556677", "ACTIVO", "BENEFICIARIO"),
            Fila("X12", "DOCUMENTO MALO", "3001230000"),
        };
        var archivo = h.EscribirXlsx("nuevaeps.xlsx", encabezado, filas);

        Task<string?> Fila3(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefono,'|',EstadoAfiliacion,'|',TipoAfiliado,'|',CAST(EstadoEnNuevaEps AS int))
              FROM dbo.NuevaEps WHERE TitularID = @t
            """, ("@t", T(n).ToString()));

        await h.FlujoEstandar("NuevaEps", def, archivo,
            new Harness.Esperado(Leidas: 7, Excluidas: 1, Duplicadas: 1, Candidatas: 5, Insertadas: 5, TitularesNuevos: 5),
            Base + 1, Base + 99,
            trasValidar: p =>
            {
                h.Igual("NuevaEps 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("NuevaEps 1: aviso celular inválido (7 dígitos)", 1, Harness.Aviso(p, "NumeroTelefono: celular inválido"));
                h.Check("NuevaEps 1: la muestra muestra el documento sin ceros",
                    p.Muestra.Any(f => f[0] == T(1).ToString()), string.Join(",", p.Muestra.Select(f => f[0])));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("NuevaEps 3: '000…301' guardado como '999000000301' (varchar sin ceros)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.NuevaEps WHERE TitularID = @t", ("@t", T(1).ToString())));
                h.Igual("NuevaEps 3: ningún TitularID del rango con ceros a la izquierda", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.NuevaEps WHERE TitularID LIKE '0%999000000%'"));
                h.Igual("NuevaEps 3: Titular 999000000301 creado (bigint)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(1))));
                h.Igual("NuevaEps 3: T1 completo", "3001234567|ACTIVO|COTIZANTE|1", await Fila3(1));
                h.Igual("NuevaEps 3: celular con 57 normalizado (T2)", "3001112233|ACTIVO||1", await Fila3(2));
                h.Igual("NuevaEps 3: número de 7 dígitos anulado, estado cuenta para el flag (T3)", "|RETIRADO||1", await Fila3(3));
                h.Igual("NuevaEps 3: solo localización y fecha → EstadoEnNuevaEps = 0 (T4)", "|||0", await Fila3(4));
                h.Igual("NuevaEps 3: repetido conserva la fila más completa (T5)", "3005556677|ACTIVO|BENEFICIARIO|1", await Fila3(5));
            });
    }
}
