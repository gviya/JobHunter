using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Cálculo puro de la puntuación de una oferta a partir del análisis de IA
/// y el perfil del candidato. Se extrae del handler para poder testearlo
/// de forma aislada.
///
/// Pesos:
///  - Remoto: Remote +30; Híbrido en Madrid +15, fuera -5; OnSite -20.
///  - Cliente final +20 / consultora -10.
///  - Salario (SalaryMax): ≥70k +25, ≥60k +20, ≥50k +15, ≥40k +5, &lt;40k -10, null -5.
///  - Techs conocidas: +3 por coincidencia (máx +15); si aparece alguna excluida -15.
///  - TotalScore = Compatibility/2 + suma de lo anterior.
/// </summary>
internal static class JobScoreCalculator
{
    public static JobScore Calculate(JobOffer offer, AiAnalysisResult analysis, UserProfile profile)
    {
        var remote = RemoteScore(offer);
        var clientFinal = offer.IsClientFinal ? 20 : -10;
        var salary = SalaryScore(offer.SalaryMax);
        var tech = TechScore(offer, analysis, profile);
        var total = (analysis.Compatibility / 2) + remote + clientFinal + salary + tech;

        return new JobScore
        {
            JobOfferId = offer.Id,
            CompatibilityScore = analysis.Compatibility,
            RemoteScore = remote,
            ClientFinalScore = clientFinal,
            SalaryScore = salary,
            TechScore = tech,
            TotalScore = total,
            AiSummary = analysis.Summary,
            AiSeniority = analysis.Seniority,
            AiTechList = string.Join(", ", analysis.ImportantTechs),
            AiRecommendation = analysis.Recommendation,
        };
    }

    private static int RemoteScore(JobOffer offer) => offer.WorkMode switch
    {
        WorkMode.Remote => 30,
        WorkMode.Hybrid => IsMadrid(offer.Location) ? 15 : -5,
        WorkMode.OnSite => -20,
        _ => 0,
    };

    private static bool IsMadrid(string? location)
        => location?.Contains("Madrid", StringComparison.OrdinalIgnoreCase) ?? false;

    private static int SalaryScore(decimal? salaryMax) => salaryMax switch
    {
        null => -5,
        >= 70000 => 25,
        >= 60000 => 20,
        >= 50000 => 15,
        >= 40000 => 5,
        _ => -10,
    };

    private static int TechScore(JobOffer offer, AiAnalysisResult analysis, UserProfile profile)
    {
        var haystack = string.Join(' ', new[]
            {
                offer.Title,
                offer.Description,
                string.Join(' ', analysis.ImportantTechs),
            }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var matches = profile.KnownTechs.Count(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        var score = Math.Min(matches * 3, 15);

        if (profile.ExcludedTechs.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            score -= 15;
        }

        return score;
    }
}
