using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

namespace CargueHarness;

/// <summary>
/// RGM (grupo otras, Bloque(1)): la relación usa el líder DEL ARCHIVO (el SP insertaba
/// LiderID = CarteraID); placas validadas y normalizadas como en SIMIT ('[object Object]' anulada,
/// ' hgk-96' → 'HGK96'); Modelo fuera de rango o no numérico anulado con aviso ('2021.0' de Excel
/// se lee 2021); titular repetido con dos vehículos igual de completos → el último del archivo.
/// </summary>
internal static class CasoRgm
{
    public static async Task Correr(Harness h)
    {
        var def = CatalogoCargueFuentes.Rgm;
        var b = h.Grupo.Bloque(1);
        long T(int n) => b + n;

        // Un par activo con LiderID ≠ CarteraID: con él el defecto del SP (LiderID = CarteraID) se notaría.
        var lider = Convert.ToInt32(await h.Escalar(
            "SELECT TOP 1 LiderID FROM dbo.Lider_Cartera WHERE Activo = 1 AND LiderID <> CarteraID ORDER BY LiderID, CarteraID"));
        var cartera = Convert.ToInt32(await h.Escalar(
            "SELECT TOP 1 CarteraID FROM dbo.Lider_Cartera WHERE Activo = 1 AND LiderID = @l AND LiderID <> CarteraID ORDER BY CarteraID",
            ("@l", lider)));

        string[] encabezado = ["Cedula", "Nombre", "CarteraID", "LiderID", "Marca", "Modelo", "Placa"];
        string[] Fila(string doc, string nombre, string marca = "", string modelo = "", string placa = "", bool conPar = false) =>
            [doc, nombre, conPar ? cartera.ToString() : "", conPar ? lider.ToString() : "", marca, modelo, placa];

        var filas = new List<string[]>
        {
            Fila(T(1).ToString(), "Óscar Peña", "CHEVROLET", "2022", "KNR827", conPar: true),
            Fila(T(2).ToString(), "PLACA BASURA", placa: "[object Object]"),
            Fila(T(3).ToString(), "PLACA SUCIA", "AKT", "2019", " hgk-96"),
            Fila(T(4).ToString(), "MODELO RARO", "MAZDA", "2323", "ABC123"),
            Fila(T(5).ToString(), "MODELO TEXTO", "SUZUKI", "ABC"),
            Fila(T(6).ToString(), "MODELO EXCEL", "YAMAHA", "2021.0", "LZB10H"),
            Fila(T(7).ToString(), "DOS VEHICULOS", "NISSAN", "2017", "IPR367", conPar: true),
            Fila(T(7).ToString(), "DOS VEHICULOS", "NISSAN", "2018", "IPR432", conPar: true),
            Fila(T(8).ToString(), "SOLO MODELO", modelo: "2020"),
            Fila("12AB", "DOCUMENTO MALO", "KIA"),
        };
        var archivo = h.EscribirXlsx("rgm.xlsx", encabezado, filas);

        Task<string?> Leer(int n) => h.Texto(
            "SELECT CONCAT(Marca,'|',Modelo,'|',Placa,'|',CAST(EstadoEnRGM AS int)) FROM dbo.RGM WHERE TitularID = @t",
            ("@t", T(n)));

        await h.FlujoEstandar("RGM", def, archivo,
            new Harness.Esperado(Leidas: 10, Excluidas: 1, Duplicadas: 1, Candidatas: 8, Insertadas: 8, TitularesNuevos: 8,
                                 Relaciones: 2),
            b + 1, b + 99,
            trasValidar: p =>
            {
                h.Igual("RGM 1: error documento inválido", 1, Harness.Error(p, "Documento inválido"));
                h.Igual("RGM 1: aviso placa que no es placa", 1, Harness.Aviso(p, "Placa: valor que no es una placa anulado"));
                h.Igual("RGM 1: aviso modelo implausible (2323)", 1, Harness.Aviso(p, ReglaRgm.AvisoModelo));
                h.Igual("RGM 1: aviso modelo no numérico (ABC)", 1, Harness.Aviso(p, "Modelo: valor no numérico anulado"));
                h.Igual("RGM 1: aviso titular repetido", 1, Harness.Aviso(p, "Titular repetido"));
                return Task.CompletedTask;
            },
            trasCargar: async (_, _) =>
            {
                h.Igual("RGM 3: T1 completo", "CHEVROLET|2022|KNR827|1", await Leer(1));
                h.Igual($"RGM 3: relación con el líder del archivo ({lider}, {cartera}), no LiderID = CarteraID", 1,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID = @t AND LiderID = @l AND CarteraID = @c",
                        ("@t", T(1)), ("@l", lider), ("@c", cartera)));
                h.Igual("RGM 3: ninguna relación del rango con LiderID = CarteraID", 0,
                    await h.Contar("SELECT COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID BETWEEN @d AND @h AND LiderID = CarteraID",
                        ("@d", b + 1), ("@h", b + 99)));
                h.Igual("RGM 3: '[object Object]' anulada → fila vacía, EstadoEnRGM = 0 (T2)", "|||0", await Leer(2));
                h.Igual("RGM 3: placa normalizada ' hgk-96' → 'HGK96' (T3)", "AKT|2019|HGK96|1", await Leer(3));
                h.Igual("RGM 3: modelo 2323 anulado, el resto entra (T4)", "MAZDA||ABC123|1", await Leer(4));
                h.Igual("RGM 3: modelo 'ABC' anulado (T5)", "SUZUKI|||1", await Leer(5));
                h.Igual("RGM 3: modelo '2021.0' de Excel → 2021 (T6)", "YAMAHA|2021|LZB10H|1", await Leer(6));
                h.Igual("RGM 3: repetido igual de completo → el último del archivo (T7)", "NISSAN|2018|IPR432|1", await Leer(7));
                h.Igual("RGM 3: solo modelo cuenta para el flag (T8)", "|2020||1", await Leer(8));
            });
    }
}
