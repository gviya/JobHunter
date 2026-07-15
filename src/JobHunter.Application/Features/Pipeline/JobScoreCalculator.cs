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
///  - Techs conocidas: +3 por coincidencia (máx +15).
///  - Techs excluidas (frontend/otros lenguajes): -12 por cada una distinta
///    (mín -36). El candidato es backend puro y apenas sabe frontend, así que
///    una oferta con varias señales de front se hunde de verdad.
///  - Calidad de la información: descripción muy corta o ausente resta hasta
///    -25; una oferta bien detallada suma +8.
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
        var infoQuality = InfoQualityScore(offer.Description);
        var total = (analysis.Compatibility / 2) + remote + clientFinal + salary + tech + infoQuality;

        return new JobScore
        {
            JobOfferId = offer.Id,
            CompatibilityScore = analysis.Compatibility,
            RemoteScore = remote,
            ClientFinalScore = clientFinal,
            SalaryScore = salary,
            TechScore = tech,
            InfoQualityScore = infoQuality,
            TotalScore = total,
            AiSummary = analysis.Summary,
            AiSeniority = analysis.Seniority,
            AiTechList = string.Join(", ", analysis.ImportantTechs),
            AiRequirements = string.Join(" · ", analysis.Requirements),
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

        var known = profile.KnownTechs.Count(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        var score = Math.Min(known * 3, 15);

        // Cada tech excluida DISTINTA que aparezca resta: cuantas más señales
        // de frontend/otro stack, más se hunde la oferta. Se usa un match por
        // "palabra" (no substring) para no confundir "Java" con "JavaScript".
        var excluded = profile.ExcludedTechs.Count(t => MentionsTech(haystack, t));
        score -= Math.Min(excluded * 12, 36);

        return score;
    }

    /// <summary>
    /// Calidad de la información de la oferta según la longitud de la
    /// descripción: sin descripción o muy corta = mal explicada (resta
    /// fuerte); bien detallada = suma. Los umbrales son en caracteres.
    /// </summary>
    private static int InfoQualityScore(string? description)
    {
        var length = description?.Trim().Length ?? 0;

        return length switch
        {
            < 250 => -25,
            < 600 => -10,
            < 1200 => 0,
            _ => 8,
        };
    }

    /// <summary>
    /// True si <paramref name="tech"/> aparece en el texto como token propio y
    /// no incrustado dentro de otra palabra. Evita el falso positivo clásico
    /// "Java" ⊂ "JavaScript" (o "React" ⊂ "Preact"): se exige que los
    /// caracteres inmediatamente anterior y posterior no sean letras.
    /// </summary>
    private static bool MentionsTech(string haystack, string tech)
    {
        var index = haystack.IndexOf(tech, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetter(haystack[index - 1]);
            var afterIndex = index + tech.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetter(haystack[afterIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = haystack.IndexOf(tech, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
