using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// Tigo Une (grupo otras, Bloque(2)): sin regla propia. Celular 3xx de 10 (el 57 se quita; un fijo
/// o un número de 10 que no empieza en 3 se anula: el SP solo miraba el largo), correo en minúsculas
/// y recortado, lista negra EXACTA ('juana@gmail.com' entra aunque contenga la entrada
/// 'a@gmail.com'; el SP la anulaba por LIKE), titular repetido → la fila más completa.
/// </summary>
internal static class CasoTigoUne
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.TigoUne;
        var b = h.Grupo.Bloque(2);
        long T(int n) => b + n;

        string[] encabezado = ["Documento", "Nombre", "Linea", "Correo"];
        string[] Fila(string doc, string nombre, string tel = "", string email = "") => [doc, nombre, tel, email];

        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "Íngrid Muñoz", "3046676738", "ingrid@gmail.com"),
            Fila(T(2).ToString(), "CON 57", "57 321 710 4660"),
            Fila(T(3).ToString(), "FIJO", "6012345678", "fijo@gmail.com"),
            Fila(T(4).ToString(), "DIEZ RAROS", "5551234567"),
            Fila(T(5).ToString(), "CORREO SUCIO", email: " Dani1018@Hotmail.com"),
            Fila(T(6).ToString(), "LIKE NO", "3001110006", "juana@gmail.com"),
            Fila(T(7).ToString(), "LISTA NEGRA", "3001110007", "no@gmail.com"),
            Fila(T(8).ToString(), "REPETIDO", "3001110008"),
            Fila(T(8).ToString(), "REPETIDO", "3001110008", "rep@gmail.com"),
            Fila("0", "DOCUMENTO MALO", "3001110009"),
        };
        var archivo = h.EscribirXlsx("tigoune.xlsx", encabezado, filas);

        Task<string?> Leer(int n) => h.Texto(
            "SELECT CONCAT(NumeroTelefono,'|',Email,'|',CAST(EstadoEnTigoUne AS int)) FROM dbo.TigoUne WHERE TitularID = @t",
            ("@t", T(n)));

        await h.FlujoEstandar("TigoUne", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8, TitularesNuevos: 8),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("TigoUne 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                // T3 (6012345678) y T4 (5551234567). El motor rotula "número fijo" a todo número de 10
                // dígitos que no es celular, aunque no empiece en 6 (TelefonoColombiano.Normalizar): el
                // dato se anula igual; solo el texto del aviso es impreciso para T4.
                h.Igual("TigoUne 1: aviso fijo en la columna de celular (T3 y T4)", 2,
                    Harness.Aviso(p, "NumeroTelefono: número fijo en la columna de celular"));
                h.Igual("TigoUne 1: aviso email en lista negra (solo el exacto)", 1, Harness.Aviso(p, "Email: email en lista negra anulado"));
                h.Check("TigoUne 1: 'Linea' y 'Correo' resuelven las dos columnas",
                    p.ColumnasEncontradas.Contains("NumeroTelefono") && p.ColumnasEncontradas.Contains("Email"),
                    string.Join(",", p.ColumnasEncontradas));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("TigoUne 3: T1 completo", "3046676738|ingrid@gmail.com|1", await Leer(1));
                h.Igual("TigoUne 3: nombre del titular limpio (T1)", "INGRID MUÑOZ",
                    await h.Texto("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(1))));
                h.Igual("TigoUne 3: el 57 se quita (T2)", "3217104660||1", await Leer(2));
                h.Igual("TigoUne 3: fijo anulado, el correo entra (T3)", "|fijo@gmail.com|1", await Leer(3));
                h.Igual("TigoUne 3: 10 dígitos que no empiezan en 3 → vacía, flag 0 (T4)", "||0", await Leer(4));
                h.Igual("TigoUne 3: correo recortado y en minúsculas (T5)", "|lucia1018@hotmail.com|1", await Leer(5));
                h.Igual("TigoUne 3: lista negra exacta, no LIKE (T6)", "3001110006|juana@gmail.com|1", await Leer(6));
                h.Igual("TigoUne 3: correo exacto de la lista negra anulado (T7)", "3001110007||1", await Leer(7));
                h.Igual("TigoUne 3: repetido conserva la fila más completa (T8)", "3001110008|rep@gmail.com|1", await Leer(8));
            });
    }
}
