using System.Text.RegularExpressions;
using JobHunter.Domain.Entities;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Filtra ofertas por ubicación: España/Madrid o remoto sin especificar
/// pasan; ubicaciones claramente ajenas (India, EEUU, LatAm...) se
/// descartan aunque digan "remote". Greenhouse/Lever devuelven TODAS las
/// ofertas de la empresa (Adzuna sí admite filtrar por "where=", los otros
/// dos providers no), así que este filtro corre después de fusionar los
/// resultados de todos los providers, no en cada uno por separado.
///
/// Ubicación vacía se acepta (muchas ofertas 100% remotas no listan
/// ciudad): mejor un falso positivo ocasional que perder una oferta remota
/// válida por falta de dato.
/// </summary>
internal static class LocationFilter
{
    private static readonly string[] SpainKeywords =
    [
        "madrid", "barcelona", "valencia", "sevilla", "bilbao", "málaga", "malaga",
        "zaragoza", "españa", "espana", "spain",
    ];

    // Cadenas largas, sin ambigüedad de substring: Contains normal es seguro.
    // Greenhouse/Lever devuelven TODAS las ofertas de la empresa (sin filtro
    // por país), así que este listado necesita cubrir bien los mercados
    // habituales de las "target companies" configuradas (outsourcing/dev
    // shops con sedes o pools de contratación repartidos por medio mundo),
    // no solo los ejemplos originales (India/EEUU) que motivaron el filtro.
    private static readonly string[] NonSpainMarkers =
    [
        "india", "delhi", "bangalore", "hyderabad", "mumbai", "pune", "chennai",
        "pakistan", "lahore", "karachi", "islamabad",
        "united states", "usa", "u.s.a", "new york", "seattle", "bellevue",
        "san francisco", "austin", "chicago", "boston",
        "mexico", "méxico", "argentina", "brazil", "brasil", "colombia", "chile", "perú", "peru",
        "philippines", "manila", "poland", "polska", "warsaw", "ukraine", "romania",
        "canada", "toronto", "vancouver",
        "china", "singapore", "vietnam", "indonesia",
        "united kingdom", "london", "france", "paris", "germany", "berlin",
        "italy", "milan", "rome", "netherlands", "amsterdam", "portugal", "lisbon",
        "ireland", "dublin", "sweden", "stockholm", "switzerland", "zurich",
        "austria", "vienna", "belgium", "brussels", "denmark", "copenhagen",
        "norway", "oslo", "finland", "helsinki", "greece", "athens", "turkey", "istanbul",
        "russia", "moscow", "cyprus", "armenia", "georgia", "latvia", "moldova", "bulgaria",
        "israel", "tel-aviv", "tel aviv", "japan", "tokyo",
        "south africa", "morocco", "egypt",
        "australia", "sydney", "melbourne", "new zealand",
        "dubai", "abu dhabi", "saudi arabia", "qatar",
    ];

    // Marcadores demasiado cortos/ambiguos para Contains: "us" coincidiría
    // con "campus", "Austin", "business"...; "uk" con palabras que lo
    // contengan como substring. Se comparan como palabra completa con regex.
    private static readonly string[] NonSpainWholeWordMarkers = ["us", "uk"];

    public static List<JobOffer> Apply(List<JobOffer> offers)
        => [.. offers.Where(o => IsSpainCompatible(o.Location))];

    public static bool IsSpainCompatible(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return true;
        }

        if (SpainKeywords.Any(k => location.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (NonSpainMarkers.Any(k => location.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !NonSpainWholeWordMarkers.Any(w => ContainsWholeWord(location, w));
    }

    private static bool ContainsWholeWord(string text, string word)
        => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
}
