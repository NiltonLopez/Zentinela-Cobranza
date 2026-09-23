using System.Text;
using ClosedXML.Excel;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class ArchivoTabularTests
{
    // ── CSV ─────────────────────────────────────────────────────────────────

    private static async Task<List<string[]>> LeerCsv(Stream stream)
    {
        var filas = new List<string[]>();
        await foreach (var fila in ArchivoTabular.LeerCsvPorFilasAsync(stream))
            filas.Add(fila);
        return filas;
    }

    private static Task<List<string[]>> LeerCsv(byte[] bytes) => LeerCsv(new MemoryStream(bytes));

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static byte[] Utf8ConBom(string s) => [0xEF, 0xBB, 0xBF, .. Utf8(s)];

    [Fact]
    public async Task Csv_separador_dentro_de_comillas()
    {
        var filas = await LeerCsv(Utf8("a;b;c\n1;\"x;y\";3"));

        Assert.Equal(2, filas.Count);
        Assert.Equal(["a", "b", "c"], filas[0]);
        Assert.Equal(["1", "x;y", "3"], filas[1]);
    }

    [Fact]
    public async Task Csv_comilla_escapada()
    {
        var filas = await LeerCsv(Utf8("col\n\"a\"\"b\""));

        Assert.Equal(["a\"b"], filas[1]);
    }

    [Fact]
    public async Task Csv_salto_de_linea_dentro_de_comillas()
    {
        var filas = await LeerCsv(Utf8("nombre,nota\r\n\"JUAN\",\"linea1\r\nlinea2\"\r\n\"ANA\",x\r\n"));

        Assert.Equal(3, filas.Count);
        Assert.Equal(["JUAN", "linea1\r\nlinea2"], filas[1]);
        Assert.Equal(["ANA", "x"], filas[2]);
    }

    [Fact]
    public async Task Csv_detecta_coma_cuando_predomina()
    {
        // El ';' dentro de comillas no cuenta.
        var filas = await LeerCsv(Utf8("a,b,\"c;d;e\"\n1,2,3"));

        Assert.Equal(["a", "b", "c;d;e"], filas[0]);
        Assert.Equal(["1", "2", "3"], filas[1]);
    }

    [Fact]
    public async Task Csv_crlf_y_campos_vacios()
    {
        var filas = await LeerCsv(Utf8("a;b;c\r\n1;;3\r\n;;\r\n"));

        Assert.Equal(3, filas.Count);
        Assert.Equal(["1", "", "3"], filas[1]);
        Assert.Equal(["", "", ""], filas[2]);
    }

    [Fact]
    public async Task Csv_lineas_vacias_no_producen_fila()
    {
        var filas = await LeerCsv(Utf8("a;b\n\n1;2\n\n"));

        Assert.Equal(2, filas.Count);
        Assert.Equal(["1", "2"], filas[1]);
    }

    [Fact]
    public async Task Csv_utf8_sin_bom()
    {
        var filas = await LeerCsv(Utf8("nombre\nPIÑA"));
        Assert.Equal("PIÑA", filas[1][0]);
    }

    [Fact]
    public async Task Csv_utf8_con_bom_no_deja_el_bom_en_el_encabezado()
    {
        var filas = await LeerCsv(Utf8ConBom("nombre;x\nPIÑA;1"));

        Assert.Equal("nombre", filas[0][0]);
        Assert.Equal("PIÑA", filas[1][0]);
    }

    [Fact]
    public async Task Csv_latin1_se_detecta()
    {
        // "nombre\nPIÑA" en ANSI/Latin-1: la Ñ es el byte suelto D1, inválido en UTF-8.
        byte[] bytes = [.. Encoding.ASCII.GetBytes("nombre\n"), 0x50, 0x49, 0xD1, 0x41];

        var filas = await LeerCsv(bytes);

        Assert.Equal("PIÑA", filas[1][0]);
    }

    [Fact]
    public async Task Csv_stream_no_seekable()
    {
        var filas = await LeerCsv(new StreamSinSeek(Utf8("a;b\n1;2")));

        Assert.Equal(["1", "2"], filas[1]);
    }

    [Fact]
    public async Task Csv_utf8_partido_en_el_limite_de_la_muestra_sigue_siendo_utf8()
    {
        // La Ñ (C3 91) queda partida justo en el byte 64 KB: no debe caer a Latin-1.
        var relleno = new string('x', 64 * 1024 - 1);
        var filas = await LeerCsv(Utf8(relleno + "Ñ\nPIÑA"));

        Assert.Equal(relleno + "Ñ", filas[0][0]);
        Assert.Equal("PIÑA", filas[1][0]);
    }

    [Fact]
    public async Task Csv_vacio_no_produce_filas()
    {
        Assert.Empty(await LeerCsv([]));
    }

    // ── XLSX ────────────────────────────────────────────────────────────────

    private static MemoryStream CrearXlsx(Action<XLWorkbook> llenar)
    {
        using var wb = new XLWorkbook();
        llenar(wb);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream XlsxDeTresFilas() => CrearXlsx(wb =>
    {
        var hoja = wb.AddWorksheet("Datos");
        hoja.Cell(1, 1).Value = "Nombre";
        hoja.Cell(1, 2).Value = "Telefono";
        hoja.Cell(2, 1).Value = "ANA";
        hoja.Cell(2, 2).Value = 3001234567;
        hoja.Cell(3, 1).Value = "PIÑA";
        hoja.Cell(3, 2).Value = 6041234567;
    });

    [Fact]
    public void Xlsx_lee_tres_filas()
    {
        using var stream = XlsxDeTresFilas();

        var filas = ArchivoTabular.LeerXlsxPorFilas(stream).ToList();

        Assert.Equal(3, filas.Count);
        Assert.Equal(["Nombre", "Telefono"], filas[0]);
        Assert.Equal(["ANA", "3001234567"], filas[1]);   // número sin notación científica
        Assert.Equal(["PIÑA", "6041234567"], filas[2]);
    }

    [Fact]
    public void Xlsx_cuenta_tres_filas()
    {
        using var stream = XlsxDeTresFilas();
        Assert.Equal(3, ArchivoTabular.ContarFilasXlsx(stream));
    }

    [Fact]
    public void Xlsx_prefiere_la_hoja_BASE()
    {
        using var stream = CrearXlsx(wb =>
        {
            wb.AddWorksheet("Resumen").Cell(1, 1).Value = "no";
            var basee = wb.AddWorksheet(" base ");
            basee.Cell(1, 1).Value = "si";
            basee.Cell(2, 1).Value = "dato";
        });

        var filas = ArchivoTabular.LeerXlsxPorFilas(stream).ToList();

        Assert.Equal(2, filas.Count);
        Assert.Equal("si", filas[0][0]);
    }

    [Fact]
    public void Xlsx_celda_vacia_mantiene_alineacion_de_columnas()
    {
        using var stream = CrearXlsx(wb =>
        {
            var hoja = wb.AddWorksheet("Datos");
            hoja.Cell(1, 1).Value = "A";
            hoja.Cell(1, 2).Value = "B";
            hoja.Cell(1, 3).Value = "C";
            hoja.Cell(2, 3).Value = "solo C";
        });

        var filas = ArchivoTabular.LeerXlsxPorFilas(stream).ToList();

        Assert.Equal(["", "", "solo C"], filas[1]);
    }

    /// <summary>Envoltorio que simula el cuerpo de una petición HTTP (no admite Seek).</summary>
    private sealed class StreamSinSeek(byte[] datos) : Stream
    {
        private readonly MemoryStream _interno = new(datos);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _interno.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
