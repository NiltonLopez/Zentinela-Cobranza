using System.Text.RegularExpressions;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;
using SqlEnv = Zentinela.Infrastructure.SQLServer.Services.Localizacion.GeneradorSqlEnvios;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Envíos a localización (ADR-040, etapa 3): estados de los lotes, retorno (cargue antes / después / sin
/// cargue), impacto, uso del departamento con lotes de dos líderes, alcance del Líder y el SQL que lo aplica.
/// El SQL real se ejecutó además contra tablas #temp en la BD (documentado en ADR-040).
/// </summary>
public class EnviosLocalizacionTests
{
    private static readonly SqlEnv.TablasEnvio Prod = SqlEnv.Produccion;
    private static readonly DateTime Envio = new(2026, 9, 20, 10, 0, 0);

    // ════════════════════════════════════════════════════════════════════════
    //  Transiciones
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(EstadoEnvio.Borrador, AccionEnvio.Enviar, true)]
    [InlineData(EstadoEnvio.Borrador, AccionEnvio.Anular, true)]
    [InlineData(EstadoEnvio.Borrador, AccionEnvio.Cerrar, false)]
    [InlineData(EstadoEnvio.Enviado, AccionEnvio.Cerrar, true)]
    [InlineData(EstadoEnvio.Enviado, AccionEnvio.Anular, true)]
    [InlineData(EstadoEnvio.Enviado, AccionEnvio.Enviar, false)]
    [InlineData(EstadoEnvio.Cerrado, AccionEnvio.Enviar, false)]
    [InlineData(EstadoEnvio.Cerrado, AccionEnvio.Cerrar, false)]
    [InlineData(EstadoEnvio.Cerrado, AccionEnvio.Anular, false)]
    [InlineData(EstadoEnvio.Anulado, AccionEnvio.Enviar, false)]
    [InlineData(EstadoEnvio.Anulado, AccionEnvio.Cerrar, false)]
    [InlineData(EstadoEnvio.Anulado, AccionEnvio.Anular, false)]
    public void Transiciones_validas_e_invalidas(EstadoEnvio actual, AccionEnvio accion, bool puede)
    {
        Assert.Equal(puede, TransicionesEnvio.Puede(actual, accion));
    }

    [Fact]
    public void Destinos_y_codigos_son_los_del_CHECK_de_la_tabla()
    {
        Assert.Equal(EstadoEnvio.Enviado, TransicionesEnvio.Destino(AccionEnvio.Enviar));
        Assert.Equal(EstadoEnvio.Cerrado, TransicionesEnvio.Destino(AccionEnvio.Cerrar));
        Assert.Equal(EstadoEnvio.Anulado, TransicionesEnvio.Destino(AccionEnvio.Anular));

        // CK_LocalizacionEnvio_Estado: ('BORRADOR', 'ENVIADO', 'CERRADO', 'ANULADO').
        Assert.Equal(["BORRADOR", "ENVIADO", "CERRADO", "ANULADO"], Enum.GetValues<EstadoEnvio>().Select(TransicionesEnvio.Codigo));
        Assert.True(TransicionesEnvio.TryParse("enviado", out var e) && e == EstadoEnvio.Enviado);
        Assert.False(TransicionesEnvio.TryParse("PERDIDO", out _));
        Assert.True(TransicionesEnvio.TryParseAccion("anular", out var a) && a == AccionEnvio.Anular);
        Assert.False(TransicionesEnvio.TryParseAccion("borrar", out _));
    }

    [Fact]
    public void Mensaje_de_transicion_no_permitida_dice_el_estado_actual_y_los_de_origen()
    {
        var m = TransicionesEnvio.MensajeNoPermitido("LOC-000007", EstadoEnvio.Cerrado, AccionEnvio.Anular);
        Assert.Contains("LOC-000007", m);
        Assert.Contains("CERRADO", m);
        Assert.Contains("BORRADOR o ENVIADO", m);
    }

    [Theory]
    [InlineData(AccionEnvio.Enviar, "'ENVIADO'", "Estado IN ('BORRADOR')", "FechaEnvio = SYSDATETIME()")]
    [InlineData(AccionEnvio.Cerrar, "'CERRADO'", "Estado IN ('ENVIADO')", "FechaCierre = SYSDATETIME()")]
    [InlineData(AccionEnvio.Anular, "'ANULADO'", "Estado IN ('BORRADOR', 'ENVIADO')", "MotivoAnulacion = @Motivo")]
    public void Transicion_SQL_con_concurrencia_optimista_y_alcance(AccionEnvio accion, string destino, string origen, string marca)
    {
        var sql = SqlEnv.Transicion(Prod, accion);
        Assert.Contains($"SET Estado = {destino}", sql);
        Assert.Contains(origen, sql);                                   // WHERE Estado = esperado → 0 filas = 409
        Assert.Contains(marca, sql);                                    // cada estado con su marca de tiempo (CK_LocalizacionEnvio_Fechas)
        Assert.Contains("AND (@LiderAlcance IS NULL OR LiderID = @LiderAlcance)", sql);
        Assert.Contains("SET @Afectadas = @@ROWCOUNT", sql);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Retorno
    // ════════════════════════════════════════════════════════════════════════

    private static CargueRetorno Cargue(long id, string fuente, DateTime fin, string usuario = "juan", string estado = "OK", DateTime? sello = null) =>
        new(id, fuente, usuario, estado, fin, sello ?? fin.Date);

    [Fact]
    public void Retorno_es_el_primer_cargue_OK_de_la_fuente_despues_del_envio()
    {
        var cargues = new[]
        {
            Cargue(1, "Fosyga", new DateTime(2026, 9, 10, 9, 0, 0)),                       // antes del envío
            Cargue(2, "Sura",   new DateTime(2026, 9, 21, 9, 0, 0)),                       // otra fuente
            Cargue(3, "Fosyga", new DateTime(2026, 9, 22, 9, 0, 0), usuario: "harness-cargue-salud"), // harness
            Cargue(4, "Fosyga", new DateTime(2026, 9, 23, 9, 0, 0), estado: "ERROR"),       // falló
            Cargue(6, "Fosyga", new DateTime(2026, 9, 26, 9, 0, 0)),
            Cargue(5, "Fosyga", new DateTime(2026, 9, 24, 15, 38, 0)),                     // el primero válido
        };

        var r = RetornoEnvio.ElegirCargue("fosyga", Envio, cargues, UsuariosPruebaCargue.EsDePrueba);
        Assert.Equal(5, r!.EjecucionID);
    }

    [Fact]
    public void Retorno_solo_con_cargues_anteriores_al_envio_no_tiene_cargue()
    {
        var cargues = new[] { Cargue(1, "Fosyga", new DateTime(2026, 9, 19, 9, 0, 0)) };
        Assert.Null(RetornoEnvio.ElegirCargue("Fosyga", Envio, cargues, UsuariosPruebaCargue.EsDePrueba));
        Assert.Null(RetornoEnvio.ElegirCargue("Fosyga", null, cargues, UsuariosPruebaCargue.EsDePrueba));   // sin enviar
    }

    [Fact]
    public void El_cargue_vinculado_al_lote_manda_sobre_el_primero_posterior()
    {
        var cargues = new[]
        {
            Cargue(5, "Fosyga", new DateTime(2026, 9, 24, 15, 38, 0)),
            Cargue(8, "Fosyga", new DateTime(2026, 10, 2, 8, 0, 0)),
        };
        Assert.Equal(8, RetornoEnvio.ElegirCargue("Fosyga", Envio, cargues, UsuariosPruebaCargue.EsDePrueba, vinculado: 8)!.EjecucionID);
        // Un vínculo a un cargue inexistente o no OK no cuenta: se usa la regla general.
        Assert.Equal(5, RetornoEnvio.ElegirCargue("Fosyga", Envio, cargues, UsuariosPruebaCargue.EsDePrueba, vinculado: 99)!.EjecucionID);
    }

    [Fact]
    public void Con_cargue_cuenta_desde_su_sello_inclusive_y_sin_cargue_despues_del_envio()
    {
        var cargue = Cargue(5, "Fosyga", new DateTime(2026, 9, 24, 15, 38, 0), sello: new DateTime(2026, 9, 24));
        var conCargue = RetornoEnvio.Umbral(Envio, cargue)!;
        Assert.Equal(new DateTime(2026, 9, 24), conCargue.Desde);
        Assert.True(conCargue.Inclusivo);
        Assert.True(RetornoEnvio.EsBuscado(new DateTime(2026, 9, 24), conCargue));        // la fila que escribió el cargue
        Assert.False(RetornoEnvio.EsBuscado(new DateTime(2026, 9, 23), conCargue));       // dato legado entre envío y cargue
        Assert.False(RetornoEnvio.EsBuscado(null, conCargue));                            // sin filas

        var sinCargue = RetornoEnvio.Umbral(Envio, null)!;
        Assert.Equal(Envio, sinCargue.Desde);
        Assert.False(sinCargue.Inclusivo);
        Assert.False(RetornoEnvio.EsBuscado(new DateTime(2026, 9, 20), sinCargue));       // fecha date del mismo día: 00:00 < envío
        Assert.True(RetornoEnvio.EsBuscado(new DateTime(2026, 9, 21), sinCargue));

        Assert.Null(RetornoEnvio.Umbral(null, cargue));                                    // BORRADOR: sin retorno
    }

    private static MetricasCrudasEnvio M(int titulares, int buscados = 0, int localizados = 0, DateTime? primerDato = null,
        int gestionados = 0, int contacto = 0, int conPago = 0, int pagos = 0, decimal valor = 0, decimal honorarios = 0, int promesas = 0) =>
        new(titulares, buscados, localizados, primerDato, gestionados, contacto, conPago, pagos, valor, honorarios, promesas);

    [Fact]
    public void Retorno_con_cargue_simulado_despues_del_envio()
    {
        var cargue = Cargue(509, "Fosyga", new DateTime(2026, 9, 24, 15, 38, 4), sello: new DateTime(2026, 9, 24));
        var r = MetricasEnvio.Retorno(Envio, RetornoEnvio.Umbral(Envio, cargue), M(300, buscados: 200, localizados: 171))!;

        Assert.Equal("CARGUE", r.Base);
        Assert.Equal(509, r.CargueEjecucionID);
        Assert.Equal(66.67m, r.PctRetorno);
        Assert.Equal(85.5m, r.PctLocalizadosBuscados);
        Assert.Equal(57m, r.PctLocalizados);
        Assert.Equal(cargue.FechaFin, r.FechaRetorno);
        Assert.Equal(4, r.DiasRetorno);
    }

    [Fact]
    public void Retorno_sin_cargue_usa_el_primer_dato_nuevo_y_sin_datos_no_tiene_fecha()
    {
        var umbral = RetornoEnvio.Umbral(Envio, null);
        var r = MetricasEnvio.Retorno(Envio, umbral, M(10, buscados: 3, localizados: 1, primerDato: new DateTime(2026, 9, 27)))!;
        Assert.Equal("FECHA", r.Base);
        Assert.Null(r.CargueEjecucionID);
        Assert.Equal(7, r.DiasRetorno);

        var nada = MetricasEnvio.Retorno(Envio, umbral, M(10))!;
        Assert.Null(nada.FechaRetorno);
        Assert.Null(nada.DiasRetorno);
        Assert.Equal(0m, nada.PctRetorno);
        Assert.Null(nada.PctLocalizadosBuscados);                                        // 0 buscados: sin universo

        Assert.Null(MetricasEnvio.Retorno(null, null, M(10)));                             // sin enviar
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Impacto
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Impacto_cuenta_gestion_contacto_pagos_y_promesas()
    {
        var i = MetricasEnvio.Impacto(Envio, M(400, gestionados: 15, contacto: 5, conPago: 40, pagos: 68, valor: 7_099_160.4m, honorarios: 191_677.35m, promesas: 3))!;
        Assert.Equal(15, i.Gestionados);
        Assert.Equal(3.75m, i.PctGestionados);
        Assert.Equal(5, i.PasaronAContacto);
        Assert.Equal(40, i.TitularesConPago);
        Assert.Equal(68, i.Pagos);
        Assert.Equal(7_099_160.4m, i.ValorPagado);
        Assert.Equal(191_677.35m, i.Honorarios);
        Assert.Equal(3, i.PromesasVigentes);
        Assert.Null(MetricasEnvio.Impacto(null, M(400)));                                  // BORRADOR
    }

    [Theory]
    [InlineData("Acuerdo de pago Total", true)]
    [InlineData("ACUERDO DE PAGO  TOTAL", true)]      // variante de escritura
    [InlineData("No hubo acuerdo", true)]
    [InlineData("Contacto con tercero", true)]
    [InlineData("Familiar informa", true)]
    [InlineData("Negociación vigente", true)]
    [InlineData("negociacion vigente", true)]
    [InlineData("Informativo Chat", false)]           // SinContacto
    [InlineData("Ilocalizado", false)]
    [InlineData("Paz y salvo", false)]                // YaPago: los pagos se miden aparte
    [InlineData("Sin gestion", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Perfil_con_contacto(string? perfil, bool esperado)
    {
        Assert.Equal(esperado, PerfilesContacto.EsConContacto(perfil));
    }

    [Fact]
    public void El_SQL_recibe_las_mismas_claves_de_contacto_normalizadas()
    {
        Assert.Contains("ACUERDO DE PAGO TOTAL", PerfilesContacto.ClavesNormalizadas);
        Assert.Contains("NEGOCIACION VIGENTE", PerfilesContacto.ClavesNormalizadas);
        Assert.All(PerfilesContacto.ClavesNormalizadas, c => Assert.Equal(PerfilesSugerencia.Normalizar(c), c));
        Assert.DoesNotContain("PAZ Y SALVO", PerfilesContacto.ClavesNormalizadas);

        var sql = SqlEnv.Metricas(Prod, [CatalogoFuentesLocalizacion.Buscar("Fosyga")!], conPagina: false);
        Assert.Contains("n.Ahora LIKE '%TERCERO%'", sql);
        Assert.Contains("n.Foto LIKE '%FAMILIAR%'", sql);
        Assert.Contains("c.Ahora = 1 AND c.Foto = 0", sql);                // pasó a contacto
        Assert.Contains("a.FechaUltimaGestion > d.DiaEnvio", sql);         // gestionado después del día del envío
        Assert.Contains("p.FechaPago > d.DiaEnvio", sql);                  // pagos después del día del envío
        Assert.Contains("a.FechaPromesa <> d.PromesaFoto", sql);           // promesa nueva
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Uso del departamento (lotes de dos líderes)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cola_aproximada_es_falta_por_buscar_mas_no_encontrados_viejos_acotados()
    {
        // Fosyga: 400 + min(100, 300); Sura: 100 + min(800, 700).
        Assert.Equal(1300, UsoDepartamentoCalculo.Cola([(1000, 600, 500, 300), (1000, 900, 100, 700)]));
        Assert.Equal(0, UsoDepartamentoCalculo.Cola([]));
    }

    [Fact]
    public void Uso_por_lider_con_lotes_de_dos_lideres()
    {
        var lotes = new[]
        {
            new UsoLote(1, 3, 4, Envio, M(200), null),                                                         // sin retorno aún
            new UsoLote(2, 3, 4, Envio, M(300, buscados: 200, localizados: 171, gestionados: 2), 4),
            new UsoLote(3, 15, 1, Envio, M(400, gestionados: 15, contacto: 5, conPago: 400, pagos: 686, valor: 70_991_604m), 10),
            new UsoLote(4, 15, 7, Envio, M(100, buscados: 50, localizados: 20), 6),
        };
        var colas = new[] { new ColaPar(3, 4, 1300), new ColaPar(15, 1, 155), new ColaPar(15, 7, 845) };

        UsoAgregado De(int lider) => UsoDepartamentoCalculo.Agregar(
            lotes.Where(l => l.LiderID == lider).ToList(), colas.Where(c => c.LiderID == lider).Sum(c => c.Cola));

        var l3 = De(3);
        Assert.Equal(2, l3.Lotes);
        Assert.Equal(500, l3.Metricas.Titulares);
        Assert.Equal(38.46m, l3.PctColaEnviada);
        Assert.Equal(40m, l3.PctRetorno);
        Assert.Equal(34.2m, l3.PctLocalizados);
        Assert.Equal(1, l3.LotesConRetorno);
        Assert.Equal(4m, l3.DiasRetornoPromedio);

        var l15 = De(15);
        Assert.Equal(2, l15.Lotes);
        Assert.Equal(500, l15.Metricas.Titulares);
        Assert.Equal(1000, l15.Cola);
        Assert.Equal(50m, l15.PctColaEnviada);
        Assert.Equal(10m, l15.PctRetorno);
        Assert.Equal(400, l15.Metricas.TitularesConPago);
        Assert.Equal(70_991_604m, l15.Metricas.ValorPagado);
        Assert.Equal(8m, l15.DiasRetornoPromedio);

        // Total = suma de los dos.
        var total = UsoDepartamentoCalculo.Agregar(lotes, colas.Sum(c => c.Cola));
        Assert.Equal(l3.Metricas.Titulares + l15.Metricas.Titulares, total.Metricas.Titulares);
        Assert.Equal(l3.Metricas.Buscados + l15.Metricas.Buscados, total.Metricas.Buscados);
        Assert.Equal(4, total.Lotes);
    }

    [Fact]
    public void Uso_sin_lotes_es_0_con_cola_y_el_retorno_queda_sin_valor()
    {
        var u = UsoDepartamentoCalculo.Agregar([], 500);
        Assert.Equal(0m, u.PctColaEnviada);
        Assert.Equal(0m, CalculoLocalizacion.ValorEnvios(u, CriterioRankingLocalizacion.Uso));
        Assert.Null(CalculoLocalizacion.ValorEnvios(u, CriterioRankingLocalizacion.Retorno));   // va al final del ranking
        Assert.Null(UsoDepartamentoCalculo.Agregar([], null).PctColaEnviada);                  // sin corte
    }

    [Fact]
    public void Uso_con_mas_enviados_que_cola_se_topa_en_100()
    {
        var u = UsoDepartamentoCalculo.Agregar([new UsoLote(1, 15, 1, Envio, M(400), null)], 155);
        Assert.Equal(100m, u.PctColaEnviada);
    }

    [Fact]
    public void Criterios_de_ranking_Uso_y_Retorno()
    {
        Assert.True(CalculoLocalizacion.TryParseCriterio("uso", out var c) && c == CriterioRankingLocalizacion.Uso);
        Assert.True(CalculoLocalizacion.TryParseCriterio("Retorno", out c) && c == CriterioRankingLocalizacion.Retorno);
        Assert.True(CalculoLocalizacion.EsDeEnvios(CriterioRankingLocalizacion.Uso));
        Assert.False(CalculoLocalizacion.EsDeEnvios(CriterioRankingLocalizacion.Indice));
        Assert.False(CalculoLocalizacion.MenorEsMejor(CriterioRankingLocalizacion.Retorno));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Crear y alcance
    // ════════════════════════════════════════════════════════════════════════

    private static CrearEnvioLocalizacionDto Pedido(Action<CrearEnvioLocalizacionDto>? ajustar = null)
    {
        var p = new CrearEnvioLocalizacionDto { LiderID = 3, CarteraID = 4, Fuente = "Fosyga", Fuentes = ["Fosyga", "Sura"], Top = 500 };
        ajustar?.Invoke(p);
        return p;
    }

    [Fact]
    public void Crear_valida_par_fuente_Funcional_y_tope()
    {
        var envios = new EnviosLocalizacionSettings();
        Assert.Equal("Fosyga", LocalizacionEnviosService.ValidarCreacion(Pedido(), envios).Clave);

        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.LiderID = null), envios));       // cartera completa
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.CarteraID = 0), envios));
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.Fuente = " "), envios));        // sin fuente
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.Fuente = "Camara"), envios));   // Intermitente
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.Fuente = "RUES"), envios));     // no está en la consulta
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.Top = 5001), envios));
        Assert.Throws<ArgumentException>(() => LocalizacionEnviosService.ValidarCreacion(Pedido(p => p.Observacion = new string('x', 501)), envios));
        // Sin fuentes en la consulta = todas las Funcionales.
        Assert.Equal("RUES", LocalizacionEnviosService.ValidarCreacion(Pedido(p => { p.Fuentes = []; p.Fuente = "RUES"; }), envios).Clave);
    }

    [Fact]
    public void Crear_SQL_valida_el_par_y_escribe_en_una_transaccion()
    {
        var sql = SqlEnv.Crear(Prod);
        Assert.Contains("tc.TitularID = f.TitularID AND tc.LiderID = @LiderID AND tc.CarteraID = @CarteraID", sql);
        Assert.Contains("lc.Activo = 1", sql);
        Assert.Contains("u.TitularID = f.TitularID AND u.CarteraID = @CarteraID", sql);       // foto del par
        var tx = sql.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        Assert.True(tx > sql.IndexOf("JOIN dbo.UltimoEstadoTitular", StringComparison.Ordinal));  // la foto, fuera de la transacción
        Assert.True(sql.IndexOf("INSERT INTO dbo.LocalizacionEnvio ", StringComparison.Ordinal) > tx);
        Assert.True(sql.IndexOf("INSERT INTO dbo.LocalizacionEnvioDetalle", StringComparison.Ordinal) > tx);
        Assert.True(sql.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal) > sql.IndexOf("UPDATE dbo.LocalizacionEnvio SET Titulares", StringComparison.Ordinal));
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("'BORRADOR'", sql);
    }

    [Fact]
    public void Las_lecturas_recortan_por_el_lider_del_alcance()
    {
        Assert.Contains("(@LiderID IS NULL    OR e.LiderID = @LiderID)", SqlEnv.Cabeceras(Prod));
        Assert.Contains("(@LiderAlcance IS NULL OR LiderID = @LiderAlcance)", SqlEnv.Transicion(Prod, AccionEnvio.Anular));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Prefijo de tablas (pruebas con #temp)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Con_prefijo_de_prueba_no_toca_las_tablas_de_produccion()
    {
        var t = SqlEnv.TablasEnvio.ConPrefijo("#");
        var fosyga = CatalogoFuentesLocalizacion.Buscar("Fosyga")!;
        var todo = string.Join("\n",
            SqlEnv.Crear(t), SqlEnv.Transicion(t, AccionEnvio.Enviar), SqlEnv.Cabeceras(t), SqlEnv.BorradoresPorLider(t),
            SqlEnv.FilasExcel(t), SqlEnv.ColaUltimoCorte(t, [fosyga]), SqlEnv.Metricas(t, [fosyga], conPagina: true));

        Assert.DoesNotMatch(new Regex(@"dbo\.Localizacion"), todo);
        Assert.Contains("INSERT INTO #LocalizacionEnvio ", todo);
        Assert.Contains("FROM #LocalizacionCorte", todo);
        Assert.Throws<ArgumentException>(() => SqlEnv.TablasEnvio.ConPrefijo("##"));
        Assert.Throws<ArgumentException>(() => SqlEnv.TablasEnvio.ConPrefijo("otra."));
    }

    [Fact]
    public void Retorno_SQL_usa_la_ultima_fila_con_el_desempate_y_la_fecha_sin_truncar()
    {
        var sql = SqlEnv.UltimaFilaFuente(CatalogoFuentesLocalizacion.Buscar("RUES")!);
        Assert.Contains("CAST(s.FechaConsulta AS datetime2(3)) AS Fecha", sql);   // RUES guarda hora: el sello es datetime
        Assert.Contains("ORDER BY s.FechaConsulta DESC, e.Enc DESC", sql);
        Assert.Contains("WHERE d.Fuente = 'RUES'", sql);
        Assert.Contains(CatalogoFuentesLocalizacion.Buscar("RUES")!.PredicadoEncontrado, sql);

        var nuevaEps = SqlEnv.UltimaFilaFuente(CatalogoFuentesLocalizacion.Buscar("NuevaEps")!);
        Assert.Contains("TRY_CAST(s.TitularID AS bigint) = d.TitularID", nuevaEps);

        var metricas = SqlEnv.Metricas(Prod, [CatalogoFuentesLocalizacion.Buscar("Fosyga")!, CatalogoFuentesLocalizacion.Buscar("Sura")!], conPagina: false);
        Assert.Contains("WHERE d.Fuente = 'Fosyga'", metricas);
        Assert.Contains("WHERE d.Fuente = 'Sura'", metricas);
        Assert.DoesNotContain("WHERE d.Fuente = 'RUES'", metricas);                 // solo las fuentes de los lotes pedidos
        Assert.Contains("u.Fecha > d.Desde OR (d.Inclusivo = 1 AND u.Fecha = d.Desde)", metricas);
        Assert.DoesNotContain("OFFSET @Offset", metricas);
        Assert.Contains("OFFSET @Offset", SqlEnv.Metricas(Prod, [CatalogoFuentesLocalizacion.Buscar("Fosyga")!], conPagina: true));
    }

    [Fact]
    public void Cola_SQL_solo_con_fuentes_Funcionales_del_ultimo_corte_OK()
    {
        var sql = SqlEnv.ColaUltimoCorte(Prod, GeneradorSqlSugerencias.Funcionales);
        Assert.Contains("WHERE Estado = 'OK' ORDER BY CorteID DESC", sql);
        foreach (var f in GeneradorSqlSugerencias.Funcionales) Assert.Contains($"'{f.Clave}'", sql);
        Assert.DoesNotContain("'Camara'", sql);
        Assert.DoesNotContain("'DatosClaro'", sql);
    }

    [Fact]
    public void Cargues_del_harness_no_cuentan_como_retorno()
    {
        Assert.Contains("Usuario NOT LIKE @PatronHarness", SqlEnv.CarguesOk);
        Assert.Contains("Estado = 'OK'", SqlEnv.CarguesOk);
    }
}
