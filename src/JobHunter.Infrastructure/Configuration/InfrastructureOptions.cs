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

    /// <summary>
    /// Id del modelo. Haiku 5.5 cuesta $0.10/$0.50 por MTok (prompts de hasta
    /// 100K tokens), una décima parte que Haiku 4.5 ($1/$5).
    /// </summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>
    /// Esfuerzo (<c>output_config.effort</c>: low, medium, high, xhigh o max;
    /// Haiku 5.5 usa medium si no se envía). "low" basta para esta extracción
    /// corta y abarata el razonamiento. Vacío = no se envía: OBLIGATORIO con
    /// claude-haiku-4-5, que rechaza el parámetro con un 400.
    /// </summary>
    public string Effort { get; set; } = "low";

    /// <summary>
    /// Límite de tokens de salida. Incluye el razonamiento que Haiku 5.5 hace
    /// por defecto, así que va holgado: si se queda corto el JSON llega
    /// truncado (stop_reason "max_tokens").
    /// </summary>
    public int MaxTokens { get; set; } = 4096;
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

/// <summary>
/// Sección "JSearch": API de RapidAPI que consulta Google for Jobs
/// (indexa LinkedIn, Indeed, Glassdoor, ZipRecruiter...).
/// </summary>
public sealed class JSearchOptions
{
    public const string SectionName = "JSearch";

    public string ApiKey { get; set; } = "";
    public string Host { get; set; } = "jsearch.p.rapidapi.com";
    public string Country { get; set; } = "es";
    public string Language { get; set; } = "es";

    /// <summary>Ventana temporal de la búsqueda ("today", "3days", "week", "month", "all").</summary>
    public string DatePosted { get; set; } = "today";
}

/// <summary>Sección "Jooble": agregador con API de partners gratuita.</summary>
public sealed class JoobleOptions
{
    public const string SectionName = "Jooble";

    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://jooble.org/api/";
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
