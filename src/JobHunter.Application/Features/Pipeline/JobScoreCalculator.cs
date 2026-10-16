using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Cálculo puro de la puntuación de una oferta a partir del análisis de IA
/// y el perfil del candidato. Se extrae del handler para poder testearlo
/// de forma aislada.
///
/// La oferta llega ya corregida con las señales de la IA (ver
/// <see cref="AiSignals"/>): modo de trabajo y cliente final son los que
/// dice la oferta, no los que supone la fuente.
///
/// Pesos:
///  - Ubicación: Remote +30 (prioridad máxima); Híbrido conmutable +15 (+5
///    si la zona es de las preferidas del perfil), no conmutable -25;
///    OnSite conmutable -10, no conmutable -30. Un modo que el perfil no
///    acepta (AcceptHybrid/AcceptOnSite = false) puntúa como el no
///    conmutable. CommuteFilter descarta ambos casos antes de analizar, pero
///    la IA puede revelar DESPUÉS que una oferta "remota" es híbrida o
///    presencial: entonces no se descarta, se hunde aquí.
///  - Cliente final +20 / consultora -10.
///  - Salario (SalaryMax): ≥70k +25, ≥60k +20, ≥50k +15, ≥40k +5, &lt;40k -10, null -5.
///  - Techs conocidas: cada tech del perfil (TechWeights, escala 0-10) que
///    aparezca en la oferta suma su peso; el total se capa en +40.
///  - Techs excluidas (frontend/otros lenguajes): -12 por cada una distinta
///    (mín -36). El candidato es backend puro y apenas sabe frontend, así que
///    una oferta con varias señales de front se hunde de verdad.
///  - Calidad de la información: descripción muy corta o ausente resta hasta
///    -25; una oferta bien detallada suma +8. Si la fuente solo da un
///    extracto (<see cref="JobOffer.DescriptionIsSnippet"/>) cuenta 0: su
///    longitud no dice nada de la oferta real.
///  - TotalScore = Compatibility/2 + suma de lo anterior.
/// </summary>
internal static class JobScoreCalculator
{
    /// <summary>Tope de la suma de pesos de techs conocidas (~4 techs fuertes).</summary>
    private const int TechMatchCap = 40;

    public static JobScore Calculate(JobOffer offer, AiAnalysisResult analysis, UserProfile profile)
    {
        var remote = LocationScore(offer, profile);
        var clientFinal = offer.IsClientFinal ? 20 : -10;
        var salary = SalaryScore(offer.SalaryMax);
        var tech = TechScore(offer, analysis, profile);
        var infoQuality = InfoQualityScore(offer);
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

    private static int LocationScore(JobOffer offer, UserProfile profile) => offer.WorkMode switch
    {
        WorkMode.Remote => 30,
        WorkMode.Hybrid => profile.AcceptHybrid && CommuteFilter.IsCommutable(offer.Location, profile)
            ? 15 + (CommuteFilter.InPreferredZone(offer.Location, profile) ? 5 : 0)
            : -25,
        WorkMode.OnSite => profile.AcceptOnSite && CommuteFilter.IsCommutable(offer.Location, profile) ? -10 : -30,
        _ => 0,
    };

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

        // Suma el peso (0-10) de cada tech del perfil presente en la oferta,
        // con tope: una oferta que clava el stack no necesita 10 matches para
        // puntuar el máximo. Match por token (no substring) para que claves
        // cortas ("RAG", "OIDC", "n8n") no coincidan dentro de otras palabras.
        var matchedWeight = profile.TechWeights
            .Where(kv => kv.Value > 0 && MentionsTech(haystack, kv.Key))
            .Sum(kv => kv.Value);
        var score = Math.Min(matchedWeight, TechMatchCap);

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
    /// fuerte); bien detallada = suma. Los umbrales son en caracteres de
    /// texto limpio: los providers ya entregan texto plano (sin HTML) y aquí
    /// las rachas de espacios/saltos cuentan como uno, así que la longitud
    /// mide contenido y no maquetación.
    /// Un extracto de la fuente es neutro (0): no se puede juzgar, y
    /// penalizarlo castigaría al agregador en vez de a la oferta.
    /// </summary>
    private static int InfoQualityScore(JobOffer offer)
    {
        if (offer.DescriptionIsSnippet)
        {
            return 0;
        }

        var length = CleanLength(offer.Description);

        return length switch
        {
            < 250 => -25,
            < 600 => -10,
            < 1200 => 0,
            _ => 8,
        };
    }

    /// <summary>Longitud del texto recortado, contando cada racha de espacios en blanco como uno.</summary>
    private static int CleanLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var length = 0;
        var previousWasWhitespace = false;

        foreach (var c in text.AsSpan().Trim())
        {
            var isWhitespace = char.IsWhiteSpace(c);
            if (!isWhitespace || !previousWasWhitespace)
            {
                length++;
            }

            previousWasWhitespace = isWhitespace;
        }

        return length;
    }

    /// <summary>
    /// True si <paramref name="tech"/> aparece en el texto como token propio y
    /// no incrustado dentro de otra palabra. Evita el falso positivo clásico
    /// "Java" ⊂ "JavaScript" (o "React" ⊂ "Preact"): se exige que los
    /// caracteres inmediatamente anterior y posterior no sean letras. El
    /// límite solo se exige en los extremos del término que son letras: así
    /// ".NET" (empieza por '.') sí coincide dentro de "ASP.NET", y "C#"
    /// (termina en '#') coincide vaya seguido de lo que vaya.
    /// Lo reutiliza <see cref="RelevanceFilter"/> para casar techs en títulos.
    /// </summary>
    internal static bool MentionsTech(string haystack, string tech)
    {
        var checkBefore = char.IsLetter(tech[0]);
        var checkAfter = char.IsLetter(tech[^1]);
        var index = haystack.IndexOf(tech, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeOk = !checkBefore || index == 0 || !char.IsLetter(haystack[index - 1]);
            var afterIndex = index + tech.Length;
            var afterOk = !checkAfter || afterIndex >= haystack.Length || !char.IsLetter(haystack[afterIndex]);

            if (beforeOk && afterOk)
            {
                return true;
            }

            index = haystack.IndexOf(tech, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
