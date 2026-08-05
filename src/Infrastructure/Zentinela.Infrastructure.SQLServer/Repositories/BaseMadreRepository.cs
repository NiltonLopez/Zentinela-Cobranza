using System.Data;
using System.Data.Common;
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

    // Carteras pesadas (P99 ~190k filas, admin sin líder ~395k) + cold cache pueden
    // demorar más que el default de 30s entre la TVF, los filtros y el TOP.
    private const int CommandTimeoutSegundos = 180;

    // Los exports recorren toda la cartera y pueden tardar minutos en carteras grandes,
    // sobre todo con el servidor bajo carga; se les da un timeout más holgado para que
    // un pico de contención no aborte la descarga a media transmisión.
    private const int ExportTimeoutSegundos = 600;

    public BaseMadreRepository(CallCenterDbContext context)
    {
        _context = context;
    }

    // ── ObtenerAsync (preview) ───────────────────────────────────────────────

    public async Task<(IEnumerable<BaseMadreRow> Filas, int Total, TipoTotalPreview TipoTotal)> ObtenerAsync(
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
        var (whereClause, paramSpecs) = ConstruirWhereYParams(
            carteraID, liderID, ultimoPerfil, mejorPerfil, promesasVigentes,
            rotacionMaxima, saldoMinimo, tipoAfiliado, estadoFosyga,
            fuentesCelular, coincidenciasCelular, fuentesEmail, coincidenciasEmail);

        var hayFiltros = !string.IsNullOrEmpty(whereClause);

        // SqlParameter no se puede reusar entre comandos — instanciamos por query.
        SqlParameter[] CreateParams() =>
            paramSpecs.Select(s => new SqlParameter(s.Name, s.Value)).ToArray();

        _context.Database.SetCommandTimeout(CommandTimeoutSegundos);

        // ── IMPORTANTE: NO se calcula un COUNT(*) exacto sobre la TVF ──────────────
        // Un COUNT sin TOP obliga a materializar TODOS los cruces de la base madre
        // (todas las fuentes EPS/celular/email por titular) solo para contar — no
        // corta temprano como el TOP. En carteras grandes (p.ej. 675k) tarda ~2 min
        // con MAXDOP 4 (CXSYNC_PORT), y bajo concurrencia satura el servidor: era la
        // causa de que la preview del Creador "nunca cargara". La query de datos con
        // TOP siempre fue rápida (~1s incluso en la cartera más grande).
        //
        // MAXDOP 4 (servidor de 8 núcleos): sin tope la query pide los 8 núcleos y
        // monopoliza el servidor. Se conserva en la query de datos.

        // Export (sin límite): traer todo; total = filas materializadas. Ruta legada:
        // el export real usa StreamFilasAsync, no este método.
        if (!limite.HasValue)
        {
            var sqlTodo = $"""
                SELECT *
                FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
                {whereClause}
                OPTION (MAXDOP 4)
                """;
            var todas = await _context.BaseMadreRows
                .FromSqlRaw(sqlTodo, CreateParams())
                .AsNoTracking()
                .ToListAsync();
            return (todas, todas.Count, TipoTotalPreview.Exacto);
        }

        // Preview: TOP(limite+1) para saber si hay MÁS de `limite` filas sin el COUNT exacto.
        var topMas  = limite.Value + 1;
        var dataSql = $"""
            SELECT TOP ({topMas}) *
            FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
            {whereClause}
            OPTION (MAXDOP 4)
            """;

        var filasList = await _context.BaseMadreRows
            .FromSqlRaw(dataSql, CreateParams())
            .AsNoTracking()
            .ToListAsync();

        var hayMas = filasList.Count > limite.Value;
        var filas  = hayMas ? filasList.Take(limite.Value).ToList() : filasList;

        // Caben todas en el preview → el total exacto es lo que trajimos.
        if (!hayMas)
            return (filas, filasList.Count, TipoTotalPreview.Exacto);

        // Hay más de `limite`. Sin filtros el total sale de Titular_Cartera (instantáneo por
        // índice), pero es APROXIMADO: la TVF puede devolver algo menos que Titular_Cartera
        // (p.ej. cartera 22: 651k TVF vs 675k Titular_Cartera). Con filtros el conteo exacto
        // exigiría materializar toda la TVF (lento) → se devuelve `limite` como cota inferior.
        if (!hayFiltros)
        {
            var totalAprox = await _context.TitularCarteras
                .Where(tc => tc.CarteraID == carteraID && (liderID == null || tc.LiderID == liderID))
                .CountAsync();
            return (filas, totalAprox, TipoTotalPreview.Aproximado);
        }

        return (filas, limite.Value, TipoTotalPreview.Minimo);
    }

    // ── StreamFilasAsync (export Excel/CSV) ──────────────────────────────────

    public async Task StreamFilasAsync(
        int                    carteraID,
        int?                   liderID,
        string?                ultimoPerfil,
        string?                mejorPerfil,
        bool?                  promesasVigentes,
        int?                   rotacionMaxima,
        decimal?               saldoMinimo,
        string?                tipoAfiliado,
        string?                estadoFosyga,
        List<string>           fuentesCelular,
        List<string>           coincidenciasCelular,
        List<string>           fuentesEmail,
        List<string>           coincidenciasEmail,
        IReadOnlyList<string>  propiedadesSeleccionadas,
        Func<DbDataReader, ValueTask> procesarFila,
        int?                   limite = null,
        CancellationToken      ct = default)
    {
        var (whereClause, paramSpecs) = ConstruirWhereYParams(
            carteraID, liderID, ultimoPerfil, mejorPerfil, promesasVigentes,
            rotacionMaxima, saldoMinimo, tipoAfiliado, estadoFosyga,
            fuentesCelular, coincidenciasCelular, fuentesEmail, coincidenciasEmail);

        // Proyección explícita: solo los campos pedidos. SqlAlias es una lista
        // espejo de ColumnasMadre — si alguien agrega una fuente al catálogo y
        // olvida el alias aquí, el SELECT tendría MENOS columnas que las que el
        // llamador (CSV/Excel) escribe como encabezado, y el reader reventaría
        // con IndexOutOfRange a media transmisión (cuerpo ya iniciado, status 200
        // → ERR_INCOMPLETE_CHUNKED_ENCODING). Por eso fallamos fuerte y temprano,
        // ANTES de abrir el reader, con un mensaje que apunta exactamente al gap.
        var sinAlias = propiedadesSeleccionadas
            .Where(p => !SqlAlias.ContainsKey(p))
            .ToList();
        if (sinAlias.Count > 0)
            throw new InvalidOperationException(
                $"Columnas sin alias SQL en BaseMadreRepository.SqlAlias: {string.Join(", ", sinAlias)}. " +
                "Agrega su mapeo (paralelo a ColumnasMadre / la TVF fn_BaseMadrePorCartera).");

        var aliasesSql = propiedadesSeleccionadas
            .Select(p => SqlAlias[p])
            .ToList();

        if (aliasesSql.Count == 0)
            aliasesSql.Add("[Numero Documento]");

        // limite (int? validado upstream) acota filas — lo usa el export a Excel para
        // no materializar carteras enormes (guard). CSV va sin límite (stream liviano).
        var topClause = limite.HasValue ? $"TOP ({limite.Value}) " : string.Empty;

        // MAXDOP 4: ver comentario en ObtenerAsync (mismo servidor de 8 núcleos,
        // misma TVF) — sin tope el export monopoliza el servidor completo.
        var sql = $"""
            SELECT {topClause}{string.Join(", ", aliasesSql)}
            FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
            {whereClause}
            OPTION (MAXDOP 4)
            """;

        var conn = _context.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText    = sql;
            cmd.CommandTimeout = ExportTimeoutSegundos;
            foreach (var (name, value) in paramSpecs)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                cmd.Parameters.Add(p);
            }

            // SequentialAccess permite leer column-by-column sin buffering — útil
            // cuando hay strings largos. No restringe lecturas si vamos en orden.
            await using var reader = await cmd.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess, ct);

            while (await reader.ReadAsync(ct))
                await procesarFila(reader);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    // ── Construcción de WHERE + parámetros (compartido) ──────────────────────

    private static (string WhereClause, List<(string Name, object Value)> Params)
        ConstruirWhereYParams(
            int carteraID, int? liderID,
            string? ultimoPerfil, string? mejorPerfil,
            bool? promesasVigentes, int? rotacionMaxima,
            decimal? saldoMinimo, string? tipoAfiliado, string? estadoFosyga,
            List<string> fuentesCelular, List<string> coincidenciasCelular,
            List<string> fuentesEmail, List<string> coincidenciasEmail)
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
        if (coincidenciasCelular.Count >= 2)
            AgregarCoincidencias(conditions, coincidenciasCelular, ColumnaCelular);

        if (coincidenciasEmail.Count >= 2)
            AgregarCoincidencias(conditions, coincidenciasEmail, ColumnaEmail);

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        return (whereClause, paramSpecs);
    }

    // ── Mapeo C# → alias SQL en la TVF (paralelo a BaseMadreRowMapping) ──────

    /// <summary>
    /// Mapea cada propiedad de <see cref="BaseMadreRow"/> al alias bracket-quoted
    /// que la TVF emite. Se usa para proyectar SELECT solo de las columnas que el
    /// usuario pidió (en lugar de SELECT *), reduciendo bytes-on-the-wire y la
    /// memoria que consumiría materializar las columnas no pedidas.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SqlAlias =
        new Dictionary<string, string>
        {
            ["NumeroDocumento"]            = "[Numero Documento]",
            ["Nombre"]                     = "[Nombre]",
            ["Saldo"]                      = "[Saldo]",
            ["UltimaFechaGestion"]         = "[Ultima Fecha Gestion]",
            ["FechaPromesa"]               = "[Fecha Promesa]",
            ["ValorPromesa"]               = "[Valor Promesa]",
            ["MejorPerfil"]                = "[Mejor Perfil]",
            ["UltimoPerfil"]               = "[Ultimo Perfil]",
            ["Rotacion"]                   = "[Rotacion]",
            ["UltimoNumeroMarcado"]        = "[Ultimo Numero Marcado en ControlNext]",
            ["EstadoEnFosyga"]             = "[Estado en Fosyga]",
            ["EPS"]                        = "[EPS]",
            ["Regimen"]                    = "[Regimen]",
            ["TipoAfiliado"]               = "[Tipo Afiliado]",
            ["Departamento"]               = "[Departamento]",
            ["Municipio"]                  = "[Municipio]",
            ["UltimaConsultaFosyga"]       = "[Ultima Consulta en Fosyga]",
            ["EPSEnCompensados"]           = "[EPS en Compensados]",
            ["UltimoPeriodoCompensado"]    = "[Ultimo Periodo Compensado]",
            ["TipoAfiliadoCompensados"]    = "[Tipo Afiliado Compensados]",
            ["UltimaConsultaCompensados"]  = "[Ultima Consulta en Compensados]",
            ["EdadRegistraduria"]          = "[Edad Registraduria]",
            ["EdadCajaCopi"]               = "[Edad CajaCopi]",
            ["EdadPositiva"]               = "[Edad Positiva]",
            ["CelularCajacopi"]            = "[Celular Cajacopi]",
            ["EmailCajacopi"]              = "[Email CajaCopi]",
            ["CelularCamara"]              = "[Celular Camara]",
            ["EmailCamara"]                = "[Email Camara]",
            ["CelularSaludTotal"]          = "[Celular Salud Total]",
            ["EmailSaludTotal"]            = "[Email Salud Total]",
            ["CelularPositiva"]            = "[Celular Positiva]",
            ["EmailPositiva"]              = "[Email Positiva]",
            ["CelularTigo"]                = "[Celular Tigo]",
            ["EmailTigo"]                  = "[Email Tigo]",
            ["CelularTigoUne"]             = "[Celular TigoUne]",
            ["EmailTigoUne"]               = "[Email TigoUne]",
            ["CelularCoosalud"]            = "[Celular Coosalud]",
            ["EmailCoosalud"]              = "[Email Coosalud]",
            ["RegimenCoosalud"]            = "[Regimen Coosalud]",
            ["TipoAfiliadoCoosalud"]       = "[Tipo Afiliado Coosalud]",
            ["EstadoAfiliacionCoosalud"]   = "[Estado Afiliacion Coosalud]",
            ["NivelSisbenCoosalud"]        = "[Nivel Sisben Coosalud]",
            ["DepartamentoCoosalud"]       = "[Departamento Coosalud]",
            ["MunicipioCoosalud"]          = "[Municipio Coosalud]",
            ["FechaNacimientoCoosalud"]    = "[Fecha Nacimiento Coosalud]",
            ["EstadoCoosalud"]             = "[Estado Coosalud]",
            ["UltimaConsultaCoosalud"]     = "[Ultima Consulta Coosalud]",
            ["CelularSaviaSalud"]          = "[Celular SaviaSalud]",
            ["RegimenSaviaSalud"]          = "[Regimen SaviaSalud]",
            ["TipoAfiliadoSaviaSalud"]     = "[Tipo Afiliado SaviaSalud]",
            ["EstadoAfiliacionSaviaSalud"] = "[Estado Afiliacion SaviaSalud]",
            ["FechaNacimientoSaviaSalud"]  = "[Fecha Nacimiento SaviaSalud]",
            ["EstadoSaviaSalud"]           = "[Estado SaviaSalud]",
            ["UltimaConsultaSaviaSalud"]   = "[Ultima Consulta SaviaSalud]",
            ["CelularSOS"]                 = "[Celular SOS]",
            ["CelularClaro"]               = "[Celular Claro]",
            ["EmailClaro"]                 = "[Email Claro]",
            ["UltimaInsercionClaro"]       = "[Ultima Insercion Claro]",
            ["CelularMiVacuna"]            = "[Celular MiVacuna]",
            ["EmailMiVacuna"]              = "[Email MiVacuna]",
            ["FechaExpedicionMiVacuna"]    = "[Fecha Expedicion MiVacuna]",
            ["EstadoMiVacuna"]             = "[Estado MiVacuna]",
            ["CelularMutualSer"]           = "[Celular MutualSer]",
            ["Celular2MutualSer"]          = "[Celular 2 MutualSer]",
            ["TelefonoFijoMutualSer"]      = "[Telefono Fijo MutualSer]",
            ["EmailMutualSer"]             = "[Email MutualSer]",
            ["RegimenMutualSer"]           = "[Regimen MutualSer]",
            ["TipoAfiliadoMutualSer"]      = "[Tipo Afiliado MutualSer]",
            ["DepartamentoMutualSer"]      = "[Departamento MutualSer]",
            ["MunicipioMutualSer"]         = "[Municipio MutualSer]",
            ["EstadoMutualSer"]            = "[Estado MutualSer]",
            ["UltimaConsultaMutualSer"]    = "[Ultima Consulta MutualSer]",
            ["CelularNuevaEps"]            = "[Celular NuevaEps]",
            ["EstadoAfiliacionNuevaEps"]   = "[Estado Afiliacion NuevaEps]",
            ["TipoAfiliadoNuevaEps"]       = "[Tipo Afiliado NuevaEps]",
            ["FechaNacimientoNuevaEps"]    = "[Fecha Nacimiento NuevaEps]",
            ["DepartamentoNuevaEps"]       = "[Departamento NuevaEps]",
            ["MunicipioNuevaEps"]          = "[Municipio NuevaEps]",
            ["EstadoNuevaEps"]             = "[Estado NuevaEps]",
            ["UltimaConsultaNuevaEps"]     = "[Ultima Consulta NuevaEps]",
            ["NITSura"]                    = "[NIT Sura]",
            ["EmpresaSura"]                = "[Empresa Sura]",
            ["NITPositiva"]                = "[NIT Positiva]",
            ["EmpresaPositiva"]            = "[Empresa Positiva]",
            ["NITSaludTotal"]              = "[NIT Salud Total]",
            ["EmpresaSaludTotal"]          = "[Empresa Salud Total]",
            ["NitSOS"]                     = "[Nit Sos]",
            ["EmpresaSOS"]                 = "[Empresa SOS]",
            ["EmpresaRUES"]                = "[Empresa RUES]",
            ["PlacaSIMIT"]                 = "[PLACA SIMIT]",
            ["Marca"]                      = "[Marca]",
            ["Modelo"]                     = "[Modelo]",
            ["Placa"]                      = "[Placa]",
        };

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
        "TigoUne"    => "[Celular TigoUne]",
        "Coosalud"   => "[Celular Coosalud]",
        "SaviaSalud" => "[Celular SaviaSalud]",
        "SOS"        => "[Celular SOS]",
        "Claro"      => "[Celular Claro]",
        "MiVacuna"   => "[Celular MiVacuna]",
        // MutualSer expone 2 celulares como columnas independientes (no por prioridad);
        // ver docs/para-tener-en-cuenta.md (excepción a "1 fuente = 1 entrada").
        "MutualSer"  => "[Celular MutualSer]",
        "MutualSer2" => "[Celular 2 MutualSer]",
        "NuevaEps"   => "[Celular NuevaEps]",
        _            => null,
    };

    private static string? ColumnaEmail(string fuente) => fuente switch
    {
        "Cajacopi"   => "[Email CajaCopi]",
        "Camara"     => "[Email Camara]",
        "SaludTotal" => "[Email Salud Total]",
        "Positiva"   => "[Email Positiva]",
        "Tigo"       => "[Email Tigo]",
        "TigoUne"    => "[Email TigoUne]",
        "Coosalud"   => "[Email Coosalud]",
        "Claro"      => "[Email Claro]",
        "MiVacuna"   => "[Email MiVacuna]",
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
