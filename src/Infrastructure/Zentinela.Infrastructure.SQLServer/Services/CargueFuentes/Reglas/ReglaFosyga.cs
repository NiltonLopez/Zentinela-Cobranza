using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Fosyga: traducción de <c>sp_ProcesarFosygaAuxiliar</c> (versión del 2026-09-22; respaldo en
/// docs/sql/legacy/).
///
/// Lo que se tradujo (lo único propio del SP; el resto es el esqueleto común que ya hace el motor):
/// <list type="bullet">
/// <item>Paso 4: <c>UPDATE … SET TipoAfiliado = 'FALLECIDO' WHERE Estado = 'AFILIADO FALLECIDO'</c>.
///   Aquí en <see cref="Transformar"/>, sobre el texto ya limpio y sin distinguir mayúsculas (la
///   collation CI de la BD tampoco distinguía). Se informa como dato (no es un problema del archivo).</item>
/// <item>Paso 5: el flag = Estado, EPS o Régimen con valor. Es la definición (<c>EntraAlFlag</c>).</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item><c>INSERT INTO Titular SELECT DISTINCT TitularID, Nombre</c> sin limpiar ni exigir nombre: una
///   sola fila sin nombre (Titular.Nombre es NOT NULL) tumbaba el INSERT entero; y luego el INSERT a
///   Fosyga fallaba entero por FK_Titular_Fosyga. Es lo que le pasó a la carga del 2026-09-23 que
///   quedó en FosygaAuxiliar (18.598 filas, Nombre/Cartera/Líder vacíos, 4.124 titulares inexistentes:
///   solo 545 filas tienen su contenido en Fosyga). Aquí un titular nuevo sin nombre se excluye con
///   error y el resto entra.</item>
/// <item><c>Titular_Cartera</c> sin validar el par en Lider_Cartera ni su Activo (con CarteraID NULL
///   fallaba entero).</item>
/// <item>Dedup del staging <c>ORDER BY FechaConsulta DESC</c> cuando esa fecha aún no estaba puesta:
///   ganador arbitrario. Aquí se conserva la fila más completa.</item>
/// <item>Flag que contaba <c>''</c> como dato (IS NULL). Aquí se calcula después de limpiar.</item>
/// <item><c>INSERT INTO Fosyga SELECT …</c> posicional.</item>
/// <item>EPS: varchar(200) en el staging y varchar(120) en el destino: un valor largo hacía fallar
///   el INSERT de todo el archivo. Aquí se trunca a 120 con advertencia.</item>
/// <item>Dedup del paso 8 sobre TODA la tabla Fosyga (2,1M filas) en cada carga y con clave recortada
///   (TitularID, Estado, EPS, Régimen, TipoAfiliado): una fila vieja que solo difería en fechas o
///   localización se borraba. Aquí la clave es completa y el contenido idéntico solo refresca la fecha.</item>
/// <item>El SP no purgaba vacías; el motor aplica la política de vacías genérica (P5, como SIMIT).</item>
/// <item>El Estado con salto de línea dentro de la celda ('AFILIADO' + salto + 'FALLECIDO', 69 filas
///   en dbo.Fosyga) no casaba con el <c>=</c> del paso 4 y se guardaba con el salto. Aquí el tipado
///   genérico colapsa los espacios antes de la regla, así que casa y se guarda en una línea.</item>
/// </list>
/// </summary>
internal sealed class ReglaFosyga : IReglaFuente
{
    internal const string EstadoFallecido = "AFILIADO FALLECIDO";
    internal const string TipoFallecido = "FALLECIDO";

    /// <summary>Mensaje fijo: agrupa en el preview.</summary>
    internal const string InfoFallecido =
        "TipoAfiliado puesto en 'FALLECIDO' porque el Estado es 'AFILIADO FALLECIDO'";

    public string Fuente => CatalogoCargueFuentes.Fosyga.Fuente;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        if (!string.Equals(ctx.GetTexto(fila, "Estado"), EstadoFallecido, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(ctx.GetTexto(fila, "TipoAfiliado"), TipoFallecido, StringComparison.Ordinal))
            ctx.Informar(InfoFallecido);
        ctx.Set(fila, "TipoAfiliado", TipoFallecido);
    }
}
