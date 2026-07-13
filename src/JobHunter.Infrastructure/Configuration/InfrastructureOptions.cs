namespace JobHunter.Infrastructure.Configuration;

/// <summary>Sección "Adzuna" de configuración.</summary>
public sealed class AdzunaOptions
{
    public const string SectionName = "Adzuna";

    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string Country { get; set; } = "es";
}

/// <summary>Sección "Anthropic" (cliente de Claude).</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
}

/// <summary>
/// Sección "Ai": elige qué implementación de <c>IAiAnalysisService</c> se
/// registra. "Claude" (por defecto, de pago) u "Ollama" (modelo local,
/// gratuito, requiere Ollama corriendo).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public const string DefaultProvider = "Claude";

    public string Provider { get; set; } = DefaultProvider;
}

/// <summary>Sección "InfoJobs" (OAuth2 client credentials).</summary>
public sealed class InfoJobsOptions
{
    public const string SectionName = "InfoJobs";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

/// <summary>Sección "TargetCompanies": board tokens de Greenhouse y Lever.</summary>
public sealed class TargetCompaniesOptions
{
    public const string SectionName = "TargetCompanies";

    public string[] Greenhouse { get; set; } = [];
    public string[] Lever { get; set; } = [];
}

/// <summary>Sección "Ollama" (embeddings para el dedup semántico y, opcionalmente, análisis de ofertas).</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Modelo de chat usado por <c>OllamaAnalysisService</c> cuando Ai:Provider="Ollama".</summary>
    public string AnalysisModel { get; set; } = "qwen3.6:27b";
}

/// <summary>Sección "Email" (SMTP para el informe diario).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
