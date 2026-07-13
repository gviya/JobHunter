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

    Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default);
}

/// <summary>Criterios de búsqueda comunes a todos los providers.</summary>
public sealed record SearchFilter(
    string[] Keywords,
    string? Location,
    WorkMode? WorkMode,
    decimal? MinSalary,
    int MaxResults = 50);
