using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// RGM: traducción de <c>sp_ProcesarRGMAuxiliar</c> (2026-04-13; respaldo en docs/sql/legacy/).
///
/// El SP no tenía nada propio además del esqueleto común: Titular, Titular_Cartera, dedup del
/// staging por titular, flag = Marca, Modelo o Placa con valor (paso 4, es la definición),
/// FechaConsulta, INSERT y dedup del destino. Todo eso lo hace el motor. La placa se valida con
/// la regla genérica de columnas tipo Placa (<c>PlacaVehiculo</c>, la misma de SIMIT).
///
/// Lo único que agrega esta regla es la validación del <b>Modelo</b> (<see cref="Transformar"/>):
/// un año entre <see cref="ModeloMinimo"/> y el año actual + <see cref="MargenAnios"/>; lo demás se
/// anula con una advertencia agrupada: un modelo como 2323 (hay uno en dbo.RGM) no es un dato de
/// localización. El desbordamiento de la columna destino (<c>int</c>) ya no depende de esta regla:
/// la definición declara <c>EnteroSql = Int</c> y el motor anula antes lo que no cabe en int, con
/// su propia advertencia. Rango real en dbo.RGM (2026-09-24): 1991..2026 más ese
/// 2323. El margen de 2 años cubre los modelos del año siguiente que se venden desde mitad de año.
///
/// Defectos del SP que NO se copian:
/// <list type="bullet">
/// <item><b><c>Titular_Cartera</c> con <c>LiderID = CarteraID</c></b> (paso 2: <c>SELECT DISTINCT
///   fa.TitularID, fa.CarteraID, fa.CarteraID</c>). Con FK_TitularCartera_LiderCartera el INSERT
///   falla entero salvo que exista el par (CarteraID, CarteraID); y con las carteras 4 y 22, que
///   tienen varios líderes y uno de ellos con el mismo número (pares activos (4, 4) y (22, 22)),
///   asignaba el titular a ese líder aunque el archivo dijera otro. El motor usa la cartera y el
///   líder del archivo, validados contra Lider_Cartera activo.</item>
/// <item>INSERT a Titular sin limpiar ni exigir nombre (una fila sin nombre tumbaba el paso y,
///   por FK_RGM, el INSERT a RGM).</item>
/// <item>Dedup del staging <c>ORDER BY FechaConsulta DESC</c> con esa fecha aún en NULL (ganador
///   arbitrario). Aquí se conserva la fila más completa.</item>
/// <item><c>INSERT INTO RGM SELECT …</c> posicional, con <c>Modelo</c> varchar(50) → int implícito:
///   un modelo no numérico ('ABC', '2021.0') hacía fallar el INSERT de todo el archivo. Aquí el
///   entero se parsea por fila y lo que no es número se anula con advertencia.</item>
/// <item><c>RGMAuxiliar.Placa</c> varchar(6): una placa de 7 se truncaba en el import, y así
///   llegaron las 21 '[objec' ('[object Object]') a dbo.RGM. Aquí la placa se lee entera, se
///   normaliza (sin espacios ni guiones, mayúsculas: ' HGK96' → 'HGK96') y se valida.</item>
/// <item>Dedup del paso 7 sobre TODA la tabla RGM (1,66M filas) en cada carga. Aquí la clave de
///   contenido completa decide refrescar o insertar, solo para los titulares del archivo.</item>
/// <item>El SP no purgaba vacías; el motor aplica la política de vacías genérica (P5).</item>
/// </list>
/// </summary>
internal sealed class ReglaRgm : IReglaFuente
{
    public const int ModeloMinimo = 1900;
    public const int MargenAnios = 2;

    /// <summary>Mensaje fijo (agrupa en el preview).</summary>
    internal const string AvisoModelo = "Modelo: no es un año de modelo plausible, anulado";

    public string Fuente => CatalogoCargueFuentes.Rgm.Fuente;

    /// <summary>Año máximo aceptado: el actual + <see cref="MargenAnios"/>.</summary>
    public static int ModeloMaximo => DateTime.Today.Year + MargenAnios;

    public void Transformar(FilaCargue fila, ContextoCargue ctx)
    {
        // El tipado genérico ya dejó un long (o null si no era número: con su propia advertencia).
        if (ctx.Get(fila, "Modelo") is not long modelo) return;
        if (modelo >= ModeloMinimo && modelo <= ModeloMaximo) return;
        ctx.Advertir(AvisoModelo);
        ctx.Set(fila, "Modelo", null);
    }
}
