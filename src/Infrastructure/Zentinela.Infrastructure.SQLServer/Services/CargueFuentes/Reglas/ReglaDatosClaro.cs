using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Datos Claro: traducción de <c>sp_ProcesarDatosClaroAuxiliar</c> (respaldo en docs/sql/legacy/).
///
/// Lo que se tradujo:
/// <list type="bullet">
/// <item>Paso 2: teléfono en lista negra → NULL. Lo hace el motor (<see cref="NecesitaListaNegraTelefonos"/>)
///   contra <c>dbo.ListaNegraTelefonos</c> con #temp + HASH JOIN, y después vuelve a llamar a
///   <see cref="Validar"/> para las filas que perdieron el teléfono.</item>
/// <item>Paso 5: fila sin teléfono ni email → no entra. Aquí es una exclusión contada (error),
///   no un DELETE silencioso por bloques.</item>
/// <item>Pasos 3 y 4 (correo en lista negra por igualdad, correo sin '@' o '.'), paso 6 (insertar
///   solo lo nuevo, comparando con ISNULL) y paso 7 (dedup en destino): el motor, con validación
///   de correo más estricta, igualdad NULL-safe con INTERSECT y refresco de la fecha.</item>
/// </list>
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item>Cruzaba contra ListaNegraTelefonosAuxiliar (staging de 1,2M filas) y no contra la lista
///   negra vigente.</item>
/// <item>El teléfono no se validaba: cualquier texto de hasta 10 caracteres entraba.</item>
/// <item>ISNULL(x, '') en la comparación igualaba NULL con '' y el paso 7 borraba duplicados
///   de TODA la tabla (3,5M filas) en cada carga.</item>
/// <item>Titular inexistente: entraba igual y en silencio. Sigue entrando (no hay FK y la fuente
///   no crea titulares), pero el motor lo cuenta como advertencia.</item>
/// </list>
/// </summary>
internal sealed class ReglaDatosClaro : IReglaFuente
{
    private const string Telefono = "NumeroTelefono";
    private const string Email = "Email";

    public string Fuente => CatalogoCargueFuentes.DatosClaro.Fuente;

    public bool NecesitaListaNegraTelefonos => true;

    public void Validar(FilaCargue fila, ContextoCargue ctx)
    {
        if (ctx.Get(fila, Telefono) is null && ctx.Get(fila, Email) is null)
            ctx.Excluir(fila, "Sin teléfono ni email válidos tras la limpieza");
    }
}
