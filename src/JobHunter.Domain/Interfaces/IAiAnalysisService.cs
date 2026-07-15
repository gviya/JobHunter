using JobHunter.Domain.Entities;

namespace JobHunter.Domain.Interfaces;

/// <summary>
/// Analiza una oferta contra el perfil del candidato usando un LLM
/// (Claude Haiku). Lo implementa Infrastructure con HttpClient.
/// </summary>
public interface IAiAnalysisService
{
    Task<AiAnalysisResult> AnalyzeAsync(JobOffer offer, UserProfile profile, CancellationToken ct = default);
}

/// <summary>Resultado estructurado del análisis de IA.</summary>
public sealed record AiAnalysisResult(
    string Summary,
    string Seniority,
    string[] ImportantTechs,
    int Compatibility,
    string[] Pros,
    string[] Cons,
    string Recommendation,
    string[] Requirements);
