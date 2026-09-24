using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Persistence;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace CargueHarness;

/// <summary>
/// SaviaSalud (grupo salud, Bloque(4): 999000001400..499): celdas con varios números separados
/// por coma que se reparten sin concatenar, fijo en NumeroCelular que pasa a NumeroTelefono, texto
/// en el teléfono anulado, y el flag "Estado" que no cuenta el Email (el destino no lo guarda).
/// </summary>
internal static class CasoSaviaSalud
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.SaviaSalud;
        var b = h.Grupo.Bloque(4);
        long T(int n) => b + n;

        ResolucionCarteraLider.IndicesResolucion indices;
        await using (var db = new CallCenterDbContext(h.DbOpciones))
            indices = await ResolucionCarteraLider.CargarIndicesResolucionAsync(db);
        var par = indices.ParesActivos.OrderBy(p => p.LiderID).ThenBy(p => p.CarteraID).First();

        string[] encabezado =
        [
            "Documento", "Nombre", "CarteraID", "LiderID", "Fecha de Nacimiento", "Tipo Afiliado", "Estado de Afiliacion",
            "Regimen", "Numero Telefono", "Numero Celular", "Email",
        ];
        string[] Fila(object doc, string nombre, string tel = "", string cel = "", string email = "", string fecha = "",
                      string tipo = "", string estado = "", string regimen = "", string cartera = "", string lider = "") =>
            [doc.ToString()!, nombre, cartera, lider, fecha, tipo, estado, regimen, tel, cel, email];

        var filas = new List<string[]>
        {
            Fila(T(1), "SAVIA COMPLETO", "6045551234", "3001234567", "savia1@gmail.com", "1970-07-07", "COTIZANTE", "ACTIVO",
                 "CONTRIBUTIVO", par.CarteraID.ToString(), par.LiderID.ToString()),
            Fila(T(2), "DOS CELULARES", cel: "3147630194,3148795401"),
            Fila(T(3), "TRES NUMEROS", "6045551235", "3147630195,3148795402"),
            Fila(T(4), "SOLO CORREO", email: "savia4@gmail.com", regimen: "SUBSIDIADO"),
            Fila(T(5), "TEXTO EN TELEFONO", "COPACABANA"),
            Fila(T(6), "FIJO EN CELULAR", cel: "6045551236"),
            Fila("ABC", "DOCUMENTO MALO", cel: "3001234569"),
            Fila(T(8), "REPETIDO", cel: "3001234561"),
            Fila(T(8), "REPETIDO", "3001234562", "3001234561"),
        };
        var archivo = h.EscribirXlsx("saviasalud.xlsx", encabezado, filas);

        Task<string?> Tels(int n) => h.Texto("""
            SELECT CONCAT(NumeroCelular, '|', NumeroTelefono, '|', CAST(Estado AS int))
              FROM dbo.SaviaSalud WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("SaviaSalud", def, archivo,
            new Harness.Esperado(Leidas: 9, Excluidas: 1, Duplicadas: 1, Candidatas: 7, Insertadas: 7,
                                 TitularesNuevos: 7, Relaciones: 1),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("SaviaSalud 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("SaviaSalud 1: aviso número adicional descartado (T3)", 1, Harness.Aviso(p, "Teléfono adicional de la lista descartado"));
                h.Igual("SaviaSalud 1: aviso texto en el teléfono", 1, Harness.Aviso(p, "NumeroTelefono: teléfono inválido anulado"));
                h.Igual("SaviaSalud 1: aviso titular repetido", 1, Harness.Aviso(p, "repetido"));
                h.Check("SaviaSalud 1: Email se informa como columna que no se usa",
                    p.Info.Any(i => i.Mensaje == "Columna del archivo que no se usa: Email"),
                    string.Join(" / ", p.Info.Select(i => i.Mensaje)));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("SaviaSalud 3: T1 completo", "1970-07-07|COTIZANTE|ACTIVO|CONTRIBUTIVO|6045551234|3001234567|1",
                    await h.Texto("""
                        SELECT CONCAT(CONVERT(varchar(10), FechaDeNacimiento, 23),'|',TipoAfiliado,'|',EstadoDeAfiliacion,'|',Regimen,'|',
                                      NumeroTelefono,'|',NumeroCelular,'|',CAST(Estado AS int))
                          FROM dbo.SaviaSalud WHERE TitularID = @t
                        """, ("@t", T(1))));
                h.Igual("SaviaSalud 3: dos celulares en una celda → uno en cada columna (T2)", "3147630194|3148795401|1", await Tels(2));
                h.Igual("SaviaSalud 3: con teléfono propio, el segundo celular se descarta (T3)", "3147630195|6045551235|1", await Tels(3));
                h.Igual("SaviaSalud 3: solo correo → Estado 0, el correo no se guarda (T4)", "||0", await Tels(4));
                h.Igual("SaviaSalud 3: texto en el teléfono anulado (T5)", "||0", await Tels(5));
                h.Igual("SaviaSalud 3: fijo en el celular → teléfono (T6)", "|6045551236|1", await Tels(6));
                h.Igual("SaviaSalud 3: repetido conserva la fila más completa (T8)", "3001234561|3001234562|1", await Tels(8));
                h.Igual("SaviaSalud 3: ningún número concatenado en el rango", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.SaviaSalud WHERE TitularID BETWEEN @d AND @h AND NumeroCelular LIKE '%,%'",
                        ("@d", b), ("@h", b + 99)));
                h.Igual("SaviaSalud 3: relación titular-cartera (T1)", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", par.LiderID), ("@c", par.CarteraID)));
            });
    }
}
