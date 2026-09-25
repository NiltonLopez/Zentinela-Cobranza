using Zentinela.Application.DTOs.CargueFuentes;

namespace Zentinela.Application;

/// <summary>
/// Columnas que el Cargue de Fuentes (ADR-039) espera en el archivo de una fuente, en el orden en que
/// se muestran en el panel "Columnas esperadas". Vive en Application porque la usan el orquestador del
/// cargue (panel del front) y el Excel de las sugerencias de localización (ADR-040), que tiene que salir
/// con estos mismos encabezados para que el cargue lo acepte tal cual.
/// </summary>
public static class ColumnasEsperadasCargue
{
    public static List<ColumnaEsperadaDto> De(DefinicionFuenteCargue d)
    {
        // Mismo filtro que aplica el motor: un alias de columna de dato no vale para las comunes.
        var aliasDeDatos = d.ColumnasDato.Concat(d.ColumnasArchivoExtra).SelectMany(c => c.Alias).ToHashSet();
        string[] Comunes(string[] alias) => alias.Where(a => !aliasDeDatos.Contains(a)).ToArray();

        var lista = new List<ColumnaEsperadaDto>
        {
            // La clave: TitularID, o la columna propia de la fuente (CamaraEmpresa: NitEmpresa).
            new() { Nombre = d.ColumnaClaveTitular, Alias = Comunes(d.AliasDeClave), Tipo = "Documento",
                    Obligatoria = true, Descripcion = d.DescripcionClaveTitular },
        };
        if (d.CreaTitular)
            lista.Add(new() { Nombre = "Nombre", Alias = Comunes(CatalogoCargueFuentes.AliasNombre), Tipo = "Texto",
                              Obligatoria = true, Descripcion = "Obligatorio para crear titulares nuevos; repara nombres rotos" });
        if (d.CreaTitularCartera)
        {
            lista.Add(new() { Nombre = "CarteraID", Alias = Comunes(CatalogoCargueFuentes.AliasCarteraId), Tipo = "Entero",
                              Descripcion = "Opcional. Con LiderID (o nombres) crea la relación titular-cartera" });
            lista.Add(new() { Nombre = "Cartera", Alias = Comunes(CatalogoCargueFuentes.AliasCartera), Tipo = "Texto",
                              Descripcion = "Opcional. Nombre de la cartera, alternativa a CarteraID" });
            lista.Add(new() { Nombre = "LiderID", Alias = Comunes(CatalogoCargueFuentes.AliasLiderId), Tipo = "Entero",
                              Descripcion = "Opcional. El par líder-cartera debe estar activo" });
            lista.Add(new() { Nombre = "Lider", Alias = Comunes(CatalogoCargueFuentes.AliasLider), Tipo = "Texto",
                              Descripcion = "Opcional. Nombre del líder, alternativa a LiderID" });
        }
        // Las columnas que produce la regla (SIMIT: Placa) no se piden en el archivo.
        lista.AddRange(d.ColumnasDato.Where(c => c.DesdeArchivo).Concat(d.ColumnasArchivoExtra).Select(c => new ColumnaEsperadaDto
        {
            Nombre      = c.Nombre,
            Alias       = c.Alias,
            Tipo        = c.EsNit ? "NIT" : c.Tipo.ToString(),   // NIT: acepta 'NIT-DV'
            Obligatoria = c.Obligatoria,
            Descripcion = c.Descripcion,
        }));
        return lista;
    }
}
