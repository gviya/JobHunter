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
/// Provider de <see href="https://rapidapi.com/letscrape-6bRBa3QguO5/api/jsearch">JSearch</see>
/// (RapidAPI): consulta Google for Jobs, que indexa ofertas de LinkedIn,
/// Indeed, Glassdoor, ZipRecruiter y más — es la vía para cubrir LinkedIn
/// sin scraping directo. Plan gratuito: 200 peticiones/mes, así que el
/// bucle por keyword (3 al día con el filtro por defecto) gasta ~90/mes.
/// </summary>
public sealed class JSearchProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<JSearchOptions> options,
    ILogger<JSearchProvider> logger) : IJobProvider
{
    private readonly JSearchOptions _options = options.Value;

    public string ProviderName => "JSearch";

    public bool FiltersByKeyword => true;

    public async Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("JSearch no configurado (ApiKey vacía): se omite el provider.");
            return [];
        }

        var client = httpClientFactory.CreateClient();
        var offers = new Dictionary<string, JobOffer>();

        // Una búsqueda POR CADA keyword, igual que Adzuna: aunque Google for
        // Jobs tolera consultas en lenguaje natural, mezclar las tres
        // keywords en una sola query diluye los resultados. Se fusionan y
        // deduplican por job_id.
        foreach (var keyword in filter.Keywords)
        {
            try
            {
                var results = await SearchKeywordAsync(client, keyword, filter, ct);

                foreach (var r in results)
                {
                    var id = r.JobId ?? Guid.NewGuid().ToString();
                    offers.TryAdd(id, Map(r));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("JSearch: búsqueda '{Keyword}' falló: {Error}", keyword, ex.Message);
            }
        }

        return [.. offers.Values];
    }

    private async Task<JSearchResult[]> SearchKeywordAsync(
        HttpClient client, string keyword, SearchFilter filter, CancellationToken ct)
    {
        var location = string.IsNullOrWhiteSpace(filter.Location) ? "España" : filter.Location;
        var query = HttpUtility.UrlEncode($"{keyword} en {location}");
        var host = string.IsNullOrWhiteSpace(_options.Host) ? "jsearch.p.rapidapi.com" : _options.Host;
        var country = string.IsNullOrWhiteSpace(_options.Country) ? "es" : _options.Country;
        var language = string.IsNullOrWhiteSpace(_options.Language) ? "es" : _options.Language;
        var datePosted = string.IsNullOrWhiteSpace(_options.DatePosted) ? "today" : _options.DatePosted;

        // "/search" está deprecado (devuelve 404 "Endpoint does not exist");
        // el endpoint vivo actual es "/search-v2".
        var url = $"https://{host}/search-v2?query={query}" +
                  $"&country={country}&language={language}&num_pages=1&date_posted={datePosted}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-RapidAPI-Key", _options.ApiKey);
        request.Headers.Add("X-RapidAPI-Host", host);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // Bytes crudos + JsonSerializer, como en Adzuna: inmune a cabeceras
        // Content-Type con charset no estándar.
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var payload = JsonSerializer.Deserialize<JSearchResponse>(bytes);

        return payload?.Data?.Jobs ?? [];
    }

    private JobOffer Map(JSearchResult r)
    {
        var location = ExtractLocation(r);

        return new JobOffer
        {
            ExternalId = r.JobId ?? Guid.NewGuid().ToString(),
            SourceProvider = ProviderName,
            Title = r.JobTitle ?? "(sin título)",
            CompanyName = r.EmployerName ?? "(empresa desconocida)",
            Url = r.JobApplyLink ?? string.Empty,
            Description = r.JobDescription,
            SalaryMin = (decimal?)r.JobMinSalary,
            SalaryMax = (decimal?)r.JobMaxSalary,
            Location = location,
            WorkMode = r.JobIsRemote == true
                ? WorkMode.Remote
                : WorkModeHeuristics.FromText(r.JobTitle, r.JobDescription, location),
            PublishedAt = r.JobPostedAt ?? DateTime.UtcNow,
        };
    }

    // "/search-v2" ya casi nunca rellena job_city/job_country: la ubicación
    // real viene como texto libre en job_location, con forma
    // "Ciudad     •  a través de Fuente" — se extrae la parte antes de "•".
    private static string? ExtractLocation(JSearchResult r)
    {
        if (!string.IsNullOrWhiteSpace(r.JobCity))
        {
            return r.JobCity;
        }

        if (!string.IsNullOrWhiteSpace(r.JobLocation))
        {
            var separatorIndex = r.JobLocation.IndexOf('•');
            var city = (separatorIndex >= 0 ? r.JobLocation[..separatorIndex] : r.JobLocation).Trim();

            if (!string.IsNullOrWhiteSpace(city))
            {
                return city;
            }
        }

        return r.JobCountry;
    }

    // ── DTOs de la respuesta de JSearch ─────────────────────────────────
    private sealed record JSearchResponse(
        [property: JsonPropertyName("data")] JSearchData? Data);

    private sealed record JSearchData(
        [property: JsonPropertyName("jobs")] JSearchResult[]? Jobs);

    private sealed record JSearchResult(
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("job_title")] string? JobTitle,
        [property: JsonPropertyName("employer_name")] string? EmployerName,
        [property: JsonPropertyName("job_apply_link")] string? JobApplyLink,
        [property: JsonPropertyName("job_description")] string? JobDescription,
        [property: JsonPropertyName("job_city")] string? JobCity,
        [property: JsonPropertyName("job_country")] string? JobCountry,
        [property: JsonPropertyName("job_location")] string? JobLocation,
        [property: JsonPropertyName("job_is_remote")] bool? JobIsRemote,
        [property: JsonPropertyName("job_posted_at_datetime_utc")] DateTime? JobPostedAt,
        [property: JsonPropertyName("job_min_salary")] double? JobMinSalary,
        [property: JsonPropertyName("job_max_salary")] double? JobMaxSalary);
}
