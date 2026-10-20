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
///    conmutable. CommuteFilter descarta ambos casos antes de analizar, y si
///    la IA revela DESPUÉS que una oferta "remota" es híbrida o presencial
///    en un modo o sitio que el perfil no acepta, el pipeline la descarta
///    tras el análisis sin llegar a puntuarla (una penalización fija no
///    garantiza hundirla: el resto de términos puede compensarla). Aquí se
///    mantiene la penalización como valor por defecto defensivo para quien
///    puntúe sin pasar por ese filtro.
///  - Cliente final +20 / consultora -10 / no se sabe 0, solo si el perfil lo
///    prefiere (<see cref="UserProfile.PreferClientFinal"/>); si al candidato
///    le da igual, cuenta 0 siempre.
///  - Salario (SalaryMax), en tramos relativos al perfil (Min =
///    <see cref="UserProfile.MinAcceptableSalary"/>, Objetivo =
///    <see cref="UserProfile.TargetSalary"/>): ≥ Objetivo+15k +25,
///    ≥ Objetivo+5k +20, ≥ Objetivo-5k +15 (nunca por debajo de Min),
///    ≥ Min +5, &lt; Min -10, null -5. Un Objetivo por debajo de Min se
///    trata como Min. Con el perfil por defecto (40k/55k) son los tramos
///    fijos de siempre: ≥70k, ≥60k, ≥50k, ≥40k.
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

    // Márgenes (EUR/año) de los tramos de salario alrededor del objetivo del
    // perfil. Están elegidos para que el perfil por defecto (mínimo 40k,
    // objetivo 55k) puntúe exactamente igual que los antiguos tramos fijos.
    private const decimal WellAboveTargetMargin = 15000;
    private const decimal AboveTargetMargin = 5000;
    private const decimal NearTargetMargin = 5000;

    public static JobScore Calculate(JobOffer offer, AiAnalysisResult analysis, UserProfile profile)
    {
        var remote = LocationScore(offer, profile);
        var clientFinal = ClientFinalScore(offer.IsClientFinal, profile);
        var salary = SalaryScore(offer.SalaryMax, profile);
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

    private static int ClientFinalScore(bool? isClientFinal, UserProfile profile)
    {
        // Si al candidato le da igual cliente final o consultora, el dato no
        // debe mover la puntuación en ningún sentido: ni premio ni castigo.
        if (!profile.PreferClientFinal)
        {
            return 0;
        }

        return isClientFinal switch
        {
            true => 20,
            false => -10,
            null => 0, // ni la fuente ni la IA lo saben: neutro
        };
    }

    /// <summary>
    /// Puntuación del salario relativa al perfil del candidato. Se mira
    /// SalaryMax porque en una horquilla el techo es lo que la empresa está
    /// dispuesta a pagar al perfil que busca (el suelo suele ser para alguien
    /// más junior), y es lo que dice si la oferta puede llegar al objetivo.
    /// Sin dato, -5: penaliza poco, porque muchas buenas ofertas no publican
    /// salario, pero queda por debajo de las que sí lo hacen y encajan. Una
    /// oferta que solo da suelo ("desde 35.000 €") cuenta como sin dato: su
    /// techo es desconocido.
    /// </summary>
    private static int SalaryScore(decimal? salaryMax, UserProfile profile)
    {
        if (salaryMax is not { } salary)
        {
            return -5;
        }

        // Defensa ante un perfil mal configurado (objetivo por debajo del
        // mínimo): el mínimo aceptable es el límite duro, así que el objetivo
        // se sube hasta él. Por la misma razón el tramo "cerca del objetivo"
        // nunca baja del mínimo: con un objetivo pegado al mínimo (p. ej.
        // 40k/42k), Objetivo-5k caería por debajo y premiaría (+15) ofertas
        // que el candidato no aceptaría.
        var minimum = profile.MinAcceptableSalary;
        var target = Math.Max(profile.TargetSalary, minimum);
        var nearTarget = Math.Max(target - NearTargetMargin, minimum);

        return salary switch
        {
            _ when salary >= target + WellAboveTargetMargin => 25,
            _ when salary >= target + AboveTargetMargin => 20,
            _ when salary >= nearTarget => 15,
            _ when salary >= minimum => 5,
            _ => -10,
        };
    }

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
