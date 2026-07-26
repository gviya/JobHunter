using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Jobs;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure;

/// <summary>
/// REGISTRO DE DEPENDENCIAS de Infrastructure: conecta las interfaces de
/// Domain/Application con sus implementaciones concretas (EF Core, los
/// providers HTTP, Claude, Ollama, MailKit y el job de Hangfire).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        AddOptions(services, configuration);
        AddProviders(services);
        AddExternalServices(services, configuration);

        // El perfil del candidato como singleton. Se lee de la sección
        // "UserProfile" (típicamente appsettings.Local.json, que está en
        // .gitignore: las preferencias personales no van al repositorio).
        // Sin sección se usa el perfil de ejemplo genérico.
        var profileSection = configuration.GetSection("UserProfile");
        var profile = profileSection.Exists()
            ? profileSection.Get<UserProfile>() ?? UserProfile.CreateDefault()
            : UserProfile.CreateDefault();
        services.AddSingleton(profile);

        // Job orquestado por Hangfire.
        services.AddScoped<DailySearchJob>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<JobHunterDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default")));

        services.AddScoped<IJobRepository, JobRepository>();
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdzunaOptions>(configuration.GetSection(AdzunaOptions.SectionName));
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<InfoJobsOptions>(configuration.GetSection(InfoJobsOptions.SectionName));
        services.Configure<JSearchOptions>(configuration.GetSection(JSearchOptions.SectionName));
        services.Configure<JoobleOptions>(configuration.GetSection(JoobleOptions.SectionName));
        services.Configure<TargetCompaniesOptions>(configuration.GetSection(TargetCompaniesOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
    }

    private static void AddProviders(IServiceCollection services)
    {
        // IHttpClientFactory para los providers que crean clientes ad hoc
        // (todos usan el cliente por defecto vía CreateClient()). Se le añade
        // resiliencia estándar (timeout total, reintentos con backoff y circuit
        // breaker) porque son APIs de terceros por Internet: un pico de latencia
        // o un 503 transitorio no debe dejar colgado ni tumbar el pipeline.
        // Ollama y Claude quedan fuera: son clientes tipados con su propio
        // nombre y sus timeouts largos (generación local / LLM).
        services.AddHttpClient(Options.DefaultName).AddStandardResilienceHandler();

        services.AddScoped<IJobProvider, AdzunaProvider>();
        services.AddScoped<IJobProvider, GreenhouseProvider>();
        services.AddScoped<IJobProvider, LeverProvider>();
        services.AddScoped<IJobProvider, JSearchProvider>();
        services.AddScoped<IJobProvider, JoobleProvider>();

        // InfoJobs pendiente de OAuth2 — descomentar cuando esté listo:
        // services.AddScoped<IJobProvider, InfoJobsProvider>();
    }

    private static void AddExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        var aiProvider = configuration.GetValue("Ai:Provider", AiOptions.DefaultProvider);

        if (string.Equals(aiProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            // Modelo de chat LOCAL vía Ollama: gratuito, sin límite de uso,
            // pero requiere Ollama corriendo con el modelo descargado.
            services.AddHttpClient<IAiAnalysisService, OllamaAnalysisService>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "http://localhost:11434" : opts.BaseUrl;
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                // Modelos locales grandes pueden tardar más que el timeout por defecto (100s).
                client.Timeout = TimeSpan.FromMinutes(3);
            });
        }
        else
        {
            // Claude Haiku (typed client: base + headers de la Messages API).
            services.AddHttpClient<IAiAnalysisService, ClaudeAnalysisService>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
                client.BaseAddress = new Uri("https://api.anthropic.com/");
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
                }
            });
        }

        // Ollama para embeddings (typed client: base URL del servidor local).
        // Se registra siempre, independientemente del provider de análisis:
        // el dedup semántico lo usa aunque el análisis vaya por Claude.
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "http://localhost:11434" : opts.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        });

        services.AddScoped<IEmailService, MailKitEmailService>();
    }
}
