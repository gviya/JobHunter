using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Provider de <see href="https://developer.adzuna.com/">Adzuna</see>:
/// agregador con API REST pública (requiere app_id + app_key gratuitos).
/// </summary>
public sealed class AdzunaProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AdzunaOptions> options,
    ILogger<AdzunaProvider> logger) : IJobProvider
{
    private readonly AdzunaOptions _options = options.Value;

    public string ProviderName => "Adzuna";

    public async Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppKey))
        {
            logger.LogWarning("Adzuna sin credenciales (AppId/AppKey vacíos): se omite el provider.");
            return [];
        }

        var client = httpClientFactory.CreateClient();
        var offers = new Dictionary<string, JobOffer>();

        // Una búsqueda POR CADA keyword: el parámetro "what" de Adzuna exige
        // TODAS las palabras a la vez (AND), así que unir "backend .NET",
        // "C# developer" y "desarrollador backend" en un solo "what" nunca
        // encuentra nada. Los resultados se fusionan y se deduplican por id.
        foreach (var keyword in filter.Keywords)
        {
            try
            {
                var results = await SearchKeywordAsync(client, keyword, filter, ct);

                foreach (var r in results)
                {
                    var id = r.Id ?? Guid.NewGuid().ToString();
                    offers.TryAdd(id, Map(r));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Adzuna: búsqueda '{Keyword}' falló: {Error}", keyword, ex.Message);
            }
        }

        return [.. offers.Values];
    }

    private async Task<AdzunaResult[]> SearchKeywordAsync(
        HttpClient client, string keyword, SearchFilter filter, CancellationToken ct)
    {
        var what = HttpUtility.UrlEncode(keyword);
        var country = string.IsNullOrWhiteSpace(_options.Country) ? "es" : _options.Country;

        var url = $"https://api.adzuna.com/v1/api/jobs/{country}/search/1" +
                  $"?app_id={_options.AppId}&app_key={_options.AppKey}" +
                  $"&results_per_page={filter.MaxResults}&what={what}";

        if (!string.IsNullOrWhiteSpace(filter.Location))
        {
            url += $"&where={HttpUtility.UrlEncode(filter.Location)}";
        }

        if (filter.MinSalary is { } minSalary)
        {
            url += $"&salary_min={minSalary.ToString(CultureInfo.InvariantCulture)}";
        }

        // Adzuna devuelve "Content-Type: application/json; charset=utf8"
        // (sin guión). Tanto GetFromJsonAsync como GetStringAsync resuelven
        // el encoding a partir de esa cabecera y "utf8" no es un nombre
        // válido para Encoding.GetEncoding: ambos lanzan "The character set
        // provided in ContentType is invalid." Se evita leyendo bytes crudos
        // (sin interpretar el charset) y dejando que JsonSerializer decodifique
        // el UTF-8 directamente — JSON siempre es UTF-8 salvo BOM explícito.
        var bytes = await client.GetByteArrayAsync(url, ct);
        var response = JsonSerializer.Deserialize<AdzunaResponse>(bytes);

        return response?.Results ?? [];
    }

    private JobOffer Map(AdzunaResult r)
    {
        var location = r.Location?.DisplayName;

        return new JobOffer
        {
            ExternalId = r.Id ?? Guid.NewGuid().ToString(),
            SourceProvider = ProviderName,
            Title = r.Title ?? "(sin título)",
            CompanyName = r.Company?.DisplayName ?? "(empresa desconocida)",
            Url = r.RedirectUrl ?? string.Empty,
            Description = r.Description,
            SalaryMin = r.SalaryMin,
            SalaryMax = r.SalaryMax,
            Location = location,
            WorkMode = WorkModeHeuristics.FromText(r.Title, r.Description, location),
            PublishedAt = r.Created ?? DateTime.UtcNow,
        };
    }

    // ── DTOs de la respuesta de Adzuna ──────────────────────────────────
    private sealed record AdzunaResponse(
        [property: JsonPropertyName("results")] AdzunaResult[]? Results);

    private sealed record AdzunaResult(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("redirect_url")] string? RedirectUrl,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("created")] DateTime? Created,
        [property: JsonPropertyName("salary_min")] decimal? SalaryMin,
        [property: JsonPropertyName("salary_max")] decimal? SalaryMax,
        [property: JsonPropertyName("company")] AdzunaCompany? Company,
        [property: JsonPropertyName("location")] AdzunaLocation? Location);

    private sealed record AdzunaCompany(
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record AdzunaLocation(
        [property: JsonPropertyName("display_name")] string? DisplayName);
}
