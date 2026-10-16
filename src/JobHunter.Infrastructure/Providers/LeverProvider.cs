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
/// trabajo. Al ser el ATS propio, se consideran de cliente final.
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

        return new JobOffer
        {
            ExternalId = p.Id ?? Guid.NewGuid().ToString(),
            SourceProvider = ProviderName,
            Title = p.Text ?? "(sin título)",
            CompanyName = token,
            Url = p.HostedUrl ?? string.Empty,
            Description = p.DescriptionPlain ?? p.Description,
            Location = location,
            WorkMode = WorkModeHeuristics.FromLeverWorkplaceType(p.WorkplaceType),
            IsClientFinal = true,
            PublishedAt = publishedAt,
        };
    }

    // ── DTOs de la respuesta de Lever ───────────────────────────────────
    private sealed record LeverPosting(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("hostedUrl")] string? HostedUrl,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("descriptionPlain")] string? DescriptionPlain,
        [property: JsonPropertyName("workplaceType")] string? WorkplaceType,
        [property: JsonPropertyName("createdAt")] long? CreatedAt,
        [property: JsonPropertyName("categories")] LeverCategories? Categories);

    private sealed record LeverCategories(
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("team")] string? Team,
        [property: JsonPropertyName("commitment")] string? Commitment);
}
