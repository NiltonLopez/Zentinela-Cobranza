using Zentinela.Application;

namespace CargueHarness;

/// <summary>
/// Tigo (grupo otras, Bloque(3)): sin SP legacy (no hay paridad posible), definición por analogía
/// con TigoUne. Se carga desde CSV (';', UTF-8) para cubrir esa ruta. Verifica que todo lo que entra
/// cumple el CHECK chk_Telefono de dbo.Tigo y la FK a Titular (titulares nuevos creados antes).
/// </summary>
internal static class CasoTigo
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Tigo;
        var b = h.Grupo.Bloque(3);
        long T(int n) => b + n;

        string[] encabezado = ["TitularID", "Nombre", "NumeroTelefono", "Email"];
        string[] Fila(string doc, string nombre, string tel = "", string email = "") => [doc, nombre, tel, email];

        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "Álvaro Tigo", "3217104660", "alvaro@gmail.com"),
            Fila(T(2).ToString(), "SIETE DIGITOS", "3001234", "siete@gmail.com"),    // CHECK aceptaría 7; el motor no
            Fila(T(3).ToString(), "SOLO CORREO", email: "correo@hotmail.com"),
            Fila(T(4).ToString(), "FIJO", "6041234567"),
            Fila(T(5).ToString(), "SIN DATOS"),
            Fila(T(6).ToString(), "REPETIDO", "3001110006"),
            Fila(T(6).ToString(), "REPETIDO", "3001110006", "rep@gmail.com"),
            Fila("99", "DOCUMENTO MALO", "3001110007"),
        };
        var archivo = h.EscribirCsv("tigo.csv", encabezado, filas);

        Task<string?> Leer(int n) => h.Texto(
            "SELECT CONCAT(NumeroTelefono,'|',Email,'|',CAST(EstadoEnTigo AS int)) FROM dbo.Tigo WHERE TitularID = @t",
            ("@t", T(n)));

        await h.FlujoEstandar("Tigo", def, archivo,
            new Harness.Esperado(Leidas: 8, Excluidas: 1, Duplicadas: 1, Candidatas: 6, Insertadas: 6, TitularesNuevos: 6),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("Tigo 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Tigo 1: aviso celular de 7 dígitos", 1, Harness.Aviso(p, "NumeroTelefono: celular inválido anulado"));
                h.Igual("Tigo 1: aviso fijo en la columna de celular", 1, Harness.Aviso(p, "NumeroTelefono: número fijo en la columna de celular"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Tigo 3: T1 completo", "3217104660|alvaro@gmail.com|1", await Leer(1));
                h.Igual("Tigo 3: celular de 7 anulado, el correo entra (T2)", "|siete@gmail.com|1", await Leer(2));
                h.Igual("Tigo 3: solo correo (T3)", "|correo@hotmail.com|1", await Leer(3));
                h.Igual("Tigo 3: fijo anulado → vacía, EstadoEnTigo = 0 (T4)", "||0", await Leer(4));
                h.Igual("Tigo 3: sin datos → EstadoEnTigo = 0 (T5)", "||0", await Leer(5));
                h.Igual("Tigo 3: repetido conserva la fila más completa (T6)", "3001110006|rep@gmail.com|1", await Leer(6));
                h.Igual("Tigo 3: todo teléfono cargado es celular de 10 (chk_Telefono)", 0, await h.Contar("""
                    SELECT COUNT(*) FROM dbo.Tigo
                     WHERE TitularID BETWEEN @d AND @h AND NumeroTelefono IS NOT NULL
                       AND NOT (LEN(NumeroTelefono) = 10 AND LEFT(NumeroTelefono, 1) = '3')
                    """, ("@d", b + 1), ("@h", b + 99)));
                h.Igual("Tigo 3: FK a Titular: todas las filas del rango tienen Titular", 0, await h.Contar("""
                    SELECT COUNT(*) FROM dbo.Tigo x
                     WHERE x.TitularID BETWEEN @d AND @h
                       AND NOT EXISTS (SELECT 1 FROM dbo.Titular t WHERE t.NumeroDocumento = x.TitularID)
                    """, ("@d", b + 1), ("@h", b + 99)));
            });
    }
}
