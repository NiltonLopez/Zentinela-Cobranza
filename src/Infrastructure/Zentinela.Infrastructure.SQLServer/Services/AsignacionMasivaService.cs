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
    // Los cargues masivos (DELETE + SqlBulkCopy) pueden superar el timeout por
    // defecto de 30 s con carteras grandes. Se eleva a 5 minutos, igual que en
    // CargueReporteService/CalificadorCarteraService.
    private const int TimeoutCargueSegundos = 300;

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

    public byte[] GenerarPlantillaCompleto() => _excelExporter.GenerarPlantillaAsignacionesCompleto();

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

    /// <summary>Construye una fila de validación en estado Error con su razón.</summary>
    private static FilaValidacionDto FilaError(long numDoc, string razon) => new()
    {
        NumeroDocumento = numDoc.ToString(),
        Estado          = EstadoValidacion.Error,
        Razon           = razon,
    };

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<PreviewAsignacionDto> ValidarExcelAsync(
        Stream excelStream, int carteraID, int liderID)
    {
        // Parsear Excel — fila 1 = encabezados, fila 2+ = datos
        var filasExcel = _excelExporter.ParsearFilasAsignacion(excelStream);

        if (filasExcel.Count == 0)
            return new PreviewAsignacionDto();

        var numerosDocumento = filasExcel.Select(f => f.NumeroDocumento).ToList();

        // Elevar el timeout: con carteras grandes (Titular_Cartera ~2.2M filas) estas
        // lecturas pueden superar el default de 30 s. Igual que en ConfirmarCargueAsync.
        _db.Database.SetCommandTimeout(TimeoutCargueSegundos);

        // Batch: titulares que ya existen en BD
        var existentesMap = await _db.Titulares
            .Where(t => numerosDocumento.Contains(t.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.NumeroDocumento, t => t.Nombre);

        // Batch: titulares ya asignados a esta cartera+líder. Se traen TODOS los
        // TitularID de la cartera+líder con un seek limpio sobre
        // IX_TitularCartera_Lider_Cartera y se filtran en memoria. Evita un IN/OPENJSON
        // con miles de parámetros contra una tabla de millones de filas — que generaba
        // un plan degenerado y era la causa real del timeout en la validación.
        var yaAsignados = await _db.TitularCarteras
            .Where(tc => tc.CarteraID == carteraID && tc.LiderID == liderID)
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
                // Incluir en validos para que el confirm los re-inserte tras el delete masivo.
                // Sin esta línea, el reemplazo los borraría al no estar en TitularIDsValidos.
                validos.Add(numDoc);
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

        // "Listos para asignar" (chip verde) = TODO lo que quedará asignado tras el
        // reemplazo: nuevos, ya-asignados (se conservan) y los Ok. Es decir, el universo
        // completo de `validos` — no solo los Ok. Así el conteo coincide con el botón
        // "Reemplazar con N" y con lo que realmente se sobrescribe. `totalNuevos` es un
        // sub-desglose informativo (de esos válidos, cuántos se crean en BD).
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
            TotalValidos      = validos.Count,
            TotalNuevos       = totalNuevos,
            TotalErrores      = totalError,
            TitularIDsValidos = validos,
            NuevosTitulares   = nuevosTitulares,
        };
    }

    // ── Validación modo "Cartera Completo" ────────────────────────────────────

    public async Task<PreviewAsignacionDto> ValidarExcelCompletoAsync(
        Stream excelStream, int carteraID)
    {
        // Parsear Excel — fila 1 = encabezados, fila 2+ = datos (col C = LiderID)
        var filasExcel = _excelExporter.ParsearFilasAsignacionCompleto(excelStream);

        if (filasExcel.Count == 0)
            return new PreviewAsignacionDto();

        var numerosDocumento = filasExcel.Select(f => f.NumeroDocumento).Distinct().ToList();

        _db.Database.SetCommandTimeout(TimeoutCargueSegundos);

        // Batch: titulares que ya existen en BD
        var existentesMap = await _db.Titulares
            .Where(t => numerosDocumento.Contains(t.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.NumeroDocumento, t => t.Nombre);

        // LiderIDs válidos = líderes con relación ACTIVA en esta cartera (Lider_Cartera).
        // Es contra qué se valida la columna LiderID de cada fila.
        var lideresValidos = await _db.LiderCarteras
            .Where(lc => lc.CarteraID == carteraID && lc.Activo)
            .Select(lc => lc.LiderID)
            .ToHashSetAsync();

        var todasLasFilas     = new List<FilaValidacionDto>(filasExcel.Count);
        var asignaciones      = new List<AsignacionValidaDto>();
        var titularIDsValidos = new List<long>();
        var nuevosTitulares   = new List<NuevoTitularDto>();
        var nuevosVistos      = new HashSet<long>();          // no crear el mismo titular 2 veces
        var vistos            = new HashSet<(long, long)>();  // dedup por par (documento, líder)

        foreach (var (numDoc, nombreExcel, liderIDNullable) in filasExcel)
        {
            // ── LiderID obligatorio ──────────────────────────────────────────
            if (liderIDNullable is null)
            {
                todasLasFilas.Add(FilaError(numDoc, "LiderID vacío o no numérico"));
                continue;
            }
            var liderIDLong = liderIDNullable.Value;

            // ── Duplicado en el archivo (mismo documento + mismo líder) ──────
            // El mismo titular puede aparecer con líderes distintos (la PK de
            // Titular_Cartera es TitularID+LiderID+CarteraID), pero no repetido
            // para el mismo líder.
            if (!vistos.Add((numDoc, liderIDLong)))
            {
                todasLasFilas.Add(FilaError(numDoc, "Duplicado en el archivo (mismo documento y líder)"));
                continue;
            }

            // ── Validación de cédula ─────────────────────────────────────────
            if (!CedulaValida(numDoc))
            {
                todasLasFilas.Add(FilaError(numDoc, "Cédula inválida (mínimo 6 dígitos)"));
                continue;
            }

            // ── LiderID debe caber en int y pertenecer a la cartera ──────────
            if (liderIDLong is < int.MinValue or > int.MaxValue
                || !lideresValidos.Contains((int)liderIDLong))
            {
                todasLasFilas.Add(FilaError(numDoc,
                    $"LiderID {liderIDLong} no pertenece a esta cartera o está inactivo"));
                continue;
            }
            var liderID = (int)liderIDLong;

            // ── ¿Existe el titular en BD? ────────────────────────────────────
            if (!existentesMap.TryGetValue(numDoc, out var nombreBD))
            {
                var nombreLimpio = LimpiarNombre(nombreExcel);
                if (!NombreValido(nombreLimpio))
                {
                    todasLasFilas.Add(FilaError(numDoc,
                        "No existe en BD y el nombre no cumple el formato (Nombre + Apellido)"));
                    continue;
                }

                // Un titular nuevo puede repartirse a varios líderes: se crea una
                // sola vez en Titular, pero genera una asignación por cada líder.
                if (nuevosVistos.Add(numDoc))
                    nuevosTitulares.Add(new NuevoTitularDto { NumeroDocumento = numDoc, Nombre = nombreLimpio });

                todasLasFilas.Add(new FilaValidacionDto
                {
                    NumeroDocumento = numDoc.ToString(),
                    Nombre          = nombreLimpio,
                    Estado          = EstadoValidacion.Nuevo,
                    Razon           = $"Se creará como titular nuevo — líder {liderID}",
                });
                asignaciones.Add(new AsignacionValidaDto { TitularID = numDoc, LiderID = liderID });
                titularIDsValidos.Add(numDoc);
                continue;
            }

            // ── Ok — existe en BD ────────────────────────────────────────────
            todasLasFilas.Add(new FilaValidacionDto
            {
                NumeroDocumento = numDoc.ToString(),
                Nombre          = nombreBD,
                Estado          = EstadoValidacion.Ok,
                Razon           = $"Líder {liderID}",
            });
            asignaciones.Add(new AsignacionValidaDto { TitularID = numDoc, LiderID = liderID });
            titularIDsValidos.Add(numDoc);
        }

        var totalError = todasLasFilas.Count(f => f.Estado == EstadoValidacion.Error);

        return new PreviewAsignacionDto
        {
            Filas               = todasLasFilas
                                      .Where(f => f.Estado != EstadoValidacion.Error)
                                      .Take(10)
                                      .ToList(),
            FilasConError       = todasLasFilas
                                      .Where(f => f.Estado == EstadoValidacion.Error)
                                      .ToList(),
            // En modo completo "TotalValidos" cuenta asignaciones (pares), que puede
            // superar el número de titulares distintos si uno se reparte a varios líderes.
            TotalValidos        = asignaciones.Count,
            TotalNuevos         = nuevosTitulares.Count,
            TotalErrores        = totalError,
            TitularIDsValidos   = titularIDsValidos,
            AsignacionesValidas = asignaciones,
            NuevosTitulares     = nuevosTitulares,
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

        // Elevar el timeout de EF Core para el DELETE masivo y el SaveChanges;
        // con carteras grandes el default de 30 s no alcanza.
        _db.Database.SetCommandTimeout(TimeoutCargueSegundos);

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
            await CrearTitularesNuevosAsync(nuevosTitulares);

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
                BulkCopyTimeout      = TimeoutCargueSegundos,
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

    // ── Confirmación modo "Cartera Completo" ──────────────────────────────────

    public async Task<ResultadoCargueDto> ConfirmarCargueCompletoAsync(
        int carteraID, List<AsignacionValidaDto> asignaciones, List<NuevoTitularDto> nuevosTitulares)
    {
        if (asignaciones.Count == 0)
            return new ResultadoCargueDto { Insertados = 0, Mensaje = "No hay asignaciones válidas para cargar." };

        // Deduplicar por par (TitularID, LiderID) — un mismo par no puede insertarse
        // dos veces (viola la PK de Titular_Cartera).
        var pares      = asignaciones
            .GroupBy(a => (a.TitularID, a.LiderID))
            .Select(g => g.First())
            .ToList();
        var duplicados = asignaciones.Count - pares.Count;

        _db.Database.SetCommandTimeout(TimeoutCargueSegundos);

        if (_db.Database.GetDbConnection().State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        var sqlConn = (SqlConnection)_db.Database.GetDbConnection();
        var sqlTx   = (SqlTransaction)tx.GetDbTransaction();

        try
        {
            // 0. Crear titulares nuevos que no existían en BD.
            await CrearTitularesNuevosAsync(nuevosTitulares);

            // 1. Reemplazar TODA la cartera: borrar asignaciones de todos los líderes.
            await _db.TitularCarteras
                .Where(tc => tc.CarteraID == carteraID)
                .ExecuteDeleteAsync();

            // 2. Bulk insert de los pares (TitularID, LiderID) con SqlBulkCopy.
            using var bulk = new SqlBulkCopy(sqlConn, SqlBulkCopyOptions.CheckConstraints, sqlTx)
            {
                DestinationTableName = "Titular_Cartera",
                BatchSize            = 10_000,
                BulkCopyTimeout      = TimeoutCargueSegundos,
            };
            bulk.ColumnMappings.Add("TitularID", "TitularID");
            bulk.ColumnMappings.Add("LiderID",   "LiderID");
            bulk.ColumnMappings.Add("CarteraID",  "CarteraID");

            var tabla = new DataTable();
            tabla.Columns.Add("TitularID", typeof(long));
            tabla.Columns.Add("LiderID",   typeof(int));
            tabla.Columns.Add("CarteraID",  typeof(int));
            foreach (var a in pares)
                tabla.Rows.Add(a.TitularID, a.LiderID, carteraID);

            await bulk.WriteToServerAsync(tabla);
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falló confirmación de asignación masiva COMPLETA (CarteraID={CarteraID}, {TotalPares} par(es), {NuevosTitulares} nuevo(s))",
                carteraID, pares.Count, nuevosTitulares.Count);
            await tx.RollbackAsync();
            throw;
        }

        var msg = duplicados > 0
            ? $"Se cargaron {pares.Count} asignación(es) en la cartera completa. {duplicados} duplicado(s) descartado(s)."
            : $"Se cargaron {pares.Count} asignación(es) en la cartera completa.";

        return new ResultadoCargueDto
        {
            Insertados = pares.Count,
            Duplicados = duplicados,
            Mensaje    = msg,
        };
    }

    // ── Helper compartido ─────────────────────────────────────────────────────

    /// <summary>
    /// Crea en Titular los titulares que aún no existan en BD. Debe invocarse
    /// dentro de la transacción del confirm para que sea atómico con el bulk insert.
    /// </summary>
    private async Task CrearTitularesNuevosAsync(List<NuevoTitularDto> nuevosTitulares)
    {
        if (nuevosTitulares.Count == 0) return;

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

}
