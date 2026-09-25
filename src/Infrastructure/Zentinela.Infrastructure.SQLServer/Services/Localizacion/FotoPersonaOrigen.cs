using Microsoft.EntityFrameworkCore;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>Foto original tal como sale de RRHH (solo lo necesario para la miniatura y el ETag).</summary>
internal sealed record FotoOriginal(int ID, byte[] Contenido, string ContentType, DateTime UploadedAt);

/// <summary>
/// Acceso a datos de <see cref="FotoPersonaService"/>, separado para poder probar el servicio
/// (autorización, caché, "sin foto") sin BD.
/// </summary>
internal interface IFotoPersonaOrigen
{
    /// <summary>El documento es de un líder activo (<c>Lider.NumeroDocumento</c>) o de un asesor activo (<c>Agente.AgenteID</c>).</summary>
    Task<bool> EsPersonaHabilitadaAsync(long documento, CancellationToken ct);

    /// <summary>La foto de RRHH del empleado con esa cédula, o null.</summary>
    Task<FotoOriginal?> LeerFotoAsync(long documento, CancellationToken ct);
}

/// <summary>Implementación sobre EF: DBCallCenter para la autorización, DBRecursosHumanos para la foto.</summary>
internal sealed class FotoPersonaOrigenSql : IFotoPersonaOrigen
{
    private readonly CallCenterDbContext _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;

    public FotoPersonaOrigenSql(CallCenterDbContext callCenter, RecursosHumanosDbContext rrhh)
    {
        _callCenter = callCenter;
        _rrhh = rrhh;
    }

    public async Task<bool> EsPersonaHabilitadaAsync(long documento, CancellationToken ct) =>
        await _callCenter.Lideres.AsNoTracking().AnyAsync(l => l.Activo && l.NumeroDocumento == documento, ct)
        || await _callCenter.Agentes.AsNoTracking().AnyAsync(a => a.Activo && a.AgenteID == documento, ct);

    public Task<FotoOriginal?> LeerFotoAsync(long documento, CancellationToken ct) =>
        // Enlace por cédula (Empleado.NumeroDocumento → FotoEmpleado.IDEmpleado), NUNCA por
        // Lider.ID: los IDs de Lider no son los de Empleado. Se proyecta solo lo que se usa
        // (no FileName/UploadedBy). NumeroDocumento no tiene duplicados en Empleado (verificado
        // 2026-09-25) y FotoEmpleado.IDEmpleado es único, así que hay a lo sumo una fila.
        (from e in _rrhh.Empleados.AsNoTracking()
         join f in _rrhh.FotosEmpleado.AsNoTracking() on e.ID equals f.IDEmpleado
         where e.NumeroDocumento == documento
         select new FotoOriginal(f.ID, f.FileContent, f.ContentType, f.UploadedAt))
        .FirstOrDefaultAsync(ct);
}
