namespace JobHunter.Domain.Entities;

/// <summary>
/// Puntuación y análisis de IA de una oferta (relación 1:1 con
/// <see cref="JobOffer"/>). <see cref="TotalScore"/> es la suma de los
/// componentes parciales y ordena el top del informe diario.
/// </summary>
public sealed class JobScore
{
    /// <summary>Clave primaria compartida: es a la vez PK y FK hacia JobOffer.</summary>
    public Guid JobOfferId { get; init; }

    public int TotalScore { get; set; }
    public int CompatibilityScore { get; set; }
    public int RemoteScore { get; set; }
    public int SalaryScore { get; set; }
    public int TechScore { get; set; }
    public int ClientFinalScore { get; set; }

    /// <summary>
    /// Penaliza ofertas mal explicadas (descripción muy corta o inexistente):
    /// si no dan detalle no se puede valorar el encaje, así que restan.
    /// </summary>
    public int InfoQualityScore { get; set; }

    public string? AiSummary { get; set; }
    public string? AiSeniority { get; set; }
    public string? AiTechList { get; set; }

    /// <summary>Requisitos clave que pide la oferta (extraídos por la IA).</summary>
    public string? AiRequirements { get; set; }

    public string? AiRecommendation { get; set; }

    public DateTime ScoredAt { get; set; } = DateTime.UtcNow;
}
