using SkiaSharp;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Transformación pura de una foto de RRHH (JPEG o PNG, hasta ~2 MB) a una miniatura cuadrada en
/// JPEG, con SkiaSharp. No toca la BD: la prueban <c>FotoPersonaServiceTests</c> con imágenes
/// generadas en memoria.
/// </summary>
internal static class MiniaturaFoto
{
    /// <summary>Lados admitidos. Pocos valores a propósito: cada uno es una entrada de caché.</summary>
    public static readonly IReadOnlyList<int> TamanosPermitidos = [48, 96, 160, 320];

    public const int TamanoPorDefecto = 160;

    /// <summary>Calidad JPEG: 82 no deja artefactos visibles en caras y pesa ~1/3 que 95.</summary>
    public const int CalidadJpeg = 82;

    /// <summary>Un tamaño fuera de la lista se toma como <see cref="TamanoPorDefecto"/>.</summary>
    public static int NormalizarTamano(int tam) =>
        TamanosPermitidos.Contains(tam) ? tam : TamanoPorDefecto;

    /// <summary>
    /// ETag de la miniatura: identifica la foto (ID), su versión (UploadedAt: si RRHH reemplaza la
    /// foto en la misma fila, cambia) y el tamaño. Va entre comillas, como exige el header.
    /// </summary>
    public static string CalcularETag(int fotoID, DateTime uploadedAt, int tam) =>
        $"\"{fotoID}-{uploadedAt.Ticks}-{NormalizarTamano(tam)}\"";

    /// <summary>
    /// Decodifica la imagen, recorta el cuadrado centrado más grande, lo lleva a
    /// <paramref name="tam"/>×<paramref name="tam"/> y lo codifica en JPEG calidad 82.
    /// Respeta la orientación EXIF (fotos de celular giradas). Devuelve null si los bytes no son
    /// una imagen que Skia sepa leer.
    /// </summary>
    public static byte[]? Generar(byte[] original, int tam)
    {
        tam = NormalizarTamano(tam);
        if (original is null || original.Length == 0) return null;

        using var datos = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(datos);
        if (codec is null) return null;

        // Se decodifica a RGBA premultiplicado: el PNG puede traer transparencia, que se aplana
        // sobre blanco al dibujar (JPEG no tiene canal alfa).
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var resultado = codec.GetPixels(info, bitmap.GetPixels());
        if (resultado != SKCodecResult.Success && resultado != SKCodecResult.IncompleteInput) return null;

        // Recorte cuadrado centrado sobre los píxeles tal como vienen guardados. Es válido hacerlo
        // antes de aplicar la orientación EXIF: el cuadrado central de una imagen girada o
        // espejada es el cuadrado central girado o espejado.
        var lado = Math.Min(info.Width, info.Height);
        var origen = SKRect.Create((info.Width - lado) / 2f, (info.Height - lado) / 2f, lado, lado);
        var destino = SKRect.Create(0, 0, tam, tam);

        // Reducción (el caso normal: fotos de 1-4 Mpx a ≤320 px): lineal con mipmaps, que promedia
        // bien sin aliasing. Ampliación (foto más chica que el tamaño pedido): cúbico Mitchell.
        var muestreo = lado > tam
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

        using var superficie = SKSurface.Create(new SKImageInfo(tam, tam, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (superficie is null) return null;

        var lienzo = superficie.Canvas;
        lienzo.Clear(SKColors.White);
        lienzo.SetMatrix(MatrizOrientacion(codec.EncodedOrigin, tam));

        using (var imagen = SKImage.FromBitmap(bitmap))
            lienzo.DrawImage(imagen, origen, destino, muestreo);

        lienzo.Flush();
        using var instantanea = superficie.Snapshot();
        using var jpeg = instantanea.Encode(SKEncodedImageFormat.Jpeg, CalidadJpeg);
        return jpeg?.ToArray();
    }

    /// <summary>
    /// Matriz que lleva el cuadrado guardado (0..tam) a como debe verse según la orientación EXIF.
    /// Con x' = sx·x + kx·y + tx, y' = ky·x + sy·y + ty; como el lienzo es cuadrado, girar 90° no
    /// cambia sus dimensiones.
    /// </summary>
    private static SKMatrix MatrizOrientacion(SKEncodedOrigin origen, float tam) => origen switch
    {
        SKEncodedOrigin.TopRight    => new SKMatrix(-1, 0, tam,  0,  1, 0,   0, 0, 1), // espejo horizontal
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, tam,  0, -1, tam, 0, 0, 1), // 180°
        SKEncodedOrigin.BottomLeft  => new SKMatrix( 1, 0, 0,    0, -1, tam, 0, 0, 1), // espejo vertical
        SKEncodedOrigin.LeftTop     => new SKMatrix( 0, 1, 0,    1,  0, 0,   0, 0, 1), // transpuesta
        SKEncodedOrigin.RightTop    => new SKMatrix( 0, -1, tam, 1,  0, 0,   0, 0, 1), // 90° horario
        SKEncodedOrigin.RightBottom => new SKMatrix( 0, -1, tam, -1, 0, tam, 0, 0, 1), // transversa
        SKEncodedOrigin.LeftBottom  => new SKMatrix( 0, 1, 0,   -1,  0, tam, 0, 0, 1), // 90° antihorario
        _                           => SKMatrix.Identity,
    };
}
