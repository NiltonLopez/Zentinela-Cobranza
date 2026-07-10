using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestión administrativa de relaciones Líder_Cartera en DBCallCenter.
/// Permite listar, crear y eliminar vínculos entre líderes y carteras,
/// así como crear carteras nuevas cuando aún no existen en el sistema.
/// </summary>
public class GestionCarterasService : IGestionCarterasService
{
    private readonly CallCenterDbContext _context;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly UserManager<ApplicationUser> _userManager;

    public GestionCarterasService(
        CallCenterDbContext context,
        RecursosHumanosDbContext rrhh,
        UserManager<ApplicationUser> userManager)
    {
        _context     = context;
        _rrhh        = rrhh;
        _userManager = userManager;
    }

    public async Task<IEnumerable<RelacionLiderCarteraDto>> ListarRelacionesAsync(bool incluirExternas = true)
    {
        // Subconsulta correlacionada para contar asignaciones de Titular_Cartera por relación.
        // Se ejecuta en SQL Server (no en memoria) gracias al Select proyectado.
        var query = _context.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .AsQueryable();

        if (!incluirExternas)
            query = query.Where(lc => !lc.EsExterna);

        // Se listan activas e inactivas; la UI distingue por el flag Activo y muestra
        // las retiradas con badge gris + botón Reactivar (ADR-022). TieneHistorico
        // resume si hay pagos/metas/mensajería/IVR (lo que impide el borrado físico) para
        // que la X del frontend decida entre Inactivar (soft) y Eliminar (hard).
        return await query
            .OrderByDescending(lc => lc.Activo)
            .ThenBy(lc => lc.Cartera.NombreCartera)
            .ThenBy(lc => lc.Lider.Nombre)
            .Select(lc => new RelacionLiderCarteraDto
            {
                LiderID           = lc.LiderID,
                CarteraID         = lc.CarteraID,
                NombreLider       = lc.Lider.Nombre,
                NombreCartera     = lc.Cartera.NombreCartera,
                EsExterna         = lc.EsExterna,
                Activo            = lc.Activo,
                TotalAsignaciones = _context.TitularCarteras
                    .Count(tc => tc.LiderID == lc.LiderID && tc.CarteraID == lc.CarteraID),
                TotalAsesores     = _context.CarteraAgentes
                    .Count(ca => ca.LiderID == lc.LiderID && ca.CarteraID == lc.CarteraID),
                TieneHistorico    =
                    _context.Pagos.Any(p => p.LiderID == lc.LiderID && p.CarteraID == lc.CarteraID)
                 || _context.MetaLiderCarteraMes.Any(m => m.LiderID == lc.LiderID && m.CarteraID == lc.CarteraID)
                 || _context.HistoricoVirtualidad.Any(s => s.LiderID == lc.LiderID && s.CarteraID == lc.CarteraID),
            })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<LiderDropdownDto>> ListarLideresAsync()
    {
        return await _context.Lideres
            .Where(l => l.Activo)
            .OrderBy(l => l.Nombre)
            .Select(l => new LiderDropdownDto { ID = l.ID, Nombre = l.Nombre })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<EmpleadoElegibleDto>> ListarEmpleadosElegiblesAsync()
    {
        // Empleados con cargo de líder y activos en RRHH. RRHH y CallCenter son
        // bases distintas → no se puede hacer join cross-DB; se filtra en dos pasos.
        var cargosLider = CargoRoles.CargosLider.ToArray();
        var candidatos = await _rrhh.Empleados
            .Where(e => e.EstadoEmpleado && cargosLider.Contains(e.CargoID))
            .Select(e => new
            {
                e.NumeroDocumento,
                e.Nombre,
                e.Apellidos,
                e.Email,
                Cargo = e.Cargo.Descripcion,
            })
            .AsNoTracking()
            .ToListAsync();

        // Cédulas que ya son líder ACTIVO en DBCallCenter — se excluyen de la lista.
        // Los inactivos permanecen elegibles: "crear" los reactiva (ver CrearLiderAsync).
        var yaLideresActivos = await _context.Lideres
            .Where(l => l.Activo && l.NumeroDocumento != null)
            .Select(l => l.NumeroDocumento!.Value)
            .ToHashSetAsync();

        return candidatos
            .Where(c => !yaLideresActivos.Contains(c.NumeroDocumento))
            .Select(c => new EmpleadoElegibleDto
            {
                NumeroDocumento = c.NumeroDocumento,
                NombreCompleto  = $"{c.Nombre} {c.Apellidos}".Trim(),
                Cargo           = c.Cargo,
                Email           = c.Email,
            })
            .OrderBy(e => e.NombreCompleto)
            .ToList();
    }

    public async Task<LiderDropdownDto> CrearLiderAsync(CrearLiderDto dto)
    {
        // 1. Validar el empleado contra DBRecursosHumanos
        var empleado = await _rrhh.Empleados
            .Include(e => e.Cargo)
            .FirstOrDefaultAsync(e => e.NumeroDocumento == dto.NumeroDocumento)
            ?? throw new InvalidOperationException(
                "No se encontró un empleado con esa cédula en Recursos Humanos.");

        if (!empleado.EstadoEmpleado)
            throw new InvalidOperationException("El empleado se encuentra inactivo en Recursos Humanos.");

        if (!CargoRoles.EsCargoLider(empleado.CargoID))
            throw new InvalidOperationException(
                $"El cargo '{empleado.Cargo.Descripcion}' no puede darse de alta como líder de cobranza.");

        var nombreCompleto = $"{empleado.Nombre} {empleado.Apellidos}".Trim();

        // 2. ¿Ya existe un líder con esa cédula?
        var liderExistente = await _context.Lideres
            .FirstOrDefaultAsync(l => l.NumeroDocumento == dto.NumeroDocumento);

        if (liderExistente is not null)
        {
            if (liderExistente.Activo)
                throw new InvalidOperationException($"'{nombreCompleto}' ya está registrado como líder.");

            // Reactivar el líder inactivo en vez de crear un duplicado; refrescar nombre/email.
            liderExistente.Activo          = true;
            liderExistente.Nombre          = nombreCompleto;
            liderExistente.Email           = empleado.Email;
            await _context.SaveChangesAsync();

            return new LiderDropdownDto { ID = liderExistente.ID, Nombre = liderExistente.Nombre };
        }

        // 3. Crear el líder. Lider.ID NO es IDENTITY → se asigna MAX(ID)+1 (igual que Cartera).
        var nextID = (await _context.Lideres.MaxAsync(l => (int?)l.ID) ?? 0) + 1;
        var nuevoLider = new Lider
        {
            ID              = nextID,
            Nombre          = nombreCompleto,
            Email           = empleado.Email,
            Activo          = true,
            NumeroDocumento = dto.NumeroDocumento,
        };
        _context.Lideres.Add(nuevoLider);
        await _context.SaveChangesAsync();

        return new LiderDropdownDto { ID = nuevoLider.ID, Nombre = nuevoLider.Nombre };
    }

    public async Task<CrearLiderExternoResponse> CrearLiderExternoAsync(CrearLiderExternoDto dto)
    {
        var nombre = (dto.Nombre ?? string.Empty).Trim();
        if (nombre.Length < 3)
            throw new InvalidOperationException("El nombre del líder externo es obligatorio (mínimo 3 caracteres).");

        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        // Unicidad por documento (solo si viene): no duplicar líder ni usuario.
        if (dto.NumeroDocumento.HasValue)
        {
            var doc = dto.NumeroDocumento.Value;
            if (await _context.Lideres.AnyAsync(l => l.NumeroDocumento == doc))
                throw new InvalidOperationException($"Ya existe un líder con el documento {doc}.");
            if (await _userManager.Users.AnyAsync(u => u.NumeroDocumento == doc))
                throw new InvalidOperationException($"Ya existe un usuario Zentinela con el documento {doc}.");
        }

        // 1. Crear el líder. ID manual (Lider.ID no es IDENTITY); NumeroDocumento nullable para externos.
        var nextID = (await _context.Lideres.MaxAsync(l => (int?)l.ID) ?? 0) + 1;
        var nuevoLider = new Lider
        {
            ID              = nextID,
            Nombre          = nombre,
            Email           = email,
            Activo          = true,
            NumeroDocumento = dto.NumeroDocumento,
        };
        _context.Lideres.Add(nuevoLider);
        await _context.SaveChangesAsync();

        // 2. Crear el usuario Zentinela con rol Líder Externo, vinculado a este líder.
        //    Líder y usuario viven en bases distintas (DBCallCenter / DBZentinela) — no hay
        //    transacción compartida, así que ante cualquier fallo se revierte manualmente lo creado.
        ApplicationUser? usuarioCreado = null;
        try
        {
            var (usuario, password, passwordEsDocumento) =
                await ConstruirCredencialesExternasAsync(nombre, dto.NumeroDocumento, nuevoLider.ID);

            var nuevoUsuario = new ApplicationUser
            {
                UserName               = usuario,
                Email                  = email ?? $"{usuario}@zentinela.local",
                EmailConfirmed         = true,
                NombreCompleto         = nombre,
                NumeroDocumento        = dto.NumeroDocumento ?? 0,
                RequiereCambioPassword = true,
                LiderID                = nuevoLider.ID,
            };

            var resultado = await _userManager.CreateAsync(nuevoUsuario, password);
            if (!resultado.Succeeded)
            {
                var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"No se pudo crear el usuario Zentinela: {errores}");
            }
            usuarioCreado = nuevoUsuario;

            var rolResultado = await _userManager.AddToRoleAsync(nuevoUsuario, Roles.LiderExterno);
            if (!rolResultado.Succeeded)
            {
                var errores = string.Join(", ", rolResultado.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"No se pudo asignar el rol Líder Externo: {errores}");
            }

            return new CrearLiderExternoResponse
            {
                LiderID          = nuevoLider.ID,
                Nombre           = nombre,
                Usuario          = usuario,
                // Con documento la contraseña es el documento (el admin ya lo conoce) → null.
                PasswordTemporal = passwordEsDocumento ? null : password,
            };
        }
        catch
        {
            // Limpiar en orden inverso: usuario Identity (si se creó) y luego el líder.
            if (usuarioCreado is not null)
                await _userManager.DeleteAsync(usuarioCreado);
            _context.Lideres.Remove(nuevoLider);
            await _context.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Construye usuario y contraseña inicial para un líder externo. Usuario:
    /// nombre.apellido + (últimos 3 del documento, o el LiderID si no hay documento),
    /// garantizando unicidad. Contraseña: el documento si viene (≥6 dígitos), o una temporal generada.
    /// </summary>
    private async Task<(string usuario, string password, bool passwordEsDocumento)> ConstruirCredencialesExternasAsync(
        string nombre, long? documento, int liderID)
    {
        var partes         = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var primerNombre   = partes.Length > 0 ? partes[0] : nombre;
        var primerApellido = partes.Length > 1 ? partes[^1] : "ext";

        var docStr = documento?.ToString();
        var sufijo = docStr is not null
            ? docStr[^Math.Min(3, docStr.Length)..]
            : liderID.ToString();

        var baseUsuario = SlugAscii($"{primerNombre}.{primerApellido}{sufijo}");
        if (string.IsNullOrWhiteSpace(baseUsuario))
            baseUsuario = $"lider.ext{liderID}";

        var usuario = baseUsuario;
        var intento = 1;
        while (await _userManager.FindByNameAsync(usuario) is not null)
            usuario = $"{baseUsuario}{++intento}";

        // Documento como contraseña solo si cumple la longitud mínima de la política (6).
        if (docStr is not null && docStr.Length >= 6)
            return (usuario, docStr, true);

        return (usuario, GenerarPasswordTemporal(), false);
    }

    /// <summary>Genera una contraseña temporal de 10 caracteres alfanuméricos (sin ambiguos).</summary>
    private static string GenerarPasswordTemporal()
    {
        const string alfabeto = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(10);
        var sb = new StringBuilder(10);
        foreach (var b in bytes)
            sb.Append(alfabeto[b % alfabeto.Length]);
        return sb.ToString();
    }

    /// <summary>
    /// Slug ASCII para username de Identity: quita diacríticos (ñ→n, á→a), pasa a minúsculas
    /// y descarta caracteres fuera del set permitido por defecto. Igual criterio que RegistroService.
    /// </summary>
    private static string SlugAscii(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-' || c == '@' || c == '+')
                sb.Append(c);
        }
        return sb.ToString().ToLowerInvariant();
    }

    public async Task<IEnumerable<CarteraDto>> BuscarCarterasAsync(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Enumerable.Empty<CarteraDto>();

        return await _context.Carteras
            .Where(c => c.NombreCartera.Contains(q))
            .OrderBy(c => c.NombreCartera)
            .Take(10)
            .Select(c => new CarteraDto { ID = c.ID, NombreCartera = c.NombreCartera })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<RelacionLiderCarteraDto> CrearRelacionAsync(CrearRelacionDto dto)
    {
        int carteraID;

        // Rama 1: el usuario seleccionó una cartera existente desde el autocompletado.
        // Solo se crea la relación Lider_Cartera; la cartera ya existe en BD.
        // Rama 2: el usuario escribió un nombre nuevo; se crea la Cartera primero
        // y luego la relación. Se valida unicidad de nombre para evitar duplicados.
        if (dto.CarteraID.HasValue)
        {
            // Usar cartera existente
            carteraID = dto.CarteraID.Value;
            var existe = await _context.Carteras.AnyAsync(c => c.ID == carteraID);
            if (!existe)
                throw new InvalidOperationException("La cartera seleccionada no existe.");
        }
        else if (!string.IsNullOrWhiteSpace(dto.NombreCartera))
        {
            // Crear nueva cartera — verificar que no exista ya con ese nombre
            var nombreDuplicado = await _context.Carteras
                .AnyAsync(c => c.NombreCartera == dto.NombreCartera);
            if (nombreDuplicado)
                throw new InvalidOperationException($"Ya existe una cartera con el nombre '{dto.NombreCartera}'.");

            var nextID = (await _context.Carteras.MaxAsync(c => (int?)c.ID) ?? 0) + 1;
            var nuevaCartera = new Cartera
            {
                ID            = nextID,
                NombreCartera = dto.NombreCartera.Trim(),
                Activa        = true,
            };
            _context.Carteras.Add(nuevaCartera);
            await _context.SaveChangesAsync();
            carteraID = nuevaCartera.ID;
        }
        else
        {
            throw new InvalidOperationException("Debe indicar una cartera existente o un nombre para la nueva cartera.");
        }

        // Verificar que la relación Lider_Cartera no exista ya
        var relacionExiste = await _context.LiderCarteras
            .AnyAsync(lc => lc.LiderID == dto.LiderID && lc.CarteraID == carteraID);
        if (relacionExiste)
            throw new InvalidOperationException("Esta relación ya existe.");

        var lider = await _context.Lideres.FindAsync(dto.LiderID)
            ?? throw new InvalidOperationException("El líder seleccionado no existe.");

        var cartera = await _context.Carteras.FindAsync(carteraID)
            ?? throw new InvalidOperationException("La cartera no existe.");

        // EsExterna se persiste en la relación (no en la cartera) — la misma cartera puede
        // tener relaciones internas y externas en paralelo según el líder que la trabaje.
        _context.LiderCarteras.Add(new LiderCartera
        {
            LiderID   = dto.LiderID,
            CarteraID = carteraID,
            EsExterna = dto.EsExterna,
        });
        await _context.SaveChangesAsync();

        return new RelacionLiderCarteraDto
        {
            LiderID           = dto.LiderID,
            CarteraID         = carteraID,
            NombreLider       = lider.Nombre,
            NombreCartera     = cartera.NombreCartera,
            EsExterna         = dto.EsExterna,
            TotalAsignaciones = 0,
        };
    }

    public async Task<byte[]> DescargarAsignacionAsync(int liderID, int carteraID)
    {
        var titulares = await _context.TitularCarteras
            .Where(tc => tc.LiderID == liderID && tc.CarteraID == carteraID)
            .Join(_context.Titulares,
                  tc => tc.TitularID,
                  t  => t.NumeroDocumento,
                  (_, t) => new { t.NumeroDocumento, t.Nombre })
            .OrderBy(x => x.Nombre)
            .AsNoTracking()
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Asignacion");

        // Encabezados en el formato exacto de la plantilla de cargue masivo
        // para que el archivo descargado pueda re-subirse directamente.
        ws.Cell(1, 1).Value = "NumeroDocumento";
        ws.Cell(1, 2).Value = "Nombre";

        var headerRange = ws.Range(1, 1, 1, 2);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
        headerRange.Style.Font.FontColor       = XLColor.White;

        for (int i = 0; i < titulares.Count; i++)
        {
            var r = i + 2;
            ws.Cell(r, 1).Value = titulares[i].NumeroDocumento;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(r, 2).Value = titulares[i].Nombre;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task EliminarRelacionAsync(int liderID, int carteraID)
    {
        var relacion = await _context.LiderCarteras
            .FirstOrDefaultAsync(lc => lc.LiderID == liderID && lc.CarteraID == carteraID)
            ?? throw new InvalidOperationException("La relación no existe.");

        // Borrado FÍSICO: solo permitido si NO hay histórico. Una relación con pagos,
        // metas, mensajería o IVR nunca se elimina (rompería la trazabilidad del recaudo
        // anclada a LiderID+CarteraID) — para esos casos existe InactivarRelacionAsync.
        // El listado de bloqueantes da un mensaje claro en vez de un error de FK. (ADR-022)
        var bloqueantes = await ObtenerBloqueantesAsync(liderID, carteraID);
        if (bloqueantes.Count > 0)
        {
            throw new InvalidOperationException(
                $"No se puede eliminar la relación porque tiene: {string.Join(", ", bloqueantes)}. " +
                "Inactívala para conservar el histórico, o reasigna/elimina esos registros primero.");
        }

        // Sin dependientes bloqueantes — eliminamos Titular_Cartera (FK no bloqueante,
        // siempre se limpia con la relación) y luego el Lider_Cartera.
        await _context.TitularCarteras
            .Where(tc => tc.LiderID == liderID && tc.CarteraID == carteraID)
            .ExecuteDeleteAsync();

        _context.LiderCarteras.Remove(relacion);
        await _context.SaveChangesAsync();
    }

    public async Task InactivarRelacionAsync(int liderID, int carteraID)
    {
        var relacion = await _context.LiderCarteras
            .FirstOrDefaultAsync(lc => lc.LiderID == liderID && lc.CarteraID == carteraID)
            ?? throw new InvalidOperationException("La relación no existe.");

        if (!relacion.Activo)
            throw new InvalidOperationException("La relación ya está inactiva.");

        // Soft-delete (ADR-022): se vacía Titular_Cartera (asignación OPERATIVA, no
        // histórica — el histórico vive en Pago/Meta/Mensajería/IVR y se conserva intacto)
        // y se marca Activo = false. Reversible vía ReactivarRelacionAsync.
        await _context.TitularCarteras
            .Where(tc => tc.LiderID == liderID && tc.CarteraID == carteraID)
            .ExecuteDeleteAsync();

        relacion.Activo = false;
        await _context.SaveChangesAsync();
    }

    public async Task ReactivarRelacionAsync(int liderID, int carteraID)
    {
        var relacion = await _context.LiderCarteras
            .FirstOrDefaultAsync(lc => lc.LiderID == liderID && lc.CarteraID == carteraID)
            ?? throw new InvalidOperationException("La relación no existe.");

        if (relacion.Activo)
            throw new InvalidOperationException("La relación ya está activa.");

        relacion.Activo = true;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Lista los dependientes que impiden el borrado FÍSICO de una relación (asesores,
    /// pagos, metas, solicitudes de virtualidad), con su conteo en texto.
    /// Vacío ⇒ se puede eliminar.
    /// </summary>
    private async Task<List<string>> ObtenerBloqueantesAsync(int liderID, int carteraID)
    {
        var asesores    = await _context.CarteraAgentes
                            .CountAsync(ca => ca.LiderID == liderID && ca.CarteraID == carteraID);
        var pagos       = await _context.Pagos
                            .CountAsync(p => p.LiderID == liderID && p.CarteraID == carteraID);
        var metas       = await _context.MetaLiderCarteraMes
                            .CountAsync(m => m.LiderID == liderID && m.CarteraID == carteraID);
        var virtualidad = await _context.HistoricoVirtualidad
                            .CountAsync(s => s.LiderID == liderID && s.CarteraID == carteraID);

        var bloqueantes = new List<string>();
        if (asesores    > 0) bloqueantes.Add($"{asesores} asesor(es) asignado(s)");
        if (pagos       > 0) bloqueantes.Add($"{pagos} pago(s) registrado(s)");
        if (metas       > 0) bloqueantes.Add($"{metas} meta(s) mensual(es)");
        if (virtualidad > 0) bloqueantes.Add($"{virtualidad} solicitud(es) de virtualidad (IVR/SMS/Correo)");
        return bloqueantes;
    }
}
