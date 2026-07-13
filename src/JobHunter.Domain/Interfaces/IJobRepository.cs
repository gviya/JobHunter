using JobHunter.Domain.Entities;

namespace JobHunter.Domain.Interfaces;

/// <summary>
/// Acceso a la persistencia de ofertas e informes. Lo implementa
/// Infrastructure con EF Core; Application solo conoce esta interfaz.
/// </summary>
public interface IJobRepository
{
    /// <summary>¿Existe ya una oferta con ese (ExternalId, SourceProvider)?</summary>
    Task<bool> ExistsAsync(string externalId, string sourceProvider, CancellationToken ct = default);

    /// <summary>Persiste una oferta nueva.</summary>
    Task SaveAsync(JobOffer offer, CancellationToken ct = default);

    /// <summary>Ofertas descubiertas pero aún sin analizar y no duplicadas.</summary>
    Task<IReadOnlyList<JobOffer>> GetUnanalyzedAsync(CancellationToken ct = default);

    /// <summary>Ofertas recientes de una misma empresa (para el dedup semántico).</summary>
    Task<IReadOnlyList<JobOffer>> GetRecentByCompanyAsync(
        string companyNormalized, int days = 30, CancellationToken ct = default);

    /// <summary>Actualiza una oferta ya existente (score, flags, embedding...).</summary>
    Task UpdateAsync(JobOffer offer, CancellationToken ct = default);

    /// <summary>Top ofertas por TotalScore (para el informe).</summary>
    Task<IReadOnlyList<JobOffer>> GetTopScoredAsync(int count = 15, CancellationToken ct = default);

    /// <summary>Guarda el informe diario.</summary>
    Task SaveReportAsync(DailyReport report, CancellationToken ct = default);

    /// <summary>Último informe generado (o null si aún no hay ninguno).</summary>
    Task<DailyReport?> GetLatestReportAsync(CancellationToken ct = default);
}
