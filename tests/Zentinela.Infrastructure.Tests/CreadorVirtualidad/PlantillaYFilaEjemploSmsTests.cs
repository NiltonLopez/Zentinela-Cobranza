using ClosedXML.Excel;
using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using Ej = Zentinela.Application.CatalogoCreadorVirtualidad.FilaEjemplo;
using M = Zentinela.Application.CatalogoCreadorVirtualidad.Motivos;

namespace Zentinela.Infrastructure.Tests.CreadorVirtualidad;

/// <summary>
/// Plantilla del Creador de Virtualidad (SMS) y la fila de ejemplo: la plantilla ya no
/// trae datos en "BASE", y la depuración excluye la fila de ejemplo de las plantillas
/// viejas con su propio motivo, sin romper la regla de un motivo por fila.
/// </summary>
public class PlantillaYFilaEjemploSmsTests
{
    private static readonly string[] Encabezado = CatalogoCreadorVirtualidad.ColumnasPlantilla;

    private static string[] Fila(string cedula, string numero, string nombre = "PEDRO PABLO RUIZ", string perfil = "No contacto") =>
        [cedula, nombre, "2026-09-10", perfil, numero, "Cartera X", "Empresa Y"];

    // ── Plantilla ───────────────────────────────────────────────────────────

    [Fact]
    public void Plantilla_BASE_es_la_primera_hoja_y_solo_trae_encabezados()
    {
        using var wb = new XLWorkbook(new MemoryStream(CreadorVirtualidadSmsService.ConstruirPlantilla()));

        var baseWs = wb.Worksheets.First();
        Assert.Equal(CatalogoCreadorVirtualidad.HojaBase, baseWs.Name);
        Assert.Equal(1, baseWs.LastRowUsed()!.RowNumber());
        Assert.Equal(Encabezado, baseWs.Row(1).Cells(1, Encabezado.Length).Select(c => c.GetString()).ToArray());

        // Mismos estilos que antes: formato texto en Cédula y Numero Marcado, comentario en las opcionales.
        Assert.Equal("@", baseWs.Column(1).Style.NumberFormat.Format);
        Assert.Equal("@", baseWs.Column(5).Style.NumberFormat.Format);
        Assert.Equal("@", baseWs.Cell(5000, 1).Style.NumberFormat.Format);
        Assert.True(baseWs.Cell(1, 6).HasComment);
        Assert.True(baseWs.Cell(1, 7).HasComment);
        Assert.True(baseWs.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public void Plantilla_trae_hoja_de_ejemplo_con_aviso_y_la_fila_del_catalogo()
    {
        using var wb = new XLWorkbook(new MemoryStream(CreadorVirtualidadSmsService.ConstruirPlantilla()));

        Assert.True(wb.TryGetWorksheet(CatalogoCreadorVirtualidad.HojaEjemplo, out var ej));
        Assert.Contains("NO SE PROCESA", ej!.Cell(1, 1).GetString());
        Assert.Equal(Encabezado, ej.Row(3).Cells(1, Encabezado.Length).Select(c => c.GetString()).ToArray());
        Assert.Equal(Ej.Valores, ej.Row(4).Cells(1, Ej.Valores.Length).Select(c => c.GetString()).ToArray());
    }

    [Fact]
    public void El_lector_lee_BASE_y_nunca_la_hoja_de_ejemplo()
    {
        var bytes = CreadorVirtualidadSmsService.ConstruirPlantilla();

        var filas = ArchivoTabular.ExtraerFilasXlsx(new MemoryStream(bytes));
        Assert.Single(filas);
        Assert.Equal(Encabezado, filas[0].Take(Encabezado.Length).ToArray());

        var (d, candidatas) = CreadorVirtualidadSmsService.DepurarLocal(filas);
        Assert.False(d.ArchivoRechazado);
        Assert.Equal(0, d.TotalLeidas);
        Assert.Empty(candidatas);

        // Con un titular escrito en BASE se lee ese y solo ese: el ejemplo sigue fuera.
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(CatalogoCreadorVirtualidad.HojaBase);
        var real = Fila("80123456", "3109876543");
        for (var i = 0; i < real.Length; i++) ws.Cell(2, i + 1).Value = real[i];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var llenas = ArchivoTabular.ExtraerFilasXlsx(new MemoryStream(ms.ToArray()));
        Assert.Equal(2, llenas.Count);
        Assert.DoesNotContain(llenas, f => f.Contains(Ej.Cedula));
    }

    // ── Fila de ejemplo en plantillas viejas ────────────────────────────────

    [Fact]
    public void Plantilla_vieja_con_la_fila_de_ejemplo_en_BASE_se_excluye_con_motivo_propio()
    {
        // Así era la plantilla hasta el 2026-09-28: el ejemplo en la fila 2 de BASE.
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("BASE");
        for (var i = 0; i < Encabezado.Length; i++) ws.Cell(1, i + 1).Value = Encabezado[i];
        for (var i = 0; i < Ej.Valores.Length; i++) ws.Cell(2, i + 1).Value = Ej.Valores[i];
        var real = Fila("80123456", "3109876543");
        for (var i = 0; i < real.Length; i++) ws.Cell(3, i + 1).Value = real[i];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var (d, candidatas) = CreadorVirtualidadSmsService.DepurarLocal(ArchivoTabular.ExtraerFilasXlsx(new MemoryStream(ms.ToArray())));

        Assert.Equal(2, d.TotalLeidas);
        Assert.Equal(1, d.ExcluidosFilaEjemplo);
        var c = Assert.Single(candidatas);
        Assert.Equal(80123456, c.Fila.Cedula);

        var error = Assert.Single(d.Errores);
        Assert.Equal(M.FilaEjemplo, error.Motivo);
        Assert.Equal(2, error.Linea);
        Assert.Equal("Fila de ejemplo de la plantilla: bórrala antes de subir el archivo.", error.Mensaje);

        var resp = d.AResponse();
        Assert.Equal(1, resp.ExcluidosFilaEjemplo);
        var grupo = Assert.Single(resp.ErroresAgrupados);
        Assert.Equal(M.FilaEjemplo, grupo.Motivo);
        Assert.Single(grupo.Ejemplos);
    }

    [Theory]
    [InlineData("1020304050", "3001234567")]
    [InlineData("1020304050.0", "3001234567.0")]   // Excel la guardó como número
    [InlineData(" 1.020.304.050 ", "+57 300 123 4567")]
    public void La_fila_de_ejemplo_se_reconoce_tras_normalizar(string cedula, string numero)
    {
        var (d, candidatas) = CreadorVirtualidadSmsService.DepurarLocal([Encabezado, Fila(cedula, numero)]);

        Assert.Equal(1, d.ExcluidosFilaEjemplo);
        Assert.Empty(candidatas);
    }

    [Theory]
    [InlineData("1020304050", "3109876543")]   // solo la cédula coincide
    [InlineData("80123456", "3001234567")]     // solo el número coincide
    public void Una_fila_con_solo_uno_de_los_dos_valores_no_se_excluye(string cedula, string numero)
    {
        var (d, candidatas) = CreadorVirtualidadSmsService.DepurarLocal([Encabezado, Fila(cedula, numero)]);

        Assert.Equal(0, d.ExcluidosFilaEjemplo);
        Assert.Empty(d.Errores);
        Assert.Single(candidatas);
    }

    [Fact]
    public void Los_conteos_cuadran_con_un_solo_motivo_por_fila()
    {
        List<string[]> filas =
        [
            Encabezado,
            Ej.Valores,                                     // fila de ejemplo
            Ej.Valores,                                     // repetida: sigue siendo "fila de ejemplo", no duplicado
            Fila("80123456", "3109876543"),                 // válida
            Fila("12", "3109876544"),                       // cédula inválida
            Fila("80123457", "12345"),                      // teléfono inválido
            Fila("80123458", "6015551234"),                 // fijo
            Fila("80123459", "3109876545", nombre: ""),     // nombre vacío
            Fila("80123460", "3109876546", perfil: "PQR"),  // perfil del archivo
            ["", "", "", "", "", "", ""],                   // en blanco: no cuenta
        ];

        var (d, candidatas) = CreadorVirtualidadSmsService.DepurarLocal(filas);

        Assert.Equal(8, d.TotalLeidas);
        Assert.Equal(2, d.ExcluidosFilaEjemplo);
        Assert.Equal(1, d.ExcluidosCedulaInvalida);
        Assert.Equal(1, d.ExcluidosTelefonoInvalido);
        Assert.Equal(1, d.ExcluidosFijo);
        Assert.Equal(1, d.ExcluidosNombreVacio);
        Assert.Equal(1, d.ExcluidosPerfilArchivo);
        Assert.Single(candidatas);

        // leídas − (las que siguen) = suma de exclusiones, y una exclusión registrada por fila.
        Assert.Equal(d.TotalLeidas - candidatas.Count, d.TotalExcluidos);
        Assert.Equal(d.TotalExcluidos, d.Errores.Count(e => e.Linea > 0));
        Assert.Equal(d.Errores.Count, d.Errores.Select(e => e.Linea).Distinct().Count());
    }
}
