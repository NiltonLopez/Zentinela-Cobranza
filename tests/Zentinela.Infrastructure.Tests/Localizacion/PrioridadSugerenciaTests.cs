using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;
using static Zentinela.Infrastructure.SQLServer.Services.Localizacion.DespliegueSugerencias;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Sugerencias de localización (ADR-040, etapa 2): puntaje explicable, exclusiones con las variantes de
/// escritura de los perfiles, penalización por contacto fresco, despliegue de las máscaras y top exacto.
/// </summary>
public class PrioridadSugerenciaTests
{
    private static readonly SugerenciasLocalizacionSettings Pesos = new();
    private static readonly DateOnly Hoy = new(2026, 9, 25);

    private static EntradaPrioridad E(
        GrupoPerfilSugerencia grupo = GrupoPerfilSugerencia.Otro, decimal? saldo = null, decimal? p90 = null,
        DateOnly? gestion = null, bool buscadoFuncional = true, CategoriaSugerencia cat = CategoriaSugerencia.EncontradoViejo,
        bool contactoFresco = false, bool incluirLocalizados = false) =>
        new(grupo, saldo, p90, gestion ?? Hoy, buscadoFuncional, cat, contactoFresco, incluirLocalizados);

    // ── Puntos ───────────────────────────────────────────────────────────────

    [Fact]
    public void Caso_completo_suma_cada_componente()
    {
        // Ilocalizado (+40), saldo = P90 (+30), sin gestión (+15), nunca en Funcional (+15), nunca en F (+10).
        var d = PrioridadSugerencia.Calcular(new EntradaPrioridad(
            GrupoPerfilSugerencia.SinDatos, 500_000m, 400_000m, null, false, CategoriaSugerencia.NuncaBuscado, false, false), Pesos, Hoy);

        Assert.Equal(new DesglosePrioridad(40, 30, 15, 15, 10, 0, 0), d);
        Assert.Equal(110, d.Total);
    }

    [Theory]
    [InlineData(GrupoPerfilSugerencia.SinDatos, 40)]
    [InlineData(GrupoPerfilSugerencia.SinContacto, 25)]
    [InlineData(GrupoPerfilSugerencia.SinGestion, 25)]
    [InlineData(GrupoPerfilSugerencia.Otro, 0)]
    public void Puntos_por_perfil(GrupoPerfilSugerencia grupo, int puntos) =>
        Assert.Equal(puntos, PrioridadSugerencia.Calcular(E(grupo), Pesos, Hoy).Perfil);

    [Theory]
    [InlineData(0, 0)]          // sin saldo
    [InlineData(50_000, 15)]    // la mitad del P90
    [InlineData(25_000, 8)]     // 7,5 → 8 (redondeo lejos de cero, como ROUND de SQL)
    [InlineData(100_000, 30)]   // igual al P90
    [InlineData(900_000, 30)]   // tope
    public void Puntos_por_saldo_relativos_al_P90(decimal saldo, int puntos) =>
        Assert.Equal(puntos, PrioridadSugerencia.Calcular(E(saldo: saldo, p90: 100_000m), Pesos, Hoy).Saldo);

    [Fact]
    public void Sin_P90_el_saldo_no_suma() =>
        Assert.Equal(0, PrioridadSugerencia.Calcular(E(saldo: 1_000_000m, p90: null), Pesos, Hoy).Saldo);

    [Theory]
    [InlineData(null, 15)]
    [InlineData(91, 15)]
    [InlineData(90, 5)]
    [InlineData(31, 5)]
    [InlineData(30, 0)]
    [InlineData(0, 0)]
    public void Puntos_por_antiguedad_de_la_gestion(int? dias, int puntos)
    {
        var entrada = E() with { FechaUltimaGestion = dias is null ? null : Hoy.AddDays(-dias.Value) };
        Assert.Equal(puntos, PrioridadSugerencia.Calcular(entrada, Pesos, Hoy).Gestion);
    }

    [Theory]
    [InlineData(CategoriaSugerencia.NuncaBuscado, 10, 0)]
    [InlineData(CategoriaSugerencia.NoEncontradoViejo, 0, 5)]
    [InlineData(CategoriaSugerencia.EncontradoViejo, 0, 0)]
    public void Puntos_por_categoria(CategoriaSugerencia cat, int nunca, int noEnc)
    {
        var d = PrioridadSugerencia.Calcular(E(cat: cat), Pesos, Hoy);
        Assert.Equal(nunca, d.NuncaFuente);
        Assert.Equal(noEnc, d.NoEncontradoViejo);
    }

    [Fact]
    public void Nunca_buscado_en_ninguna_Funcional_suma_15_ademas_de_nunca_en_F()
    {
        var d = PrioridadSugerencia.Calcular(E(buscadoFuncional: false, cat: CategoriaSugerencia.NuncaBuscado), Pesos, Hoy);
        Assert.Equal(15, d.NuncaFuncional);
        Assert.Equal(10, d.NuncaFuente);
    }

    [Fact]
    public void Contacto_fresco_resta_100_salvo_incluir_localizados()
    {
        Assert.Equal(-100, PrioridadSugerencia.Calcular(E(contactoFresco: true), Pesos, Hoy).ContactoFresco);
        Assert.Equal(0, PrioridadSugerencia.Calcular(E(contactoFresco: true, incluirLocalizados: true), Pesos, Hoy).ContactoFresco);
        Assert.Equal(0, PrioridadSugerencia.Calcular(E(contactoFresco: false), Pesos, Hoy).ContactoFresco);
    }

    [Fact]
    public void Los_pesos_salen_de_la_configuracion()
    {
        var pesos = new SugerenciasLocalizacionSettings { PuntosPerfilSinDatos = 7, PenalizacionContactoFresco = 3 };
        var d = PrioridadSugerencia.Calcular(E(GrupoPerfilSugerencia.SinDatos, contactoFresco: true), pesos, Hoy);
        Assert.Equal(7, d.Perfil);
        Assert.Equal(-3, d.ContactoFresco);
    }

    // ── Perfiles: variantes de escritura ─────────────────────────────────────

    [Theory]
    [InlineData("Paz y Salvo", MotivoExclusionSugerencia.YaPago)]
    [InlineData("PAZ Y SALVO", MotivoExclusionSugerencia.YaPago)]
    [InlineData("  paz  y   salvo ", MotivoExclusionSugerencia.YaPago)]
    [InlineData("Ya pagó", MotivoExclusionSugerencia.YaPago)]
    [InlineData("YA PAGO", MotivoExclusionSugerencia.YaPago)]
    [InlineData("Pago reflejado", MotivoExclusionSugerencia.YaPago)]
    [InlineData("Pago  reflejado", MotivoExclusionSugerencia.YaPago)]
    [InlineData("Consignación por Aplicar", MotivoExclusionSugerencia.YaPago)]
    [InlineData("Fallecido", MotivoExclusionSugerencia.Fallecido)]
    [InlineData("CLIENTE FALLECIDO", MotivoExclusionSugerencia.Fallecido)]
    [InlineData("No gestionar", MotivoExclusionSugerencia.NoGestionar)]
    [InlineData("NO GESTIONAR", MotivoExclusionSugerencia.NoGestionar)]
    [InlineData("PQR", MotivoExclusionSugerencia.Reclamacion)]
    [InlineData("RECLAMACION O DERECHO DE PETICION", MotivoExclusionSugerencia.Reclamacion)]
    [InlineData("Reclamación o derecho de petición", MotivoExclusionSugerencia.Reclamacion)]
    [InlineData("Acuerdo de pago Total", MotivoExclusionSugerencia.Acuerdo)]
    [InlineData("COMPROMISO DE ABONO", MotivoExclusionSugerencia.Acuerdo)]
    public void Perfiles_que_excluyen_con_sus_variantes(string perfil, MotivoExclusionSugerencia motivo) =>
        Assert.Equal(motivo, PerfilesSugerencia.Clasificar(perfil).Exclusion);

    [Theory]
    [InlineData("Ilocalizado", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("ILOCALIZADO PARCIAL", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("ILOCALIZADO TOTAL", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Equivocado", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("NUMERO EQUIVOCADO", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Número equivocado", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Datos Localizacion", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("DATOS DE LOCALIZACION", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Datos de localización", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("LINEA ERRADA", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Numero fuera de servicio", GrupoPerfilSugerencia.SinDatos)]
    [InlineData("Mensaje en buzon", GrupoPerfilSugerencia.SinContacto)]
    [InlineData("MENSAJE CONTESTADORA/BUZON", GrupoPerfilSugerencia.SinContacto)]
    [InlineData("NO CONTACTADO", GrupoPerfilSugerencia.SinContacto)]
    [InlineData("SIN GESTION", GrupoPerfilSugerencia.SinGestion)]
    [InlineData("Sin gestión", GrupoPerfilSugerencia.SinGestion)]
    [InlineData("", GrupoPerfilSugerencia.SinGestion)]
    [InlineData(null, GrupoPerfilSugerencia.SinGestion)]
    [InlineData("No hubo acuerdo", GrupoPerfilSugerencia.Otro)]
    [InlineData("Mensaje con tercero", GrupoPerfilSugerencia.Otro)]
    [InlineData("Perfil que nadie conoce", GrupoPerfilSugerencia.Otro)]
    public void Grupo_de_puntaje_con_sus_variantes(string? perfil, GrupoPerfilSugerencia grupo)
    {
        var c = PerfilesSugerencia.Clasificar(perfil);
        Assert.Null(c.Exclusion);
        Assert.Equal(grupo, c.Grupo);
    }

    [Fact]
    public void Normalizar_quita_tildes_mayusculas_y_espacios()
    {
        Assert.Equal("DATOS DE LOCALIZACION", PerfilesSugerencia.Normalizar("  Datos\tde   localización "));
        Assert.Equal("", PerfilesSugerencia.Normalizar(null));
    }

    [Fact]
    public void La_tabla_para_SQL_tiene_claves_normalizadas_unicas_y_el_perfil_vacio()
    {
        var tabla = PerfilesSugerencia.Tabla(Pesos);
        Assert.Equal(tabla.Count, tabla.Select(t => t.Clave).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tabla, t => Assert.Equal(PerfilesSugerencia.Normalizar(t.Clave), t.Clave));
        Assert.Contains(tabla, t => t.Clave == "" && t.Puntos == 25 && t.Exclusion is null);
        Assert.Contains(tabla, t => t.Clave == "PAZ Y SALVO" && t.YaPago && t.Exclusion == MotivoExclusionSugerencia.YaPago);
        Assert.Contains(tabla, t => t.Clave == "DATOS DE LOCALIZACION" && t.Puntos == 40);
        // Todas las claves caben en la columna de #PG.
        Assert.All(tabla, t => Assert.True(t.Clave.Length <= 60));
    }

    // ── Exclusiones: precedencia ─────────────────────────────────────────────

    [Fact]
    public void MejorPerfil_ya_pago_excluye_aunque_el_ultimo_sea_otro() =>
        Assert.Equal(MotivoExclusionSugerencia.YaPago,
            PrioridadSugerencia.Excluir("Mensaje en buzon", "PAZ Y SALVO", null, 100m, false, true, Hoy));

    [Fact]
    public void Promesa_vigente_es_de_hoy_en_adelante()
    {
        Assert.Equal(MotivoExclusionSugerencia.PromesaVigente,
            PrioridadSugerencia.Excluir("Mensaje en buzon", null, Hoy, 100m, false, true, Hoy));
        Assert.Null(PrioridadSugerencia.Excluir("Mensaje en buzon", null, Hoy.AddDays(-1), 100m, false, true, Hoy));
    }

    [Fact]
    public void Precedencia_perfil_promesa_enviado_saldo()
    {
        Assert.Equal(MotivoExclusionSugerencia.Fallecido,
            PrioridadSugerencia.Excluir("Fallecido", null, Hoy, null, true, true, Hoy));
        Assert.Equal(MotivoExclusionSugerencia.PromesaVigente,
            PrioridadSugerencia.Excluir(null, null, Hoy, null, true, true, Hoy));
        Assert.Equal(MotivoExclusionSugerencia.EnviadoReciente,
            PrioridadSugerencia.Excluir(null, null, null, null, true, true, Hoy));
        Assert.Equal(MotivoExclusionSugerencia.SinSaldo,
            PrioridadSugerencia.Excluir(null, null, null, null, false, true, Hoy));
        Assert.Null(PrioridadSugerencia.Excluir(null, null, null, null, false, false, Hoy));
    }

    // ── Despliegue de máscaras (lo que el servidor hace con el result set del SQL) ──

    private static readonly IReadOnlyList<FuenteBit> Tres = [new("A", 1), new("B", 2), new("C", 4)];

    [Fact]
    public void Motivo_por_fuente_sigue_la_misma_precedencia_que_Excluir()
    {
        Assert.Equal(MotivoExclusionSugerencia.Acuerdo, Motivo(MotivoExclusionSugerencia.Acuerdo, true, 1, 1));
        Assert.Equal(MotivoExclusionSugerencia.EnviadoReciente, Motivo(null, true, 1, 1));
        Assert.Equal(MotivoExclusionSugerencia.SinSaldo, Motivo(null, true, 1, 2));
        Assert.Null(Motivo(null, false, 1, 2));
    }

    [Fact]
    public void Conteos_despliegan_cada_combinacion_por_fuente()
    {
        var grupos = new List<GrupoMascaras>
        {
            // 10 titulares: nunca en A, no encontrados y viejos en B, enviados a B, 3 con contacto fresco.
            new(null, false, 2, 1, 2, 0, 10, 3, 1000m),
            // 4 titulares ya pagados: nunca en A y C.
            new(MotivoExclusionSugerencia.YaPago, false, 0, 5, 0, 0, 4, 1, 400m),
        };
        var c = Conteos(grupos, Tres);

        Conteo Uno(string f, CategoriaSugerencia cat, MotivoExclusionSugerencia? m) =>
            Assert.Single(c, x => x.Fuente == f && x.Categoria == cat && x.Motivo == m);

        Assert.Equal(10, Uno("A", CategoriaSugerencia.NuncaBuscado, null).Filas);
        Assert.Equal(3, Uno("A", CategoriaSugerencia.NuncaBuscado, null).ConFresco);
        Assert.Equal(10, Uno("B", CategoriaSugerencia.NoEncontradoViejo, MotivoExclusionSugerencia.EnviadoReciente).Filas);
        Assert.Equal(0, Uno("B", CategoriaSugerencia.NoEncontradoViejo, MotivoExclusionSugerencia.EnviadoReciente).ConFresco);
        Assert.Equal(4, Uno("A", CategoriaSugerencia.NuncaBuscado, MotivoExclusionSugerencia.YaPago).Filas);
        Assert.Equal(4, Uno("C", CategoriaSugerencia.NuncaBuscado, MotivoExclusionSugerencia.YaPago).Filas);
        Assert.Equal(28, c.Sum(x => x.Filas));
    }

    [Fact]
    public void Titulares_por_motivo_se_cuentan_una_vez_y_el_enviado_parcial_cuenta_en_los_dos()
    {
        var grupos = new List<GrupoMascaras>
        {
            new(MotivoExclusionSugerencia.Fallecido, false, 0, 1, 0, 0, 5, 0, 50m),   // 5 fallecidos (una fuente)
            new(null, false, 1, 3, 0, 0, 7, 2, 700m),                                // 7: A enviada, B libre
            new(null, true, 0, 2, 0, 0, 4, 0, 0m),                                   // 4 sin saldo
            new(null, false, 3, 3, 0, 0, 2, 0, 20m),                                 // 2: todo enviado
            new(null, false, 0, 0, 0, 0, 9, 0, 90m),                                 // sin candidatos: no cuentan
        };
        var (porMotivo, candidatos, saldo) = Titulares(grupos);

        Assert.Equal(5, porMotivo[MotivoExclusionSugerencia.Fallecido]);
        Assert.Equal(9, porMotivo[MotivoExclusionSugerencia.EnviadoReciente]);
        Assert.Equal(4, porMotivo[MotivoExclusionSugerencia.SinSaldo]);
        Assert.Equal(7, candidatos);
        Assert.Equal(700m, saldo);
    }

    [Theory]
    [InlineData(1_550, "chico")]
    [InlineData(36_197, "mediano")]
    [InlineData(223_353, "grande")]
    [InlineData(526_934, "enorme")]
    public void El_tramo_de_tamano_separa_los_planes(int filas, string tramo)
    {
        Assert.Equal(tramo, GeneradorSqlSugerencias.TramoTamano(filas));
        Assert.StartsWith($"-- tamaño: {tramo}", GeneradorSqlSugerencias.Batch(GeneradorSqlSugerencias.Funcionales, filas));
    }

    [Fact]
    public void Top_ordena_por_prioridad_saldo_titular_y_fuente_y_salta_las_enviadas()
    {
        var t1 = T(1, ptsBase: 50, saldo: 10m, cat1: 3, cat2: 4, env: 2);   // A nunca (60), B enviada, C no enc. (55)
        var t2 = T(2, ptsBase: 50, saldo: 20m, cat1: 1);                    // A nunca (60), más saldo que t1
        var filas = Top([t1, t2], Tres, 10, Pesos);

        Assert.Equal([(2L, "A", 60), (1L, "A", 60), (1L, "C", 55)],
                     filas.Select(f => (f.Titular.TitularID, f.Fuente, f.Prioridad)).ToList());
        Assert.Equal(CategoriaSugerencia.NoEncontradoViejo, filas[2].Categoria);
        Assert.Equal(5, filas[2].PtsNoEncontradoViejo);
    }

    /// <summary>
    /// El SQL solo devuelve los N mejores titulares por su mejor fila; desplegar esos N debe dar exactamente
    /// las N mejores filas de TODOS los titulares (se compara contra fuerza bruta con datos al azar).
    /// </summary>
    [Fact]
    public void Top_de_los_mejores_titulares_es_el_top_de_todas_las_filas()
    {
        var azar = new Random(40);
        for (var ronda = 0; ronda < 50; ronda++)
        {
            var titulares = Enumerable.Range(1, 200).Select(id =>
            {
                long c1 = azar.Next(8), c2 = azar.Next(8) & ~c1, c3 = azar.Next(8) & ~(c1 | c2);
                return T(id, ptsBase: azar.Next(-20, 60), saldo: azar.Next(3) == 0 ? null : azar.Next(1, 5) * 1000m,
                         cat1: c1, cat2: c2, cat3: c3, env: azar.Next(8));
            }).ToList();
            var n = azar.Next(1, 60);

            var todas = Top(titulares, Tres, int.MaxValue, Pesos);
            var esperado = todas.Take(n).Select(f => (f.Titular.TitularID, f.Fuente)).ToList();

            // Lo que hace el SQL: mejor fila por titular (MaxPri), orden por MaxPri, saldo, TitularID; top N.
            var mejores = titulares
                .Select(t => (t, Max: todas.Where(f => f.Titular.TitularID == t.TitularID).Select(f => (int?)f.Prioridad).Max()))
                .Where(x => x.Max is not null)
                .OrderByDescending(x => x.Max).ThenByDescending(x => x.t.Saldo ?? 0).ThenBy(x => x.t.TitularID)
                .Take(n).Select(x => x.t).ToList();
            var obtenido = Top(mejores, Tres, n, Pesos).Select(f => (f.Titular.TitularID, f.Fuente)).ToList();

            Assert.Equal(esperado, obtenido);
        }
    }

    private static TitularTop T(long id, int ptsBase, decimal? saldo, long cat1 = 0, long cat2 = 0, long cat3 = 0, long env = 0) =>
        new(id, $"T{id}", 1, "L", saldo, null, null, 0, 0, 0, 0, 0, ptsBase, 0, true, false, env, cat1, cat2, cat3,
            [null, null, null]);

    // ── Parámetros ───────────────────────────────────────────────────────────

    [Fact]
    public void Solo_se_aceptan_fuentes_Funcionales()
    {
        var ex = Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.ResolverFuentes(["Camara"]));
        Assert.Contains("Intermitente", ex.Message);
        Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.ResolverFuentes(["NoExiste"]));

        var todas = LocalizacionSugerenciasService.ResolverFuentes([]);
        Assert.All(todas, f => Assert.Equal(EstadoFuente.Funcional, f.Estado));
        Assert.Contains(todas, f => f.Clave == "Fosyga");
        Assert.DoesNotContain(todas, f => f.Clave == "CamaraEmpresa");
    }

    [Fact]
    public void Top_y_umbrales_se_validan()
    {
        Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.Validar(new() { CarteraID = 1, Top = 5001 }, Pesos));
        Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.Validar(new() { CarteraID = 1, Top = 0 }, Pesos));
        Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.Validar(new() { CarteraID = 0 }, Pesos));
        Assert.Throws<ArgumentException>(() => LocalizacionSugerenciasService.Validar(new() { CarteraID = 1, UmbralRefrescoDias = 0 }, Pesos));
        LocalizacionSugerenciasService.Validar(new ParametrosSugerenciasLocalizacion { CarteraID = 1, Top = 5000 }, Pesos);
    }

    // ── SQL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void El_batch_usa_la_ultima_fila_de_la_etapa_1_y_no_depende_de_las_tablas_de_resumen()
    {
        var sql = GeneradorSqlSugerencias.Batch(GeneradorSqlSugerencias.Funcionales);
        Assert.Contains("ORDER BY s.FechaConsulta DESC, e.Enc DESC, e.Con DESC", sql);
        Assert.Contains("INNER JOIN @Tit t ON t.TitularID = TRY_CAST(s.TitularID AS bigint)", sql);   // NuevaEps, sobre la variable de tabla
        Assert.DoesNotContain("RECOMPILE, MAXDOP 4, HASH JOIN", sql);
        Assert.DoesNotContain("LocalizacionResumen", sql);
        Assert.DoesNotContain("LocalizacionCorte", sql);
        Assert.Contains("IF @ConEnvios = 1 AND @LimEnvio IS NOT NULL", sql);
        Assert.Contains("lc.Activo = 1", sql);
        Assert.Contains("INNER JOIN dbo.Titular ti WITH (FORCESEEK) ON ti.NumeroDocumento = g.TitularID", sql);
        Assert.Contains("UltimoEstadoTitular u WITH (INDEX(1), FORCESEEK)", sql);
        Assert.Contains("PERCENTILE_CONT(0.9)", sql);
        Assert.DoesNotContain("dbo.Camara ", sql);   // Intermitente: no entra
    }

    [Fact]
    public void El_batch_rechaza_fuentes_no_Funcionales()
    {
        var camara = CatalogoFuentesLocalizacion.Buscar("Camara")!;
        Assert.Throws<ArgumentException>(() => GeneradorSqlSugerencias.Batch([camara]));
    }
}
