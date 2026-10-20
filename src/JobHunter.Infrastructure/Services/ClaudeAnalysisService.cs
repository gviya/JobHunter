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
/// (HttpClient puro, sin SDK). La salida va restringida con structured
/// outputs al JSON Schema de <see cref="JobAnalysisPrompt.ResponseSchema"/>
/// y se deserializa a <see cref="AiAnalysisResult"/>.
/// <para>
/// Haiku 5.5 razona por defecto (thinking adaptativo): la respuesta puede
/// empezar por bloques "thinking" antes del "text", y ese razonamiento
/// cuenta para max_tokens. Por eso se lee el primer bloque de texto, el
/// límite es holgado y configurable, y el esfuerzo se baja a "low" (esta
/// extracción es corta). No se envían temperature/top_p/top_k (Haiku 5.5
/// responde 400) ni prefill del assistant (el formato ya lo impone el schema).
/// </para>
/// </summary>
public sealed class ClaudeAnalysisService(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<ClaudeAnalysisService> logger) : IAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly AnthropicOutputFormat JsonSchemaFormat = new("json_schema", JobAnalysisPrompt.ResponseSchema);

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
            var reply = await CallModelAsync(prompt, ct);

            // Una negativa llega como 200 con stop_reason "refusal": repetir
            // el mismo prompt solo gastaría otra llamada para el mismo "no",
            // así que se corta aquí, fuera del bucle de reintentos.
            if (reply.StopReason == "refusal")
            {
                throw new InvalidOperationException(
                    $"Claude se negó a analizar la oferta {offer.Id} (stop_reason=refusal, categoría: " +
                    $"{reply.RefusalCategory ?? "sin especificar"}). No se reintenta: el mismo prompt volvería a rechazarse.");
            }

            // Cortada por max_tokens (el razonamiento también consume): el
            // JSON puede venir truncado. Se avisa para poder subir el límite
            // y se deja que el parseo y el reintento decidan.
            if (reply.StopReason == "max_tokens")
            {
                logger.LogWarning(
                    "Respuesta de Claude cortada por max_tokens ({MaxTokens}) (intento {Attempt}/2) para la oferta {OfferId}: puede estar truncada. Si se repite, sube Anthropic:MaxTokens.",
                    _options.MaxTokens, attempt, offer.Id);
            }

            lastContent = reply.Text;

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

    private async Task<ModelReply> CallModelAsync(string prompt, CancellationToken ct)
    {
        var request = new AnthropicRequest(
            Model: _options.Model,
            MaxTokens: _options.MaxTokens,
            Messages: [new AnthropicMessage("user", prompt)],
            // Effort vacío = no se envía: Haiku 4.5 rechaza el parámetro (400)
            // y así basta con dejarlo vacío en la configuración para volver a él.
            OutputConfig: new AnthropicOutputConfig(
                Effort: string.IsNullOrWhiteSpace(_options.Effort) ? null : _options.Effort.Trim(),
                Format: JsonSchemaFormat));

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

        // El primer bloque "text", no el primero a secas: con thinking
        // activo los bloques "thinking" van delante.
        var text = payload?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        return new ModelReply(text, payload?.StopReason, ReadRefusalCategory(payload?.StopDetails));
    }

    /// <summary>
    /// Lee stop_details.category con tolerancia: es opcional y solo sirve
    /// para el mensaje de error, así que una forma inesperada no debe hacer
    /// fallar la deserialización de toda la respuesta.
    /// </summary>
    private static string? ReadRefusalCategory(JsonElement? stopDetails) =>
        stopDetails is { ValueKind: JsonValueKind.Object } details
        && details.TryGetProperty("category", out var category)
        && category.ValueKind == JsonValueKind.String
            ? category.GetString()
            : null;

    private sealed record ModelReply(string Text, string? StopReason, string? RefusalCategory);

    // ── DTOs de la Messages API ─────────────────────────────────────────
    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] AnthropicMessage[] Messages,
        [property: JsonPropertyName("output_config")] AnthropicOutputConfig OutputConfig);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicOutputConfig(
        [property: JsonPropertyName("effort")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Effort,
        [property: JsonPropertyName("format")] AnthropicOutputFormat Format);

    private sealed record AnthropicOutputFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("schema")] JsonElement Schema);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] AnthropicContentBlock[]? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason,
        // JsonElement: stop_details es opcional y su forma puede crecer.
        [property: JsonPropertyName("stop_details")] JsonElement? StopDetails);

    private sealed record AnthropicContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
