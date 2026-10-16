using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Provider de Lever: lee las postings públicas de cada empresa objetivo
/// (sin autenticación). Usa el campo <c>workplaceType</c> para el modo de
/// trabajo. Al ser el ATS propio, se marcan de cliente final POR DEFECTO
/// (la IA lo corrige al analizar).
/// </summary>
public sealed class LeverProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TargetCompaniesOptions> options,
    ILogger<LeverProvider> logger) : IJobProvider
{
    private readonly TargetCompaniesOptions _options = options.Value;

    public string ProviderName => "Lever";

    // Igual que Greenhouse: devuelve el board completo, sin filtro por keyword.
    public bool FiltersByKeyword => false;

    public async Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        var tokens = _options.Lever;

        if (tokens.Length == 0)
        {
            logger.LogInformation("Lever sin empresas objetivo configuradas: se omite el provider.");
            return [];
        }

        var client = httpClientFactory.CreateClient();
        var offers = new List<JobOffer>();

        foreach (var token in tokens)
        {
            try
            {
                var url = $"https://api.lever.co/v0/postings/{token}?mode=json";
                var postings = await client.GetFromJsonAsync<LeverPosting[]>(url, ct);

                foreach (var posting in postings ?? [])
                {
                    offers.Add(Map(token, posting));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Lever '{Token}' falló: {Error}", token, ex.Message);
            }
        }

        return offers;
    }

    private JobOffer Map(string token, LeverPosting p)
    {
        var location = p.Categories?.Location;

        var publishedAt = p.CreatedAt is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : DateTime.UtcNow;

        var description = BuildDescription(p);

        // El workplaceType estructurado manda; si la empresa no lo rellena
        // ("unspecified"), se recurre al texto como en el resto de fuentes.
        var workMode = WorkModeHeuristics.FromLeverWorkplaceType(p.WorkplaceType);
        if (workMode == WorkMode.Unknown)
        {
            workMode = WorkModeHeuristics.FromText(title: p.Text, location: location, description: description);
        }

        return new JobOffer
        {
            ExternalId = p.Id ?? Guid.NewGuid().ToString(),
            SourceProvider = ProviderName,
            Title = p.Text ?? JobOffer.UntitledTitle,
            CompanyName = token,
            Url = p.HostedUrl ?? string.Empty,
            Description = description,
            Location = location,
            WorkMode = workMode,
            IsClientFinal = true,
            PublishedAt = publishedAt,
        };
    }

    /// <summary>
    /// Descripción COMPLETA de la posting. En Lever "description" suele ser
    /// solo la presentación: los requisitos viven en "lists" ([{"text":
    /// "Requirements", "content": "&lt;li&gt;...&lt;/li&gt;"}]) y el cierre en
    /// "additionalPlain". Sin ellos la IA no ve qué pide la oferta y la
    /// puntuación la toma por mal explicada. Cada lista va como
    /// "Título:" + sus viñetas, separando bloques con una línea en blanco.
    /// </summary>
    private static string? BuildDescription(LeverPosting p)
    {
        var intro = string.IsNullOrWhiteSpace(p.DescriptionPlain)
            ? HtmlText.ToPlainText(p.Description)
            : p.DescriptionPlain.Trim();

        List<string> blocks = [];

        if (!string.IsNullOrWhiteSpace(intro))
        {
            blocks.Add(intro);
        }

        foreach (var list in p.Lists ?? [])
        {
            var content = HtmlText.ToPlainText(list.Content);
            if (content is null)
            {
                continue;
            }

            var title = list.Text?.Trim().TrimEnd(':');
            blocks.Add(string.IsNullOrWhiteSpace(title) ? content : $"{title}:\n{content}");
        }

        if (!string.IsNullOrWhiteSpace(p.AdditionalPlain))
        {
            blocks.Add(p.AdditionalPlain.Trim());
        }

        return blocks.Count > 0 ? string.Join("\n\n", blocks) : null;
    }

    // ── DTOs de la respuesta de Lever ───────────────────────────────────
    private sealed record LeverPosting(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("hostedUrl")] string? HostedUrl,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("descriptionPlain")] string? DescriptionPlain,
        [property: JsonPropertyName("lists")] LeverList[]? Lists,
        [property: JsonPropertyName("additionalPlain")] string? AdditionalPlain,
        [property: JsonPropertyName("workplaceType")] string? WorkplaceType,
        [property: JsonPropertyName("createdAt")] long? CreatedAt,
        [property: JsonPropertyName("categories")] LeverCategories? Categories);

    /// <summary>Bloque de la posting: título ("Requirements") y viñetas en HTML.</summary>
    private sealed record LeverList(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record LeverCategories(
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("team")] string? Team,
        [property: JsonPropertyName("commitment")] string? Commitment);
}
