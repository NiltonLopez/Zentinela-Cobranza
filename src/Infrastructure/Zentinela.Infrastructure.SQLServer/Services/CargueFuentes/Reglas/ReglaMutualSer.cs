using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// MutualSer: traducción de <c>sp_ProcesarMutualSerAuxiliar</c> (respaldo en docs/sql/legacy/).
/// El proveedor mezcla las tres columnas de teléfono; la regla reubica cada número en la que
/// le corresponde ANTES de la validación genérica (que después anula lo que siga mal ubicado).
///
/// Lo que se tradujo, en este orden:
/// <list type="number">
/// <item>Paso 4 del SP: celular 2 igual al celular → NULL.</item>
/// <item>Pasos 5 y 6: fijo que en realidad es un celular (3xx de 10 dígitos) → celular 2 si está
///   libre; si repite el celular o el celular 2, se anula. Si el celular 2 está ocupado queda
///   donde está y la validación genérica lo anula con su advertencia.</item>
/// <item>Pasos 8 y 9: celular o celular 2 que empieza en 60 (fijo de 10 dígitos) → fijo. Si el
///   fijo ya tiene OTRO fijo válido, el número se descarta con advertencia.</item>
/// <item>Paso 5 otra vez, al final: fijo igual a un celular → NULL.</item>
/// </list>
/// Las comparaciones son sobre el número normalizado (sin 57, sin separadores), no sobre el
/// texto con LTRIM/RTRIM: "573001234567" y "3001234567" son el mismo celular. El paso 7 (fijo
/// de largo ≠ 10), el paso 10 (lista negra de correos) y el 15 (vacías) los hace el motor.
///
/// El orden es el mismo del SP: el fijo que es celular pasa a celular 2 (paso 6) ANTES de llevar
/// los 60x al fijo (pasos 8 y 9), y por eso el intercambio (celular en el fijo + 60x en el
/// celular) deja cada número en su columna en los dos lados. Donde el SP sí perdía datos era con
/// el celular 2 ocupado: el celular se quedaba en el fijo, el 60x se le concatenaba y el
/// UPDATE del paso 8 (o 9) fallaba para TODA la carga (ver el primer defecto abajo). Aquí el 60x
/// reemplaza, con advertencia, al celular mal ubicado en el fijo (que no tiene columna libre y
/// se pierde), y el resto de las filas no se ve afectado.
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Pasos 8 y 9 concatenaban <c>fijo + ', ' + celular</c> en
///   <c>MutualSerAuxiliar.NumeroTelefonoFijo varchar(10)</c>: con un solo conflicto el UPDATE
///   fallaba entero (error 2628) y las filas sin conflicto quedaban con su 60x como celular.
///   Aquí nunca se concatena.</item>
/// <item>Paso 10 anulaba correos con <c>LIKE '%' + CorreoErroneo + '%'</c> y los dejaba en ''
///   (no NULL): el motor usa igualdad exacta de correo o dominio y deja NULL.</item>
/// <item>El flag se calculaba contando '' como vacío pero el INSERT guardaba '' como dato.</item>
/// </list>
/// </summary>
internal sealed class ReglaMutualSer : IReglaFuente
{
    private const string Cel = "NumeroTelefonoCelular";
    private const string Cel2 = "NumeroTelefonoCelular2";
    private const string Fijo = "NumeroTelefonoFijo";

    public string Fuente => CatalogoCargueFuentes.MutualSer.Fuente;

    /// <summary>Un teléfono tal como vino y, si es válido, normalizado a 10 dígitos.</summary>
    private readonly record struct Tel(string? Bruto, string? Numero, bool EsCelular)
    {
        public static readonly Tel Vacio = new(null, null, false);

        public static Tel De(string? bruto)
        {
            var (numero, esCelular) = TelefonoColombiano.Normalizar(bruto);
            return new Tel(bruto, numero, esCelular);
        }

        public bool EsFijoValido => Numero is not null && !EsCelular && Numero[0] == '6';
        public bool EmpiezaEn60 => Numero is not null && Numero.StartsWith("60", StringComparison.Ordinal);

        /// <summary>Lo que se escribe de vuelta: normalizado si es válido; si no, tal cual (lo anula el motor).</summary>
        public string? Valor => Numero ?? Bruto;
    }

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        var cel  = Tel.De(ctx.GetTexto(fila, Cel));
        var cel2 = Tel.De(ctx.GetTexto(fila, Cel2));
        var fijo = Tel.De(ctx.GetTexto(fila, Fijo));

        // 1. Celular 2 repetido.
        if (cel2.Numero is not null && cel2.Numero == cel.Numero) cel2 = Tel.Vacio;

        // 2. Fijo que en realidad es celular.
        if (fijo.EsCelular)
        {
            if (fijo.Numero == cel.Numero || fijo.Numero == cel2.Numero) fijo = Tel.Vacio;
            else if (cel2.Bruto is null) (cel2, fijo) = (fijo, Tel.Vacio);
        }

        // 3. Celulares que en realidad son fijos (60x) → fijo. Nunca se concatena.
        (cel, fijo) = MoverAFijo(cel, fijo, Cel, ctx);
        (cel2, fijo) = MoverAFijo(cel2, fijo, Cel2, ctx);

        // 4. Fijo repetido en un celular.
        if (fijo.Numero is not null && (fijo.Numero == cel.Numero || fijo.Numero == cel2.Numero)) fijo = Tel.Vacio;

        ctx.Set(fila, Cel, cel.Valor);
        ctx.Set(fila, Cel2, cel2.Valor);
        ctx.Set(fila, Fijo, fijo.Valor);
    }

    private static (Tel Celular, Tel Fijo) MoverAFijo(Tel celular, Tel fijo, string columna, ContextoCargue ctx)
    {
        if (!celular.EmpiezaEn60) return (celular, fijo);

        if (fijo.EsFijoValido)
        {
            // El mismo número en las dos columnas no es un conflicto: basta con uno.
            if (fijo.Numero != celular.Numero)
                ctx.Advertir($"{columna}: fijo en la columna de celular descartado (la columna de fijo ya tenía otro fijo)");
            return (Tel.Vacio, fijo);
        }

        if (fijo.Bruto is not null)
            ctx.Advertir($"{Fijo}: valor inválido reemplazado por el fijo que venía en la columna de celular");
        return (Tel.Vacio, celular);
    }
}
