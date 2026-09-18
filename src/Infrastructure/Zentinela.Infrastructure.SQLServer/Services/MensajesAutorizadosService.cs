using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.MensajesAutorizados;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// CRUD del catálogo cerrado de SMS (ADR-037). La validación es estricta a propósito: un
/// texto que pase aquí tiene que poder enviarse tal cual en 160 caracteres GSM-7.
/// </summary>
public class MensajesAutorizadosService : IMensajesAutorizadosService
{
    private const int MaxTitulo = 100;
    private const int MaxTexto  = 320;

    private readonly CallCenterDbContext _db;

    public MensajesAutorizadosService(CallCenterDbContext db) => _db = db;

    public async Task<List<MensajeAutorizadoDto>> ListarAsync(bool soloActivos, CancellationToken ct = default)
    {
        var q = _db.MensajesAutorizados.AsNoTracking();
        if (soloActivos) q = q.Where(m => m.Activo);

        var lista = await q.OrderByDescending(m => m.Activo).ThenBy(m => m.Titulo).ToListAsync(ct);
        return lista.Select(ADto).ToList();
    }

    public async Task<MensajeAutorizadoDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var m = await _db.MensajesAutorizados.AsNoTracking().FirstOrDefaultAsync(x => x.ID == id, ct);
        return m is null ? null : ADto(m);
    }

    public async Task<MensajeAutorizadoDto> CrearAsync(
        GuardarMensajeAutorizadoRequest request, string usuario, CancellationToken ct = default)
    {
        Validar(request);
        var ahora = DateTime.Now;
        var m = new MensajeAutorizado
        {
            Titulo               = request.Titulo.Trim(),
            Texto                = request.Texto.Trim(),
            Activo               = request.Activo,
            FechaCreacion        = ahora,
            UsuarioCreacion      = usuario,
            FechaActualizacion   = ahora,
            UsuarioActualizacion = usuario,
        };
        _db.MensajesAutorizados.Add(m);
        await _db.SaveChangesAsync(ct);
        return ADto(m);
    }

    public async Task<MensajeAutorizadoDto?> ActualizarAsync(
        int id, GuardarMensajeAutorizadoRequest request, string usuario, CancellationToken ct = default)
    {
        Validar(request);
        var m = await _db.MensajesAutorizados.FirstOrDefaultAsync(x => x.ID == id, ct);
        if (m is null) return null;

        m.Titulo               = request.Titulo.Trim();
        m.Texto                = request.Texto.Trim();
        m.Activo               = request.Activo;
        m.FechaActualizacion   = DateTime.Now;
        m.UsuarioActualizacion = usuario;
        await _db.SaveChangesAsync(ct);
        return ADto(m);
    }

    public async Task<bool> CambiarActivoAsync(int id, bool activo, string usuario, CancellationToken ct = default)
    {
        var m = await _db.MensajesAutorizados.FirstOrDefaultAsync(x => x.ID == id, ct);
        if (m is null) return false;
        m.Activo               = activo;
        m.FechaActualizacion   = DateTime.Now;
        m.UsuarioActualizacion = usuario;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Reglas, en orden: título y texto no vacíos y dentro del largo; los placeholders del
    /// asesor presentes; ningún placeholder desconocido; y ningún carácter fuera de GSM-7
    /// (una sola tilde convierte el SMS a UCS-2 y el límite real cae a 70).
    /// </summary>
    public void Validar(GuardarMensajeAutorizadoRequest request)
    {
        var titulo = request.Titulo?.Trim() ?? string.Empty;
        var texto  = request.Texto?.Trim()  ?? string.Empty;

        if (titulo.Length == 0)         throw new MensajeAutorizadoInvalidoException("El título es obligatorio.");
        if (titulo.Length > MaxTitulo)  throw new MensajeAutorizadoInvalidoException($"El título no puede superar {MaxTitulo} caracteres.");
        if (texto.Length == 0)          throw new MensajeAutorizadoInvalidoException("El texto es obligatorio.");
        if (texto.Length > MaxTexto)    throw new MensajeAutorizadoInvalidoException($"El texto no puede superar {MaxTexto} caracteres.");

        var faltantes = CatalogoCreadorVirtualidad.PlaceholdersObligatorios
            .Where(p => !texto.Contains(p, StringComparison.Ordinal))
            .ToList();
        if (faltantes.Count > 0)
            throw new MensajeAutorizadoInvalidoException(
                $"El texto debe incluir {string.Join(" y ", faltantes)}: sin ellos el titular no tiene a quién responder.");

        var desconocidos = CatalogoCreadorVirtualidad.CualquierPlaceholder.Matches(texto)
            .Select(m => m.Value)
            .Where(p => !CatalogoCreadorVirtualidad.PlaceholdersPermitidos.Contains(p))
            .Distinct()
            .ToList();
        if (desconocidos.Count > 0)
            throw new MensajeAutorizadoInvalidoException(
                $"Variables no reconocidas: {string.Join(", ", desconocidos)}. " +
                $"Las permitidas son {string.Join(", ", CatalogoCreadorVirtualidad.PlaceholdersPermitidos)}.");

        var rechazados = CatalogoCreadorVirtualidad.CaracteresNoGsm7(texto);
        if (rechazados.Count > 0)
        {
            var detalle = rechazados
                .Select(r => new CaracterRechazadoDto { Caracter = r.Caracter.ToString(), Posicion = r.Posicion })
                .ToList();
            var distintos = string.Join(" ", rechazados.Select(r => r.Caracter).Distinct());
            throw new MensajeAutorizadoInvalidoException(
                $"El texto tiene caracteres que no caben en un SMS de 160 (tildes, ñ o símbolos): {distintos}. " +
                "Reemplázalos: 'Señor' → 'Senor', 'gestión' → 'gestion'.", detalle);
        }
    }

    private static MensajeAutorizadoDto ADto(MensajeAutorizado m) => new()
    {
        ID                   = m.ID,
        Titulo               = m.Titulo,
        Texto                = m.Texto,
        Activo               = m.Activo,
        Longitud             = m.Texto.Length,
        LongitudBase         = CatalogoCreadorVirtualidad.LongitudBase(m.Texto),
        FechaCreacion        = m.FechaCreacion,
        UsuarioCreacion      = m.UsuarioCreacion,
        FechaActualizacion   = m.FechaActualizacion,
        UsuarioActualizacion = m.UsuarioActualizacion,
    };
}
