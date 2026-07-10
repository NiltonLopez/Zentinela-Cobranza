using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface IGestionCarterasService
{
    /// <summary>
    /// Lista todas las relaciones Líder_Cartera con el conteo de titulares asignados.
    /// Pasa <c>incluirExternas=false</c> en flujos administrativos (selector de Pagos, etc.)
    /// donde las carteras con <c>Cartera.EsExterna=1</c> no aplican.
    /// </summary>
    Task<IEnumerable<RelacionLiderCarteraDto>> ListarRelacionesAsync(bool incluirExternas = true);

    /// <summary>Lista todos los líderes activos para el dropdown de creación.</summary>
    Task<IEnumerable<LiderDropdownDto>> ListarLideresAsync();

    /// <summary>
    /// Lista empleados de DBRecursosHumanos elegibles para darse de alta como líder:
    /// cargo de líder (Líder/Coordinador/Director, ver <see cref="CargoRoles.CargosLider"/>),
    /// activos en RRHH y que aún no existan como líder activo en DBCallCenter.
    /// </summary>
    Task<IEnumerable<EmpleadoElegibleDto>> ListarEmpleadosElegiblesAsync();

    /// <summary>
    /// Crea un líder en DBCallCenter a partir de un empleado de RRHH (por cédula).
    /// Valida que el empleado exista, esté activo y tenga cargo de líder. Si ya existía
    /// un líder inactivo con esa cédula lo reactiva en vez de duplicarlo.
    /// Lanza InvalidOperationException ante datos inválidos o si ya es líder activo.
    /// </summary>
    Task<LiderDropdownDto> CrearLiderAsync(CrearLiderDto dto);

    /// <summary>
    /// Crea un líder EXTERNO (no empleado) en DBCallCenter y su usuario Zentinela con rol
    /// Líder Externo. El documento es opcional. Contraseña inicial: el documento si viene
    /// (≥6 dígitos), o una temporal generada que se devuelve en la respuesta para mostrarla
    /// una sola vez. Si la creación del usuario falla, revierte el líder creado.
    /// </summary>
    Task<CrearLiderExternoResponse> CrearLiderExternoAsync(CrearLiderExternoDto dto);

    /// <summary>Busca carteras por nombre (máximo 10 resultados) para el autocompletado.</summary>
    Task<IEnumerable<CarteraDto>> BuscarCarterasAsync(string q);

    /// <summary>
    /// Crea una relación Líder_Cartera. Si CarteraID es null crea la cartera primero.
    /// Lanza InvalidOperationException si la relación ya existe.
    /// </summary>
    Task<RelacionLiderCarteraDto> CrearRelacionAsync(CrearRelacionDto dto);

    /// <summary>
    /// Elimina FÍSICAMENTE una relación Líder_Cartera. Solo permitido cuando la relación
    /// no tiene dependientes (pagos, asesores, metas, mensajería, IVR). Para relaciones
    /// con histórico usar <see cref="InactivarRelacionAsync"/>. Ver ADR-022.
    /// </summary>
    Task EliminarRelacionAsync(int liderID, int carteraID);

    /// <summary>
    /// Retira (soft-delete) una relación Líder_Cartera: vacía sus Titular_Cartera y la
    /// marca Activo = false, conservando intacto su histórico. Reversible. Ver ADR-022.
    /// </summary>
    Task InactivarRelacionAsync(int liderID, int carteraID);

    /// <summary>Reactiva una relación previamente inactivada (Activo = true). Ver ADR-022.</summary>
    Task ReactivarRelacionAsync(int liderID, int carteraID);

    /// <summary>
    /// Genera un Excel con las asignaciones actuales (NumeroDocumento + Nombre)
    /// de una cartera+líder. El formato es compatible con la plantilla de cargue masivo,
    /// permitiendo descargarlo, editarlo y re-subirlo directamente.
    /// </summary>
    Task<byte[]> DescargarAsignacionAsync(int liderID, int carteraID);
}
