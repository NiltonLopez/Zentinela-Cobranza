using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace CargueHarness;

/// <summary>
/// Fosyga (grupo otras, Bloque(0)): Estado 'AFILIADO FALLECIDO' (también con salto de línea en la
/// celda) → TipoAfiliado 'FALLECIDO'; el flag solo mira Estado, EPS y Régimen; EPS de más de 120
/// caracteres truncada con aviso; fecha 31/12/2999 (vigente) conservada; fecha 1900 y fecha
/// ilegible anuladas; titular nuevo sin nombre excluido (en el SP tumbaba el INSERT entero).
/// </summary>
internal static class CasoFosyga
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Fosyga;
        var b = h.Grupo.Bloque(0);
        long T(int n) => b + n;

        // Encabezados como los exporta la consulta de ADRES.
        string[] encabezado =
        [
            "Numero Documento", "Nombre", "ESTADO", "ENTIDAD", "REGIMEN", "FECHA DE AFILIACIÓN EFECTIVA",
            "FECHA DE FINALIZACIÓN DE AFILIACIÓN", "TIPO DE AFILIADO", "DEPARTAMENTO", "MUNICIPIO",
        ];
        string[] Fila(string doc, string nombre, string estado = "", string eps = "", string regimen = "",
                      string afil = "", string fin = "", string tipo = "", string depto = "", string municipio = "") =>
            [doc, nombre, estado, eps, regimen, afil, fin, tipo, depto, municipio];

        var epsLarga = "ENTIDAD PROMOTORA DE SALUD " + new string('X', 110);   // 137 > 120
        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "José Núñez", "ACTIVO", "NUEVA EPS S.A.", "SUBSIDIADO", "2016-01-01", "",
                 "CABEZA DE FAMILIA", "BOLIVAR", "SAN PABLO"),
            Fila(T(2).ToString(), "FALLECIDO COTIZANTE", "AFILIADO FALLECIDO", "COOSALUD EPS S.A.", "SUBSIDIADO",
                 tipo: "COTIZANTE"),
            Fila(T(3).ToString(), "FALLECIDO CON SALTO", "AFILIADO\nFALLECIDO"),
            Fila(T(4).ToString(), "SOLO LOCALIZACION", afil: "2020-05-01", tipo: "BENEFICIARIO", depto: "CESAR",
                 municipio: "VALLEDUPAR"),
            Fila(T(5).ToString(), "EPS LARGA", "ACTIVO", epsLarga, "CONTRIBUTIVO", fin: "31/12/2999"),
            Fila(T(6).ToString(), "FECHA ILEGIBLE", "RETIRADO", afil: "no aplica"),
            Fila(T(7).ToString(), "REPETIDO", "ACTIVO"),
            Fila(T(7).ToString(), "REPETIDO", "ACTIVO", "SALUD TOTAL", "CONTRIBUTIVO"),
            Fila("ABC", "DOCUMENTO MALO", "ACTIVO"),
            Fila(T(9).ToString(), "", "ACTIVO", "SANITAS"),                                   // nuevo sin nombre
            Fila(T(10).ToString(), "FECHA 1900", "ACTIVO", afil: "1900-01-01"),
        };
        var archivo = h.EscribirXlsx("fosyga.xlsx", encabezado, filas);

        Task<string?> Leer(int n) => h.Texto("""
            SELECT CONCAT(Estado,'|',EPS,'|',Regimen,'|',CONVERT(char(10),FechaAfiliacionEfectiva,23),'|',
                          CONVERT(char(10),FechaFinalizacionAfiliacion,23),'|',TipoAfiliado,'|',Departamento,'|',
                          Municipio,'|',CAST(EstadoEnFosyga AS int))
              FROM dbo.Fosyga WHERE TitularID = @t
            """, ("@t", T(n)));

        await h.FlujoEstandar("Fosyga", def, archivo,
            new Harness.Esperado(Leidas: 11, Excluidas: 2, Duplicadas: 1, Candidatas: 8, Insertadas: 8, TitularesNuevos: 8),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("Fosyga 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("Fosyga 1: error titular nuevo sin nombre", 1, Harness.Error(p, "Titular nuevo sin nombre"));
                h.Igual("Fosyga 1: aviso EPS truncada a 120", 1, Harness.Aviso(p, "EPS: valor truncado a 120"));
                h.Igual("Fosyga 1: aviso fecha ilegible", 1, Harness.Aviso(p, "FechaAfiliacionEfectiva: fecha no reconocida"));
                h.Igual("Fosyga 1: info FALLECIDO (T2 y T3)", 2,
                    p.Info.Where(i => i.Mensaje == ReglaFosyga.InfoFallecido).Sum(i => i.Cantidad));
                h.Check("Fosyga 1: los encabezados de ADRES resuelven todas las columnas",
                    def.ColumnasDato.All(c => p.ColumnasEncontradas.Contains(c.Nombre)),
                    string.Join(",", p.ColumnasEncontradas));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("Fosyga 3: T1 completo", "ACTIVO|NUEVA EPS S.A.|SUBSIDIADO|2016-01-01||CABEZA DE FAMILIA|BOLIVAR|SAN PABLO|1",
                    await Leer(1));
                h.Igual("Fosyga 3: nombre del titular limpio (T1)", "JOSE NUÑEZ",
                    await h.Texto("SELECT Nombre FROM dbo.Titular WHERE NumeroDocumento = @t", ("@t", T(1))));
                h.Igual("Fosyga 3: AFILIADO FALLECIDO → TipoAfiliado FALLECIDO (T2)",
                    "AFILIADO FALLECIDO|COOSALUD EPS S.A.|SUBSIDIADO|||FALLECIDO|||1", await Leer(2));
                h.Igual("Fosyga 3: estado con salto de línea colapsado y FALLECIDO (T3)",
                    "AFILIADO FALLECIDO|||||FALLECIDO|||1", await Leer(3));
                h.Igual("Fosyga 3: solo localización y fechas → EstadoEnFosyga = 0 (T4)",
                    "|||2020-05-01||BENEFICIARIO|CESAR|VALLEDUPAR|0", await Leer(4));
                h.Igual("Fosyga 3: EPS de 120 y fecha 31/12/2999 conservada (T5)",
                    $"ACTIVO|{epsLarga[..120]}|CONTRIBUTIVO||2999-12-31||||1", await Leer(5));
                h.Igual("Fosyga 3: fecha ilegible anulada, la fila entra (T6)", "RETIRADO||||||||1", await Leer(6));
                h.Igual("Fosyga 3: repetido conserva la fila más completa (T7)",
                    "ACTIVO|SALUD TOTAL|CONTRIBUTIVO||||||1", await Leer(7));
                h.Igual("Fosyga 3: el titular nuevo sin nombre no entró (T9)", 0, await h.EnRango("Fosyga", T(9), T(9)));
                h.Igual("Fosyga 3: fecha 1900 = vacío de Excel → NULL (T10)", "ACTIVO||||||||1", await Leer(10));
            });
    }
}
