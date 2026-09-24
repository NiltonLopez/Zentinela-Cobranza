using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Regla por fuente. Separado de <see cref="CatalogoCargueFuentes"/> porque las reglas son
/// código con lógica (y a veces SQL) y viven en Infrastructure; la definición la consume el front.
/// </summary>
internal static class CatalogoReglasFuente
{
    private static readonly Dictionary<string, IReglaFuente> Reglas = new(StringComparer.OrdinalIgnoreCase)
    {
        [CatalogoCargueFuentes.Sura.Fuente]      = new ReglaGenerica(CatalogoCargueFuentes.Sura.Fuente),
        [CatalogoCargueFuentes.Cajacopi.Fuente]  = new ReglaCajacopi(),
        [CatalogoCargueFuentes.MutualSer.Fuente] = new ReglaMutualSer(),
        // NuevaEps: sp_ProcesarNuevaEpsAuxiliar solo creaba Titular/relaciones, calculaba el flag
        // e insertaba: todo genérico. Lo propio (TitularID varchar) es de la definición.
        [CatalogoCargueFuentes.NuevaEps.Fuente]  = new ReglaGenerica(CatalogoCargueFuentes.NuevaEps.Fuente),
        [CatalogoCargueFuentes.Simit.Fuente]     = new ReglaSimit(),
        [CatalogoCargueFuentes.DatosClaro.Fuente] = new ReglaDatosClaro(),

        // ── Fase 2, grupo salud ──
        [CatalogoCargueFuentes.Positiva.Fuente]   = new ReglaPositiva(),
        // SaludTotal: sp_ProcesarSaludTotalAuxiliar solo pasaba vacíos a NULL ('' , NIT 0, fecha
        // 1900-01-01), creaba Titular/relaciones, calculaba el flag e insertaba: todo genérico.
        // No cruzaba ListaNegraCorreos ni validaba el teléfono: el motor sí (ADR-039 §4).
        [CatalogoCargueFuentes.SaludTotal.Fuente] = new ReglaGenerica(CatalogoCargueFuentes.SaludTotal.Fuente),
        // SOS: sp_ProcesarSOSAuxiliar solo pasaba vacíos a NULL ('' y NitEmpresa 0), creaba
        // Titular/relaciones, calculaba el flag e insertaba: todo genérico. Direccion > 70 (la
        // Auxiliar admitía 150) hacía fallar su INSERT entero; el motor la trunca con advertencia.
        [CatalogoCargueFuentes.Sos.Fuente]        = new ReglaGenerica(CatalogoCargueFuentes.Sos.Fuente),
        // Coosalud: sp_ProcesarCoosaludAuxiliar anulaba teléfonos de largo ≠ 10 y correos de la
        // lista negra (con LIKE), calculaba el flag e insertaba solo lo nuevo (clave recortada con
        // ISNULL(x, '')): todo genérico en el motor, con validación exacta y clave completa.
        [CatalogoCargueFuentes.Coosalud.Fuente]   = new ReglaGenerica(CatalogoCargueFuentes.Coosalud.Fuente),
        [CatalogoCargueFuentes.SaviaSalud.Fuente] = new ReglaSaviaSalud(),
        // MiVacuna: sp_ProcesarMiVacunaAuxiliar solo pasaba el celular '' o '0' a NULL, cruzaba la
        // lista negra de correos (con LIKE, anulando a ''), calculaba el flag e insertaba: todo
        // genérico en el motor, con igualdad exacta y NULL.
        [CatalogoCargueFuentes.MiVacuna.Fuente]   = new ReglaGenerica(CatalogoCargueFuentes.MiVacuna.Fuente),
        // ── Fase 2 · grupo "otras" ──
        [CatalogoCargueFuentes.Fosyga.Fuente]    = new ReglaFosyga(),
        [CatalogoCargueFuentes.Rgm.Fuente]       = new ReglaRgm(),
        // TigoUne: sp_ProcesarTigoUneAuxiliar solo hacía lo genérico (Titular, relaciones, dedup
        // por titular, lista negra de correos —con LIKE, no se copia—, teléfono de 10, flag,
        // INSERT de lo nuevo). Ver el <summary> de CatalogoCargueFuentes.TigoUne.
        [CatalogoCargueFuentes.TigoUne.Fuente]   = new ReglaGenerica(CatalogoCargueFuentes.TigoUne.Fuente),
        // Tigo: NO hay SP que traducir (la tabla se llenó a mano en 2025 y nadie la escribe desde
        // entonces). Misma forma que TigoUne → lo genérico basta. Ver CatalogoCargueFuentes.Tigo.
        [CatalogoCargueFuentes.Tigo.Fuente]      = new ReglaGenerica(CatalogoCargueFuentes.Tigo.Fuente),
        // ── Fase 2 — grupo empresas ──
        // RUES: sp_ProcesarRUESAuxiliar no limpiaba nada (Titular, relaciones, flag e INSERT: todo
        // genérico). Lo propio (sello datetime) es de la definición.
        [CatalogoCargueFuentes.Rues.Fuente]      = new ReglaGenerica(CatalogoCargueFuentes.Rues.Fuente),
        [CatalogoCargueFuentes.Camara.Fuente]    = new ReglaCamara(),
        [CatalogoCargueFuentes.CamaraEmpresa.Fuente] = new ReglaCamaraEmpresa(),
        // PositivaEmpresas: su SP no limpiaba nada (Titular, relaciones, flag NIT/Empresa, INSERT).
        [CatalogoCargueFuentes.PositivaEmpresas.Fuente] = new ReglaGenerica(CatalogoCargueFuentes.PositivaEmpresas.Fuente),
        // ARLSura (≠ Sura, la EPS): su SP no limpiaba nada (Titular, relaciones, flag NIT/Empresa, INSERT).
        [CatalogoCargueFuentes.ArlSura.Fuente]   = new ReglaGenerica(CatalogoCargueFuentes.ArlSura.Fuente),
        [CatalogoCargueFuentes.Compensados.Fuente] = new ReglaCompensados(),
    };

    public static bool Existe(string fuente) => Reglas.ContainsKey(fuente);

    public static IReglaFuente Obtener(string fuente) =>
        Reglas.TryGetValue(fuente, out var regla)
            ? regla
            : throw new ArgumentException($"La fuente '{fuente}' no tiene regla de cargue registrada.");
}
