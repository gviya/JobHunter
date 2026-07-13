using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Provider de InfoJobs. Requiere OAuth2 (client credentials) contra
/// https://developer.infojobs.net. Se deja como ESQUELETO para el MVP:
/// no está registrado en DI (ver DependencyInjection). Cuando se complete
/// el flujo OAuth, descoméntalo allí.
/// </summary>
public sealed class InfoJobsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<InfoJobsOptions> options,
    ILogger<InfoJobsProvider> logger) : IJobProvider
{
    private readonly InfoJobsOptions _options = options.Value;

    public string ProviderName => "InfoJobs";

    public Task<IReadOnlyList<JobOffer>> SearchAsync(SearchFilter filter, CancellationToken ct = default)
    {
        // TODO: implementar OAuth2 client credentials contra
        // https://developer.infojobs.net/ (token endpoint) y la búsqueda de
        // ofertas. De momento devuelve vacío para no bloquear el MVP.
        _ = httpClientFactory;
        _ = _options;
        logger.LogInformation("InfoJobs aún no implementado (OAuth2 pendiente): se omite.");
        return Task.FromResult<IReadOnlyList<JobOffer>>([]);
    }
}
