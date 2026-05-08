using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Zentinela.Application.DTOs.Asignaciones;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Gestiona el cargue masivo de asignaciones Titular→Cartera+Líder:
/// parse y validación del Excel, y confirmación mediante SqlBulkCopy + transacción.
/// </summary>
public class AsignacionMasivaService : IAsignacionMasivaService
{
    private readonly CallCenterDbContext _db;
    private readonly IExcelExporter     _excelExporter;
    private readonly ILogger<AsignacionMasivaService> _logger;

    public AsignacionMasivaService(
        CallCenterDbContext db,
        IExcelExporter excelExporter,
        ILogger<AsignacionMasivaService> logger)
    {
        _db            = db;
        _excelExporter = excelExporter;
        _logger        = logger;
    }

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla() => _excelExporter.GenerarPlantillaAsignaciones();

    // ── Helpers de validación y limpieza ──────────────────────────────────────

    // Elimina caracteres especiales del nombre; conserva letras, tildes, ñ y espacios.
    private static readonly Regex _caracteresInvalidos =
        new(@"[^a-zA-ZáéíóúÁÉÍÓÚüÜñÑ\s]", RegexOptions.Compiled);

    // Colapsa múltiples espacios consecutivos en uno solo.
    private static readonly Regex _espaciosMultiples =
        new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Limpia y normaliza un nombre: elimina caracteres especiales,
    /// colapsa espacios múltiples y recorta extremos.
    /// </summary>
    private static string LimpiarNombre(string nombre)
    {
        var limpio = _caracteresInvalidos.Replace(nombre, " ");
        return _espaciosMultiples.Replace(limpio, " ").Trim();
    }

    /// <summary>
    /// Valida que el nombre limpio tenga al menos dos palabras (Nombre + Apellido).
    /// </summary>
    private static bool NombreValido(string nombreLimpio) =>
        nombreLimpio.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;

    /// <summary>Cédula válida: mínimo 6 dígitos.</summary>
    private static bool CedulaValida(long numDoc) =>
        numDoc.ToString().Length >= 6;

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<PreviewAsignacionDto> ValidarExcelAsync(
        Stream excelStream, int carteraID, int liderID)
    {
        // Parsear Excel — fila 1 = encabezados, fila 2+ = datos
        var filasExcel = _excelExporter.ParsearFilasAsignacion(excelStream);

        if (filasExcel.Count == 0)
            return new PreviewAsignacionDto();

        var numerosDocumento = filasExcel.Select(f => f.NumeroDocumento).ToList();

        // Batch: titulares que ya existen en BD
        var existentesMap = await _db.Titulares
            .Where(t => numerosDocumento.Contains(t.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.NumeroDocumento, t => t.Nombre);

        // Batch: titulares ya asignados a esta cartera+líder
        var candidatos  = existentesMap.Keys.ToList();
        var yaAsignados = await _db.TitularCarteras
            .Where(tc => tc.CarteraID == carteraID
                      && tc.LiderID   == liderID
                      && candidatos.Contains(tc.TitularID))
            .Select(tc => tc.TitularID)
            .ToHashSetAsync();

        // Construir resultado fila a fila
        var todasLasFilas   = new List<FilaValidacionDto>(filasExcel.Count);
        var validos         = new List<long>();
        var nuevosTitulares = new List<NuevoTitularDto>();
        var vistos          = new HashSet<long>(); // detecta duplicados en el mismo archivo

        foreach (var (numDoc, nombreExcel) in filasExcel)
        {
            // ── Duplicado en el archivo ──────────────────────────────────────
            if (!vistos.Add(numDoc))
            {
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Estado          = EstadoValidacion.Error,
                    Razon           = "Duplicado en el archivo",
                });
                continue;
            }

            // ── Validación de cédula ─────────────────────────────────────────
            if (!CedulaValida(numDoc))
            {
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Estado          = EstadoValidacion.Error,
                    Razon           = "Cédula inválida (mínimo 6 dígitos)",
                });
                continue;
            }

            // ── ¿Existe en BD? ───────────────────────────────────────────────
            if (!existentesMap.TryGetValue(numDoc, out var nombreBD))
            {
                // No existe — validar nombre para poder crearlo
                var nombreLimpio = LimpiarNombre(nombreExcel);

                if (!NombreValido(nombreLimpio))
                {
                    todasLasFilas.Add(new FilaValidacionDto
                    {
                        NumeroDocumento = numDoc.ToString(),
                        Estado          = EstadoValidacion.Error,
                        Razon           = "No existe en BD y el nombre no cumple el formato (Nombre + Apellido)",
                    });
                    continue;
                }

                nuevosTitulares.Add(new NuevoTitularDto
                {
                    NumeroDocumento = numDoc,
                    Nombre          = nombreLimpio,
                });
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Nombre          = nombreLimpio,
                    Estado          = EstadoValidacion.Nuevo,
                    Razon           = "Se creará como titular nuevo",
                });
                validos.Add(numDoc);
                continue;
            }

            // ── Ya asignado ──────────────────────────────────────────────────
            if (yaAsignados.Contains(numDoc))
            {
                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Nombre          = nombreBD,
                    Estado          = EstadoValidacion.YaAsignado,
                    Razon           = "Ya asignado a esta cartera",
                });
                continue;
            }

            // ── Ok ───────────────────────────────────────────────────────────
            todasLasFilas.Add(new FilaValidacionDto
            {
                NumeroDocumento = numDoc.ToString(),
                Nombre          = nombreBD,
                Estado          = EstadoValidacion.Ok,
            });
            validos.Add(numDoc);
        }

        var totalOk     = todasLasFilas.Count(f => f.Estado == EstadoValidacion.Ok);
        var totalNuevos = nuevosTitulares.Count;
        var totalError  = todasLasFilas.Count(f => f.Estado == EstadoValidacion.Error);

        // Filas: vista previa de hasta 10 registros sin error (Ok/Nuevo/YaAsignado).
        // FilasConError: TODOS los errores con su razón para que el usuario los pueda corregir.
        return new PreviewAsignacionDto
        {
            Filas             = todasLasFilas
                                    .Where(f => f.Estado != EstadoValidacion.Error)
                                    .Take(10)
                                    .ToList(),
            FilasConError     = todasLasFilas
                                    .Where(f => f.Estado == EstadoValidacion.Error)
                                    .ToList(),
            TotalValidos      = totalOk,
            TotalNuevos       = totalNuevos,
            TotalErrores      = totalError,
            TitularIDsValidos = validos,
            NuevosTitulares   = nuevosTitulares,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<ResultadoCargueDto> ConfirmarCargueAsync(
        int carteraID, int liderID, List<long> titularIDs, List<NuevoTitularDto> nuevosTitulares)
    {
        if (titularIDs.Count == 0)
            return new ResultadoCargueDto { Insertados = 0, Mensaje = "No hay titulares válidos para asignar." };

        // Deduplicar por si el archivo traía el mismo ID más de una vez
        var idsUnicos   = titularIDs.Distinct().ToList();
        var duplicados  = titularIDs.Count - idsUnicos.Count;
        titularIDs      = idsUnicos;

        // Abrir conexión si EF Core aún no la ha abierto
        if (_db.Database.GetDbConnection().State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        var sqlConn = (SqlConnection)_db.Database.GetDbConnection();
        var sqlTx   = (SqlTransaction)tx.GetDbTransaction();

        try
        {
            // 0. Crear registros en Titular para los que no existían en BD.
            //    Se hace dentro de la misma transacción para que sea atómico con el resto.
            if (nuevosTitulares.Count > 0)
            {
                var nuevosDocs = nuevosTitulares.Select(n => n.NumeroDocumento).ToList();
                var yaExisten  = await _db.Titulares
                    .Where(t => nuevosDocs.Contains(t.NumeroDocumento))
                    .Select(t => t.NumeroDocumento)
                    .ToHashSetAsync();

                var porInsertar = nuevosTitulares
                    .Where(n => !yaExisten.Contains(n.NumeroDocumento))
                    .Select(n => new Zentinela.Core.Entities.Titular
                    {
                        NumeroDocumento = n.NumeroDocumento,
                        Nombre          = n.Nombre,
                    })
                    .ToList();

                if (porInsertar.Count > 0)
                {
                    _db.Titulares.AddRange(porInsertar);
                    await _db.SaveChangesAsync();
                }
            }

            // 1. Reemplazar: borrar asignaciones actuales de esta cartera+líder.
            await _db.TitularCarteras
                .Where(tc => tc.CarteraID == carteraID && tc.LiderID == liderID)
                .ExecuteDeleteAsync();

            // 2. Bulk insert de las nuevas asignaciones con SqlBulkCopy.
            //    Soporta hasta 1M de filas en segundos sin cargar el ORM.
            using var bulk = new SqlBulkCopy(sqlConn, SqlBulkCopyOptions.CheckConstraints, sqlTx)
            {
                DestinationTableName = "Titular_Cartera",
                BatchSize            = 10_000,
            };
            bulk.ColumnMappings.Add("TitularID", "TitularID");
            bulk.ColumnMappings.Add("LiderID",   "LiderID");
            bulk.ColumnMappings.Add("CarteraID",  "CarteraID");

            var tabla = new DataTable();
            tabla.Columns.Add("TitularID", typeof(long));
            tabla.Columns.Add("LiderID",   typeof(int));
            tabla.Columns.Add("CarteraID",  typeof(int));
            foreach (var id in titularIDs)
                tabla.Rows.Add(id, liderID, carteraID);

            await bulk.WriteToServerAsync(tabla);
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falló confirmación de asignación masiva (CarteraID={CarteraID}, LiderID={LiderID}, {TotalTitulares} titular(es), {NuevosTitulares} nuevo(s))",
                carteraID, liderID, titularIDs.Count, nuevosTitulares.Count);
            await tx.RollbackAsync();
            throw;
        }

        var msg = duplicados > 0
            ? $"Se asignaron {titularIDs.Count} titular(es). {duplicados} duplicado(s) descartado(s)."
            : $"Se asignaron {titularIDs.Count} titular(es) correctamente.";

        return new ResultadoCargueDto
        {
            Insertados = titularIDs.Count,
            Duplicados = duplicados,
            Mensaje    = msg,
        };
    }

}
