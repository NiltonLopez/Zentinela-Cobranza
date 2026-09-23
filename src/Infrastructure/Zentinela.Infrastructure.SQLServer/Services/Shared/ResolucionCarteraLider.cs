using Microsoft.EntityFrameworkCore;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services.Shared;

/// <summary>
/// Resolución de Cartera y Líder por su NOMBRE tal como viene en un archivo cargado.
///
/// Nació dentro de <c>HistoricoVirtualidadService</c> (Histórico IVR / SMS / Correo) y se
/// extrajo sin cambio de comportamiento porque el Cargue de Fuentes de Datos necesita la
/// misma regla: un archivo trae "Cartera" y "Líder" escritos a mano, y solo se acepta la
/// fila si ambos existen y el par líder-cartera está ACTIVO en LiderCartera.
///
/// La comparación de nombres usa <see cref="ArchivoTabular.Normalizar"/> (sin tildes, sin
/// espacios, minúsculas): es idéntica a la <c>Normalizar</c> privada que usaba el service,
/// así "Banco de Bogotá" y "BANCO DE BOGOTA" resuelven a la misma cartera.
/// </summary>
internal static class ResolucionCarteraLider
{
    /// <summary>
    /// Índices para resolver cartera y líder por su NOMBRE tal como viene en el archivo,
    /// más el conjunto de pares líder-cartera activos. Se cargan una vez por validación.
    /// </summary>
    public sealed class IndicesResolucion
    {
        public required Dictionary<string, (int ID, string Nombre)> CarteraPorNombre { get; init; }
        public required Dictionary<string, (int ID, string Nombre)> LiderPorNombre   { get; init; }
        public required HashSet<(int LiderID, int CarteraID)>       ParesActivos     { get; init; }

        /// <summary>Devuelve los IDs y nombres canónicos, o el motivo del fallo.</summary>
        public (int CarteraID, string NombreCartera, int LiderID, string NombreLider, string? Error)
            Resolver(string nombreCartera, string nombreLider)
        {
            if (!CarteraPorNombre.TryGetValue(ArchivoTabular.Normalizar(nombreCartera), out var cartera))
                return (0, nombreCartera, 0, nombreLider,
                        $"Cartera '{nombreCartera}' no encontrada (o inactiva).");

            if (!LiderPorNombre.TryGetValue(ArchivoTabular.Normalizar(nombreLider), out var lider))
                return (0, nombreCartera, 0, nombreLider,
                        $"Líder '{nombreLider}' no encontrado.");

            if (!ParesActivos.Contains((lider.ID, cartera.ID)))
                return (0, cartera.Nombre, 0, lider.Nombre,
                        $"'{lider.Nombre}' no está asignado a la cartera '{cartera.Nombre}'.");

            return (cartera.ID, cartera.Nombre, lider.ID, lider.Nombre, null);
        }
    }

    /// <summary>
    /// Carga carteras ACTIVAS, todos los líderes y los pares líder-cartera activos.
    /// Tres consultas sin tracking; el volumen es de catálogo (cientos de filas), no de datos.
    /// </summary>
    public static async Task<IndicesResolucion> CargarIndicesResolucionAsync(
        CallCenterDbContext db, CancellationToken ct = default)
    {
        var carteras = await db.Carteras
            .Where(c => c.Activa)
            .Select(c => new { c.ID, c.NombreCartera })
            .AsNoTracking().ToListAsync(ct);

        var lideres = await db.Lideres
            .Select(l => new { l.ID, l.Nombre })
            .AsNoTracking().ToListAsync(ct);

        var pares = await db.LiderCarteras
            .Where(lc => lc.Activo)
            .Select(lc => new { lc.LiderID, lc.CarteraID })
            .AsNoTracking().ToListAsync(ct);

        return new IndicesResolucion
        {
            CarteraPorNombre = ConstruirIndicePorNombre(carteras.Select(c => (c.ID, c.NombreCartera))),
            LiderPorNombre   = ConstruirIndicePorNombre(lideres.Select(l => (l.ID, l.Nombre))),
            ParesActivos     = pares.Select(p => (p.LiderID, p.CarteraID)).ToHashSet(),
        };
    }

    /// <summary>Índice Normalizar(nombre) -> (ID, Nombre). Ante nombres duplicados
    /// normalizados, conserva el primero (no lanza).</summary>
    public static Dictionary<string, (int ID, string Nombre)> ConstruirIndicePorNombre(
        IEnumerable<(int ID, string Nombre)> items)
    {
        var dict = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        foreach (var (id, nombre) in items)
        {
            var clave = ArchivoTabular.Normalizar(nombre);
            if (!string.IsNullOrEmpty(clave) && !dict.ContainsKey(clave))
                dict[clave] = (id, nombre);
        }
        return dict;
    }
}
