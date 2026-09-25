using Zentinela.Application.Settings;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Despliega por fuente lo que <see cref="GeneradorSqlSugerencias"/> devuelve por titular en máscaras de bits
/// (ADR-040, etapa 2): los conteos de los KPIs y las filas del top. Sin BD: se prueba con datos en memoria.
/// </summary>
internal static class DespliegueSugerencias
{
    /// <summary>Una fuente pedida y su bit en las máscaras.</summary>
    public sealed record FuenteBit(string Clave, long Bit);

    /// <summary>Una combinación de máscaras del result set 2 y cuántos titulares la tienen.</summary>
    public sealed record GrupoMascaras(
        MotivoExclusionSugerencia? MotivoTitular, bool SinSaldo, long EnvMask, long Cat1, long Cat2, long Cat3,
        int Titulares, int ConFresco, decimal Saldo);

    /// <summary>Un titular del result set 4. <see cref="UltimasConsultas"/> va en el orden de las fuentes pedidas.</summary>
    public sealed record TitularTop(
        long TitularID, string? Nombre, int LiderID, string? Lider, decimal? Saldo, string? UltimoPerfil, DateTime? FechaUltimaGestion,
        int PtsPerfil, int PtsSaldo, int PtsGestion, int PtsNuncaFuncional, int PtsContacto, int PtsBase,
        long EncMask, bool BuscadoFuncional, bool ConFresco, long EnvMask, long Cat1, long Cat2, long Cat3,
        IReadOnlyList<DateTime?> UltimasConsultas);

    /// <summary>Un conteo por fuente, categoría y motivo (null = candidato que queda).</summary>
    public sealed record Conteo(string Fuente, CategoriaSugerencia Categoria, MotivoExclusionSugerencia? Motivo, int Filas, int ConFresco, decimal Saldo);

    /// <summary>Una fila del top: el titular, la fuente (índice en las pedidas), su categoría y su prioridad.</summary>
    public sealed record FilaTop(TitularTop Titular, int IndiceFuente, string Fuente, CategoriaSugerencia Categoria,
                                 int PtsNuncaFuente, int PtsNoEncontradoViejo, int Prioridad);

    /// <summary>Categoría de (titular, fuente) según las máscaras; null = no es candidato en esa fuente.</summary>
    public static CategoriaSugerencia? Categoria(long cat1, long cat2, long cat3, long bit) =>
        (cat1 & bit) != 0 ? CategoriaSugerencia.NuncaBuscado
        : (cat2 & bit) != 0 ? CategoriaSugerencia.NoEncontradoViejo
        : (cat3 & bit) != 0 ? CategoriaSugerencia.EncontradoViejo
        : null;

    /// <summary>
    /// Motivo de exclusión de (titular, fuente), con la precedencia de <see cref="PrioridadSugerencia.Excluir"/>:
    /// lo del titular (perfil, promesa), luego "ya enviado a esa fuente" y al final "sin saldo".
    /// </summary>
    public static MotivoExclusionSugerencia? Motivo(MotivoExclusionSugerencia? motivoTitular, bool sinSaldo, long envMask, long bit) =>
        motivoTitular
        ?? ((envMask & bit) != 0 ? MotivoExclusionSugerencia.EnviadoReciente
          : sinSaldo ? MotivoExclusionSugerencia.SinSaldo
          : null);

    /// <summary>Conteos por fuente, categoría y motivo.</summary>
    public static List<Conteo> Conteos(IEnumerable<GrupoMascaras> grupos, IReadOnlyList<FuenteBit> fuentes)
    {
        var acumulado = new Dictionary<(string, CategoriaSugerencia, MotivoExclusionSugerencia?), (int Filas, int ConFresco, decimal Saldo)>();
        foreach (var g in grupos)
            foreach (var f in fuentes)
            {
                if (Categoria(g.Cat1, g.Cat2, g.Cat3, f.Bit) is not { } cat) continue;
                var motivo = Motivo(g.MotivoTitular, g.SinSaldo, g.EnvMask, f.Bit);
                var clave = (f.Clave, cat, motivo);
                var a = acumulado.GetValueOrDefault(clave);
                acumulado[clave] = (a.Filas + g.Titulares, a.ConFresco + (motivo is null ? g.ConFresco : 0), a.Saldo + g.Saldo);
            }
        return acumulado.Select(kv => new Conteo(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.Filas, kv.Value.ConFresco, kv.Value.Saldo)).ToList();
    }

    /// <summary>
    /// Titulares distintos por motivo de exclusión y los que quedan como candidatos (con su saldo). Todos los
    /// titulares de una combinación de máscaras comparten datos, así que se cuentan por combinación. Un titular
    /// sin exclusión propia puede tener fuentes ya enviadas y otras no: cuenta en "enviado" y en lo que le quede.
    /// </summary>
    public static (Dictionary<MotivoExclusionSugerencia, int> PorMotivo, int Candidatos, decimal SaldoCandidatos) Titulares(
        IEnumerable<GrupoMascaras> grupos)
    {
        var porMotivo = new Dictionary<MotivoExclusionSugerencia, int>();
        int candidatos = 0;
        decimal saldo = 0;
        void Sumar(MotivoExclusionSugerencia m, int n) => porMotivo[m] = porMotivo.GetValueOrDefault(m) + n;

        foreach (var g in grupos)
        {
            var cand = g.Cat1 | g.Cat2 | g.Cat3;
            if (cand == 0) continue;
            if (g.MotivoTitular is { } m) { Sumar(m, g.Titulares); continue; }
            if ((cand & g.EnvMask) != 0) Sumar(MotivoExclusionSugerencia.EnviadoReciente, g.Titulares);
            if ((cand & ~g.EnvMask) == 0) continue;
            if (g.SinSaldo) Sumar(MotivoExclusionSugerencia.SinSaldo, g.Titulares);
            else { candidatos += g.Titulares; saldo += g.Saldo; }
        }
        return (porMotivo, candidatos, saldo);
    }

    /// <summary>
    /// Las <paramref name="top"/> mejores filas (titular, fuente) no excluidas de los titulares recibidos, por
    /// prioridad; en empate, más saldo, menor TitularID y la fuente en orden alfabético. Los titulares vienen
    /// ya filtrados (sin exclusión propia) y son los <paramref name="top"/> mejores por su mejor fila: eso
    /// basta para que estas sean las mejores filas de todo el par (ver <see cref="GeneradorSqlSugerencias"/>).
    /// </summary>
    public static List<FilaTop> Top(IEnumerable<TitularTop> titulares, IReadOnlyList<FuenteBit> fuentes, int top,
                                    SugerenciasLocalizacionSettings pesos)
    {
        var filas = new List<FilaTop>();
        foreach (var t in titulares)
            for (var i = 0; i < fuentes.Count; i++)
            {
                var f = fuentes[i];
                if ((t.EnvMask & f.Bit) != 0) continue;
                if (Categoria(t.Cat1, t.Cat2, t.Cat3, f.Bit) is not { } cat) continue;
                var nunca = cat == CategoriaSugerencia.NuncaBuscado ? pesos.PuntosNuncaFuente : 0;
                var noEnc = cat == CategoriaSugerencia.NoEncontradoViejo ? pesos.PuntosNoEncontradoViejo : 0;
                filas.Add(new FilaTop(t, i, f.Clave, cat, nunca, noEnc, t.PtsBase + nunca + noEnc));
            }

        return filas
            .OrderByDescending(r => r.Prioridad)
            .ThenByDescending(r => r.Titular.Saldo ?? 0)
            .ThenBy(r => r.Titular.TitularID)
            .ThenBy(r => r.Fuente, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }
}
