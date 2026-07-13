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
/// Analiza ofertas con Claude Haiku vía la Messages API de Anthropic
/// (HttpClient puro, sin SDK). Pide al modelo que responda SOLO con JSON
/// y lo deserializa a <see cref="AiAnalysisResult"/>.
/// </summary>
public sealed class ClaudeAnalysisService(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<ClaudeAnalysisService> logger) : IAiAnalysisService
{
    private const int MaxTokens = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AnthropicOptions _options = options.Value;

    public async Task<AiAnalysisResult> AnalyzeAsync(JobOffer offer, UserProfile profile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Anthropic:ApiKey no configurada.");
        }

        var prompt = JobAnalysisPrompt.Build(offer, profile);
        string? lastContent = null;

        // Un reintento: si el modelo devuelve algo que no parsea, se repite
        // la llamada una vez antes de rendirse.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            lastContent = await CallModelAsync(prompt, ct);

            if (JobAnalysisPrompt.TryParse(lastContent, out var result))
            {
                return result;
            }

            logger.LogWarning(
                "Respuesta de Claude no parseable (intento {Attempt}/2) para la oferta {OfferId}.",
                attempt, offer.Id);
        }

        throw new InvalidOperationException(
            $"No se pudo parsear la respuesta de Claude tras 2 intentos. Contenido recibido: {lastContent}");
    }

    private async Task<string> CallModelAsync(string prompt, CancellationToken ct)
    {
        var request = new AnthropicRequest(
            Model: _options.Model,
            MaxTokens: MaxTokens,
            Messages: [new AnthropicMessage("user", prompt)]);

        using var response = await http.PostAsJsonAsync("v1/messages", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            // La API de Anthropic devuelve el motivo real en el body (créditos
            // agotados, modelo inválido, API key incorrecta...). Incluirlo en
            // la excepción evita depurar a ciegas un "400 Bad Request" genérico.
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic API respondió {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, ct);

        return payload?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;
    }

    // ── DTOs de la Messages API ─────────────────────────────────────────
    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] AnthropicMessage[] Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] AnthropicContentBlock[]? Content);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
