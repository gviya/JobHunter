using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobHunter.Domain.Entities;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Filtro barato de relevancia por TÍTULO para los providers que no filtran
/// por keyword en origen (<see cref="Domain.Interfaces.IJobProvider.FiltersByKeyword"/>
/// = false): Greenhouse/Lever devuelven el board COMPLETO de la empresa
/// (ventas, marketing, RR. HH....) y, sin este filtro, cada puesto no técnico
/// en España/remoto acabaría en el análisis de IA, que es el paso caro.
///
/// Es deliberadamente PERMISIVO: pasa cualquier título que mencione un rol
/// de desarrollo genérico ("developer", "ingeniero/a", "software"...), una
/// tecnología del perfil (TechWeights) o un término de las keywords de
/// búsqueda. Un falso positivo solo cuesta un análisis; un falso negativo
/// pierde una oferta. El encaje fino lo decide después la puntuación.
///
/// No se mira la descripción: casi cualquier oferta de una empresa
/// tecnológica menciona "software" en algún párrafo.
/// </summary>
internal static partial class RelevanceFilter
{
    // Palabras vacías que pueden aparecer en las keywords ("programador de
    // .NET") y casarían con casi cualquier título en español o inglés.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "la", "el", "los", "las", "en", "y", "con", "para", "al",
        "the", "and", "for", "of", "in", "a", "an", "to", "with",
    };

    public static List<JobOffer> Apply(IEnumerable<JobOffer> offers, string[] keywords, UserProfile profile)
    {
        var terms = Terms(keywords, profile);
        return [.. offers.Where(o => IsRelevant(o.Title, terms))];
    }

    public static bool IsRelevant(string? title, string[] keywords, UserProfile profile)
        => IsRelevant(title, Terms(keywords, profile));

    private static bool IsRelevant(string? title, string[] terms)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var text = RemoveDiacritics(title);

        return RoleRegex().IsMatch(text)
            || terms.Any(t => JobScoreCalculator.MentionsTech(text, t));
    }

    /// <summary>
    /// Techs del perfil con peso + términos sueltos de las keywords
    /// ("C# backend developer" → "C#", "backend", "developer"), sin acentos.
    /// Los términos de menos de 3 caracteres solo cuentan si llevan algún
    /// símbolo ("C#"): uno de solo letras ("de", "IT") casaría con demasiado.
    /// </summary>
    private static string[] Terms(string[] keywords, UserProfile profile)
    {
        var techs = profile.TechWeights
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key);

        var keywordTerms = keywords
            .SelectMany(k => k.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(t => !StopWords.Contains(t))
            .Where(t => t.Length >= 3 || t.Any(c => !char.IsLetter(c)));

        return [.. techs
            .Concat(keywordTerms)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(RemoveDiacritics)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Raíces de rol técnico con límite de palabra solo al INICIO, para cubrir
    // variantes: "desarrollador" → desarrolladora/es, "ingenier" → ingeniero/a,
    // ingeniería; "engineer" → engineering; "arquitect" → arquitecto/a.
    // Las siglas cortas (dev, sre, cto) exigen palabra completa para no casar
    // dentro de otras ("devolución", "cto" en "director"...).
    [GeneratedRegex(
        @"\b(developer|desarrollador|programador|programmer|engineer|ingenier|software|backend|back-end|back end|architect|arquitect|devops)|\b(dev|sre|cto)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleRegex();
}
