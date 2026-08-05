using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Zentinela.Application.DTOs.Consultas;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Busca un número de teléfono en las tablas de Localización de DBCallCenter
/// (las fuentes externas usadas para localizar contactabilidad de titulares,
/// alineadas con el bloque Localización del Calificador de Cartera) y devuelve,
/// agrupado por tabla, los titulares asociados.
///
/// Tablas: Cajacopi, Camara, SaludTotal, Positiva, Tigo, TigoUne, Coosalud, SaviaSalud, SOS, DatosClaro, MutualSer, MiVacuna, NuevaEps.
/// NO incluye auditorías, listas negras, directorios, ni logs salientes — esas
/// no son fuentes de localización.
///
/// Estrategia: un único query SQL con UNION ALL, sin mapear cada tabla externa
/// como entidad EF (la BD es del sistema de cobranza, propiedad de otro equipo).
/// </summary>
public class RastreoLineasService : IRastreoLineasService
{
    private readonly CallCenterDbContext _db;

    public RastreoLineasService(CallCenterDbContext db) => _db = db;

    public async Task<RastreoLineaDto> RastrearAsync(string numeroTelefono)
    {
        // Validación: 10 dígitos numéricos que comiencen en 3 (celular Colombia)
        var numero = (numeroTelefono ?? string.Empty).Trim();
        var dto    = new RastreoLineaDto { NumeroBuscado = numero };

        if (numero.Length != 10 || !numero.All(char.IsDigit) || numero[0] != '3')
            return dto;

        var rows = await _db.Database
            .SqlQueryRaw<RastreoRow>(SqlConsulta, new SqlParameter("@numero", numero))
            .ToListAsync();

        if (rows.Count == 0) return dto;

        // ── Agrupar en tarjetas por tabla ─────────────────────────────────────
        dto.PorTabla = rows
            .GroupBy(r => new { r.Tabla, r.Etiqueta })
            .Select(g => new RastreoTablaCard
            {
                Tabla       = g.Key.Tabla,
                Etiqueta    = g.Key.Etiqueta,
                Apariciones = g.Sum(r => r.Apariciones),
                Titulares   = g
                    .GroupBy(r => new { r.NumeroDocumento, r.Nombre })
                    .Select(t => new RastreoTitularEnTabla
                    {
                        NumeroDocumento = t.Key.NumeroDocumento,
                        Nombre          = t.Key.Nombre,
                        Apariciones     = t.Sum(r => r.Apariciones),
                    })
                    .OrderByDescending(t => t.Apariciones)
                    .ToList(),
            })
            .OrderByDescending(c => c.Apariciones)
            .ToList();

        // ── Resumen global por titular ────────────────────────────────────────
        dto.Titulares = rows
            .GroupBy(r => new { r.NumeroDocumento, r.Nombre })
            .Select(g => new RastreoTitularResumen
            {
                NumeroDocumento    = g.Key.NumeroDocumento,
                Nombre             = g.Key.Nombre,
                TablasDondeAparece = g.Select(r => r.Etiqueta).Distinct().Count(),
                TotalApariciones   = g.Sum(r => r.Apariciones),
                Tablas             = g.Select(r => r.Etiqueta).Distinct().OrderBy(t => t).ToList(),
            })
            .OrderByDescending(t => t.TablasDondeAparece)
                .ThenByDescending(t => t.TotalApariciones)
            .ToList();

        dto.TotalApariciones   = dto.PorTabla.Sum(c => c.Apariciones);
        dto.TitularesDistintos = dto.Titulares.Count;

        return dto;
    }

    // ── Tipo de proyección sólo para SqlQueryRaw ──────────────────────────────
    private class RastreoRow
    {
        public string Tabla           { get; set; } = string.Empty;
        public string Etiqueta        { get; set; } = string.Empty;
        public long   NumeroDocumento { get; set; }
        public string Nombre          { get; set; } = string.Empty;
        public int    Apariciones     { get; set; }
    }

    // ── SQL: UNION ALL sobre las fuentes de Localización ─────────────────────
    // Solo tablas del bloque Localización del Calificador con columna de teléfono:
    // Cajacopi, Camara, SaludTotal, Positiva, Tigo, TigoUne, Coosalud, SaviaSalud, SOS, DatosClaro, MutualSer.
    // Cajacopi y Positiva tienen Celular y Fijo → 2 SELECTs cada una.
    // MutualSer tiene Celular, Celular2 y Fijo → 3 SELECTs.
    // ISNULL en Nombre por si algún TitularID no existe en dbo.Titular (orphan).
    private const string SqlConsulta = """
        ;WITH Resultados AS (
            -- Cajacopi (celular)
            SELECT 'Cajacopi' AS Tabla, 'Cajacopi (celular)' AS Etiqueta,
                   x.TitularID AS NumeroDocumento,
                   ISNULL(t.Nombre, '(sin titular)') AS Nombre, COUNT(*) AS Apariciones
              FROM dbo.Cajacopi x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoCelular = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Cajacopi (fijo)
            SELECT 'Cajacopi', 'Cajacopi (fijo)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Cajacopi x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoFijo = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Camara
            SELECT 'Camara', 'Cámara',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Camara x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Positiva (celular)
            SELECT 'Positiva', 'Positiva (celular)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Positiva x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoCelular = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Positiva (fijo)
            SELECT 'Positiva', 'Positiva (fijo)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Positiva x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoFijo = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- SaludTotal
            SELECT 'SaludTotal', 'Salud Total',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.SaludTotal x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- SOS
            SELECT 'SOS', 'SOS',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.SOS x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.Telefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Tigo
            SELECT 'Tigo', 'Tigo',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Tigo x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- TigoUne
            SELECT 'TigoUne', 'TigoUne',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.TigoUne x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Coosalud
            SELECT 'Coosalud', 'Coosalud',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.Coosalud x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- SaviaSalud (columna NumeroCelular; NumeroTelefono está vacío)
            SELECT 'SaviaSalud', 'Savia Salud',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.SaviaSalud x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroCelular = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- Claro (DatosClaro)
            SELECT 'Claro', 'Claro',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.DatosClaro x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefono = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- MutualSer (celular 1)
            SELECT 'MutualSer', 'MutualSer (celular 1)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.MutualSer x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoCelular = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- MutualSer (celular 2)
            SELECT 'MutualSer', 'MutualSer (celular 2)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.MutualSer x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoCelular2 = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- MutualSer (fijo)
            SELECT 'MutualSer', 'MutualSer (fijo)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.MutualSer x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.NumeroTelefonoFijo = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- MiVacuna (celular)
            SELECT 'MiVacuna', 'MiVacuna (celular)',
                   x.TitularID, ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.MiVacuna x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID
             WHERE x.Celular = @numero
             GROUP BY x.TitularID, t.Nombre
            UNION ALL
            -- NuevaEps (teléfono) — TitularID es varchar, se castea a bigint para unificar
            SELECT 'NuevaEps', 'NuevaEps (teléfono)',
                   TRY_CAST(x.TitularID AS BIGINT), ISNULL(t.Nombre, '(sin titular)'), COUNT(*)
              FROM dbo.NuevaEps x
              LEFT JOIN dbo.Titular t ON t.NumeroDocumento = TRY_CAST(x.TitularID AS BIGINT)
             WHERE x.NumeroTelefono = @numero
             GROUP BY TRY_CAST(x.TitularID AS BIGINT), t.Nombre
        )
        SELECT Tabla, Etiqueta, NumeroDocumento, Nombre, Apariciones
          FROM Resultados
         WHERE NumeroDocumento IS NOT NULL;
        """;
}
