using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Application.DTOs.Virtualidad.Historico;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Grupos con conteo EXACTO y hasta <see cref="DesgloseCargue.MaxEjemplos"/> ejemplos cada uno.
/// Memoria acotada: como mucho <c>maxGrupos</c> grupos con nombre; lo que llegue después va
/// al grupo "Otros motivos" (su conteo sigue siendo exacto y el ejemplo lleva el motivo
/// original). Nunca guarda filas: con 1M de filas pesa lo mismo que con 100.
/// </summary>
internal sealed class AcumuladorGrupos
{
    public const string TituloOtros = "Otros motivos (demasiados distintos para listarlos uno por uno)";

    private sealed class Grupo(string titulo, TonoDesglose tono)
    {
        public string Titulo { get; } = titulo;
        public TonoDesglose Tono { get; } = tono;
        public int Cantidad;
        public readonly List<string> Ejemplos = [];
    }

    // Orden de llegada: los grupos de composición fija (Leídas, Nombres) conservan el suyo.
    private readonly Dictionary<string, Grupo> _grupos = new(StringComparer.Ordinal);
    private readonly List<Grupo> _orden = [];
    private readonly TonoDesglose _tono;
    private readonly int _maxGrupos;
    private Grupo? _otros;

    public AcumuladorGrupos(TonoDesglose tono, int maxGrupos = CatalogoCargueFuentes.MaxMensajesAgrupados)
    {
        _tono = tono;
        _maxGrupos = maxGrupos;
    }

    /// <summary>Suma de las cantidades de todos los grupos (incluido "Otros").</summary>
    public int Total { get; private set; }

    public int CantidadGrupos => _orden.Count + (_otros is null ? 0 : 1);

    /// <summary>
    /// Suma <paramref name="cantidad"/> al grupo y, si aún le caben ejemplos, arma uno con los
    /// datos de la fila. El texto del ejemplo solo se construye cuando hace falta.
    /// </summary>
    public void Sumar(string titulo, int cantidad = 1, int linea = 0, long? titularId = null,
                      string? valor = null, TonoDesglose? tono = null)
    {
        var g = Resolver(titulo, tono, out var desbordado);
        g.Cantidad += cantidad;
        Total += cantidad;
        if (g.Ejemplos.Count >= DesgloseCargue.MaxEjemplos || (linea <= 0 && titularId is null && valor is null && !desbordado))
            return;
        g.Ejemplos.Add(DesgloseCargue.Ejemplo(linea, titularId, desbordado ? Unir(titulo, valor) : valor));
    }

    /// <summary>Igual que <see cref="Sumar"/> pero con el ejemplo ya armado (o perezoso).</summary>
    public void SumarConEjemplo(string titulo, int cantidad, Func<string>? ejemplo, TonoDesglose? tono = null)
    {
        var g = Resolver(titulo, tono, out var desbordado);
        g.Cantidad += cantidad;
        Total += cantidad;
        if (ejemplo is null || g.Ejemplos.Count >= DesgloseCargue.MaxEjemplos) return;
        g.Ejemplos.Add(desbordado ? Unir(titulo, ejemplo()) : ejemplo());
    }

    /// <summary>Hasta <see cref="DesgloseCargue.MaxEjemplos"/> ejemplos, tomados en orden de los grupos.</summary>
    public List<string> EjemplosMezclados() =>
        Ordenados().SelectMany(g => g.Ejemplos).Take(DesgloseCargue.MaxEjemplos).ToList();

    /// <summary>Grupos para el desglose. <paramref name="porCantidad"/>: de mayor a menor (listas de motivos).</summary>
    public List<GrupoDesgloseDto> Grupos(bool porCantidad = true) =>
        (porCantidad ? Ordenados() : _orden.Concat(_otros is null ? [] : [_otros]))
            .Where(g => g.Cantidad != 0)
            .Select(g => new GrupoDesgloseDto { Titulo = g.Titulo, Cantidad = g.Cantidad, Tono = g.Tono, Ejemplos = [.. g.Ejemplos] })
            .ToList();

    /// <summary>Para las listas Errores / Advertencias del preview (sin ejemplos: van en el desglose).</summary>
    public List<ErrorAgrupadoDto> Agrupados() =>
        Ordenados().Select(g => new ErrorAgrupadoDto { Mensaje = g.Titulo, Cantidad = g.Cantidad }).ToList();

    private IEnumerable<Grupo> Ordenados()
    {
        var lista = _orden.OrderByDescending(g => g.Cantidad).ThenBy(g => g.Titulo, StringComparer.Ordinal);
        return _otros is null ? lista : lista.Append(_otros);
    }

    private Grupo Resolver(string titulo, TonoDesglose? tono, out bool desbordado)
    {
        desbordado = false;
        if (_grupos.TryGetValue(titulo, out var g)) return g;
        if (_orden.Count >= _maxGrupos)
        {
            desbordado = true;
            return _otros ??= new Grupo(TituloOtros, _tono);
        }
        g = new Grupo(titulo, tono ?? _tono);
        _grupos.Add(titulo, g);
        _orden.Add(g);
        return g;
    }

    private static string Unir(string titulo, string? valor) => valor is null ? titulo : $"{titulo}: {valor}";
}

/// <summary>
/// Desglose de los chips de conteo del Cargue de Fuentes (qué compone cada número, con
/// ejemplos). Uno por corrida del motor, colgado de <see cref="ContextoCargue.Desglose"/>.
///
/// El motor solo lo ALIMENTA con llamadas puntuales (una línea en cada punto donde decide
/// algo de una fila); toda la lógica vive aquí. Dos orígenes:
/// <list type="bullet">
/// <item>C#, mientras recorre las filas: excluidas, advertencias, duplicadas, expansión,
///   titulares nuevos, nombres, filas sin relación.</item>
/// <item>SQL, al final de los cruces (<see cref="ConsultarBdAsync"/>): <c>TOP (10)</c> por
///   grupo sobre #Carga / #Relaciones con las marcas Refrescada / Vacia ya calculadas.</item>
/// </list>
/// Invariante: la suma de los grupos de cada chip es el número del chip
/// (<see cref="NumeroDelChip(PreviewCargueDto, ChipCargue)"/>). En el preview se verifica y
/// lanza; en el resultado se concilia con los conteos finales de la escritura.
/// </summary>
internal sealed class DesgloseCargue
{
    /// <summary>Ejemplos por grupo.</summary>
    public const int MaxEjemplos = 10;

    public const string TituloSinColumnasRelacion = "El archivo no trae cartera ni líder para la fila";
    public const string TituloTitularRepetido = "Titular repetido (se conservó la fila más completa)";
    public const string TituloFilaIdentica = "Fila idéntica a otra del archivo";
    public const string TituloContenidoDistinto = "Contenido distinto al vigente";
    public const string TituloContenidoIdentico = "Contenido idéntico al vigente (solo se actualiza la fecha)";
    public const string TituloReparado = "Reparado: estaba roto en Titular y el archivo trae el nombre limpio";
    public const string TituloIrrecuperable = "Irrecuperable (con '?'): entra tal cual y no repara Titular";
    public const string TituloAjuste = "Diferencia entre la preparación y la escritura (la base cambió entre ambas)";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly DefinicionFuenteCargue _def;

    public DesgloseCargue(DefinicionFuenteCargue def)
    {
        _def = def;
        // Composición fija: el orden de los grupos no depende de cuál llegue primero (los
        // grupos en 0 no salen en el desglose).
        _insertadas.Sumar(TituloSinDatosPrevios, 0);
        _insertadas.Sumar(TituloContenidoDistinto, 0);
        _nombres.Sumar(TituloReparado, 0, tono: TonoDesglose.Info);
        _nombres.Sumar(TituloIrrecuperable, 0, tono: TonoDesglose.Aviso);
    }

    // ── Acumuladores ────────────────────────────────────────────────────────
    // Excluidas / Advertencias son los mismos que usa ContextoCargue para Errores() / Advertencias().

    public AcumuladorGrupos Excluidas { get; } = new(TonoDesglose.Error);
    public AcumuladorGrupos Advertencias { get; } = new(TonoDesglose.Aviso);
    public AcumuladorGrupos Duplicadas { get; } = new(TonoDesglose.Aviso);
    public AcumuladorGrupos SinCarteraLider { get; } = new(TonoDesglose.Aviso);
    private readonly AcumuladorGrupos _candidatas = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _expansion = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _titularesNuevos = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _nombres = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _insertadas = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _refrescadas = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _vacias = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _purga = new(TonoDesglose.Info);
    private readonly AcumuladorGrupos _relaciones = new(TonoDesglose.Info);

    public const string TituloExpansion = "Filas destino agregadas al expandir (p. ej. una por placa): se restan";
    private string TituloSinDatosPrevios => $"Titular sin datos previos en {_def.Etiqueta}";
    private string TituloVacia => $"Fila sin datos de un titular que sí tiene datos (en {_def.Etiqueta} o en otra fila del archivo)";
    private string TituloPurga => $"Fila vacía vieja en {_def.Etiqueta} de un titular que ahora trae datos";
    private const string TituloTitularNuevo = "No existía en Titular: se crea con el nombre del archivo";

    // ── Formato de ejemplos ─────────────────────────────────────────────────

    /// <summary>"Fila 12 · 1234567 · valor" (omite lo que no haya). El valor se recorta a 80.</summary>
    public static string Ejemplo(int linea, long? titularId, string? valor)
    {
        var sb = new StringBuilder();
        if (linea > 0) sb.Append("Fila ").Append(linea.ToString(Inv));
        if (titularId is not null) (sb.Length > 0 ? sb.Append(" · ") : sb).Append(titularId.Value.ToString(Inv));
        if (valor is not null) (sb.Length > 0 ? sb.Append(" · ") : sb).Append(Recortar(valor));
        return sb.ToString();
    }

    /// <summary>Valor de celda para un ejemplo: comillas si trae espacios en los bordes, "(vacío)" si no hay.</summary>
    public static string Valor(string? bruto) =>
        string.IsNullOrWhiteSpace(bruto) ? "(vacío)" : Recortar(bruto.Trim());

    private static string Recortar(string s) => s.Length <= 80 ? s : s[..79] + "…";

    // ── Alimentación desde el motor (C#) ────────────────────────────────────

    /// <summary>Una fila del archivo produjo <paramref name="filasDestino"/> filas (SIMIT: una por placa).</summary>
    public void Expansion(FilaCargue fila, int filasDestino)
    {
        if (filasDestino <= 1) return;
        _expansion.Sumar(TituloExpansion, filasDestino - 1, fila.Linea, fila.TitularID,
            $"{filasDestino.ToString(Inv)} filas destino");
    }

    /// <summary>UnaPorTitular: el titular vino en <paramref name="filas"/> filas y se conservó una.</summary>
    public void TitularRepetido(long titularId, int filas, int lineaConservada) =>
        Duplicadas.SumarConEjemplo(TituloTitularRepetido, filas - 1,
            () => $"{titularId.ToString(Inv)} × {filas.ToString(Inv)} filas (se conservó la fila {lineaConservada.ToString(Inv)})");

    /// <summary>VariasPorTitular: fila exactamente igual (titular + clave) a otra ya vista.</summary>
    public void FilaIdentica(FilaCargue fila, Func<string> clave) =>
        Duplicadas.SumarConEjemplo(TituloFilaIdentica, 1, () => Ejemplo(fila.Linea, fila.TitularID, clave()));

    /// <summary>Las candidatas finales (tras cruces): se cuentan y se guardan las primeras como ejemplo.</summary>
    public void Candidatas(IReadOnlyCollection<FilaCargue> candidatas)
    {
        _candidatas.Sumar(TituloCandidatas, candidatas.Count);
        foreach (var f in candidatas.Take(MaxEjemplos))
            _candidatas.Sumar(TituloCandidatas, 0, f.Linea, f.TitularID);
    }

    private string TituloCandidatas => $"Candidatas: pasan a compararse con {_def.Etiqueta}";

    public void TitularNuevo(long titularId, string? nombre) =>
        _titularesNuevos.Sumar(TituloTitularNuevo, 1, 0, titularId, nombre);

    public void NombreReparado(long titularId, string? antes, string? despues) =>
        _nombres.Sumar(TituloReparado, 1, 0, titularId, $"{antes ?? "(vacío)"} → {despues ?? "(vacío)"}", TonoDesglose.Info);

    /// <summary>Filas con nombre irrecuperable entre <paramref name="filas"/> (mismo conteo que NombresIrrecuperables).</summary>
    public void Irrecuperables(IEnumerable<FilaCargue> filas)
    {
        foreach (var f in filas)
            if (f.NombreIrrecuperable)
                _nombres.Sumar(TituloIrrecuperable, 1, f.Linea, f.TitularID, f.Nombre, TonoDesglose.Aviso);
    }

    /// <summary>Candidatas cuyo titular quedó sin ninguna relación, por el motivo de su fila.</summary>
    public void SinRelacion(IEnumerable<FilaCargue> filas)
    {
        foreach (var f in filas)
        {
            if (f.MotivoSinRelacion is { } motivo)
                SinCarteraLider.Sumar(motivo, 1, f.Linea, f.TitularID);
            else
                SinCarteraLider.Sumar(TituloSinColumnasRelacion, 1, f.Linea, f.TitularID, tono: TonoDesglose.Info);
        }
    }

    // ── Alimentación desde la BD (fin de la fase de cruces) ─────────────────

    /// <summary>
    /// Columnas de dato que identifican una fila en los ejemplos de "se insertarán": primero
    /// las de contacto (celular, email, placa…), si no, las de la clave de contenido. Máx. 2.
    /// </summary>
    internal List<ColumnaFuenteCargue> ColumnasDeEjemplo()
    {
        var contacto = _def.ColumnasDato
            .Where(c => c.Tipo is TipoColumnaCargue.Celular or TipoColumnaCargue.Email or TipoColumnaCargue.Placa
                                or TipoColumnaCargue.Telefono or TipoColumnaCargue.Fijo)
            .Take(2).ToList();
        return contacto.Count > 0 ? contacto : _def.ColumnasDato.Where(c => c.EntraAClave).Take(2).ToList();
    }

    /// <summary>
    /// SQL de los ejemplos que dependen de la BD. Todo sobre las #temp de la corrida y las
    /// marcas ya calculadas; contra el destino solo hay UNA pasada de EXISTS por titular sobre
    /// las filas a insertar (para separar "sin datos previos" de "contenido distinto", que las
    /// marcas no distinguen), materializada en #DesgloseInsertar para no repetirla. La purga
    /// (TOP 10 en orden de la PK de #Carga) solo corre si hay algo que purgar.
    /// Result sets, en orden: conteos de insertar, ejemplos de insertar, refrescadas, vacías,
    /// [purga], [conteos de relaciones, ejemplos de relaciones].
    /// </summary>
    internal string SqlBd(bool conPurga, bool conRelaciones)
    {
        var d = _def;
        var destino = GeneradorSqlCargue.Tabla(d.TablaDestino);
        var cols = ColumnasDeEjemplo();
        string Col(int i) => i < cols.Count
            ? (cols[i].Tipo == TipoColumnaCargue.Fecha
                ? $"CONVERT(nvarchar(100), c.{GeneradorSqlCargue.Id(cols[i].Nombre)}, 23)"
                : $"CONVERT(nvarchar(100), c.{GeneradorSqlCargue.Id(cols[i].Nombre)})")
            : "CAST(NULL AS nvarchar(100))";

        var sb = new StringBuilder();
        sb.AppendLine($"""
            DROP TABLE IF EXISTS #DesgloseInsertar;
            SELECT c.TitularID, c.Fila,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM {destino} s WHERE {GeneradorSqlCargue.MismoTitular(d, "s", "c")})
                             THEN 1 ELSE 0 END AS bit) AS Previo
              INTO #DesgloseInsertar
              FROM #Carga c
             WHERE c.Refrescada = 0 AND c.Vacia = 0;
            SELECT Previo, COUNT(*) FROM #DesgloseInsertar GROUP BY Previo;
            SELECT x.Previo, c.TitularID, {Col(0)} AS K1, {Col(1)} AS K2
              FROM (SELECT * FROM (SELECT TOP ({MaxEjemplos}) i.Previo, i.TitularID, i.Fila FROM #DesgloseInsertar i
                                    WHERE i.Previo = 0 ORDER BY i.Fila) a
                    UNION ALL
                    SELECT * FROM (SELECT TOP ({MaxEjemplos}) i.Previo, i.TitularID, i.Fila FROM #DesgloseInsertar i
                                    WHERE i.Previo = 1 ORDER BY i.Fila) b) x
              JOIN #Carga c ON c.TitularID = x.TitularID AND c.Fila = x.Fila
             ORDER BY x.Previo, x.Fila;
            DROP TABLE #DesgloseInsertar;
            SELECT TOP ({MaxEjemplos}) c.TitularID FROM #Carga c WHERE c.Refrescada = 1 AND c.Vacia = 0 ORDER BY c.Fila;
            SELECT TOP ({MaxEjemplos}) c.TitularID FROM #Carga c WHERE c.Vacia = 1 ORDER BY c.Fila;
            """);
        if (conPurga)
        {
            var flag = GeneradorSqlCargue.Id(d.ColumnaFlag!);
            sb.AppendLine($"""
                SELECT DISTINCT TOP ({MaxEjemplos}) c.TitularID
                  FROM #Carga c
                 WHERE c.{flag} = 1
                   AND EXISTS (SELECT 1 FROM {destino} s WHERE {GeneradorSqlCargue.MismoTitular(d, "s", "c")} AND s.{flag} = 0)
                 ORDER BY c.TitularID;
                """);
        }
        if (conRelaciones)
        {
            // PK (Cartera, Líder, Titular): el conteo y el ROW_NUMBER van en orden de índice, sin sort.
            sb.AppendLine($"""
                DROP TABLE IF EXISTS #DesgloseRelaciones;
                CREATE TABLE #DesgloseRelaciones (
                    CarteraID int NOT NULL, LiderID int NOT NULL, TitularID bigint NOT NULL,
                    PRIMARY KEY CLUSTERED (CarteraID, LiderID, TitularID));
                INSERT INTO #DesgloseRelaciones (CarteraID, LiderID, TitularID)
                SELECT r.CarteraID, r.LiderID, r.TitularID
                {GeneradorSqlCargue.OrigenRelacionesNuevas};
                SELECT g.CarteraID, g.LiderID, g.Cantidad,
                       ISNULL(ca.NombreCartera, CONCAT('Cartera ', g.CarteraID)) AS Cartera,
                       ISNULL(l.Nombre, CONCAT('Líder ', g.LiderID)) AS Lider
                  FROM (SELECT CarteraID, LiderID, COUNT(*) AS Cantidad FROM #DesgloseRelaciones GROUP BY CarteraID, LiderID) g
                  LEFT JOIN dbo.Cartera ca ON ca.ID = g.CarteraID
                  LEFT JOIN dbo.Lider l ON l.ID = g.LiderID;
                SELECT x.CarteraID, x.LiderID, x.TitularID
                  FROM (SELECT CarteraID, LiderID, TitularID,
                               ROW_NUMBER() OVER (PARTITION BY CarteraID, LiderID ORDER BY TitularID) AS rn
                          FROM #DesgloseRelaciones) x
                 WHERE x.rn <= {MaxEjemplos};
                DROP TABLE #DesgloseRelaciones;
                """);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Ejemplos y grupos que dependen de la BD, sobre la conexión del cargue (donde viven
    /// #Carga y #Relaciones). Se llama al final de los cruces, con los conteos ya leídos.
    /// </summary>
    public async Task ConsultarBdAsync(
        SqlConnection conn, int refrescadas, int descartadasVacias, int vaciasQuePurgara, int relacionesNuevas,
        int timeoutSegundos, CancellationToken ct)
    {
        var conPurga = vaciasQuePurgara > 0 && _def.ColumnaFlag is not null;
        var conRelaciones = _def.CreaTitularCartera && relacionesNuevas > 0;

        await using var cmd = new SqlCommand(SqlBd(conPurga, conRelaciones), conn) { CommandTimeout = timeoutSegundos };
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        // 1. Conteos de "se insertarán" por grupo.
        var conteoInsertar = new Dictionary<bool, int>();
        while (await rd.ReadAsync(ct)) conteoInsertar[rd.GetBoolean(0)] = rd.GetInt32(1);
        string TituloInsertar(bool previo) => previo ? TituloContenidoDistinto : TituloSinDatosPrevios;
        // Los conteos entran primero (exactos); los ejemplos después, con cantidad 0.
        foreach (var previo in new[] { false, true })
            if (conteoInsertar.TryGetValue(previo, out var n))
                _insertadas.Sumar(TituloInsertar(previo), n);

        // 2. Ejemplos de "se insertarán": documento + hasta 2 datos clave.
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var datos = string.Join(" · ", new[] { rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3) }
                .Where(v => v is not null));
            _insertadas.Sumar(TituloInsertar(rd.GetBoolean(0)), 0, 0, rd.GetInt64(1), datos.Length > 0 ? datos : null);
        }

        // 3–5. Grupo único con el conteo que ya calculó el motor; aquí solo los ejemplos.
        _refrescadas.Sumar(TituloContenidoIdentico, refrescadas);
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct)) _refrescadas.Sumar(TituloContenidoIdentico, 0, 0, rd.GetInt64(0));

        _vacias.Sumar(TituloVacia, descartadasVacias);
        await rd.NextResultAsync(ct);
        while (await rd.ReadAsync(ct)) _vacias.Sumar(TituloVacia, 0, 0, rd.GetInt64(0));

        _purga.Sumar(TituloPurga, vaciasQuePurgara);
        if (conPurga)
        {
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct)) _purga.Sumar(TituloPurga, 0, 0, rd.GetInt64(0));
        }

        // 6. Relaciones nuevas por "Cartera — Líder".
        if (conRelaciones)
        {
            var titulos = new Dictionary<(int, int), string>();
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var par = (rd.GetInt32(0), rd.GetInt32(1));
                titulos[par] = $"{rd.GetString(3)} — {rd.GetString(4)}";
                _relaciones.Sumar(titulos[par], rd.GetInt32(2));
            }
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct))
                if (titulos.TryGetValue((rd.GetInt32(0), rd.GetInt32(1)), out var titulo))
                    _relaciones.Sumar(titulo, 0, 0, rd.GetInt64(2));
        }
    }

    // ── Armado de los chips ─────────────────────────────────────────────────

    /// <summary>Número que muestra cada chip del preview. "Nombres" = reparados + irrecuperables.</summary>
    public static int NumeroDelChip(PreviewCargueDto p, ChipCargue chip) => chip switch
    {
        ChipCargue.Leidas            => p.TotalLeidas,
        ChipCargue.Excluidas         => p.Excluidas,
        ChipCargue.Duplicadas        => p.DuplicadasEnArchivo,
        ChipCargue.Insertadas        => p.Insertadas,
        ChipCargue.Refrescadas       => p.Refrescadas,
        ChipCargue.VaciasDescartadas => p.DescartadasVacias,
        ChipCargue.VaciasPurgadas    => p.VaciasQuePurgara,
        ChipCargue.TitularesNuevos   => p.TitularesNuevos,
        ChipCargue.NombresReparados  => p.NombresReparados + p.NombresIrrecuperables,
        ChipCargue.RelacionesNuevas  => p.RelacionesNuevas,
        ChipCargue.SinCarteraLider   => p.FilasSinCarteraLider,
        ChipCargue.Advertencias      => p.Advertencias.Sum(a => a.Cantidad),
        _ => throw new ArgumentOutOfRangeException(nameof(chip), chip, null),
    };

    /// <summary>Chips de la tarjeta de resultado.</summary>
    public static readonly ChipCargue[] ChipsResultado =
    [
        ChipCargue.Insertadas, ChipCargue.Refrescadas, ChipCargue.VaciasDescartadas, ChipCargue.VaciasPurgadas,
        ChipCargue.TitularesNuevos, ChipCargue.NombresReparados, ChipCargue.RelacionesNuevas, ChipCargue.Advertencias,
    ];

    /// <summary>Número que muestra cada métrica del resultado (los conteos FINALES de la escritura).</summary>
    public static int NumeroDelChip(ResultadoCargueDto r, ChipCargue chip) => chip switch
    {
        ChipCargue.Insertadas        => r.Insertadas,
        ChipCargue.Refrescadas       => r.Refrescadas,
        ChipCargue.VaciasDescartadas => r.DescartadasVacias,
        ChipCargue.VaciasPurgadas    => r.VaciasPurgadas,
        ChipCargue.TitularesNuevos   => r.TitularesNuevos,
        ChipCargue.NombresReparados  => r.NombresReparados + r.NombresIrrecuperables,
        ChipCargue.RelacionesNuevas  => r.RelacionesNuevas,
        ChipCargue.Advertencias      => r.Advertencias,
        _ => throw new ArgumentOutOfRangeException(nameof(chip), chip, "No es una métrica del resultado."),
    };

    private List<GrupoDesgloseDto> GruposDe(ChipCargue chip) => chip switch
    {
        ChipCargue.Leidas            => GruposLeidas(),
        ChipCargue.Excluidas         => Excluidas.Grupos(),
        ChipCargue.Duplicadas        => Duplicadas.Grupos(),
        ChipCargue.Insertadas        => _insertadas.Grupos(porCantidad: false),
        ChipCargue.Refrescadas       => _refrescadas.Grupos(),
        ChipCargue.VaciasDescartadas => _vacias.Grupos(),
        ChipCargue.VaciasPurgadas    => _purga.Grupos(),
        ChipCargue.TitularesNuevos   => _titularesNuevos.Grupos(),
        ChipCargue.NombresReparados  => _nombres.Grupos(porCantidad: false),
        ChipCargue.RelacionesNuevas  => _relaciones.Grupos(),
        ChipCargue.SinCarteraLider   => SinCarteraLider.Grupos(),
        ChipCargue.Advertencias      => Advertencias.Grupos(),
        _ => throw new ArgumentOutOfRangeException(nameof(chip), chip, null),
    };

    /// <summary>
    /// Leídas = candidatas + excluidas + duplicadas − agregadas por expansión (invariante 1 del
    /// motor). Las agregadas van con cantidad NEGATIVA: son filas destino, no del archivo.
    /// </summary>
    private List<GrupoDesgloseDto> GruposLeidas()
    {
        var grupos = new List<GrupoDesgloseDto>();
        void Agregar(string titulo, int cantidad, TonoDesglose tono, List<string> ejemplos)
        {
            if (cantidad != 0) grupos.Add(new GrupoDesgloseDto { Titulo = titulo, Cantidad = cantidad, Tono = tono, Ejemplos = ejemplos });
        }
        Agregar(TituloCandidatas, _candidatas.Total, TonoDesglose.Info, _candidatas.EjemplosMezclados());
        Agregar("Excluidas: no se cargan", Excluidas.Total, TonoDesglose.Error, Excluidas.EjemplosMezclados());
        Agregar("Duplicadas en el archivo", Duplicadas.Total, TonoDesglose.Aviso, Duplicadas.EjemplosMezclados());
        Agregar(TituloExpansion, -_expansion.Total, TonoDesglose.Info, _expansion.EjemplosMezclados());
        return grupos;
    }

    /// <summary>
    /// Desglose del preview. Los chips que dependen de conteos tomados en otra consulta
    /// (relaciones, insertar) se concilian con un grupo de ajuste; el resto DEBE cuadrar por
    /// construcción y, si no, lanza (como las invariantes del motor: un conteo que no cuadra
    /// es una fila que se perdió en silencio).
    /// </summary>
    public List<DesgloseChipDto> ParaPreview(PreviewCargueDto p)
    {
        var chips = Enum.GetValues<ChipCargue>()
            .Select(c => new DesgloseChipDto { Clave = c, Grupos = GruposDe(c) })
            .ToList();
        foreach (var chip in chips.Where(c => c.Clave is ChipCargue.RelacionesNuevas or ChipCargue.Insertadas))
            Conciliar(chip, NumeroDelChip(p, chip.Clave));

        var errores = Incoherencias(chips, c => NumeroDelChip(p, c));
        if (errores.Count > 0)
            throw new InvalidOperationException("Desglose incoherente con los conteos del preview: " + string.Join("; ", errores));
        return chips;
    }

    /// <summary>
    /// Desglose del resultado: grupos y ejemplos de la preparación que corrió al confirmar,
    /// conciliados con los conteos FINALES de la escritura (que se re-marcan dentro de la
    /// transacción). Nunca lanza: los datos ya están escritos.
    /// </summary>
    public List<DesgloseChipDto> ParaResultado(ResultadoCargueDto r)
    {
        var chips = ChipsResultado.Select(c => new DesgloseChipDto { Clave = c, Grupos = GruposDe(c) }).ToList();
        foreach (var chip in chips) Conciliar(chip, NumeroDelChip(r, chip.Clave));
        return chips;
    }

    /// <summary>
    /// Deja la suma igual a <paramref name="esperado"/>: con un solo grupo ajusta su cantidad;
    /// con varios (o ninguno) agrega el grupo de ajuste. En la práctica no ocurre salvo que
    /// otra sesión escriba en las mismas tablas entre la preparación y la escritura.
    /// </summary>
    internal static void Conciliar(DesgloseChipDto chip, int esperado)
    {
        var diferencia = esperado - chip.Grupos.Sum(g => g.Cantidad);
        if (diferencia == 0) return;
        if (chip.Grupos.Count == 1 && chip.Grupos[0].Cantidad + diferencia > 0)
            chip.Grupos[0].Cantidad += diferencia;
        else
            chip.Grupos.Add(new GrupoDesgloseDto { Titulo = TituloAjuste, Cantidad = diferencia, Tono = TonoDesglose.Aviso });
    }

    /// <summary>Chips cuya suma de grupos no es su número (vacío = todo cuadra).</summary>
    public static List<string> Incoherencias(IEnumerable<DesgloseChipDto> chips, Func<ChipCargue, int> numero) =>
        chips.Select(c => (c.Clave, Suma: c.Grupos.Sum(g => g.Cantidad), Numero: numero(c.Clave)))
             .Where(x => x.Suma != x.Numero)
             .Select(x => $"{x.Clave}: grupos suman {x.Suma} y el chip dice {x.Numero}")
             .ToList();
}
