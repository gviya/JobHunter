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

    /// <summary>
    /// True cuando la fuente solo devuelve un EXTRACTO de la descripción
    /// (Adzuna la trunca a ~500 caracteres; Jooble da un "snippet"): su
    /// longitud no dice nada de lo bien o mal explicada que está la oferta,
    /// así que no se puntúa la calidad de la información y se avisa a la IA.
    /// </summary>
    public bool DescriptionIsSnippet { get; set; }

    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }

    /// <summary>
    /// Modo de trabajo. El provider lo estima (heurística o campo de la
    /// fuente) y, tras el análisis, se sustituye por el que la IA lee en la
    /// oferta si esta lo deja claro.
    /// </summary>
    public WorkMode WorkMode { get; set; }

    public string? Location { get; set; }

    /// <summary>
    /// Cliente final (empresa de producto) frente a consultora/agencia; null
    /// = no se sabe. Los providers de ATS (Greenhouse/Lever) lo ponen a true
    /// por defecto y los agregadores lo dejan en null; tras el análisis manda
    /// lo que deduzca la IA, porque ni los agregadores saben marcarlo ni todas
    /// las "empresas objetivo" son cliente final. El "no se sabe" es explícito
    /// para que puntúe neutro: con un bool, toda oferta de agregador nacía
    /// "consultora" y la fuente seguía decidiendo la puntuación.
    /// </summary>
    public bool? IsClientFinal { get; set; }

    public DateTime PublishedAt { get; set; }
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
    public bool IsAnalyzed { get; set; }
    public bool IsDuplicate { get; set; }
    public JobScore? Score { get; set; }
    public float[]? Embedding { get; set; }
}

public enum WorkMode { Unknown, OnSite, Hybrid, Remote }
