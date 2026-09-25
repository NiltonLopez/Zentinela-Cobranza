using ClosedXML.Excel;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Infrastructure.FileExporter;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// El Excel de las sugerencias de localización (ADR-040, etapa 2) sale con el formato que acepta el Cargue de
/// Fuentes (ADR-039): mismos encabezados que "Columnas esperadas", clave como texto, y el lector del cargue
/// (<see cref="ArchivoTabular"/> + <c>CargueFuentesMotor.ResolverColumnas</c>) lo acepta una vez lleno.
/// </summary>
public class ExcelListaLocalizacionTests
{
    private static readonly ExcelExporter Exportador = new();

    private static readonly List<FilaListaLocalizacion> Filas =
    [
        new(1234567890123, "PEREZ GOMEZ JUAN", 8, "DELMAR EXPERTOS", 6, "TOMAS IGNACIO VARGAS LEAL"),
        new(71234567, "MARÍA PEÑA", 8, "DELMAR EXPERTOS", 6, "TOMAS IGNACIO VARGAS LEAL"),
    ];

    /// <summary>Toda fuente que se puede sugerir tiene definición de cargue (si no, no habría Excel).</summary>
    public static TheoryData<string> Funcionales()
    {
        var datos = new TheoryData<string>();
        foreach (var f in GeneradorSqlSugerencias.Funcionales) datos.Add(f.Clave);
        return datos;
    }

    private static DefinicionFuenteCargue Def(string clave) =>
        CatalogoCargueFuentes.Buscar(clave) ?? throw new InvalidOperationException($"{clave} no tiene definición de cargue.");

    [Theory]
    [MemberData(nameof(Funcionales))]
    public void Cada_fuente_Funcional_tiene_definicion_de_cargue_habilitada(string clave)
    {
        var def = Def(clave);
        Assert.True(def.Habilitada);
        Assert.True(def.ClaveEsTitular);
    }

    [Theory]
    [MemberData(nameof(Funcionales))]
    public void Encabezados_son_las_columnas_esperadas_del_cargue(string clave)
    {
        var def = Def(clave);
        using var wb = new XLWorkbook(new MemoryStream(Exportador.GenerarListaLocalizacion(def, Filas)));
        var ws = Assert.Single(wb.Worksheets);
        Assert.Equal("BASE", ws.Name);

        var esperadas = ColumnasEsperadasCargue.De(def).Select(c => c.Nombre).ToList();
        var encabezados = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(esperadas, encabezados);
        Assert.Equal("TitularID", encabezados[0]);
    }

    [Fact]
    public void TitularID_va_como_texto_y_sin_filas_de_ejemplo()
    {
        var def = Def("Fosyga");
        using var wb = new XLWorkbook(new MemoryStream(Exportador.GenerarListaLocalizacion(def, Filas)));
        var ws = wb.Worksheet(1);

        Assert.Equal(Filas.Count + 1, ws.RowsUsed().Count());   // encabezado + una fila por titular
        var clave = ws.Cell(2, 1);
        Assert.Equal(XLDataType.Text, clave.DataType);
        Assert.Equal("1234567890123", clave.GetString());
        Assert.Equal("@", ws.Column(1).Style.NumberFormat.Format);

        // Columnas de dato vacías, con formato texto salvo las fechas.
        var columnas = ColumnasEsperadasCargue.De(def);
        var iEps = columnas.FindIndex(c => c.Nombre == "EPS") + 1;
        var iFecha = columnas.FindIndex(c => c.Nombre == "FechaAfiliacionEfectiva") + 1;
        Assert.True(ws.Cell(2, iEps).IsEmpty());
        Assert.Equal("@", ws.Column(iEps).Style.NumberFormat.Format);
        Assert.NotEqual("@", ws.Column(iFecha).Style.NumberFormat.Format);
    }

    [Theory]
    [MemberData(nameof(Funcionales))]
    public void El_lector_del_cargue_acepta_el_archivo_lleno(string clave)
    {
        var def = Def(clave);
        var columnas = ColumnasEsperadasCargue.De(def);
        var tipos = def.ColumnasDato.Concat(def.ColumnasArchivoExtra).GroupBy(c => c.Nombre).ToDictionary(g => g.Key, g => g.First().Tipo);

        // Lo que haría el proveedor: abrir el archivo, llenar las columnas de dato y guardarlo.
        var bytes = Exportador.GenerarListaLocalizacion(def, Filas);
        using (var wb = new XLWorkbook(new MemoryStream(bytes)))
        {
            var ws = wb.Worksheet(1);
            for (var r = 2; r <= Filas.Count + 1; r++)
                for (var c = 0; c < columnas.Count; c++)
                    if (tipos.TryGetValue(columnas[c].Nombre, out var tipo))
                        ws.Cell(r, c + 1).SetValue(Valor(tipo));
            using var lleno = new MemoryStream();
            wb.SaveAs(lleno);
            bytes = lleno.ToArray();
        }

        var filas = ArchivoTabular.LeerXlsxPorFilas(new MemoryStream(bytes)).ToList();
        var (mapa, encontradas, faltantes, _, sinUso) = CargueFuentesMotor.ResolverColumnas(def, filas[0]);

        Assert.Empty(faltantes);
        Assert.Empty(sinUso);
        Assert.Equal(columnas.Count, encontradas.Count);
        Assert.All(mapa.Datos.Where((_, i) => def.ColumnasDato[i].DesdeArchivo), i => Assert.True(i >= 0));
        Assert.All(mapa.Extra, i => Assert.True(i >= 0));

        // La clave se lee tal cual (sin notación científica) y es un documento válido para el cargue.
        Assert.Equal("1234567890123", filas[1][mapa.TitularID]);
        Assert.True(CargueFuentesMotor.TryDocumento(filas[1][mapa.TitularID], out var doc));
        Assert.Equal(1234567890123, doc);
        Assert.Equal("8", filas[1][mapa.CarteraID]);
        Assert.Equal("6", filas[1][mapa.LiderID]);
        Assert.Equal("MARÍA PEÑA", filas[2][mapa.Nombre]);

        // Un celular pegado como texto no se vuelve número.
        if (tipos.ContainsValue(TipoColumnaCargue.Celular))
        {
            var i = columnas.FindIndex(c => tipos.TryGetValue(c.Nombre, out var t) && t == TipoColumnaCargue.Celular);
            Assert.Equal("3001234567", filas[1][i]);
        }
    }

    private static string Valor(TipoColumnaCargue tipo) => tipo switch
    {
        TipoColumnaCargue.Celular or TipoColumnaCargue.Telefono => "3001234567",
        TipoColumnaCargue.Fijo    => "6041234567",
        TipoColumnaCargue.Email   => "persona@correo.com",
        TipoColumnaCargue.Entero  => "2019",
        TipoColumnaCargue.Fecha   => "2026-01-15",
        TipoColumnaCargue.Placa   => "ABC123",
        _                         => "DATO",
    };
}
