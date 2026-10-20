using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Services;

/// <summary>
/// Genera embeddings con Ollama (modelo local, p.ej. nomic-embed-text) e
/// implementa la similitud coseno a mano. Se usa para el dedup semántico.
/// <para>
/// Usa POST /api/embed (la antigua /api/embeddings está obsoleta). Este
/// endpoint devuelve los vectores normalizados (L2); la similitud coseno no
/// depende de la escala, así que esa normalización no rompe la comparación
/// con los embeddings que el endpoint anterior dejó guardados en BD.
/// </para>
/// </summary>
public sealed class OllamaEmbeddingService(
    HttpClient http,
    IOptions<OllamaOptions> options) : IEmbeddingService
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new OllamaEmbedRequest(_options.EmbeddingModel, text);

        using var response = await http.PostAsJsonAsync("api/embed", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // El body dice el porqué (modelo no descargado, Ollama demasiado
            // antiguo para /api/embed...); acaba en el aviso del dedup.
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama respondió {(int)response.StatusCode} {response.StatusCode} en api/embed: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);

        // "input" admite un lote; aquí va un solo texto, así que el vector
        // es el primero. Sin vectores = vacío, que el dedup ya ignora.
        return payload?.Embeddings?.FirstOrDefault() ?? [];
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

    private sealed record OllamaEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed record OllamaEmbedResponse(
        [property: JsonPropertyName("embeddings")] float[]?[]? Embeddings);
}
