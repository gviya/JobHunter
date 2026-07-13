namespace JobHunter.Domain.Interfaces;

/// <summary>
/// Genera embeddings de texto (para el dedup semántico) y calcula la
/// similitud coseno entre dos vectores. Lo implementa Infrastructure
/// contra Ollama.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    float CosineSimilarity(float[] a, float[] b);
}
