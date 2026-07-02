using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zentinela.Application.DTOs.AuditoriaGestiones;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Evaluador de congruencia perfil ↔ gestión usando Gemini (API REST).
/// Adaptación del prototipo Python: un lote de gestiones por llamada,
/// salida JSON estructurada con un veredicto por registro.
/// La API key vive en configuración ("Gemini:ApiKey"), nunca hardcodeada.
/// </summary>
public class EvaluadorCongruenciaGemini : IEvaluadorCongruencia
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _modelo;

    private static readonly JsonSerializerOptions _jsonCaseInsensitive =
        new() { PropertyNameCaseInsensitive = true };

    public EvaluadorCongruenciaGemini(HttpClient http, IConfiguration config)
    {
        _http   = http;
        _apiKey = config["Gemini:ApiKey"]
                  ?? throw new InvalidOperationException("Falta 'Gemini:ApiKey' en la configuración.");
        _modelo = config["Gemini:Modelo"] ?? "gemini-3.1-flash-lite";
    }

    // Arma el prompt de auditoría para un lote (compartido por evaluación y conteo de tokens).
    private static string ConstruirPrompt(IReadOnlyList<GestionParaEvaluar> lote)
    {
        var registros = lote.Select(g => new
        {
            indice       = g.Indice,
            ultimoperfil = g.UltimoPerfil,
            gestion      = g.Gestion,
        });
        var jsonRegistros = JsonSerializer.Serialize(registros);

        return
            "Eres auditor de cobranza. \"ultimoperfil\" es la tipificación (categoría de resultado) que el " +
            "asesor asignó a la gestión; NO son palabras que deban aparecer en \"gestion\". Evalúa si el " +
            "contenido de \"gestion\" es coherente con el SIGNIFICADO de esa categoría (por sentido y contexto, " +
            "no por coincidencia literal).\n\n" +
            "Significado de las tipificaciones principales:\n" +
            "- Acuerdo de pago total: se compromete a pagar el total de la deuda.\n" +
            "- Acuerdo de pago abono: se compromete a pagar una parte/abono.\n" +
            "- Realizo abono: ya pagó un abono parcial.\n" +
            "- Paz y salvo: la deuda quedó saldada (a paz y salvo).\n" +
            "- Incumplido: no cumplió un acuerdo previo.\n" +
            "- No hubo acuerdo: se le contactó pero no aceptó pagar ni acordar.\n" +
            "- No contacto: no se logró contactar (no contesta, no responde).\n" +
            "- Mensaje en buzon: cayó en buzón/contestadora; se dejó mensaje.\n" +
            "- Mensaje con tercero: se habló o dejó razón con un tercero (familiar, etc.).\n" +
            "- Ilocalizado: no se pudo ubicar (datos errados, número inexistente).\n" +
            "- Fallecido: el titular falleció.\n" +
            "- Recordar: recordatorio o seguimiento; volver a contactar (aunque sea por mensaje de voz, buzón o WhatsApp/wsp).\n" +
            "- Sin gestion: no se realizó gestión.\n" +
            "Si aparece otra no listada, interprétala por su significado literal.\n\n" +
            "Para cada registro: primero identifica el contexto e intención de la tipificación (según el glosario) " +
            "y luego decide si lo que ocurre en la gestión encaja en ese contexto.\n\n" +
            "Reglas:\n" +
            "- No coincide: solo si la gestión contradice claramente la tipificación (ej. perfil \"No contacto\" pero sí habló con el titular).\n" +
            "- Ambiguo: si la gestión es vaga o no alcanza para confirmar.\n" +
            "- Coincide: si el contenido respalda razonablemente la tipificación.\n" +
            "- Ignora datos sueltos (teléfonos, cédulas, montos) salvo que aporten al sentido.\n\n" +
            "Analiza TODOS los registros y responde ÚNICAMENTE un arreglo JSON. La \"justificacion\" concisa, MÁXIMO 12 palabras.\n\n" +
            $"Registros: {jsonRegistros}\n\n" +
            "Devuelve exactamente: [ { \"indice\": 0, \"resultado\": \"Coincide\" | \"No coincide\" | \"Ambiguo\", " +
            "\"justificacion\": \"máx 12 palabras\" } ]";
    }

    /// <summary>
    /// Cuenta los tokens de ENTRADA de un lote usando el endpoint :countTokens (gratis).
    /// Si la llamada falla, estima ~4 caracteres por token como respaldo.
    /// </summary>
    public async Task<int> ContarTokensEntradaAsync(
        IReadOnlyList<GestionParaEvaluar> lote, CancellationToken ct = default)
    {
        if (lote.Count == 0) return 0;

        var prompt = ConstruirPrompt(lote);
        var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelo}:countTokens?key={_apiKey}";

        try
        {
            using var resp = await _http.PostAsJsonAsync(url, body, ct);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<CountTokensResponse>(_jsonCaseInsensitive, ct);
            return doc?.TotalTokens ?? prompt.Length / 4;
        }
        catch
        {
            return prompt.Length / 4;   // respaldo aproximado si countTokens no responde
        }
    }

    public async Task<IReadOnlyList<VeredictoGestion>> EvaluarLoteAsync(
        IReadOnlyList<GestionParaEvaluar> lote, CancellationToken ct = default)
    {
        if (lote.Count == 0) return [];

        var prompt = ConstruirPrompt(lote);

        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                // Tope de seguridad holgado (no truncar el JSON): la justificación ya se
                // limita por prompt a ~12 palabras; esto solo evita respuestas desbocadas.
                maxOutputTokens = lote.Count * 100 + 512,
                responseJsonSchema = new
                {
                    type  = "array",
                    items = new
                    {
                        type       = "object",
                        properties = new
                        {
                            indice        = new { type = "integer" },
                            resultado     = new { type = "string" },
                            justificacion = new { type = "string" },
                        },
                        required = new[] { "indice", "resultado", "justificacion" },
                    },
                },
            },
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelo}:generateContent?key={_apiKey}";
        using var resp = await _http.PostAsJsonAsync(url, body, ct);
        resp.EnsureSuccessStatusCode();

        var doc   = await resp.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var texto = doc?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "[]";
        texto = texto.Replace("```json", "").Replace("```", "").Trim();   // por si vienen fences

        return JsonSerializer.Deserialize<List<VeredictoGestion>>(texto, _jsonCaseInsensitive) ?? [];
    }

    // ── Forma mínima de la respuesta de Gemini ──────────────────────────────────
    private sealed class GeminiResponse { public List<Candidate>? Candidates { get; set; } }
    private sealed class Candidate      { public Content? Content { get; set; } }
    private sealed class Content        { public List<Part>? Parts { get; set; } }
    private sealed class Part           { public string? Text { get; set; } }
    private sealed class CountTokensResponse { public int TotalTokens { get; set; } }
}
