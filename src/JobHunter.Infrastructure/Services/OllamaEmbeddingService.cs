using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Services;

/// <summary>
/// Genera embeddings con Ollama (modelo local, p.ej. nomic-embed-text) e
/// implementa la similitud coseno a mano. Se usa para el dedup semántico.
/// </summary>
public sealed class OllamaEmbeddingService(
    HttpClient http,
    IOptions<OllamaOptions> options) : IEmbeddingService
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new OllamaEmbeddingRequest(_options.EmbeddingModel, text);

        using var response = await http.PostAsJsonAsync("api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(ct);

        return payload?.Embedding ?? [];
    }

    public float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0f;
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }

    private sealed record OllamaEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
