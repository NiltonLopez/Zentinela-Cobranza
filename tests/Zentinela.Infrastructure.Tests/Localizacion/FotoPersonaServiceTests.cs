using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Miniaturas de fotos (ADR-040) sin BD: la transformación pura (<see cref="MiniaturaFoto"/>) con
/// imágenes generadas en memoria, y el servicio con un origen de datos falso (autorización, caché y
/// "sin foto").
/// </summary>
public class FotoPersonaServiceTests
{
    // ── Imágenes de prueba ───────────────────────────────────────────────────

    /// <summary>
    /// Imagen apaisada o vertical con tres franjas: rojo | verde | azul a lo largo del lado mayor.
    /// El cuadrado centrado cae entero en la franja verde si el lado mayor es ≥ 3× el menor.
    /// </summary>
    private static byte[] Franjas(int ancho, int alto, SKEncodedImageFormat formato, int calidad = 95)
    {
        using var bmp = new SKBitmap(ancho, alto);
        using (var canvas = new SKCanvas(bmp))
        {
            var horizontal = ancho >= alto;
            var tercio = (horizontal ? ancho : alto) / 3f;
            SKColor[] colores = [SKColors.Red, SKColors.Lime, SKColors.Blue];
            for (var i = 0; i < 3; i++)
            {
                using var paint = new SKPaint { Color = colores[i] };
                var r = horizontal
                    ? SKRect.Create(i * tercio, 0, tercio + 1, alto)
                    : SKRect.Create(0, i * tercio, ancho, tercio + 1);
                canvas.DrawRect(r, paint);
            }
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(formato, calidad);
        return data.ToArray();
    }

    private static SKBitmap Decodificar(byte[] jpeg) => SKBitmap.Decode(jpeg);

    private static void AssertEsJpeg(byte[] bytes)
    {
        Assert.True(bytes.Length > 4);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    // ── MiniaturaFoto (pura) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    [InlineData(160)]
    [InlineData(320)]
    public void Png_apaisado_sale_jpeg_cuadrado_del_tamano_pedido(int tam)
    {
        var png = Franjas(900, 300, SKEncodedImageFormat.Png);

        var jpeg = MiniaturaFoto.Generar(png, tam);

        Assert.NotNull(jpeg);
        AssertEsJpeg(jpeg!);
        using var bmp = Decodificar(jpeg!);
        Assert.Equal(tam, bmp.Width);
        Assert.Equal(tam, bmp.Height);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(320)]
    public void Jpeg_vertical_sale_jpeg_cuadrado_del_tamano_pedido(int tam)
    {
        var original = Franjas(300, 900, SKEncodedImageFormat.Jpeg);

        var jpeg = MiniaturaFoto.Generar(original, tam);

        Assert.NotNull(jpeg);
        AssertEsJpeg(jpeg!);
        using var bmp = Decodificar(jpeg!);
        Assert.Equal((tam, tam), (bmp.Width, bmp.Height));
    }

    [Fact]
    public void Una_foto_mas_chica_que_el_tamano_se_amplia()
    {
        var jpeg = MiniaturaFoto.Generar(Franjas(90, 30, SKEncodedImageFormat.Png), 320);

        using var bmp = Decodificar(jpeg!);
        Assert.Equal((320, 320), (bmp.Width, bmp.Height));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    [InlineData(4096)]
    public void Tamano_invalido_se_toma_como_160(int tam)
    {
        Assert.Equal(160, MiniaturaFoto.NormalizarTamano(tam));

        var jpeg = MiniaturaFoto.Generar(Franjas(600, 200, SKEncodedImageFormat.Png), tam);
        using var bmp = Decodificar(jpeg!);
        Assert.Equal((160, 160), (bmp.Width, bmp.Height));
    }

    [Theory]
    [InlineData(900, 300)] // apaisada: el recorte descarta las franjas roja y azul de los lados
    [InlineData(300, 900)] // vertical: descarta las de arriba y abajo
    public void El_recorte_es_el_cuadrado_centrado(int ancho, int alto)
    {
        var jpeg = MiniaturaFoto.Generar(Franjas(ancho, alto, SKEncodedImageFormat.Png), 96);
        using var bmp = Decodificar(jpeg!);

        // Esquinas y centro: todo verde (tolerancia por la compresión JPEG y el filtrado del borde).
        foreach (var (x, y) in new[] { (4, 4), (91, 4), (4, 91), (91, 91), (48, 48) })
        {
            var c = bmp.GetPixel(x, y);
            Assert.True(c.Green > 200 && c.Red < 60 && c.Blue < 60, $"Pixel ({x},{y}) = {c}: no es verde");
        }
    }

    [Fact]
    public void Png_con_transparencia_se_aplana_sobre_blanco()
    {
        using var bmp = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);

        var jpeg = MiniaturaFoto.Generar(png.ToArray(), 48);
        using var salida = Decodificar(jpeg!);
        var c = salida.GetPixel(24, 24);
        Assert.True(c.Red > 240 && c.Green > 240 && c.Blue > 240, $"Esperaba blanco, fue {c}");
    }

    [Fact]
    public void Bytes_que_no_son_imagen_devuelven_null()
    {
        Assert.Null(MiniaturaFoto.Generar([1, 2, 3, 4, 5, 6, 7, 8], 160));
        Assert.Null(MiniaturaFoto.Generar([], 160));
    }

    [Fact]
    public void ETag_es_estable_y_cambia_con_la_foto_o_el_tamano()
    {
        var subida = new DateTime(2026, 9, 25, 10, 30, 0, DateTimeKind.Unspecified);

        var a = MiniaturaFoto.CalcularETag(17, subida, 160);
        Assert.Equal(a, MiniaturaFoto.CalcularETag(17, subida, 160));
        Assert.Equal($"\"17-{subida.Ticks}-160\"", a);
        Assert.StartsWith("\"", a);
        Assert.EndsWith("\"", a);

        Assert.NotEqual(a, MiniaturaFoto.CalcularETag(17, subida.AddSeconds(1), 160)); // RRHH reemplazó la foto
        Assert.NotEqual(a, MiniaturaFoto.CalcularETag(18, subida, 160));
        Assert.NotEqual(a, MiniaturaFoto.CalcularETag(17, subida, 96));
        // Un tamaño inválido comparte ETag con 160, porque es la misma imagen.
        Assert.Equal(a, MiniaturaFoto.CalcularETag(17, subida, 123));
    }

    [Fact]
    public void Mismos_bytes_dan_la_misma_miniatura()
    {
        var png = Franjas(640, 480, SKEncodedImageFormat.Png);
        Assert.Equal(MiniaturaFoto.Generar(png, 96), MiniaturaFoto.Generar(png, 96));
    }

    // ── FotoPersonaService (con origen falso) ────────────────────────────────

    private sealed class OrigenFalso : IFotoPersonaOrigen
    {
        public HashSet<long> Habilitados { get; } = [];
        public Dictionary<long, FotoOriginal> Fotos { get; } = [];
        public int LecturasFoto { get; private set; }
        public int ConsultasHabilitado { get; private set; }

        public Task<bool> EsPersonaHabilitadaAsync(long documento, CancellationToken ct)
        {
            ConsultasHabilitado++;
            return Task.FromResult(Habilitados.Contains(documento));
        }

        public Task<FotoOriginal?> LeerFotoAsync(long documento, CancellationToken ct)
        {
            LecturasFoto++;
            return Task.FromResult(Fotos.TryGetValue(documento, out var f) ? f : null);
        }
    }

    private static (FotoPersonaService Servicio, OrigenFalso Origen, FotoPersonaCache Cache) Crear()
    {
        var origen = new OrigenFalso();
        var cache = new FotoPersonaCache();
        return (new FotoPersonaService(origen, cache, NullLogger<FotoPersonaService>.Instance), origen, cache);
    }

    private static FotoOriginal Foto(int id = 7) =>
        new(id, Franjas(600, 400, SKEncodedImageFormat.Jpeg), "image/jpeg", new DateTime(2026, 1, 2, 3, 4, 5));

    [Fact]
    public async Task Documento_no_autorizado_devuelve_null_sin_leer_RRHH()
    {
        var (servicio, origen, cache) = Crear();
        using var _ = cache;
        origen.Fotos[123456] = Foto(); // hay foto en RRHH, pero no es líder ni asesor activo

        Assert.Null(await servicio.ObtenerMiniaturaAsync(123456, 160));
        Assert.Equal(0, origen.LecturasFoto);

        // Y el "no" queda en caché: la segunda vez ni siquiera pregunta.
        Assert.Null(await servicio.ObtenerMiniaturaAsync(123456, 96));
        Assert.Equal(1, origen.ConsultasHabilitado);
    }

    [Fact]
    public async Task Documento_autorizado_con_foto_devuelve_jpeg_y_etag_y_lo_cachea()
    {
        var (servicio, origen, cache) = Crear();
        using var _ = cache;
        origen.Habilitados.Add(1010);
        origen.Fotos[1010] = Foto(id: 42);

        var r1 = await servicio.ObtenerMiniaturaAsync(1010, 96);
        var r2 = await servicio.ObtenerMiniaturaAsync(1010, 96);

        Assert.NotNull(r1);
        AssertEsJpeg(r1!.Value.Jpeg);
        using (var bmp = Decodificar(r1.Value.Jpeg))
            Assert.Equal((96, 96), (bmp.Width, bmp.Height));
        Assert.Equal(MiniaturaFoto.CalcularETag(42, origen.Fotos[1010].UploadedAt, 96), r1.Value.ETag);
        Assert.Equal(r1.Value.ETag, r2!.Value.ETag);
        Assert.Equal(1, origen.LecturasFoto); // la segunda salió del caché

        // Otro tamaño es otra entrada de caché, con su propio ETag.
        var r3 = await servicio.ObtenerMiniaturaAsync(1010, 320);
        Assert.NotEqual(r1.Value.ETag, r3!.Value.ETag);
        Assert.Equal(2, origen.LecturasFoto);
    }

    [Fact]
    public async Task Autorizado_sin_foto_devuelve_null_y_no_vuelve_a_consultar()
    {
        var (servicio, origen, cache) = Crear();
        using var _ = cache;
        origen.Habilitados.Add(2020);

        Assert.Null(await servicio.ObtenerMiniaturaAsync(2020, 160));
        Assert.Null(await servicio.ObtenerMiniaturaAsync(2020, 48));
        Assert.Equal(1, origen.LecturasFoto);
    }

    [Fact]
    public async Task Foto_corrupta_se_trata_como_sin_foto()
    {
        var (servicio, origen, cache) = Crear();
        using var _ = cache;
        origen.Habilitados.Add(3030);
        origen.Fotos[3030] = new FotoOriginal(9, [0x00, 0x11, 0x22], "image/png", DateTime.Now);

        Assert.Null(await servicio.ObtenerMiniaturaAsync(3030, 160));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Documento_no_positivo_devuelve_null(long documento)
    {
        var (servicio, origen, cache) = Crear();
        using var _ = cache;

        Assert.Null(await servicio.ObtenerMiniaturaAsync(documento, 160));
        Assert.Equal(0, origen.ConsultasHabilitado);
    }
}
