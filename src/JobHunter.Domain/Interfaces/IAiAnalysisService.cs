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

/// <summary>
/// Resultado estructurado del análisis de IA.
/// <para>
/// <paramref name="IsClientFinal"/> y <paramref name="WorkMode"/> son lo que
/// la IA deduce LEYENDO la oferta (null = no se puede saber). Corrigen los
/// valores que pone cada provider, que dependen de la fuente y no de la
/// oferta: los agregadores nunca marcan cliente final y el modo de trabajo
/// sale de una heurística por palabras clave. Son opcionales y van al final
/// para no romper las construcciones existentes.
/// </para>
/// </summary>
public sealed record AiAnalysisResult(
    string Summary,
    string Seniority,
    string[] ImportantTechs,
    int Compatibility,
    string[] Pros,
    string[] Cons,
    string Recommendation,
    string[] Requirements,
    bool? IsClientFinal = null,
    WorkMode? WorkMode = null);
