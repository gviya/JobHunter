using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Infrastructure.Services;

/// <summary>
/// Construye el prompt de análisis y parsea la respuesta JSON del modelo.
/// Lo comparten todas las implementaciones de <see cref="IAiAnalysisService"/>
/// (Claude, Ollama...) para que el criterio de evaluación sea idéntico sin
/// importar qué LLM responda.
/// </summary>
internal static class JobAnalysisPrompt
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string Build(JobOffer offer, UserProfile profile)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Eres un asistente que evalúa ofertas de empleo para un desarrollador backend .NET.");
        sb.AppendLine();
        sb.AppendLine("PERFIL DEL CANDIDATO:");
        sb.AppendLine($"- Tecnologías que domina: {string.Join(", ", profile.KnownTechs)}");
        sb.AppendLine($"- Tecnologías que desea aprender: {string.Join(", ", profile.DesiredTechs)}");
        sb.AppendLine($"- Tecnologías excluyentes (no quiere): {string.Join(", ", profile.ExcludedTechs)}");
        sb.AppendLine($"- Modo de trabajo preferido: {profile.PreferredWorkMode}");
        sb.AppendLine($"- Salario objetivo: {profile.TargetSalary} EUR/año (mínimo {profile.MinAcceptableSalary}).");
        sb.AppendLine();
        sb.AppendLine("OFERTA:");
        sb.AppendLine($"- Título: {offer.Title}");
        sb.AppendLine($"- Empresa: {offer.CompanyName}");
        sb.AppendLine($"- Modo de trabajo detectado: {offer.WorkMode}");
        sb.AppendLine($"- Localización: {offer.Location ?? "(desconocida)"}");
        sb.AppendLine($"- Salario: {FormatSalary(offer)}");
        sb.AppendLine($"- Descripción: {Truncate(offer.Description, 4000)}");
        sb.AppendLine();
        sb.AppendLine("Analiza la oferta contra el perfil y responde ÚNICAMENTE con un objeto JSON válido");
        sb.AppendLine("(sin texto adicional, sin fences) con EXACTAMENTE esta forma:");
        sb.AppendLine("""
            {
              "summary": "resumen en 1-2 frases",
              "seniority": "Junior|Mid|Senior|Lead",
              "importantTechs": ["tech1", "tech2"],
              "compatibility": 0,
              "pros": ["pro1", "pro2"],
              "cons": ["con1", "con2"],
              "recommendation": "recomendación breve"
            }
            """);
        sb.AppendLine("Donde compatibility es un entero 0-100 de encaje con el perfil.");

        return sb.ToString();
    }

    public static bool TryParse(string content, out AiAnalysisResult result)
    {
        result = null!;

        var json = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AnalysisJson>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            result = new AiAnalysisResult(
                Summary: parsed.Summary ?? string.Empty,
                Seniority: parsed.Seniority ?? "Unknown",
                ImportantTechs: parsed.ImportantTechs ?? [],
                Compatibility: Math.Clamp(parsed.Compatibility, 0, 100),
                Pros: parsed.Pros ?? [],
                Cons: parsed.Cons ?? [],
                Recommendation: parsed.Recommendation ?? string.Empty);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Limpia fences ```json y recorta al objeto JSON externo.</summary>
    private static string ExtractJson(string content)
    {
        var text = content.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            // Quita la primera línea (```json o ```) y la última fence.
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                text = text[..lastFence];
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static string FormatSalary(JobOffer offer) => (offer.SalaryMin, offer.SalaryMax) switch
    {
        (null, null) => "(no indicado)",
        (var min, null) => $"desde {min}",
        (null, var max) => $"hasta {max}",
        var (min, max) => $"{min} - {max}",
    };

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(sin descripción)";
        }

        return text.Length <= max ? text : text[..max];
    }

    private sealed record AnalysisJson(
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("seniority")] string? Seniority,
        [property: JsonPropertyName("importantTechs")] string[]? ImportantTechs,
        [property: JsonPropertyName("compatibility")] int Compatibility,
        [property: JsonPropertyName("pros")] string[]? Pros,
        [property: JsonPropertyName("cons")] string[]? Cons,
        [property: JsonPropertyName("recommendation")] string? Recommendation);
}
