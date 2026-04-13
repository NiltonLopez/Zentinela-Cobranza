using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Calcula las métricas del Calificador de Cartera a partir de vw_BaseMadre.
/// La mayor parte del cómputo se hace en memoria tras un único ToListAsync().
/// </summary>
public class CalificadorCarteraService : ICalificadorCarteraService
{
    private static readonly string[] FuentesCelular =
        ["Cajacopi", "Camara", "SaludTotal", "Positiva", "Tigo", "SOS"];

    private readonly CallCenterDbContext _db;

    public CalificadorCarteraService(CallCenterDbContext db)
    {
        _db = db;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.Rol == Roles.Lider && contexto.LiderID.HasValue)
        {
            // Líder: solo sus combinaciones expuestas en vw_BaseMadre
            var lid = contexto.LiderID.Value;
            return await _db.BaseMadreRows
                .Where(b => b.LiderID == lid)
                .Select(b => new { b.CarteraID, b.LiderID })
                .Distinct()
                .Join(_db.Carteras,
                    b => b.CarteraID,
                    c => c.ID,
                    (b, c) => new { b.CarteraID, b.LiderID, c.NombreCartera })
                .Join(_db.Lideres,
                    x => x.LiderID,
                    l => l.ID,
                    (x, l) => new CarteraLiderDropdownDto
                    {
                        CarteraID = x.CarteraID,
                        LiderID   = x.LiderID,
                        Etiqueta  = x.NombreCartera + " — " + l.Nombre,
                    })
                .OrderBy(o => o.Etiqueta)
                .AsNoTracking()
                .ToListAsync();
        }

        // Director / Admin: todas las asignaciones activas de Lider_Cartera
        return await _db.LiderCarteras
            .Select(lc => new CarteraLiderDropdownDto
            {
                CarteraID = lc.CarteraID,
                LiderID   = lc.LiderID,
                Etiqueta  = lc.Cartera.NombreCartera + " — " + lc.Lider.Nombre,
            })
            .OrderBy(o => o.Etiqueta)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Métricas ──────────────────────────────────────────────────────────────

    public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID)
    {
        // Carteras grandes pueden tardar; se amplía el timeout solo para esta operación.
        _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var filas = await _db.BaseMadreRows
            .Where(b => b.CarteraID == carteraID && b.LiderID == liderID)
            .AsNoTracking()
            .ToListAsync();

        var total = filas.Count;
        if (total == 0)
            return new CalificadorCarteraDto { TotalTitulares = 0 };

        // ── Bloque 2: gestión ─────────────────────────────────────────────────
        var gestionDisponible = filas.Any(b =>
            !string.IsNullOrEmpty(b.Saldo) && b.Saldo != "0");

        var saldos = filas
            .Where(b => decimal.TryParse(b.Saldo, NumberStyles.Any,
                CultureInfo.InvariantCulture, out _))
            .Select(b => decimal.Parse(b.Saldo!, NumberStyles.Any, CultureInfo.InvariantCulture))
            .ToList();

        var saldoTotal    = saldos.Sum();
        var saldoPromedio = saldos.Count > 0 ? saldos.Average() : 0m;

        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var titularesGestionados = filas.Count(b =>
            !string.IsNullOrEmpty(b.UltimaFechaGestion) && b.UltimaFechaGestion != "0");

        var titularesConAcuerdoVigente = filas.Count(b =>
            DateOnly.TryParseExact(b.FechaPromesa, "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fp) && fp >= hoy);

        var valorTotalPromesas = filas
            .Where(b => !string.IsNullOrEmpty(b.ValorPromesa) && b.ValorPromesa != "0"
                && decimal.TryParse(b.ValorPromesa, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out _))
            .Sum(b => decimal.Parse(b.ValorPromesa!, NumberStyles.Any, CultureInfo.InvariantCulture));

        // ── Bloque 3: rotación ───────────────────────────────────────────────
        var rotaciones = filas
            .Where(b => int.TryParse(b.Rotacion, out var r) && r > 0)
            .Select(b => int.Parse(b.Rotacion!))
            .ToList();

        var rotacionPromedio = rotaciones.Count > 0
            ? Math.Round((decimal)rotaciones.Average(), 1)
            : (decimal?)null;

        var desgloseRotacion = rotaciones
            .GroupBy(r => r)
            .Select(g => new DesglosePar
            {
                Etiqueta   = g.Key.ToString(),
                Cantidad   = g.Count(),
                Porcentaje = total > 0
                    ? Math.Round((decimal)g.Count() / total * 100, 1) : 0,
            })
            .OrderBy(d => int.Parse(d.Etiqueta))
            .ToList();

        // ── Bloque 4: tipificación último perfil ──────────────────────────────
        var desglosePerfil = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.UltimoPerfil) && b.UltimoPerfil != "0")
                 .Select(b => b.UltimoPerfil!),
            total);

        // ── Bloque 4: Fosyga ──────────────────────────────────────────────────
        // Se considera localizado si devolvió al menos un campo con dato real.
        var titularesConFosyga = filas.Count(b =>
            (!string.IsNullOrEmpty(b.EPS)          && b.EPS          != "0") ||
            (!string.IsNullOrEmpty(b.Regimen)      && b.Regimen      != "0") ||
            (!string.IsNullOrEmpty(b.TipoAfiliado) && b.TipoAfiliado != "0"));

        var desgloseTipoAfiliado = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.TipoAfiliado) && b.TipoAfiliado != "0")
                 .Select(b => b.TipoAfiliado!),
            total);

        // ── Bloque 5: régimen ─────────────────────────────────────────────────
        var desgloseRegimen = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.Regimen) && b.Regimen != "0")
                 .Select(b => b.Regimen!),
            total);

        // ── Bloque 6: EPS (top 10 + Otras) ───────────────────────────────────
        var desgloseEPS = DesgloseConOtras(
            filas.Where(b => !string.IsNullOrEmpty(b.EPS) && b.EPS != "0")
                 .Select(b => b.EPS!),
            total, top: 10);

        // ── Bloque 7: departamento ────────────────────────────────────────────
        var desgloseDepartamento = Desglosar(
            filas.Where(b => !string.IsNullOrEmpty(b.Departamento) && b.Departamento != "0")
                 .Select(b => b.Departamento!),
            total);

        // ── Bloque 8: contactabilidad ─────────────────────────────────────────
        var conCelular       = filas.Count(TieneCelular);
        var conEmail         = filas.Count(TieneEmail);
        var conCelularYEmail = filas.Count(b => TieneCelular(b) && TieneEmail(b));
        var sinContacto      = filas.Count(b => !TieneCelular(b) && !TieneEmail(b));

        var desgloseFuentesCelular = FuentesCelular
            .Select(f =>
            {
                var cantidad = filas.Count(b => TieneCelularFuente(b, f));
                return new DesglosePar
                {
                    Etiqueta   = f,
                    Cantidad   = cantidad,
                    Porcentaje = total > 0 ? Math.Round((decimal)cantidad / total * 100, 1) : 0,
                };
            })
            .Where(d => d.Cantidad > 0)
            .OrderByDescending(d => d.Cantidad)
            .ToList();

        // ── Bloque 9: edad Registraduría ─────────────────────────────────────
        // Solo aparecen personas mayores de 33 años; null o 0 = no aparece en la fuente.
        var conEdad = filas
            .Where(b => b.EdadRegistraduria.HasValue && b.EdadRegistraduria.Value > 0)
            .ToList();

        var conEdadRegistraduria      = conEdad.Count;
        var sinEdadRegistraduria      = total - conEdadRegistraduria;
        var edadPromedioRegistraduria = conEdad.Count > 0
            ? Math.Round((decimal)conEdad.Average(b => b.EdadRegistraduria!.Value), 1)
            : (decimal?)null;

        // ── Bloque 10: localización por fuente ───────────────────────────────
        // Se consulta directo a cada tabla fuente (no la vista) para distinguir:
        //   · EstadoEn* = 1  → encontrado
        //   · EstadoEn* = 0  → buscado sin resultado
        //   · sin fila       → aún no se ha consultado esa fuente
        var localizacion = await ObtenerLocalizacionAsync(carteraID, liderID, total);

        return new CalificadorCarteraDto
        {
            TotalTitulares             = total,
            GestionDisponible          = gestionDisponible,
            TitularesGestionados       = titularesGestionados,
            SaldoTotal                 = saldoTotal,
            SaldoPromedio              = saldoPromedio,
            TitularesConAcuerdoVigente = titularesConAcuerdoVigente,
            ValorTotalPromesas         = valorTotalPromesas,
            RotacionPromedio           = rotacionPromedio,
            DesgloseRotacion           = desgloseRotacion,
            DesglosePerfil             = desglosePerfil,
            TitularesConFosyga         = titularesConFosyga,
            DesgloseTipoAfiliado       = desgloseTipoAfiliado,
            DesgloseRegimen            = desgloseRegimen,
            DesgloseEPS                = desgloseEPS,
            DesgloseDepartamento       = desgloseDepartamento,
            ConCelular                 = conCelular,
            ConEmail                   = conEmail,
            ConCelularYEmail           = conCelularYEmail,
            SinContacto                = sinContacto,
            DesgloseFuentesCelular     = desgloseFuentesCelular,
            ConEdadRegistraduria       = conEdadRegistraduria,
            SinEdadRegistraduria       = sinEdadRegistraduria,
            EdadPromedioRegistraduria  = edadPromedioRegistraduria,
            Localizacion               = localizacion,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TieneCelular(BaseMadreRow b) =>
        TieneCelularFuente(b, "Cajacopi")   ||
        TieneCelularFuente(b, "Camara")     ||
        TieneCelularFuente(b, "SaludTotal") ||
        TieneCelularFuente(b, "Positiva")   ||
        TieneCelularFuente(b, "Tigo")       ||
        TieneCelularFuente(b, "SOS");

    private static bool TieneCelularFuente(BaseMadreRow b, string fuente) => fuente switch
    {
        "Cajacopi"      => !string.IsNullOrEmpty(b.CelularCajacopi)      && b.CelularCajacopi      != "0",
        "Camara"        => !string.IsNullOrEmpty(b.CelularCamara)        && b.CelularCamara        != "0",
        "SaludTotal"    => !string.IsNullOrEmpty(b.CelularSaludTotal)    && b.CelularSaludTotal    != "0",
        "Positiva"      => !string.IsNullOrEmpty(b.CelularPositiva)      && b.CelularPositiva      != "0",
        "Tigo"          => !string.IsNullOrEmpty(b.CelularTigo)          && b.CelularTigo          != "0",
        "SOS"           => !string.IsNullOrEmpty(b.CelularSOS)           && b.CelularSOS           != "0",
        _               => false,
    };

    private static bool TieneEmail(BaseMadreRow b) =>
        (!string.IsNullOrEmpty(b.EmailCajacopi)      && b.EmailCajacopi      != "0") ||
        (!string.IsNullOrEmpty(b.EmailCamara)        && b.EmailCamara        != "0") ||
        (!string.IsNullOrEmpty(b.EmailSaludTotal)    && b.EmailSaludTotal    != "0") ||
        (!string.IsNullOrEmpty(b.EmailPositiva)      && b.EmailPositiva      != "0") ||
        (!string.IsNullOrEmpty(b.EmailTigo)          && b.EmailTigo          != "0");

    /// <summary>Agrupa por etiqueta, calcula cantidad y % sobre total.</summary>
    private static List<DesglosePar> Desglosar(
        IEnumerable<string> valores, int total) =>
        valores
            .GroupBy(v => v)
            .Select(g => new DesglosePar
            {
                Etiqueta   = g.Key,
                Cantidad   = g.Count(),
                Porcentaje = total > 0
                    ? Math.Round((decimal)g.Count() / total * 100, 1) : 0,
            })
            .OrderByDescending(d => d.Cantidad)
            .ToList();

    /// <summary>
    /// Base: vw_BaseMadre (filtrada por carteraID+liderID) → LEFT JOIN a cada tabla fuente.
    ///   encontrado    → tiene datos reales en la vista para esa fuente
    ///   no encontrado → sin datos en la vista PERO existe en la tabla fuente (fue buscado)
    ///   falta buscar  → sin datos en la vista Y sin fila en la tabla fuente (total - enc - noEnc)
    /// Usar la vista como base garantiza que enc y total coinciden con el resto del calificador.
    /// La vista aplana a "0" los sin-dato, por eso el "no encontrado" vs "falta buscar" se
    /// distingue mediante la presencia de fila en cada tabla fuente.
    /// </summary>
    private async Task<List<LocalizacionFuente>> ObtenerLocalizacionAsync(
        int carteraID, int liderID, int total)
    {
        // SQL Server no permite EXISTS dentro de SUM(CASE WHEN ...).
        // Solución: el subquery interno resuelve los EXISTS como columnas bit (0/1),
        // y el SELECT exterior solo hace SUM sobre valores simples.
        var r = await _db.Database.SqlQuery<LocalizacionResumenRaw>($"""
            SELECT
                -- Cajacopi
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCajacopi,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailCajacopi,  ''), '0') IS NOT NULL
                           OR (b.EdadCajacopi IS NOT NULL AND b.EdadCajacopi != 0)
                         THEN 1 ELSE 0 END) AS CajacopiEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCajacopi,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailCajacopi,  ''), '0') IS NULL
                          AND (b.EdadCajacopi IS NULL OR b.EdadCajacopi = 0)
                          AND b.EnCajacopi = 1
                         THEN 1 ELSE 0 END) AS CajacopiNoEnc,
                -- Camara
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCamara,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailCamara,  ''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS CamaraEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCamara,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailCamara,  ''), '0') IS NULL
                          AND b.EnCamara = 1
                         THEN 1 ELSE 0 END) AS CamaraNoEnc,
                -- SaludTotal
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSaludTotal,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailSaludTotal,  ''), '0') IS NOT NULL
                           OR (b.NITSaludTotal IS NOT NULL AND b.NITSaludTotal != 0)
                           OR NULLIF(NULLIF(b.EmpresaSaludTotal,''),'0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SaludTotalEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSaludTotal,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailSaludTotal,  ''), '0') IS NULL
                          AND (b.NITSaludTotal IS NULL OR b.NITSaludTotal = 0)
                          AND NULLIF(NULLIF(b.EmpresaSaludTotal,''),'0') IS NULL
                          AND b.EnSaludTotal = 1
                         THEN 1 ELSE 0 END) AS SaludTotalNoEnc,
                -- Fosyga
                SUM(CASE WHEN NULLIF(NULLIF(b.EPS,         ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.Regimen,     ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.TipoAfiliado,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS FosygaEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPS,         ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.Regimen,     ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.TipoAfiliado,''), '0') IS NULL
                          AND b.EnFosyga = 1
                         THEN 1 ELSE 0 END) AS FosygaNoEnc,
                -- Compensados (solo campos de texto: la vista puede devolver fechas por defecto)
                SUM(CASE WHEN NULLIF(NULLIF(b.EPSCompensados,         ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.TipoAfiliadoCompensados,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS CompensadosEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPSCompensados,         ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.TipoAfiliadoCompensados,''), '0') IS NULL
                          AND b.EnCompensados = 1
                         THEN 1 ELSE 0 END) AS CompensadosNoEnc,
                -- Positiva
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularPositiva,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailPositiva,  ''), '0') IS NOT NULL
                           OR (b.EdadPositiva IS NOT NULL AND b.EdadPositiva != 0)
                         THEN 1 ELSE 0 END) AS PositivaEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularPositiva,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailPositiva,  ''), '0') IS NULL
                          AND (b.EdadPositiva IS NULL OR b.EdadPositiva = 0)
                          AND b.EnPositiva = 1
                         THEN 1 ELSE 0 END) AS PositivaNoEnc,
                -- Tigo
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularTigo,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailTigo,  ''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS TigoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularTigo,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailTigo,  ''), '0') IS NULL
                          AND b.EnTigo = 1
                         THEN 1 ELSE 0 END) AS TigoNoEnc,
                -- SOS
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSOS,  ''), '0') IS NOT NULL
                           OR (b.NitSOS IS NOT NULL AND b.NitSOS != 0)
                           OR NULLIF(NULLIF(b.EmpresaSOS,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SosEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSOS,  ''), '0') IS NULL
                          AND (b.NitSOS IS NULL OR b.NitSOS = 0)
                          AND NULLIF(NULLIF(b.EmpresaSOS,''), '0') IS NULL
                          AND b.EnSOS = 1
                         THEN 1 ELSE 0 END) AS SosNoEnc,
                -- Sura (ARL)
                SUM(CASE WHEN (b.NITSura IS NOT NULL AND b.NITSura != 0)
                           OR NULLIF(NULLIF(b.EmpresaSura,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SuraEnc,
                SUM(CASE WHEN (b.NITSura IS NULL OR b.NITSura = 0)
                          AND NULLIF(NULLIF(b.EmpresaSura,''), '0') IS NULL
                          AND b.EnSura = 1
                         THEN 1 ELSE 0 END) AS SuraNoEnc,
                -- Positiva Empresa
                SUM(CASE WHEN (b.NITPositiva IS NOT NULL AND b.NITPositiva != 0)
                           OR NULLIF(NULLIF(b.EmpresaPositiva,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS PosEmpEnc,
                SUM(CASE WHEN (b.NITPositiva IS NULL OR b.NITPositiva = 0)
                          AND NULLIF(NULLIF(b.EmpresaPositiva,''), '0') IS NULL
                          AND b.EnPosEmp = 1
                         THEN 1 ELSE 0 END) AS PosEmpNoEnc,
                -- RUES
                SUM(CASE WHEN NULLIF(NULLIF(b.EmpresaRUES,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS RuesEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EmpresaRUES,''), '0') IS NULL
                          AND b.EnRUES = 1
                         THEN 1 ELSE 0 END) AS RuesNoEnc,
                -- SIMIT
                SUM(CASE WHEN NULLIF(NULLIF(b.PlacaSIMIT,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SimitEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.PlacaSIMIT,''), '0') IS NULL
                          AND b.EnSIMIT = 1
                         THEN 1 ELSE 0 END) AS SimitNoEnc,
                -- RGM
                SUM(CASE WHEN NULLIF(NULLIF(b.Marca,''), '0') IS NOT NULL
                           OR (b.Modelo IS NOT NULL AND b.Modelo != 0)
                           OR NULLIF(NULLIF(b.Placa,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS RgmEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.Marca,''), '0') IS NULL
                          AND (b.Modelo IS NULL OR b.Modelo = 0)
                          AND NULLIF(NULLIF(b.Placa,''), '0') IS NULL
                          AND b.EnRGM = 1
                         THEN 1 ELSE 0 END) AS RgmNoEnc
            FROM (
                SELECT
                    TRY_CAST(v.[Numero Documento] AS bigint) AS TitularID,
                    v.[Celular Cajacopi]          AS CelularCajacopi,
                    v.[Email CajaCopi]            AS EmailCajacopi,
                    v.[Edad CajaCopi]             AS EdadCajacopi,
                    v.[Celular Camara]            AS CelularCamara,
                    v.[Email Camara]              AS EmailCamara,
                    v.[Celular Salud Total]       AS CelularSaludTotal,
                    v.[Email Salud Total]         AS EmailSaludTotal,
                    v.[NIT Salud Total]           AS NITSaludTotal,
                    v.[Empresa Salud Total]       AS EmpresaSaludTotal,
                    v.EPS,
                    v.Regimen,
                    v.[Tipo Afiliado]             AS TipoAfiliado,
                    v.[EPS en Compensados]        AS EPSCompensados,
                    v.[Tipo Afiliado Compensados] AS TipoAfiliadoCompensados,
                    v.[Ultimo Periodo Compensado] AS UltimoPeriodoCompensado,
                    v.[Celular Positiva]          AS CelularPositiva,
                    v.[Email Positiva]            AS EmailPositiva,
                    v.[Edad Positiva]             AS EdadPositiva,
                    v.[Celular Tigo]              AS CelularTigo,
                    v.[Email Tigo]                AS EmailTigo,
                    v.[Celular SOS]               AS CelularSOS,
                    v.[Nit Sos]                   AS NitSOS,
                    v.[Empresa SOS]               AS EmpresaSOS,
                    v.[NIT Sura]                  AS NITSura,
                    v.[Empresa Sura]              AS EmpresaSura,
                    v.[NIT Positiva]              AS NITPositiva,
                    v.[Empresa Positiva]          AS EmpresaPositiva,
                    v.[Empresa RUES]              AS EmpresaRUES,
                    v.[PLACA SIMIT]               AS PlacaSIMIT,
                    v.Marca,
                    v.Modelo,
                    v.Placa,
                    -- Presencia en tabla fuente resuelta con DISTINCT LEFT JOIN:
                    -- más eficiente que EXISTS correlacionado para carteras grandes.
                    CASE WHEN cj.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCajacopi,
                    CASE WHEN cm.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCamara,
                    CASE WHEN st.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSaludTotal,
                    CASE WHEN f.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnFosyga,
                    CASE WHEN co.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCompensados,
                    CASE WHEN p.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnPositiva,
                    CASE WHEN ti.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnTigo,
                    CASE WHEN s.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnSOS,
                    CASE WHEN ar.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSura,
                    CASE WHEN pe.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnPosEmp,
                    CASE WHEN ru.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnRUES,
                    CASE WHEN si.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSIMIT,
                    CASE WHEN rg.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnRGM
                FROM vw_BaseMadre v
                LEFT JOIN (SELECT DISTINCT TitularID FROM Cajacopi)         cj ON cj.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Camara)           cm ON cm.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SaludTotal)       st ON st.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Fosyga)           f  ON f.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Compensados)      co ON co.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Positiva)         p  ON p.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Tigo)             ti ON ti.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SOS)              s  ON s.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM ARLSura)          ar ON ar.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM PositivaEmpresas) pe ON pe.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM RUES)             ru ON ru.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SIMIT)            si ON si.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM RGM)              rg ON rg.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                WHERE v.CarteraID = {carteraID} AND v.LiderID = {liderID}
            ) b
            """).FirstOrDefaultAsync();

        if (r is null) return [];

        return
        [
            Fuente("Cajacopi",       r.CajacopiEnc,    r.CajacopiNoEnc,    total),
            Fuente("Cámara",         r.CamaraEnc,      r.CamaraNoEnc,      total),
            Fuente("Salud Total",    r.SaludTotalEnc,  r.SaludTotalNoEnc,  total),
            Fuente("Fosyga",         r.FosygaEnc,      r.FosygaNoEnc,      total),
            Fuente("Compensados",    r.CompensadosEnc, r.CompensadosNoEnc, total),
            Fuente("Positiva",       r.PositivaEnc,    r.PositivaNoEnc,    total),
            Fuente("Tigo",           r.TigoEnc,        r.TigoNoEnc,        total),
            Fuente("SOS",            r.SosEnc,         r.SosNoEnc,         total),
            Fuente("Sura (ARL)",     r.SuraEnc,        r.SuraNoEnc,        total),
            Fuente("Positiva Emp.",  r.PosEmpEnc,      r.PosEmpNoEnc,      total),
            Fuente("RUES",           r.RuesEnc,        r.RuesNoEnc,        total),
            Fuente("SIMIT",          r.SimitEnc,       r.SimitNoEnc,       total),
            Fuente("RGM",            r.RgmEnc,         r.RgmNoEnc,         total),
        ];

        static LocalizacionFuente Fuente(string nombre, int enc, int noEnc, int tot) => new()
        {
            Fuente          = nombre,
            Encontrados     = enc,
            NoEncontrados   = noEnc,
            FaltanPorBuscar = tot - enc - noEnc,
        };
    }

    // ── DTO interno para mapear el resultado del SQL de localización ──────────
    private sealed class LocalizacionResumenRaw
    {
        public int CajacopiEnc    { get; set; } public int CajacopiNoEnc    { get; set; }
        public int CamaraEnc      { get; set; } public int CamaraNoEnc      { get; set; }
        public int SaludTotalEnc  { get; set; } public int SaludTotalNoEnc  { get; set; }
        public int FosygaEnc      { get; set; } public int FosygaNoEnc      { get; set; }
        public int CompensadosEnc { get; set; } public int CompensadosNoEnc { get; set; }
        public int PositivaEnc    { get; set; } public int PositivaNoEnc    { get; set; }
        public int TigoEnc        { get; set; } public int TigoNoEnc        { get; set; }
        public int SosEnc         { get; set; } public int SosNoEnc         { get; set; }
        public int SuraEnc        { get; set; } public int SuraNoEnc        { get; set; }
        public int PosEmpEnc      { get; set; } public int PosEmpNoEnc      { get; set; }
        public int RuesEnc        { get; set; } public int RuesNoEnc        { get; set; }
        public int SimitEnc       { get; set; } public int SimitNoEnc       { get; set; }
        public int RgmEnc         { get; set; } public int RgmNoEnc         { get; set; }
    }

    /// <summary>
    /// Como Desglosar, pero limita a los top N y agrupa el resto en "(Otras)".
    /// </summary>
    private static List<DesglosePar> DesgloseConOtras(
        IEnumerable<string> valores, int total, int top)
    {
        var todos = Desglosar(valores, total);
        if (todos.Count <= top)
            return todos;

        var resultado = todos.Take(top).ToList();
        var otrasCount = todos.Skip(top).Sum(d => d.Cantidad);
        resultado.Add(new DesglosePar
        {
            Etiqueta   = "(Otras)",
            Cantidad   = otrasCount,
            Porcentaje = total > 0 ? Math.Round((decimal)otrasCount / total * 100, 1) : 0,
        });
        return resultado;
    }
}
