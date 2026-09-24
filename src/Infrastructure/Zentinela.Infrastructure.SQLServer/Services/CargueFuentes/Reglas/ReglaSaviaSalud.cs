using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// SaviaSalud: traducción de <c>sp_ProcesarSaviaSaludAuxiliar</c> (respaldo en docs/sql/legacy/).
/// El proveedor manda a veces VARIOS números en una celda, separados por coma
/// ('3147630194,3148795401': 161 filas legacy en NumeroCelular).
///
/// Lo que se tradujo (en <see cref="Transformar"/>, antes de la validación genérica):
/// <list type="bullet">
/// <item>Pasos 5 y 6 del SP: partir NumeroTelefono y NumeroCelular por coma, quitar los
///   fragmentos que no tienen 10 caracteres y anular la columna si no queda ninguno. Aquí un
///   fragmento sirve si es un teléfono válido (celular 3xx o fijo 6xx de 10 dígitos, sin 57);
///   los que no, se descartan con advertencia. Se aceptan también ';' y '/' como separador.</item>
/// <item>El SP volvía a unir los fragmentos buenos con coma en la misma columna (NumeroCelular es
///   varchar(100) en el destino). Aquí NO se concatenan: NumeroCelular se queda con el primer celular;
///   NumeroTelefono, con su propio número o, si venía vacío, con el siguiente de la lista; los
///   que sobran se descartan con advertencia. Así se conservan hasta dos números, cada uno
///   marcable, en vez de una lista que ningún consumidor sabe marcar ([Celular SaviaSalud] de la
///   BaseMadre la entregaba entera al selector de celulares).</item>
/// <item>Un fijo (6xx) que llega solo en NumeroCelular pasa a NumeroTelefono si esa columna viene
///   vacía (175 filas legacy tienen un fijo en NumeroCelular, que la BaseMadre expone como
///   celular). Si NumeroTelefono ya trae otro número, la validación genérica anula el fijo.</item>
/// <item>Paso 4 (lista negra de correos), paso 7 (flag), pasos 1-3 (Titular, Titular_Cartera,
///   dedup) y paso 8 (insertar solo lo nuevo): el motor.</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>El flag <c>Estado</c> contaba el Email, que el SP limpiaba pero <b>no guarda</b>:
///   <c>dbo.SaviaSalud</c> no tiene columna de correo. Un titular con solo correo quedaba con
///   <c>Estado = 1</c> y ningún dato (2.051 filas legacy con Estado 1 y sin teléfonos). Aquí el
///   flag solo mira las columnas que se guardan, y el Email del archivo se informa como columna
///   que no se usa.</item>
/// <item>El filtro "LEN = 10" dejaba pasar texto: hay municipios truncados guardados como teléfono
///   ('COPACABANA', 'LA ESTRELL': filas de un import corrido). La validación genérica los anula.</item>
/// <item>Lista negra de correos con <c>LIKE '%' + CorreoErroneo + '%'</c>: solo movía el flag,
///   porque el correo no se guardaba.</item>
/// <item>Dedup de la Auxiliar con <c>ORDER BY TitularID</c> (fila arbitraria), clave de "solo lo
///   nuevo" recortada (teléfono, celular, tipo de afiliado con <c>ISNULL(x, '')</c>: un cambio de
///   régimen o estado de afiliación se descartaba), sin refresco de fecha y Titular_Cartera sin
///   validar el par.</item>
/// </list>
/// </summary>
internal sealed class ReglaSaviaSalud : IReglaFuente
{
    private const string Tel = "NumeroTelefono";
    private const string Cel = "NumeroCelular";
    private static readonly char[] Separadores = [',', ';', '/'];

    public string Fuente => CatalogoCargueFuentes.SaviaSalud.Fuente;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        var fragCel = Fragmentos(ctx.GetTexto(fila, Cel));
        var fragTel = Fragmentos(ctx.GetTexto(fila, Tel));

        if (fragCel.Count <= 1 && fragTel.Count <= 1)
        {
            // Un número por celda: la validación genérica hace el resto. Solo se reubica el fijo
            // que llegó en NumeroCelular si NumeroTelefono está libre.
            if (fragCel.Count == 1 && fragTel.Count == 0 && Fijo(fragCel[0]) is { } fijo)
            {
                ctx.Set(fila, Tel, fijo);
                ctx.Set(fila, Cel, null);
            }
            return;
        }

        // Alguna celda trae una lista: se reparte sin concatenar.
        var celulares = new List<string>();
        var otros = new List<string>();
        foreach (var f in fragCel)
        {
            var (numero, esCelular) = TelefonoColombiano.Normalizar(f);
            if (numero is not null && esCelular) celulares.Add(numero);
            else if (Fijo(f) is { } fijo) otros.Add(fijo);
            else ctx.Advertir($"{Cel}: número de la lista que no es un teléfono válido, descartado");
        }

        var propiosTel = new List<string>();
        foreach (var f in fragTel)
        {
            var (numero, esCelular) = TelefonoColombiano.Normalizar(f);
            if (numero is not null && (esCelular || numero[0] == '6')) propiosTel.Add(numero);
            else ctx.Advertir($"{Tel}: número de la lista que no es un teléfono válido, descartado");
        }

        var nuevoCel = celulares.FirstOrDefault();
        var paraTel = propiosTel.Concat(celulares.Skip(1)).Concat(otros)
            .Where(n => n != nuevoCel)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        ctx.Set(fila, Cel, nuevoCel);
        ctx.Set(fila, Tel, paraTel.FirstOrDefault());
        for (var i = 1; i < paraTel.Count; i++)
            ctx.Advertir("Teléfono adicional de la lista descartado (SaviaSalud guarda un celular y un teléfono; no se concatenan)");
    }

    /// <summary>Fragmentos no vacíos de una celda (coma, punto y coma o barra).</summary>
    private static List<string> Fragmentos(string? texto) =>
        texto is null
            ? []
            : texto.Split(Separadores, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                   .Select(CargueFuentesMotor.LimpiarTexto)
                   .OfType<string>()
                   .ToList();

    /// <summary>El número normalizado si es un fijo válido (6xx de 10 dígitos); si no, null.</summary>
    private static string? Fijo(string fragmento) => TelefonoColombiano.NormalizarFijo(fragmento);
}
