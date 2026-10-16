using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Provider de Greenhouse: lee el board público de cada empresa objetivo
/// (sin autenticación). Como es el ATS propio de la empresa, las ofertas
/// se consideran de cliente final.
/// </summary>
public sealed class GreenhouseProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TargetCompaniesOptions> options,
    ILogger<GreenhouseProvider> logger) : IJobProvider
{
    private readonly TargetCompaniesOptions _options = options.Value;

    public string ProviderName => "Greenhouse";

    // El board devuelve TODAS las ofertas de la empresa (ventas, marketing...):
    // el pipeline las filtra por título antes de analizarlas.
    public bool FiltersByKeyword => false;

    public async Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        var tokens = _options.Greenhouse;

        if (tokens.Length == 0)
        {
            logger.LogInformation("Greenhouse sin empresas objetivo configuradas: se omite el provider.");
            return [];
        }

        var client = httpClientFactory.CreateClient();
        var offers = new List<JobOffer>();

        foreach (var token in tokens)
        {
            try
            {
                var url = $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true";
                var response = await client.GetFromJsonAsync<GreenhouseResponse>(url, ct);

                foreach (var job in response?.Jobs ?? [])
                {
                    offers.Add(Map(token, job));
                }
            }
            catch (Exception ex)
            {
                // Una empresa que falla NO tumba al resto.
                logger.LogWarning("Greenhouse '{Token}' falló: {Error}", token, ex.Message);
            }
        }

        return offers;
    }

    private JobOffer Map(string token, GreenhouseJob job)
    {
        var location = job.Location?.Name;

        return new JobOffer
        {
            ExternalId = job.Id?.ToString() ?? Guid.NewGuid().ToString(),
            SourceProvider = ProviderName,
            Title = job.Title ?? "(sin título)",
            CompanyName = token,
            Url = job.AbsoluteUrl ?? string.Empty,
            Description = job.Content,
            Location = location,
            WorkMode = WorkModeHeuristics.FromText(title: job.Title, location: location, description: job.Content),
            IsClientFinal = true,
            PublishedAt = job.UpdatedAt ?? DateTime.UtcNow,
        };
    }

    // ── DTOs de la respuesta de Greenhouse ──────────────────────────────
    private sealed record GreenhouseResponse(
        [property: JsonPropertyName("jobs")] GreenhouseJob[]? Jobs);

    private sealed record GreenhouseJob(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("absolute_url")] string? AbsoluteUrl,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
        [property: JsonPropertyName("location")] GreenhouseLocation? Location);

    private sealed record GreenhouseLocation(
        [property: JsonPropertyName("name")] string? Name);
}
