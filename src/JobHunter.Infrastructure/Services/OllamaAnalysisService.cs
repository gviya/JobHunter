using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Services;

/// <summary>
/// Analiza ofertas con un modelo de chat local vía Ollama — alternativa
/// gratuita a Claude cuando Ai:Provider="Ollama". Comparte el mismo prompt
/// y criterio de puntuación que <see cref="ClaudeAnalysisService"/>.
///
/// Los modelos "híbridos" (familia Qwen3.x) razonan internamente antes de
/// responder si no se lo impides: eso multiplica por 10 la latencia
/// (~80s vs ~8s por oferta) sin mejorar el JSON de salida. Por eso se pide
/// "think": false explícitamente.
///
/// La salida va restringida con el mismo JSON Schema que Claude
/// (<see cref="JobAnalysisPrompt.ResponseSchema"/>) en el campo "format":
/// Ollama lo convierte en una gramática y el modelo solo puede generar JSON
/// que cumpla el contrato (requiere Ollama 0.5 o superior).
/// </summary>
public sealed class OllamaAnalysisService(
    HttpClient http,
    IOptions<OllamaOptions> options,
    ILogger<OllamaAnalysisService> logger) : IAiAnalysisService
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<AiAnalysisResult> AnalyzeAsync(JobOffer offer, UserProfile profile, CancellationToken ct = default)
    {
        var prompt = JobAnalysisPrompt.Build(offer, profile);
        string? lastContent = null;

        // Un reintento: si el modelo devuelve algo que no parsea, se repite
        // la llamada una vez antes de rendirse.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var reply = await CallModelAsync(prompt, ct);

            // done_reason "length": la generación se cortó por límite de
            // tokens y el JSON puede venir truncado. Se avisa para que el
            // fallo no parezca un modelo que ignora el schema, y el parseo y
            // el reintento deciden.
            if (reply.DoneReason == "length")
            {
                logger.LogWarning(
                    "Respuesta de Ollama cortada por longitud (intento {Attempt}/2) para la oferta {OfferId}: puede estar truncada.",
                    attempt, offer.Id);
            }

            lastContent = reply.Response ?? string.Empty;

            if (JobAnalysisPrompt.TryParse(lastContent, out var result))
            {
                return result;
            }

            logger.LogWarning(
                "Respuesta de Ollama no parseable (intento {Attempt}/2) para la oferta {OfferId}.",
                attempt, offer.Id);
        }

        throw new InvalidOperationException(
            $"No se pudo parsear la respuesta de Ollama tras 2 intentos. Contenido recibido: {lastContent}");
    }

    private async Task<OllamaGenerateResponse> CallModelAsync(string prompt, CancellationToken ct)
    {
        var request = new OllamaGenerateRequest(
            _options.AnalysisModel, prompt, JobAnalysisPrompt.ResponseSchema, Stream: false, Think: false);

        using var response = await http.PostAsJsonAsync("api/generate", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Ollama explica el fallo en el body ({"error": "model ... not
            // found"}, un "format" que una versión antigua no entiende...):
            // sin él solo quedaría un "404 Not Found" genérico.
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama respondió {(int)response.StatusCode} {response.StatusCode} en api/generate: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);

        return payload ?? new OllamaGenerateResponse(Response: null, DoneReason: null);
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("format")] JsonElement Format,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done_reason")] string? DoneReason);
}
