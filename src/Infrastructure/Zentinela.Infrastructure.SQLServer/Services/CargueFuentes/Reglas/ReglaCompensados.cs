using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Compensados: traducción de <c>sp_ProcesarCompensadosAuxiliar</c> (respaldo en docs/sql/legacy/).
///
/// Lo que se tradujo: todo es genérico. Pasos 1 a 3 (Titular, Titular_Cartera, dedup por
/// titular), 4 (flag), 5 (fecha), 7 (INSERT, que en el SP era posicional) y 8 (dedup en destino):
/// el motor. La conversión implícita varchar(50) → date de UltimoPeriodoCompensado (la Auxiliar lo
/// tenía como texto) la hace el tipado <c>Fecha</c> del motor, con aviso si no es una fecha.
///
/// Propio del cargue nuevo (<see cref="Transformar"/>): la marca del proveedor "no encontrado"
/// ('NO SE ENCUENTRA': 594 filas hoy en TipoAfiliado, todas con EPS y período vacíos) se anula con
/// advertencia en EPS y TipoAfiliado: no es un tipo de afiliado, y el Calificador cuenta como
/// "encontrado en Compensados" toda fila con TipoAfiliado no vacío.
///
/// Flag (paso 4 del SP): <c>Estado = CASE WHEN Estado IS NULL AND EPS IS NULL AND
/// UltimoPeriodoCompensado IS NULL THEN 0 ELSE 1</c>. El usuario confirmó (2026-09-24, H2 de
/// PARIDAD-empresas.md) que el Estado del archivo SÍ significa "encontrado en Compensados aunque sin
/// datos": es la columna extra <c>Estado</c> con <c>FlagDesdeArchivo</c> de la definición, y el
/// motor la resuelve de forma genérica (<see cref="FlagEnArchivo"/>, <c>CargueFuentesMotor.CalcularFlag</c>).
/// Esta regla no la toca. Estado no se guarda (el destino no tiene la columna) y es opcional.
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Flag 1 con CUALQUIER Estado no nulo, incluso 0 ("no encontrado"): aquí '0', 'NO' o
///   'NO SE ENCUENTRA' no encienden el flag, y un valor desconocido se avisa y tampoco.</item>
/// <item><c>INSERT INTO Compensados SELECT …</c> posicional (Estado → EstadoEnCompensados por posición).</item>
/// <item>Dedup del paso 8 sobre TODA la tabla, con clave sin EstadoEnCompensados.</item>
/// </list>
/// </summary>
internal sealed class ReglaCompensados : IReglaFuente
{
    private const string Eps = "EPS";
    private const string TipoAfiliado = "TipoAfiliado";

    public string Fuente => CatalogoCargueFuentes.Compensados.Fuente;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        AnularMarca(fila, Eps, ctx);
        AnularMarca(fila, TipoAfiliado, ctx);
    }

    private static void AnularMarca(FilaCargue fila, string columna, ContextoCargue ctx)
    {
        var valor = ctx.GetTexto(fila, columna);
        // Mismas marcas que "no encontrado" en la columna Estado (FlagEnArchivo).
        if (valor is null || !FlagEnArchivo.MarcasNoEncontrado.Contains(ArchivoTabular.Normalizar(valor))) return;
        ctx.Set(fila, columna, null);
        ctx.Advertir($"{columna}: marca 'NO SE ENCUENTRA' del proveedor anulada (no es un dato)");
    }
}
