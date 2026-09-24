using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Positiva: traducción de <c>sp_ProcesarPositivaAuxiliar</c> (respaldo en docs/sql/legacy/).
///
/// Lo que se tradujo (en <see cref="Transformar"/>, antes de la validación genérica):
/// <list type="bullet">
/// <item>Paso 4 del SP ("Organizar Telefonos Fijos"): un número que llega en la columna de
///   celular y empieza en 6 es un fijo → pasa a <c>NumeroTelefonoFijo</c> y el celular queda
///   vacío. Aquí se mueve solo si es un fijo válido (10 dígitos que empiezan en 6, sin
///   separadores); lo demás lo anula después la validación genérica del celular.</item>
/// <item>Si la columna de fijo ya trae un fijo válido distinto, el SP concatenaba
///   <c>fijo + ', ' + celular</c>. Aquí se conserva el fijo que venía en su columna y el otro se
///   descarta con advertencia: un "fijo" con dos números no es marcable.</item>
/// <item>Si la columna de fijo traía algo que no es un fijo, el SP también lo concatenaba. Aquí:
///   si es un celular válido (el proveedor intercambió las columnas), pasa a la columna de
///   celular, que el paso anterior dejó libre, y el fijo ocupa la suya: los dos números quedan,
///   cada uno en su columna, como quedaban (concatenados) en el SP. Si es basura, el fijo la
///   reemplaza con advertencia.</item>
/// <item>Pasos 5 (lista negra de correos), 6 (flag), 1-2 (Titular, Titular_Cartera), 3 y 9
///   (deduplicación) y 10 (vacías): el motor.</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Concatenaba dos teléfonos en <c>NumeroTelefonoFijo varchar(30)</c> ('6071236176,
///   6076981919': 522 filas legacy); ningún consumidor puede marcar eso.</item>
/// <item>Paso 5: lista negra de correos con <c>LIKE '%' + CorreoErroneo + '%'</c> y el correo
///   anulado a <c>''</c>, no a NULL; y como el paso 6 mira <c>IS NULL</c>, ese <c>''</c> contaba como
///   dato: un titular cuyo único dato era un correo vetado quedaba con <c>EstadoEnPositiva = 1</c>.</item>
/// <item>Ningún teléfono se validaba (el celular '0' se guardaba: ~30.000 filas legacy).</item>
/// <item>Paso 8: <c>INSERT INTO Positiva SELECT …</c> posicional.</item>
/// <item>Paso 9: deduplicación sobre TODA la tabla (2,2M filas) con clave recortada
///   (titular, celular, fijo, email): un cambio de FechaNacimiento o Municipio borraba la fila
///   anterior.</item>
/// <item>Paso 10: purga de vacías sobre toda la tabla y con la condición mal armada (exigía una
///   fila con celular Y fijo Y email a la vez): un titular con solo celular conservaba sus filas
///   vacías. El motor purga las vacías del titular que ahora trae datos.</item>
/// </list>
/// </summary>
internal sealed class ReglaPositiva : IReglaFuente
{
    private const string Cel = "NumeroTelefonoCelular";
    private const string Fijo = "NumeroTelefonoFijo";

    public string Fuente => CatalogoCargueFuentes.Positiva.Fuente;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        var (numeroCel, celEsCelular) = TelefonoColombiano.Normalizar(ctx.GetTexto(fila, Cel));
        if (numeroCel is null || celEsCelular || numeroCel[0] != '6') return;   // no es un fijo en el celular

        var brutoFijo = ctx.GetTexto(fila, Fijo);
        var (numeroFijo, fijoEsCelular) = TelefonoColombiano.Normalizar(brutoFijo);

        if (numeroFijo is not null && !fijoEsCelular && numeroFijo[0] == '6')
        {
            // Ya hay un fijo válido en su columna: el SP los concatenaba; aquí gana el de su columna.
            if (numeroFijo != numeroCel)
                ctx.Advertir($"{Cel}: fijo en la columna de celular descartado (la columna de fijo ya tenía otro fijo)");
            ctx.Set(fila, Cel, null);
            return;
        }

        if (numeroFijo is not null && fijoEsCelular)
        {
            // Columnas intercambiadas: cada número a la suya.
            ctx.Set(fila, Cel, numeroFijo);
            ctx.Set(fila, Fijo, numeroCel);
            return;
        }

        if (brutoFijo is not null)
            ctx.Advertir($"{Fijo}: valor inválido reemplazado por el fijo que venía en la columna de celular");
        ctx.Set(fila, Cel, null);
        ctx.Set(fila, Fijo, numeroCel);
    }
}
