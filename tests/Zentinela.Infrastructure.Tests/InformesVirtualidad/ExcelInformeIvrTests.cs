using ClosedXML.Excel;
using Zentinela.Application.DTOs.Virtualidad.IVR;
using Zentinela.Infrastructure.FileExporter;

namespace Zentinela.Infrastructure.Tests.InformesVirtualidad;

/// <summary>
/// Excel del informe IVR que descarga el líder (Informes de Virtualidad → IVR): hoja Resumen con
/// las cifras del análisis y hoja "Detalle por cliente". Lo que se cuida es que el archivo no se
/// rompa con los datos sucios de la BD de cobranza y que los números se puedan operar en Excel.
/// </summary>
public class ExcelInformeIvrTests
{
    private static readonly ExcelExporter Exportador = new();

    private static AnalisisIvrDto Analisis() => new()
    {
        Desde = new DateOnly(2026, 8, 1), Hasta = new DateOnly(2026, 8, 31),
        TotalMarcaciones = 30, ClientesMarcados = 20, ClientesConResultado = 20, Contactados = 12,
        EscucharonMenu = 6, MarcaronOpcion1 = 3, Contactabilidad = 60m, ConversionOpcion1 = 25m,
        Embudo = [new() { Etiqueta = "Contactados", Clientes = 12, Porcentaje = 60m, Base = "de marcados" }],
        Desenlaces = [new() { Rango = 1, Desenlace = "Marcaron opción 1 (transferidos)", Clientes = 3, Porcentaje = 25m }],
        PorLider =
        [
            new()
            {
                LiderID = 21, NombreLider = "LIDER PRUEBA", TotalCarteras = 1, ClientesMarcados = 20,
                ClientesConResultado = 20, Contactados = 12, MarcaronOpcion1 = 3, Contactabilidad = 60m,
                PorCartera = [new() { CarteraID = 8, NombreCartera = "DELMAR", ClientesMarcados = 20, Contactados = 12 }],
            },
        ],
        Apreciacion = "De 20 clientes con resultado, 12 contestaron.",
    };

    private static DetalleClienteIvrDto Fila(long cedula) => new()
    {
        Cedula = cedula, Nombre = "PEREZ GOMEZ JUAN", Lider = "LIDER PRUEBA", Cartera = "DELMAR",
        Telefonos = "3001234567, 3109876543", Marcaciones = 2,
        PrimeraMarcacion = new DateOnly(2026, 8, 3), UltimaMarcacion = new DateOnly(2026, 8, 9),
        RangoDesenlace = 1, MejorDesenlace = "Marcaron opción 1 (transferidos)", UltimoResultado = "COLGAR",
        UltimoPerfil = "PROMESA", MejorPerfil = "PROMESA", FechaUltimaGestion = new DateOnly(2026, 8, 10),
        FechaPromesa = new DateOnly(2026, 8, 15), ValorPromesa = 150000m, SaldoTotal = 980000m,
        PagosPosteriores = 1, ValorPagado = 150000m, FechaUltimoPago = new DateOnly(2026, 8, 15),
    };

    private static XLWorkbook Abrir(byte[] archivo) => new(new MemoryStream(archivo));

    [Fact]
    public void Tiene_hoja_Resumen_y_hoja_Detalle_con_una_fila_por_cliente()
    {
        using var wb = Abrir(Exportador.GenerarInformeIvr(Analisis(), [Fila(71234567), Fila(1234567890123)],
            "Líder LIDER PRUEBA", new DateOnly(2026, 9, 1)));

        Assert.True(wb.TryGetWorksheet("Resumen", out _));
        Assert.True(wb.TryGetWorksheet("Detalle por cliente", out var det));
        Assert.Equal("Cédula", det.Cell(1, 1).GetString());
        Assert.Equal(3, det.LastRowUsed()!.RowNumber());          // encabezado + 2 clientes
        Assert.NotNull(det.AutoFilter);
    }

    [Fact]
    public void Cedula_y_valores_quedan_como_numeros_y_telefonos_como_texto()
    {
        using var wb = Abrir(Exportador.GenerarInformeIvr(Analisis(), [Fila(1234567890123)], "x", new DateOnly(2026, 9, 1)));
        var det = wb.Worksheet("Detalle por cliente");

        // Numéricos para que el líder filtre, sume y cruce con BUSCARV sin convertir nada.
        Assert.Equal(1234567890123d, det.Cell(2, 1).GetDouble());
        Assert.Equal("0", det.Cell(2, 1).Style.NumberFormat.Format);   // sin notación científica
        Assert.Equal(980000d, det.Cell(2, 16).GetDouble());
        Assert.Equal(new DateTime(2026, 8, 15), det.Cell(2, 14).GetDateTime());
        Assert.Equal("3001234567, 3109876543", det.Cell(2, 5).GetString());
    }

    [Fact]
    public void Una_fecha_con_anio_invalido_no_rompe_el_archivo()
    {
        // UltimoEstadoTitular trae promesas con años como 0202: ClosedXML revienta con fechas < 1900.
        var sucia = Fila(71234567);
        sucia.FechaPromesa = new DateOnly(202, 6, 30);

        using var wb = Abrir(Exportador.GenerarInformeIvr(Analisis(), [sucia], "x", new DateOnly(2026, 9, 1)));

        Assert.Contains("fecha inválida", wb.Worksheet("Detalle por cliente").Cell(2, 14).GetString());
    }

    [Fact]
    public void Campos_de_texto_vacios_quedan_como_celdas_vacias()
    {
        var f = Fila(71234567);
        f.Nombre = null; f.Telefonos = null; f.UltimoResultado = null; f.UltimoPerfil = null; f.MejorPerfil = null;
        f.FechaPromesa = null; f.ValorPromesa = null; f.FechaUltimoPago = null;

        using var wb = Abrir(Exportador.GenerarInformeIvr(Analisis(), [f], "x", new DateOnly(2026, 9, 1)));
        var det = wb.Worksheet("Detalle por cliente");

        Assert.True(det.Cell(2, 2).IsEmpty());
        Assert.True(det.Cell(2, 14).IsEmpty());
        Assert.True(det.Cell(2, 15).IsEmpty());
    }

    [Fact]
    public void Porcentajes_del_resumen_van_como_fraccion_con_formato_porcentaje()
    {
        using var wb = Abrir(Exportador.GenerarInformeIvr(Analisis(), [], "x", new DateOnly(2026, 9, 1)));
        var res = wb.Worksheet("Resumen");

        var celda = res.CellsUsed().First(c => c.GetString() == "Contactabilidad").CellRight();
        Assert.Equal(0.60d, celda.GetDouble(), 4);
        Assert.Contains("%", celda.Style.NumberFormat.Format);
    }

    [Fact]
    public void Avisa_cuando_el_periodo_supera_los_pagos_cargados()
    {
        // Pagos hasta el 28/07 y período de agosto: "0 pagos posteriores" no significa "no pagó".
        using var sinPagos = Abrir(Exportador.GenerarInformeIvr(Analisis(), [], "x", new DateOnly(2026, 7, 28)));
        Assert.Contains(sinPagos.Worksheet("Resumen").CellsUsed(), c => c.GetString().StartsWith("ATENCIÓN"));

        using var conPagos = Abrir(Exportador.GenerarInformeIvr(Analisis(), [], "x", new DateOnly(2026, 9, 1)));
        Assert.DoesNotContain(conPagos.Worksheet("Resumen").CellsUsed(), c => c.GetString().StartsWith("ATENCIÓN"));
    }

    [Fact]
    public void Volumen_de_un_mes_completo_se_genera()
    {
        // ~120k clientes en un mes con todos los líderes (medido 2026-09-28).
        var filas = Enumerable.Range(0, 120_000).Select(i => Fila(70_000_000L + i)).ToList();

        var archivo = Exportador.GenerarInformeIvr(Analisis(), filas, "Todos", new DateOnly(2026, 9, 1));

        using var wb = Abrir(archivo);
        Assert.Equal(120_001, wb.Worksheet("Detalle por cliente").LastRowUsed()!.RowNumber());
    }
}
