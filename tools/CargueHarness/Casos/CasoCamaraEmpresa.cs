using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// CamaraEmpresa (grupo empresas, Bloque(2) = 999000002200..299, como NitEmpresa): primera
/// fuente cuya clave no es TitularID (<c>ColumnaClaveTitular = NitEmpresa</c>). No crea Titular ni
/// relaciones ni avisa "titular inexistente"; 'NIT-DV' pierde el DV; fijo de 7 dígitos con
/// indicativo por el Departamento del archivo (que no se guarda); doble carga = 0 insertadas.
/// </summary>
internal static class CasoCamaraEmpresa
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.CamaraEmpresa;
        var @base = h.Grupo.Bloque(2);
        long T(int n) => @base + n;

        string[] encabezado = ["NIT Empresa", "Nombre Empresa", "Telefono", "Correo", "Departamento"];
        string[] Fila(string nit, string empresa, string tel = "", string email = "", string depto = "") =>
            [nit, empresa, tel, email, depto];

        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "EMPRESA COMPLETA SAS", "3001234567", "empresa1@gmail.com", "ANTIOQUIA"),
            Fila($"{T(2)}-7", "NIT CON DV SAS", "6041234567"),
            Fila(T(3).ToString(), "FIJO NORTE SAS", "2345678", depto: "NORTE DE SANTANDER"),
            Fila(T(4).ToString(), "RELLENO SAS", "2345679", depto: "Seleccione un departamento"),
            Fila(T(5).ToString(), "NO TIENE SAS", "3001234568", "NO TIENE"),
            Fila(T(6).ToString(), "REPETIDA SAS", "3001234569"),
            Fila(T(6).ToString(), "REPETIDA SAS", "3001234569", "repetida@gmail.com"),
            Fila("ABC", "NIT MALO SAS", "3001234560"),
            Fila(T(7).ToString(), "SIN DATOS SAS"),
        };
        var archivo = h.EscribirXlsx("camaraempresa.xlsx", encabezado, filas);

        Task<string?> Fila3(int n) => h.Texto("""
            SELECT CONCAT(NumeroTelefono,'|',Email,'|',CAST(EstadoEnCamara AS int))
              FROM dbo.CamaraEmpresa WHERE NitEmpresa = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("CamaraEmpresa", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7, TitularesNuevos: 0),
            T(1), T(99),
            trasValidar: p =>
            {
                h.Igual("CamaraEmpresa 1: error NIT inválido (mensaje con la clave)", 1, Harness.Error(p, "NitEmpresa inválido o vacío"));
                h.Igual("CamaraEmpresa 1: sin aviso de titular inexistente", 0, Harness.Aviso(p, "inexistente"));
                h.Igual("CamaraEmpresa 1: info NIT con DV", 1,
                    p.Info.Where(i => i.Mensaje.Contains("dígito de verificación")).Sum(i => i.Cantidad));
                h.Igual("CamaraEmpresa 1: aviso fijo sin prefijo resoluble", 1, Harness.Aviso(p, "sin prefijo resoluble"));
                h.Igual("CamaraEmpresa 1: aviso relleno del formulario", 1, Harness.Aviso(p, "texto de relleno del formulario"));
                h.Igual("CamaraEmpresa 1: aviso email inválido", 1, Harness.Aviso(p, "Email: email con formato inválido"));
                h.Check("CamaraEmpresa 1: columna encontrada NitEmpresa", p.ColumnasEncontradas.Contains("NitEmpresa"),
                    string.Join(",", p.ColumnasEncontradas));
                h.Check("CamaraEmpresa 1: la muestra muestra el NIT en la primera columna",
                    p.ColumnasMuestra[0] == "NitEmpresa" && p.Muestra.Any(f => f[0] == T(1).ToString()),
                    string.Join(",", p.ColumnasMuestra));
                h.Check("CamaraEmpresa 1: 'Nombre Empresa' se informa como no usada",
                    p.Info.Any(i => i.Mensaje.Contains("no se usa: Nombre Empresa")), "");
                return Task.CompletedTask;
            },
            trasCargar: async (_, ejecucion) =>
            {
                h.Igual("CamaraEmpresa 3: T1 completo", "3001234567|empresa1@gmail.com|1", await Fila3(1));
                h.Igual("CamaraEmpresa 3: 'NIT-DV' guardado sin DV (T2)", "6041234567||1", await Fila3(2));
                h.Igual("CamaraEmpresa 3: ningún NIT del rango con el DV pegado", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.CamaraEmpresa WHERE NitEmpresa = @n", ("@n", T(2) * 10 + 7)));
                h.Igual("CamaraEmpresa 3: fijo de 7 + 'NORTE DE SANTANDER' → 607 (T3)", "6072345678||1", await Fila3(3));
                h.Igual("CamaraEmpresa 3: relleno → fijo anulado, EstadoEnCamara = 0 (T4)", "||0", await Fila3(4));
                h.Igual("CamaraEmpresa 3: 'NO TIENE' anulado (T5)", "3001234568||1", await Fila3(5));
                h.Igual("CamaraEmpresa 3: repetido conserva la fila más completa (T6)", "3001234569|repetida@gmail.com|1", await Fila3(6));
                h.Igual("CamaraEmpresa 3: sin datos → EstadoEnCamara = 0 (T7)", "||0", await Fila3(7));
                h.Igual("CamaraEmpresa 3: no se crearon titulares con esos números", 0, await h.EnRango("Titular", T(1), T(99)));
                h.Igual("CamaraEmpresa 3: reversión 'I' guarda el NIT como TitularID", 7,
                    await h.Contar("""
                        SELECT COUNT(*) FROM dbo.CargueFuenteReversion
                         WHERE EjecucionID = @e AND Accion = 'I' AND TRY_CAST(TitularID AS bigint) BETWEEN @d AND @h
                        """, ("@e", ejecucion), ("@d", T(1)), ("@h", T(99))));
            });
    }
}
