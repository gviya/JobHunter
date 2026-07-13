using JobHunter.Domain.Entities;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Heurística compartida para deducir el modo de trabajo a partir de texto
/// libre (título, descripción, localización) cuando la fuente no lo indica
/// de forma estructurada.
/// </summary>
internal static class WorkModeHeuristics
{
    public static WorkMode FromText(params string?[] fragments)
    {
        var text = string.Join(' ', fragments.Where(f => !string.IsNullOrWhiteSpace(f)))
            .ToLowerInvariant();

        if (text.Length == 0)
        {
            return WorkMode.Unknown;
        }

        if (Contains(text, "remoto", "remote", "teletrabajo", "en remoto", "fully remote"))
        {
            return WorkMode.Remote;
        }

        if (Contains(text, "híbrido", "hibrido", "hybrid", "semipresencial"))
        {
            return WorkMode.Hybrid;
        }

        if (Contains(text, "presencial", "on-site", "onsite", "on site", "in office"))
        {
            return WorkMode.OnSite;
        }

        return WorkMode.Unknown;
    }

    /// <summary>Mapea el <c>workplaceType</c> de Lever a nuestro enum.</summary>
    public static WorkMode FromLeverWorkplaceType(string? workplaceType) => workplaceType?.ToLowerInvariant() switch
    {
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        "on-site" or "onsite" or "unspecified" => WorkMode.OnSite,
        _ => WorkMode.Unknown,
    };

    private static bool Contains(string text, params string[] needles)
        => needles.Any(text.Contains);
}
