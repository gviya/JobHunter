using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Provider de <see href="https://jooble.org/api/about">Jooble</see>:
/// agregador con API de partners gratuita y buena cobertura del mercado
/// español. La API key viaja EN LA URL (POST /api/{key}), así que ningún
/// log de este provider debe incluir la URL completa.
/// </summary>
public sealed class JoobleProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<JoobleOptions> options,
    ILogger<JoobleProvider> logger) : IJobProvider
{
    private readonly JoobleOptions _options = options.Value;

    public string ProviderName => "Jooble";

    public bool FiltersByKeyword => true;

    public async Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("Jooble no configurado (ApiKey vacía): se omite el provider.");
            return [];
        }

        var client = httpClientFactory.CreateClient();
        var offers = new Dictionary<string, JobOffer>();

        // Una búsqueda POR CADA keyword, igual que Adzuna y JSearch,
        // fusionando y deduplicando por id.
        foreach (var keyword in filter.Keywords)
        {
            try
            {
                var results = await SearchKeywordAsync(client, keyword, filter, ct);

                foreach (var r in results)
                {
                    var id = r.Id?.ToString() ?? Guid.NewGuid().ToString();
                    offers.TryAdd(id, Map(r, id));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Jooble: búsqueda '{Keyword}' falló: {Error}", keyword, ex.Message);
            }
        }

        return [.. offers.Values];
    }

    private async Task<JoobleJob[]> SearchKeywordAsync(
        HttpClient client, string keyword, SearchFilter filter, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://es.jooble.org/api/"
            : _options.BaseUrl;
        var url = $"{baseUrl.TrimEnd('/')}/{_options.ApiKey}";
        var body = new JoobleRequest(keyword, string.IsNullOrWhiteSpace(filter.Location) ? "España" : filter.Location);

        using var response = await client.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        // Bytes crudos + JsonSerializer, como en Adzuna: inmune a cabeceras
        // Content-Type con charset no estándar.
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var payload = JsonSerializer.Deserialize<JoobleResponse>(bytes);

        return payload?.Jobs ?? [];
    }

    private JobOffer Map(JoobleJob r, string id)
    {
        var description = HtmlText.ToPlainText(r.Snippet);

        // El salario llega como texto libre ("30.000 € - 40.000 €"). SalaryText
        // solo lo convierte si es anual, en EUR y sin ambigüedad; si no, queda
        // sin salario: uno mal leído distorsionaría la puntuación.
        var salary = SalaryText.Parse(r.Salary);

        return new JobOffer
        {
            ExternalId = id,
            SourceProvider = ProviderName,
            Title = r.Title ?? JobOffer.UntitledTitle,
            CompanyName = string.IsNullOrWhiteSpace(r.Company) ? JobOffer.UnknownCompany : r.Company,
            Url = r.Link ?? string.Empty,
            Description = description,
            DescriptionIsSnippet = true,
            Location = r.Location,
            SalaryMin = salary?.Min,
            SalaryMax = salary?.Max,
            WorkMode = WorkModeHeuristics.FromText(title: r.Title, location: r.Location, description: description, extra: r.Type),
            PublishedAt = r.Updated ?? DateTime.UtcNow,
        };
    }

    // ── DTOs de la petición/respuesta de Jooble ─────────────────────────
    private sealed record JoobleRequest(
        [property: JsonPropertyName("keywords")] string Keywords,
        [property: JsonPropertyName("location")] string Location);

    private sealed record JoobleResponse(
        [property: JsonPropertyName("totalCount")] long? TotalCount,
        [property: JsonPropertyName("jobs")] JoobleJob[]? Jobs);

    private sealed record JoobleJob(
        // El id viene como número (a veces enorme, a veces como string):
        // AllowReadingFromString acepta ambas formas sin romper el parseo.
        [property: JsonPropertyName("id")]
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        long? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("company")] string? Company,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("snippet")] string? Snippet,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("salary")] string? Salary,
        [property: JsonPropertyName("updated")] DateTime? Updated,
        [property: JsonPropertyName("type")] string? Type);
}
