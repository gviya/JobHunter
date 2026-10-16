using JobHunter.Domain.Entities;

namespace JobHunter.Domain.Interfaces;

/// <summary>
/// Contrato de una fuente de ofertas de empleo. Cada implementación
/// (Adzuna, Greenhouse, Lever...) traduce su API concreta a
/// <see cref="JobOffer"/> normalizadas.
/// </summary>
public interface IJobProvider
{
    string ProviderName { get; }

    /// <summary>
    /// True si la fuente aplica las <see cref="SearchFilter.Keywords"/> en
    /// origen (Adzuna, JSearch, Jooble...). False si devuelve TODAS sus
    /// ofertas sin filtrar (boards de empresa como Greenhouse/Lever): el
    /// pipeline les aplica entonces un filtro de relevancia por título antes
    /// de guardarlas, para no gastar análisis de IA en puestos de ventas,
    /// marketing, etc.
    /// </summary>
    bool FiltersByKeyword { get; }

    Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default);
}

/// <summary>Criterios de búsqueda comunes a todos los providers.</summary>
public sealed record SearchFilter(
    string[] Keywords,
    string? Location,
    WorkMode? WorkMode,
    decimal? MinSalary,
    int MaxResults = 50);
