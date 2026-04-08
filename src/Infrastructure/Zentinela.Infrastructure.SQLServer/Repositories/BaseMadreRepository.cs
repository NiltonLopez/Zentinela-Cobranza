using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Repositories;

public class BaseMadreRepository : IBaseMadreRepository
{
    private readonly CallCenterDbContext _context;

    public BaseMadreRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    // ── Mapeo fuente → tabla + columna de teléfono celular ───────────────────
    // La vista almacena solo UNA fila por titular, pero las tablas fuente
    // pueden tener MÚLTIPLES filas (un registro por número). El filtro de
    // coincidencias debe consultar las tablas crudas para no perder titulares
    // que tienen el número coincidente en un registro distinto al expuesto
    // por la vista.
    private static readonly Dictionary<string, (string Tabla, string Columna)> _fuenteCelularTabla = new()
    {
        ["Cajacopi"]      = ("Cajacopi",      "NumeroTelefonoCelular"),
        ["Camara"]        = ("Camara",        "NumeroTelefono"),
        ["SaludTotal"]    = ("SaludTotal",    "NumeroTelefono"),
        ["DatosExternos"] = ("DatosExternos", "NumeroTelefono"),
        ["Positiva"]      = ("Positiva",      "NumeroTelefonoCelular"),
        ["Tigo"]          = ("Tigo",          "NumeroTelefono"),
        ["SOS"]           = ("SOS",           "Telefono"),
    };

    private static readonly Dictionary<string, (string Tabla, string Columna)> _fuenteEmailTabla = new()
    {
        ["Cajacopi"]      = ("Cajacopi",      "Email"),
        ["Camara"]        = ("Camara",        "Email"),
        ["SaludTotal"]    = ("SaludTotal",    "Email"),
        ["DatosExternos"] = ("DatosExternos", "Email"),
        ["Positiva"]      = ("Positiva",      "Email"),
        ["Tigo"]          = ("Tigo",          "Email"),
    };

    public async Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int          carteraID,
        int?         liderID,
        string?      ultimoPerfil,
        string?      mejorPerfil,
        bool?        tieneFechaPromesa,
        decimal?     saldoMinimo,
        string?      tipoAfiliado,
        string?      estadoFosyga,
        List<string> fuentesCelular,
        List<string> coincidenciasCelular,
        List<string> fuentesEmail,
        List<string> coincidenciasEmail,
        int?         limite)
    {
        // JOIN vw_BaseMadreBaseGeneral ⋈ Titular_Cartera para acotar por CarteraID
        var joined = _context.BaseMadreRows
            .Join(_context.TitularCarteras,
                  bm => bm.NumeroDocumento,
                  tc => tc.TitularID.ToString(),
                  (bm, tc) => new { bm, tc })
            .Where(x => x.tc.CarteraID == carteraID);

        // Líder: solo sus titulares. Admin/Director: liderID null → sin restricción
        if (liderID.HasValue)
            joined = joined.Where(x => x.tc.LiderID == liderID.Value);

        var query = joined.Select(x => x.bm).Distinct();

        // ── Filtros traducibles a SQL ─────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(ultimoPerfil))
            query = query.Where(b => b.UltimoPerfil == ultimoPerfil);

        if (!string.IsNullOrWhiteSpace(mejorPerfil))
            query = query.Where(b => b.MejorPerfil == mejorPerfil);

        if (tieneFechaPromesa == true)
            query = query.Where(b => b.FechaPromesa != null && b.FechaPromesa != "" && b.FechaPromesa != "0");

        if (!string.IsNullOrWhiteSpace(tipoAfiliado))
            query = query.Where(b => b.TipoAfiliado == tipoAfiliado);

        if (!string.IsNullOrWhiteSpace(estadoFosyga))
            query = query.Where(b => b.EstadoEnFosyga == estadoFosyga);

        // Fuentes de celular: titular debe tener número en TODAS las fuentes elegidas
        foreach (var fuente in fuentesCelular)
        {
            query = fuente switch
            {
                "Cajacopi"      => query.Where(b => b.CelularCajacopi      != null && b.CelularCajacopi      != "" && b.CelularCajacopi      != "0"),
                "Camara"        => query.Where(b => b.CelularCamara        != null && b.CelularCamara        != "" && b.CelularCamara        != "0"),
                "SaludTotal"    => query.Where(b => b.CelularSaludTotal    != null && b.CelularSaludTotal    != "" && b.CelularSaludTotal    != "0"),
                "DatosExternos" => query.Where(b => b.CelularDatosExternos != null && b.CelularDatosExternos != "" && b.CelularDatosExternos != "0"),
                "Positiva"      => query.Where(b => b.CelularPositiva      != null && b.CelularPositiva      != "" && b.CelularPositiva      != "0"),
                "Tigo"          => query.Where(b => b.CelularTigo          != null && b.CelularTigo          != "" && b.CelularTigo          != "0"),
                "SOS"           => query.Where(b => b.CelularSOS           != null && b.CelularSOS           != "" && b.CelularSOS           != "0"),
                _               => query,
            };
        }

        // Fuentes de email: titular debe tener email en TODAS las fuentes elegidas
        foreach (var fuente in fuentesEmail)
        {
            query = fuente switch
            {
                "Cajacopi"      => query.Where(b => b.EmailCajacopi      != null && b.EmailCajacopi      != "" && b.EmailCajacopi      != "0"),
                "Camara"        => query.Where(b => b.EmailCamara        != null && b.EmailCamara        != "" && b.EmailCamara        != "0"),
                "SaludTotal"    => query.Where(b => b.EmailSaludTotal    != null && b.EmailSaludTotal    != "" && b.EmailSaludTotal    != "0"),
                "DatosExternos" => query.Where(b => b.EmailDatosExternos != null && b.EmailDatosExternos != "" && b.EmailDatosExternos != "0"),
                "Positiva"      => query.Where(b => b.EmailPositiva      != null && b.EmailPositiva      != "" && b.EmailPositiva      != "0"),
                "Tigo"          => query.Where(b => b.EmailTigo          != null && b.EmailTigo          != "" && b.EmailTigo          != "0"),
                _               => query,
            };
        }

        // ── Coincidencias: consulta contra tablas crudas (no la vista) ────────
        // La vista expone un solo número por fuente, pero las tablas reales
        // tienen múltiples filas por titular. Usar la vista para comparar
        // coincidencias pierde titulares que tienen número coincidente en un
        // registro distinto al que la vista selecciona.
        // Se obtiene el conjunto de TitularIDs que cumplen la condición vía
        // INTERSECT en las tablas fuente, y se aplica como filtro IN en la
        // consulta principal antes de cargar datos.

        if (coincidenciasCelular.Count >= 2)
        {
            var ids = await ObtenerIDsConCoincidenciaAsync(
                carteraID, coincidenciasCelular, _fuenteCelularTabla);
            query = query.Where(b => ids.Contains(b.NumeroDocumento));
        }

        if (coincidenciasEmail.Count >= 2)
        {
            var idsEmail = await ObtenerIDsConCoincidenciaAsync(
                carteraID, coincidenciasEmail, _fuenteEmailTabla);
            query = query.Where(b => idsEmail.Contains(b.NumeroDocumento));
        }

        // ── Filtro en memoria: SaldoMinimo (Saldo es string en la vista) ──────
        var todasLasFilas = await query.AsNoTracking().ToListAsync();

        if (saldoMinimo.HasValue)
            todasLasFilas = todasLasFilas
                .Where(b => decimal.TryParse(
                    b.Saldo,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var s) && s >= saldoMinimo.Value)
                .ToList();

        var total = todasLasFilas.Count;
        var filas = limite.HasValue ? todasLasFilas.Take(limite.Value) : todasLasFilas;

        return (filas, total);
    }

    /// <summary>
    /// Retorna los TitularIDs de la cartera que tienen al menos un valor
    /// en común en TODAS las fuentes indicadas.
    /// Usa INTERSECT sobre las tablas crudas para capturar cualquier
    /// combinación de registros, no solo el que la vista agrega.
    /// </summary>
    private async Task<HashSet<string>> ObtenerIDsConCoincidenciaAsync(
        int carteraID,
        List<string> fuentes,
        Dictionary<string, (string Tabla, string Columna)> mapa)
    {
        var fuentesValidas = fuentes
            .Where(mapa.ContainsKey)
            .ToList();

        if (fuentesValidas.Count < 2)
            return new HashSet<string>();

        // Cada subquery del INTERSECT busca valores no nulos/vacíos de esa fuente
        // para el titular en evaluación (correlacionado por tc.TitularID).
        var subqueries = fuentesValidas.Select(f =>
        {
            var (tabla, col) = mapa[f];
            return $"SELECT [{col}] FROM [{tabla}]"
                 + $" WHERE TitularID = tc.TitularID"
                 + $" AND [{col}] IS NOT NULL"
                 + $" AND LTRIM(RTRIM([{col}])) NOT IN ('', '0')";
        });

        var intersectSql = string.Join("\n          INTERSECT\n          ", subqueries);

        // El EXISTS con INTERSECT devuelve verdadero si hay al menos un valor
        // que aparece en todas las fuentes simultáneamente para ese titular.
        var sql = $@"
SELECT DISTINCT tc.TitularID
FROM Titular_Cartera tc
WHERE tc.CarteraID = @carteraID
  AND EXISTS (
          {intersectSql}
      )";

        var connection = _context.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            var param = cmd.CreateParameter();
            param.ParameterName = "@carteraID";
            param.DbType        = DbType.Int32;
            param.Value         = carteraID;
            cmd.Parameters.Add(param);

            var ids = new HashSet<string>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetInt64(0).ToString());

            return ids;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }
}
