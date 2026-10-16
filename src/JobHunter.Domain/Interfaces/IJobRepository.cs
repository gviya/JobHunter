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

    /// <summary>
    /// Ofertas sin analizar y no duplicadas descubiertas desde
    /// <paramref name="discoveredSince"/>: las nuevas de esta ejecución más
    /// las que fallaron al analizarse en ejecuciones anteriores (IA caída,
    /// créditos agotados...), que así se reintentan en vez de perderse.
    /// </summary>
    Task<IReadOnlyList<JobOffer>> GetUnanalyzedAsync(DateTime discoveredSince, CancellationToken ct = default);

    /// <summary>Ofertas recientes de una misma empresa (para el dedup semántico).</summary>
    Task<IReadOnlyList<JobOffer>> GetRecentByCompanyAsync(
        string companyNormalized, int days = 30, CancellationToken ct = default);

    /// <summary>Actualiza una oferta ya existente (score, flags, embedding...).</summary>
    Task UpdateAsync(JobOffer offer, CancellationToken ct = default);

    /// <summary>
    /// Top ofertas por TotalScore descubiertas desde <paramref name="discoveredSince"/>
    /// (para el informe). Sin esa ventana el informe "diario" sería el top
    /// histórico: las mismas ofertas cada día, aunque ya hayan caducado.
    /// </summary>
    Task<IReadOnlyList<JobOffer>> GetTopScoredAsync(
        DateTime discoveredSince, int count = 15, CancellationToken ct = default);

    /// <summary>Guarda el informe diario.</summary>
    Task SaveReportAsync(DailyReport report, CancellationToken ct = default);

    /// <summary>Último informe generado (o null si aún no hay ninguno).</summary>
    Task<DailyReport?> GetLatestReportAsync(CancellationToken ct = default);
}
