using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Core.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Repositories;

/// <summary>
/// Repositorio para vw_BaseMadre vía dbo.fn_BaseMadrePorCartera (iTVF).
///
/// IMPORTANTE: NO usamos composición LINQ (.Where(...)) encima de FromSql porque
/// EF Core wrappea como SELECT [t].* FROM (SELECT * FROM TVF(...)) AS [t] WHERE ...
/// y SQL Server pierde optimizaciones (batch mode / inline TVF) en ese envoltorio,
/// causando timeouts de 30s sobre carteras de tamaño moderado. En su lugar,
/// construimos el SQL completo con todos los filtros incluidos (incluso saldoMinimo,
/// rotacionMaxima, promesasVigentes y coincidencias — antes en C# memoria) y TOP en
/// SQL para preview.
/// </summary>
public class BaseMadreRepository : IBaseMadreRepository
{
    private readonly CallCenterDbContext _context;

    public BaseMadreRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    public async Task<(IEnumerable<BaseMadreRow> Filas, int Total)> ObtenerAsync(
        int          carteraID,
        int?         liderID,
        string?      ultimoPerfil,
        string?      mejorPerfil,
        bool?        promesasVigentes,
        int?         rotacionMaxima,
        decimal?     saldoMinimo,
        string?      tipoAfiliado,
        string?      estadoFosyga,
        List<string> fuentesCelular,
        List<string> coincidenciasCelular,
        List<string> fuentesEmail,
        List<string> coincidenciasEmail,
        int?         limite)
    {
        var conditions = new List<string>();
        var paramSpecs = new List<(string Name, object Value)>
        {
            ("@CarteraID", carteraID),
            ("@LiderID",   (object?)liderID ?? DBNull.Value),
        };

        void AddCondition(string condition, params (string Name, object Value)[] extraParams)
        {
            conditions.Add(condition);
            paramSpecs.AddRange(extraParams);
        }

        // ── Filtros simples por igualdad ─────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(ultimoPerfil))
            AddCondition("[Ultimo Perfil] = @UltimoPerfil", ("@UltimoPerfil", ultimoPerfil));

        if (!string.IsNullOrWhiteSpace(mejorPerfil))
            AddCondition("[Mejor Perfil] = @MejorPerfil",   ("@MejorPerfil", mejorPerfil));

        if (!string.IsNullOrWhiteSpace(tipoAfiliado))
            AddCondition("[Tipo Afiliado] = @TipoAfiliado", ("@TipoAfiliado", tipoAfiliado));

        if (!string.IsNullOrWhiteSpace(estadoFosyga))
            AddCondition("[Estado en Fosyga] = @EstadoFosyga", ("@EstadoFosyga", estadoFosyga));

        // ── Filtros numéricos / fecha (antes en C# memoria) ──────────────────
        if (saldoMinimo.HasValue)
            AddCondition("TRY_CAST([Saldo] AS decimal(18,2)) >= @SaldoMinimo",
                         ("@SaldoMinimo", saldoMinimo.Value));

        if (rotacionMaxima.HasValue)
            AddCondition("TRY_CAST([Rotacion] AS int) <= @RotacionMaxima",
                         ("@RotacionMaxima", rotacionMaxima.Value));

        if (promesasVigentes.HasValue)
        {
            // Vista guarda FechaPromesa en formato dd/MM/yyyy ("103" en CONVERT)
            conditions.Add(promesasVigentes.Value
                ? "TRY_CONVERT(date, [Fecha Promesa], 103) >= CAST(GETDATE() AS date)"
                : "(TRY_CONVERT(date, [Fecha Promesa], 103) IS NULL OR TRY_CONVERT(date, [Fecha Promesa], 103) < CAST(GETDATE() AS date))");
        }

        // ── Fuentes (al menos una requerida por cada checkbox) ───────────────
        // Whitelist de columnas: el switch impide inyección por nombre de fuente.
        foreach (var fuente in fuentesCelular)
        {
            var col = ColumnaCelular(fuente);
            if (col != null) conditions.Add($"{col} NOT IN ('','0')");
        }

        foreach (var fuente in fuentesEmail)
        {
            var col = ColumnaEmail(fuente);
            if (col != null) conditions.Add($"{col} NOT IN ('','0')");
        }

        // ── Coincidencias entre fuentes (mismo valor en N celulares / N emails) ─
        // C# verificaba: todos los valores no vacíos AND todos iguales. SQL: cada
        // columna NOT IN ('','0') AND igual a la primera.
        if (coincidenciasCelular.Count >= 2)
            AgregarCoincidencias(conditions, coincidenciasCelular, ColumnaCelular);

        if (coincidenciasEmail.Count >= 2)
            AgregarCoincidencias(conditions, coincidenciasEmail, ColumnaEmail);

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        // limite es int? validado upstream — interpolación segura (no es input string)
        var topClause = limite.HasValue ? $"TOP ({limite.Value})" : string.Empty;

        var countSql = $"""
            SELECT COUNT(*) AS Value
            FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
            {whereClause}
            """;

        var dataSql = $"""
            SELECT {topClause} *
            FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
            {whereClause}
            """;

        // SqlParameter no se puede reusar entre comandos — instanciamos por query.
        SqlParameter[] CreateParams() =>
            paramSpecs.Select(s => new SqlParameter(s.Name, s.Value)).ToArray();

        var total = await _context.Database
            .SqlQueryRaw<int>(countSql, CreateParams())
            .FirstOrDefaultAsync();

        var filas = await _context.BaseMadreRows
            .FromSqlRaw(dataSql, CreateParams())
            .AsNoTracking()
            .ToListAsync();

        return (filas, total);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whitelist de columnas de celular por fuente. Devuelve null si la fuente no
    /// está en la lista (silencioso, mantiene paridad con el switch original).
    /// </summary>
    private static string? ColumnaCelular(string fuente) => fuente switch
    {
        "Cajacopi"   => "[Celular Cajacopi]",
        "Camara"     => "[Celular Camara]",
        "SaludTotal" => "[Celular Salud Total]",
        "Positiva"   => "[Celular Positiva]",
        "Tigo"       => "[Celular Tigo]",
        "SOS"        => "[Celular SOS]",
        "Claro"      => "[Celular Claro]",
        // MutualSer expone 2 celulares como columnas independientes (no por prioridad);
        // ver docs/para-tener-en-cuenta.md (excepción a "1 fuente = 1 entrada").
        "MutualSer"  => "[Celular MutualSer]",
        "MutualSer2" => "[Celular 2 MutualSer]",
        _            => null,
    };

    private static string? ColumnaEmail(string fuente) => fuente switch
    {
        "Cajacopi"   => "[Email CajaCopi]",
        "Camara"     => "[Email Camara]",
        "SaludTotal" => "[Email Salud Total]",
        "Positiva"   => "[Email Positiva]",
        "Tigo"       => "[Email Tigo]",
        "Claro"      => "[Email Claro]",
        "MutualSer"  => "[Email MutualSer]",
        _            => null,
    };

    /// <summary>
    /// Agrega cláusulas AND para que las N fuentes elegidas tengan el mismo valor
    /// no vacío. C# original: <c>valores.Distinct().Count() == 1</c> tras filtrar
    /// vacíos/"0". Aquí: cada columna NOT IN ('','0') y todas iguales a la primera.
    /// </summary>
    private static void AgregarCoincidencias(
        List<string> conditions, List<string> fuentes, Func<string, string?> mapper)
    {
        var cols = fuentes.Select(mapper).Where(c => c is not null).Cast<string>().ToList();
        if (cols.Count < 2) return;

        var sb = new StringBuilder();
        sb.Append('(');
        sb.Append(cols[0]).Append(" NOT IN ('','0')");
        for (var i = 1; i < cols.Count; i++)
        {
            sb.Append(" AND ").Append(cols[i]).Append(" NOT IN ('','0')");
            sb.Append(" AND ").Append(cols[0]).Append(" = ").Append(cols[i]);
        }
        sb.Append(')');
        conditions.Add(sb.ToString());
    }
}
