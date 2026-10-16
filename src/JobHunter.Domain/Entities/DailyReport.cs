namespace JobHunter.Domain.Entities;

/// <summary>
/// Informe diario generado por el pipeline: métricas de la ejecución más
/// el HTML que se envía por email. <see cref="TopOffers"/> es material de
/// trabajo en memoria y NO se persiste (se reconstruye desde la BD).
/// </summary>
public sealed class DailyReport
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public int TotalFound { get; set; }
    public int TotalNew { get; set; }
    public int TotalDuplicates { get; set; }
    public int TotalAnalyzed { get; set; }

    /// <summary>Top ofertas del día. No mapeada a BD.</summary>
    public List<JobOffer> TopOffers { get; set; } = [];

    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// True si la oferta se puntuó en ESTA ejecución (nueva o reintentada) y
    /// no en una anterior. El top abarca varios días, así que esto distingue
    /// la novedad de lo que ya salió en informes previos.
    /// </summary>
    public bool IsNewInThisRun(JobOffer offer) => offer.Score?.ScoredAt >= GeneratedAt;

    /// <summary>¿Hay alguna oferta del top puntuada en esta ejecución?</summary>
    public bool HasNewTopOffers() => TopOffers.Any(IsNewInThisRun);
}
