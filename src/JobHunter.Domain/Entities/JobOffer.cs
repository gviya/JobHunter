namespace JobHunter.Domain.Entities;

/// <summary>
/// Oferta de empleo normalizada procedente de cualquier fuente (Adzuna,
/// Greenhouse, Lever...). Es la entidad central del sistema: se descubre,
/// se deduplica, se analiza con IA y se puntúa.
/// </summary>
public sealed class JobOffer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ExternalId { get; init; }
    public required string SourceProvider { get; init; }
    public required string Title { get; set; }
    public required string CompanyName { get; set; }
    public string? CompanyNormalized { get; set; }
    public required string Url { get; init; }
    public string? Description { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public WorkMode WorkMode { get; set; }
    public string? Location { get; set; }
    public bool IsClientFinal { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
    public bool IsAnalyzed { get; set; }
    public bool IsDuplicate { get; set; }
    public JobScore? Score { get; set; }
    public float[]? Embedding { get; set; }
}

public enum WorkMode { Unknown, OnSite, Hybrid, Remote }
