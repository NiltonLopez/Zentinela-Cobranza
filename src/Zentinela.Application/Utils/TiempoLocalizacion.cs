using System.Globalization;

namespace Zentinela.Application.Utils;

/// <summary>
/// "Ahora" de los Informes de Localización (ADR-040, etapa 4): la hora del negocio (Colombia), no la del
/// equipo donde corre la API. En producción la API corre en el servidor de la BD (misma zona); el equipo de
/// desarrollo está en otra (medido el 2026-09-26: 7 h de diferencia con <c>SYSDATETIME()</c> de la BD). Con la
/// zona fija, la regla de las 13:00, el recálculo de las 13:05 y la <c>FechaCorte</c> que se guarda coinciden
/// con el reloj de la BD (<c>FechaEnvio</c>, <c>FechaFin</c> de los cargues) en cualquier equipo.
/// </summary>
public static class ZonaHorariaLocalizacion
{
    /// <summary>La zona configurada (id IANA o de Windows); si no existe en el equipo, la local.</summary>
    public static TimeZoneInfo Resolver(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        var limpio = id.Trim();
        if (Buscar(limpio) is { } z) return z;
        // Un id IANA en un Windows sin ICU (o al revés): se intenta la traducción.
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(limpio, out var win) && Buscar(win) is { } zw) return zw;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(limpio, out var iana) && Buscar(iana) is { } zi) return zi;
        return TimeZoneInfo.Local;
    }

    /// <summary>Hora actual en la zona, como <see cref="DateTime"/> sin zona (la que se compara y se guarda).</summary>
    public static DateTime Ahora(TimeProvider reloj, TimeZoneInfo zona) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime, DateTimeKind.Unspecified);

    private static TimeZoneInfo? Buscar(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}

/// <summary>Qué hacer en un intento del recálculo programado.</summary>
public enum DecisionCorteProgramado
{
    /// <summary>Pedir el corte y esperar a que termine.</summary>
    Calcular,
    /// <summary>Ya hay un corte OK posterior a las 13:00 de hoy (lo calculó un cargue, una lectura o el botón): nada que hacer.</summary>
    YaHayCorte,
    /// <summary>El ETL de gestión está reescribiendo UltimoEstadoTitular: el saldo saldría a medias. Reintentar.</summary>
    EsperarEtl,
    /// <summary>El usuario aún no aplicó el script: aviso y hasta mañana.</summary>
    SinTablas,
}

/// <summary>Lo que el recálculo programado necesita saber de la BD antes de pedir un corte.</summary>
/// <param name="EtlEnCurso">El último intento del ETL de gestión está EN CURSO y no parece colgado.</param>
public sealed record EstadoCorteProgramado(bool TablasExisten, DateTime? UltimoCorteOk, bool EtlEnCurso);

/// <summary>
/// Reglas del recálculo programado (ADR-040, etapa 4). Sin I/O para probarlas con un reloj falso
/// (<c>RecalculoProgramadoTests</c>).
/// </summary>
public static class ProgramaRecalculoLocalizacion
{
    public static readonly TimeSpan HoraPorDefecto = new(13, 5, 0);

    /// <summary>"HH:mm" (00:00–23:59). Inválido = <see cref="HoraPorDefecto"/> y false.</summary>
    public static bool TryParseHora(string? texto, out TimeSpan hora)
    {
        hora = HoraPorDefecto;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        if (!TimeSpan.TryParseExact(texto.Trim(), ["hh\\:mm", "h\\:mm"], CultureInfo.InvariantCulture, out var h)) return false;
        if (h < TimeSpan.Zero || h >= TimeSpan.FromDays(1)) return false;
        hora = h;
        return true;
    }

    /// <summary>La próxima vez que toca: hoy a <paramref name="hora"/> si aún no llegó; si no, mañana.</summary>
    public static DateTime ProximaEjecucion(DateTime ahora, TimeSpan hora)
    {
        var hoy = ahora.Date.Add(hora);
        return ahora < hoy ? hoy : hoy.AddDays(1);
    }

    /// <summary>
    /// ¿Calcular ahora? Sin tablas no se intenta. Si ya hay un corte OK posterior al corte de las 13:00 de hoy
    /// (<see cref="ReglaCorteLocalizacion.UltimoCorteProgramado"/>) no se duplica. Con el ETL de gestión en curso
    /// se espera (el corte lee el saldo de UltimoEstadoTitular).
    /// </summary>
    public static DecisionCorteProgramado Decidir(EstadoCorteProgramado e, DateTime ahora)
    {
        if (!e.TablasExisten) return DecisionCorteProgramado.SinTablas;
        if (e.UltimoCorteOk is { } ok && ok >= ReglaCorteLocalizacion.UltimoCorteProgramado(ahora))
            return DecisionCorteProgramado.YaHayCorte;
        if (e.EtlEnCurso) return DecisionCorteProgramado.EsperarEtl;
        return DecisionCorteProgramado.Calcular;
    }
}

/// <summary>Cómo terminó una ronda de cálculo del coordinador (lo que espera el recálculo programado).</summary>
public enum ResultadoRondaCorte
{
    Ok,
    /// <summary>Otro proceso (otra instancia de la API) tenía un corte EN CURSO: no se calculó.</summary>
    Omitido,
    SinTablas,
    Error,
}
