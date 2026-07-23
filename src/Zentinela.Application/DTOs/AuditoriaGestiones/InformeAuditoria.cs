namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>Respuesta del análisis de auditoría (consumida por el frontend).</summary>
public class AnalizarAuditoriaResponse
{
    public string NombreArchivo { get; set; } = "";   // etiqueta del origen, p.ej. "3 cartera(s)"
    public int    FilasLeidas   { get; set; }
    public InformeAuditoria Informe { get; set; } = new();
}

public class InformeAuditoria
{
    public int     TotalGestiones           { get; set; }
    public int     TotalGestionesConTexto   { get; set; }   // gestiones con texto en el rango (universo IA)
    public int     GestionesEvaluadasIA     { get; set; }   // las que realmente fueron a la IA (tope MaxGestionesIA)
    public int     GestionesSinEvaluar      { get; set; }   // se enviaron a la IA pero su lote falló tras reintentar
    public int     TotalCriticas            { get; set; }   // inconsistencias perfil ↔ gestión (IA)
    public int     TotalMedias              { get; set; }   // promesas incompletas
    public int     TotalBajas               { get; set; }   // valor de promesa < 5 dígitos
    public int     TotalRevision            { get; set; }   // IA no concluyó (Ambiguo) → requiere revisión
    public decimal PorcentajeIncongruencias { get; set; }   // 0-100 (gestiones con cualquier hallazgo)
    public decimal DuracionPromedioMin      { get; set; }   // sin columna de duración en BD → 0

    public List<IncongruenciaPorCartera> IncongruenciasPorCartera { get; set; } = [];
    public PalabraMasRepetida            PalabraMasRepetida       { get; set; } = new();   // sobre todas las gestiones
    public PalabraMasRepetida            PalabraInconsistencias   { get; set; } = new();   // solo gestiones con hallazgo
    public List<PalabraConteo>           TopPalabras              { get; set; } = [];
    public List<GrupoLider>              IncongruenciasPorLider   { get; set; } = [];
    public List<TipificacionRanking>     TipificacionesConMasIncongruencias { get; set; } = [];
}

public class IncongruenciaPorCartera
{
    public string Cartera        { get; set; } = "";
    public int    Incongruencias { get; set; }
    public int    Total          { get; set; }
}

public class PalabraMasRepetida
{
    public string       Palabra  { get; set; } = "";
    public int          Conteo   { get; set; }
    public List<string> Carteras { get; set; } = [];
}

public class PalabraConteo
{
    public string Palabra { get; set; } = "";
    public int    Conteo  { get; set; }
}

public class GrupoLider
{
    public string                 Lider { get; set; } = "";
    public List<IncongruenciaFila> Items { get; set; } = [];
}

public class TipificacionRanking
{
    public string Tipificacion   { get; set; } = "";
    public int    Incongruencias { get; set; }
    public int    Total          { get; set; }
}

public class IncongruenciaFila
{
    public int    Fila          { get; set; }
    public string Identificacion { get; set; } = "";
    public string AsesorGestion  { get; set; } = "";
    public string Lider          { get; set; } = "";
    public string Cartera        { get; set; } = "";
    public string Tipo           { get; set; } = "";   // perfil-gestion | promesa-sin-valor | valor-sin-fecha | valor-bajo
    public string Severidad      { get; set; } = "";   // critico | medio | bajo
    public string Detalle        { get; set; } = "";
    public string UltimoPerfil   { get; set; } = "";
    public string Contacto       { get; set; } = "";
    public string Gestion        { get; set; } = "";
    public string?  FechaPromesa { get; set; }   // ISO yyyy-mm-dd o null
    public decimal? ValorPromesa { get; set; }
}
