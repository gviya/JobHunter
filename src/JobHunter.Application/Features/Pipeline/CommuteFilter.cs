using JobHunter.Domain.Entities;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Filtro de desplazamiento según el modo de trabajo y el perfil:
///  - Remoto (y modo desconocido) pasa siempre: es la máxima prioridad y no
///    exige desplazarse.
///  - Híbrido/presencial solo pasa si el perfil lo acepta Y la localización
///    es "conmutable": menciona la ciudad del candidato o una de sus zonas
///    preferidas, y NO menciona un área excluida (municipios de cercanías o
///    metro ligero como "Alcobendas, Madrid", que contienen la ciudad pero
///    no valen).
///
/// Si el perfil no define ciudad, el filtro no aplica (pasa todo). Una
/// localización vacía también pasa: mejor un falso positivo que perder una
/// oferta válida por falta de dato — la puntuación ya penaliza la falta de
/// información.
///
/// Corre después de <see cref="LocationFilter"/> y ANTES del análisis de IA
/// para no gastar cuota en ofertas que se descartarían igualmente.
/// </summary>
internal static class CommuteFilter
{
    public static List<JobOffer> Apply(List<JobOffer> offers, UserProfile profile)
        => [.. offers.Where(o => IsAcceptable(o, profile))];

    public static bool IsAcceptable(JobOffer offer, UserProfile profile) => offer.WorkMode switch
    {
        WorkMode.Hybrid => profile.AcceptHybrid && IsCommutable(offer.Location, profile),
        WorkMode.OnSite => profile.AcceptOnSite && IsCommutable(offer.Location, profile),
        _ => true,
    };

    public static bool IsCommutable(string? location, UserProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.City) || string.IsNullOrWhiteSpace(location))
        {
            return true;
        }

        if (profile.ExcludedAreas.Any(a => location.Contains(a, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return location.Contains(profile.City, StringComparison.OrdinalIgnoreCase)
            || InPreferredZone(location, profile);
    }

    public static bool InPreferredZone(string? location, UserProfile profile)
        => !string.IsNullOrWhiteSpace(location)
           && profile.PreferredZones.Any(z => location.Contains(z, StringComparison.OrdinalIgnoreCase));
}
