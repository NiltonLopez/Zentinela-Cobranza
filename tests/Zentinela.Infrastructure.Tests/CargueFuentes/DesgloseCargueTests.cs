using Zentinela.Application;
using Zentinela.Application.DTOs.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Desglose de los chips del Cargue de Fuentes: conteos exactos, tope de 10 ejemplos por
/// grupo, tope de grupos distintos y la invariante "la suma de los grupos = el número del chip".
/// </summary>
public class DesgloseCargueTests
{
    private static FilaCargue FilaDe(ContextoCargue ctx, int linea, long titular) => new()
    {
        Linea = linea, TitularID = titular, Datos = new object?[ctx.Definicion.ColumnasDato.Count], Extra = [],
    };

    // ── Acumulador ──────────────────────────────────────────────────────────

    [Fact]
    public void Conteo_exacto_aunque_los_ejemplos_se_corten_en_diez()
    {
        var acc = new AcumuladorGrupos(TonoDesglose.Aviso);
        for (var i = 1; i <= 25; i++) acc.Sumar("Celular: celular inválido anulado", 1, i + 1, 1_000_000 + i, "123");

        var g = Assert.Single(acc.Grupos());
        Assert.Equal(25, g.Cantidad);
        Assert.Equal(25, acc.Total);
        Assert.Equal(DesgloseCargue.MaxEjemplos, g.Ejemplos.Count);
        Assert.Equal("Fila 2 · 1000001 · 123", g.Ejemplos[0]);
        Assert.Equal(TonoDesglose.Aviso, g.Tono);
    }

    [Fact]
    public void Pasado_el_tope_de_grupos_lo_demas_va_a_Otros_con_conteo_exacto()
    {
        var acc = new AcumuladorGrupos(TonoDesglose.Aviso);
        var tope = CatalogoCargueFuentes.MaxMensajesAgrupados;
        for (var i = 0; i < tope + 50; i++)
        {
            acc.Sumar($"Cartera 'C{i}' no encontrada", 1, 10 + i, 5_000_000 + i);
            acc.Sumar($"Cartera 'C{i}' no encontrada", 1, 10 + i, 5_000_000 + i);
        }

        var grupos = acc.Grupos();
        Assert.Equal(tope + 1, grupos.Count);
        Assert.Equal(2 * (tope + 50), grupos.Sum(g => g.Cantidad));
        var otros = grupos[^1];
        Assert.Equal(AcumuladorGrupos.TituloOtros, otros.Titulo);
        Assert.Equal(100, otros.Cantidad);
        Assert.Equal(DesgloseCargue.MaxEjemplos, otros.Ejemplos.Count);
        Assert.Contains($"Cartera 'C{tope}' no encontrada", otros.Ejemplos[0]);   // el ejemplo conserva el motivo real
        Assert.Equal(tope + 1, acc.Agrupados().Count);
    }

    [Fact]
    public void Titulos_sin_numero_de_fila_y_ejemplos_recortados()
    {
        Assert.Equal("Fila 12 · 1234567 · x", DesgloseCargue.Ejemplo(12, 1_234_567, "x"));
        Assert.Equal("1234567", DesgloseCargue.Ejemplo(0, 1_234_567, null));
        Assert.Equal("Fila 3", DesgloseCargue.Ejemplo(3, null, null));
        var largo = DesgloseCargue.Ejemplo(0, null, new string('A', 200));
        Assert.Equal(80, largo.Length);
        Assert.EndsWith("…", largo);
        Assert.Equal("(vacío)", DesgloseCargue.Valor("   "));
    }

    // ── Contexto ────────────────────────────────────────────────────────────

    [Fact]
    public void Advertir_usa_la_fila_en_curso_de_ejemplo_y_Excluir_cuenta_una_vez()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        ctx.EnFila(7, 1_234_567);
        ctx.Advertir("Email: email con formato inválido anulado", "correo-sin-arroba.com");
        var fila = FilaDe(ctx, 9, 7_654_321);
        ctx.Excluir(fila, "Titular nuevo sin nombre en el archivo");
        ctx.Excluir(fila, "Otro motivo");   // ya tenía motivo: no cuenta
        ctx.ExcluirCruda("Documento inválido o vacío (se esperan de 5 a 15 dígitos)", 4, "documento ABC");

        var aviso = Assert.Single(ctx.Desglose.Advertencias.Grupos());
        Assert.Equal("Fila 7 · 1234567 · correo-sin-arroba.com", Assert.Single(aviso.Ejemplos));
        Assert.Equal(1, ctx.TotalAdvertencias);
        Assert.Equal(2, ctx.Errores().Sum(e => e.Cantidad));
        Assert.Contains(ctx.Desglose.Excluidas.Grupos(), g => g.Ejemplos.SequenceEqual(["Fila 4 · documento ABC"]));
        Assert.Contains(ctx.Desglose.Excluidas.Grupos(), g => g.Ejemplos.SequenceEqual(["Fila 9 · 7654321"]));
    }

    [Fact]
    public void SinRelacion_guarda_el_motivo_en_la_fila()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        var fila = FilaDe(ctx, 5, 1_234_567);
        ctx.SinRelacion(fila, "Cartera sin líder: la fila entra sin relación", "12");

        ctx.Desglose.SinRelacion([fila, FilaDe(ctx, 6, 2_345_678)]);

        var grupos = ctx.Desglose.SinCarteraLider.Grupos();
        Assert.Equal(2, grupos.Sum(g => g.Cantidad));
        Assert.Contains(grupos, g => g.Titulo == "Cartera sin líder: la fila entra sin relación" && g.Tono == TonoDesglose.Aviso);
        Assert.Contains(grupos, g => g.Titulo == DesgloseCargue.TituloSinColumnasRelacion && g.Tono == TonoDesglose.Info);
        Assert.Equal(1, ctx.TotalAdvertencias);
    }

    // ── Chips del preview ───────────────────────────────────────────────────

    /// <summary>
    /// Corrida sintética sin BD, 10 filas leídas: 2 excluidas (filas 3 y 4), un titular en 3
    /// filas (7, 8, 9: 2 duplicadas, se conserva la 9), la fila 6 que se expande en 3 filas
    /// destino (+2) y 4 filas normales → 8 candidatas. Insertar/refrescar/relaciones en 0.
    /// </summary>
    private static (ContextoCargue Ctx, PreviewCargueDto P) CorridaSintetica()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        var d = ctx.Desglose;
        ctx.ExcluirCruda("Fila vacía", 3);
        ctx.Excluir(FilaDe(ctx, 4, 1_111_111), "Titular nuevo sin nombre en el archivo");
        var repetidas = new[] { FilaDe(ctx, 7, 2_222_222), FilaDe(ctx, 8, 2_222_222), FilaDe(ctx, 9, 2_222_222) };
        d.TitularRepetido(repetidas, repetidas[2]);
        var expandidas = new List<FilaCargue> { FilaDe(ctx, 6, 3_333_333), FilaDe(ctx, 6, 3_333_333), FilaDe(ctx, 6, 3_333_333) };
        d.Expansion(expandidas);
        var candidatas = Enumerable.Range(0, 4).Select(i => FilaDe(ctx, 10 + i, 4_000_000 + i))
            .Append(repetidas[2]).Concat(expandidas).ToList();
        candidatas[0].NombreIrrecuperable = true;
        candidatas[0].Nombre = "PI?A";
        d.Candidatas(candidatas);
        d.Irrecuperables(candidatas);
        d.NombreReparado(4_000_001, "PIÃ‘A", "PIÑA");
        d.TitularNuevo(4_000_002, "ANA");
        d.SinRelacion(candidatas);
        ctx.EnFila(11, 4_000_001);
        ctx.Advertir("Celular: celular inválido anulado", "123");

        var p = new PreviewCargueDto
        {
            TotalLeidas = 10, Excluidas = 2, DuplicadasEnArchivo = 2, AgregadasPorExpansion = 2, Candidatas = 8,
            TitularesNuevos = 1, NombresReparados = 1, NombresIrrecuperables = 1, FilasSinCarteraLider = 8,
            Advertencias = ctx.Advertencias(),
        };
        return (ctx, p);
    }

    [Fact]
    public void Cada_chip_del_preview_suma_su_numero()
    {
        var (ctx, p) = CorridaSintetica();

        var chips = ctx.Desglose.ParaPreview(p);

        Assert.Equal(Enum.GetValues<ChipCargue>().Length, chips.Count);
        Assert.Empty(DesgloseCargue.Incoherencias(chips, c => DesgloseCargue.NumeroDelChip(p, c)));
        Assert.All(chips.SelectMany(c => c.Grupos), g => Assert.True(g.Ejemplos.Count <= DesgloseCargue.MaxEjemplos));
        Assert.All(chips.SelectMany(c => c.Grupos), g => Assert.DoesNotMatch(@"(Fila|Línea) \d", g.Titulo));
    }

    [Fact]
    public void Leidas_sin_grupos_negativos_con_expansion()
    {
        var (ctx, p) = CorridaSintetica();

        var leidas = ctx.Desglose.ParaPreview(p).Single(c => c.Clave == ChipCargue.Leidas);

        Assert.Equal(10, leidas.Grupos.Sum(g => g.Cantidad));
        Assert.All(leidas.Grupos, g => Assert.True(g.Cantidad > 0));
        Assert.Collection(leidas.Grupos,
            g => { Assert.Equal(DesgloseCargue.TituloExcluidasLeidas, g.Titulo); Assert.Equal(2, g.Cantidad); },
            g =>
            {
                Assert.Equal(DesgloseCargue.TituloDuplicadasLeidas, g.Titulo);
                Assert.Equal(2, g.Cantidad);
                Assert.Equal("2222222 × 3 filas (se conservó la fila 9)", g.Ejemplos.Single());
            },
            g =>
            {
                // candidatas (8) − agregadas por expansión (2); el título dice cuántas filas destino generan.
                Assert.Equal("Pasaron a la carga (generan 8 filas destino, una por placa)", g.Titulo);
                Assert.Equal(6, g.Cantidad);
                Assert.Equal(6, g.Ejemplos.Count);   // una por fila del archivo: las agregadas no repiten la fila 6
                Assert.Single(g.Ejemplos, e => e == "Fila 6 · 3333333");
            });
    }

    [Fact]
    public void Leidas_sin_expansion_no_menciona_filas_destino()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Sura);
        ctx.Desglose.Candidatas([FilaDe(ctx, 2, 1_234_567)]);
        var p = new PreviewCargueDto { TotalLeidas = 1, Candidatas = 1, Advertencias = [] };

        var leidas = ctx.Desglose.ParaPreview(p).Single(c => c.Clave == ChipCargue.Leidas);

        Assert.Equal(DesgloseCargue.TituloPasaron, Assert.Single(leidas.Grupos).Titulo);
    }

    /// <summary>
    /// El caso en que "candidatas − agregadas" sería negativo: una fila con 3 placas de un
    /// titular nuevo sin nombre (sus 3 filas destino se excluyen en los cruces). La fila del
    /// archivo cuenta UNA vez como excluida y ningún grupo queda negativo.
    /// </summary>
    [Fact]
    public void Fila_expandida_y_luego_excluida_cuenta_una_vez()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Simit);
        var expandidas = new List<FilaCargue> { FilaDe(ctx, 2, 1_234_567), FilaDe(ctx, 2, 1_234_567), FilaDe(ctx, 2, 1_234_567) };
        ctx.Desglose.Expansion(expandidas);
        foreach (var f in expandidas) ctx.Excluir(f, "Titular nuevo sin nombre en el archivo");
        ctx.Desglose.Candidatas([]);
        var p = new PreviewCargueDto { TotalLeidas = 1, Excluidas = 3, AgregadasPorExpansion = 2, Advertencias = [] };

        var chips = ctx.Desglose.ParaPreview(p);

        var leidas = chips.Single(c => c.Clave == ChipCargue.Leidas);
        Assert.Equal(1, Assert.Single(leidas.Grupos, g => g.Titulo == DesgloseCargue.TituloExcluidasLeidas).Cantidad);
        Assert.Equal(3, chips.Single(c => c.Clave == ChipCargue.Excluidas).Grupos.Sum(g => g.Cantidad));   // el chip sigue en filas destino
    }

    [Fact]
    public void Un_grupo_negativo_es_incoherente()
    {
        var chip = new DesgloseChipDto
        {
            Clave = ChipCargue.Leidas,
            Grupos = [new() { Titulo = "a", Cantidad = 5 }, new() { Titulo = "b", Cantidad = -1 }],
        };

        Assert.Contains("negativa", Assert.Single(DesgloseCargue.Incoherencias([chip], _ => 4)));
    }

    [Fact]
    public void Nombres_agrupa_reparados_e_irrecuperables()
    {
        var (ctx, p) = CorridaSintetica();

        var nombres = ctx.Desglose.ParaPreview(p).Single(c => c.Clave == ChipCargue.NombresReparados);

        Assert.Collection(nombres.Grupos,
            g => { Assert.Equal(DesgloseCargue.TituloReparado, g.Titulo); Assert.Equal("4000001 · PIÃ‘A → PIÑA", g.Ejemplos.Single()); },
            g => { Assert.Equal(DesgloseCargue.TituloIrrecuperable, g.Titulo); Assert.Equal(TonoDesglose.Aviso, g.Tono); });
    }

    [Fact]
    public void Preview_incoherente_lanza()
    {
        var (ctx, p) = CorridaSintetica();
        p.Excluidas = 3;   // un motivo que el desglose no vio: una fila perdida en silencio

        var ex = Assert.Throws<InvalidOperationException>(() => ctx.Desglose.ParaPreview(p));
        Assert.Contains("Excluidas", ex.Message);
    }

    // ── Resultado ───────────────────────────────────────────────────────────

    [Fact]
    public void Resultado_se_concilia_con_los_conteos_finales()
    {
        var (ctx, _) = CorridaSintetica();
        var r = new ResultadoCargueDto
        {
            Insertadas = 7, Refrescadas = 0, TitularesNuevos = 2, NombresReparados = 1, NombresIrrecuperables = 1,
            Advertencias = ctx.TotalAdvertencias,
        };

        var chips = ctx.Desglose.ParaResultado(r);

        Assert.Equal(DesgloseCargue.ChipsResultado, chips.Select(c => c.Clave));
        Assert.Empty(DesgloseCargue.Incoherencias(chips, c => DesgloseCargue.NumeroDelChip(r, c)));
        // Un solo grupo: se ajusta su cantidad. Ninguno: grupo de ajuste.
        Assert.Equal(2, Assert.Single(chips.Single(c => c.Clave == ChipCargue.TitularesNuevos).Grupos).Cantidad);
        Assert.Equal(DesgloseCargue.TituloAjuste, Assert.Single(chips.Single(c => c.Clave == ChipCargue.Insertadas).Grupos).Titulo);
    }

    [Fact]
    public void Conciliar_con_varios_grupos_agrega_el_ajuste()
    {
        var chip = new DesgloseChipDto
        {
            Clave = ChipCargue.Insertadas,
            Grupos = [new() { Titulo = "a", Cantidad = 3 }, new() { Titulo = "b", Cantidad = 2 }],
        };

        DesgloseCargue.Conciliar(chip, 4);

        Assert.Equal(4, chip.Grupos.Sum(g => g.Cantidad));
        Assert.Equal(-1, chip.Grupos[^1].Cantidad);
    }

    // ── SQL de los ejemplos que salen de la BD ──────────────────────────────

    [Fact]
    public void Sql_de_ejemplos_usa_TOP_por_grupo_y_solo_toca_el_destino_para_insertar()
    {
        var sura = new DesgloseCargue(CatalogoCargueFuentes.Sura);

        var sinExtras = sura.SqlBd(conPurga: false, conRelaciones: false);
        var completo = sura.SqlBd(conPurga: true, conRelaciones: true);

        Assert.Contains($"TOP ({DesgloseCargue.MaxEjemplos})", sinExtras);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sinExtras, @"\[dbo\]\.\[Sura\]"));
        Assert.DoesNotContain("#DesgloseRelaciones", sinExtras);
        Assert.Contains("#DesgloseRelaciones", completo);
        Assert.Contains("lc.Activo = 1", completo);
        // NuevaEps guarda el documento como texto: el lado de #Carga se convierte.
        Assert.Contains("CONVERT(varchar(30), c.TitularID)", new DesgloseCargue(CatalogoCargueFuentes.NuevaEps).SqlBd(false, false));
    }

    [Fact]
    public void Columnas_de_ejemplo_prefieren_contacto()
    {
        Assert.Equal(["NumeroCelular", "Email"], new DesgloseCargue(CatalogoCargueFuentes.Sura).ColumnasDeEjemplo().Select(c => c.Nombre));
        Assert.Equal(["Placa"], new DesgloseCargue(CatalogoCargueFuentes.Simit).ColumnasDeEjemplo().Select(c => c.Nombre));
    }
}
