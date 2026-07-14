using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class JSearchProviderTests
{
    // Estructura real de /search-v2 (verificada contra la API en vivo): los
    // resultados van anidados en data.jobs, no directamente en data.
    private const string SampleJson = """
        {
          "data": {
            "jobs": [
              {
                "job_id": "abc123",
                "job_title": "Backend .NET Developer",
                "employer_name": "Acme Corp",
                "job_apply_link": "https://jsearch.example/abc123",
                "job_description": "Buscamos experiencia en C# y Azure.",
                "job_city": "Madrid",
                "job_country": "ES",
                "job_location": "Madrid     •  a través de LinkedIn",
                "job_is_remote": true,
                "job_posted_at_datetime_utc": "2026-07-10T08:00:00Z",
                "job_min_salary": 40000,
                "job_max_salary": 55000
              }
            ]
          }
        }
        """;

    // Sin employer_name, sin job_city/job_country y sin job_is_remote: activa
    // los fallbacks (empresa desconocida, ubicación extraída de job_location
    // recortando el "• a través de..." y la heurística de texto para el modo
    // de trabajo). Este es el caso REAL más común de /search-v2: job_city y
    // job_country casi siempre vienen null.
    private const string FallbacksJson = """
        {
          "data": {
            "jobs": [
              {
                "job_id": "xyz789",
                "job_title": "C# Developer",
                "employer_name": null,
                "job_apply_link": "https://jsearch.example/xyz789",
                "job_description": "Modelo híbrido: 2 días de oficina.",
                "job_city": null,
                "job_country": null,
                "job_location": "Alcalá de Henares     •  a través de Jooble",
                "job_is_remote": false,
                "job_posted_at_datetime_utc": null,
                "job_min_salary": null,
                "job_max_salary": null
              }
            ]
          }
        }
        """;

    private static JSearchProvider CreateProvider(string json, string apiKey = "key")
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(json));
        var options = Options.Create(new JSearchOptions { ApiKey = apiKey });
        return new JSearchProvider(factory, options, NullLogger<JSearchProvider>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ConJsonDeEjemplo_DeserializaYMapea()
    {
        var provider = CreateProvider(SampleJson);

        var result = await provider.SearchAsync(new SearchFilter(["backend .NET"], "España", null, null));

        result.Should().ContainSingle();

        var offer = result[0];
        offer.ExternalId.Should().Be("abc123");
        offer.SourceProvider.Should().Be("JSearch");
        offer.Title.Should().Be("Backend .NET Developer");
        offer.CompanyName.Should().Be("Acme Corp");
        offer.Url.Should().Be("https://jsearch.example/abc123");
        offer.SalaryMin.Should().Be(40000m);
        offer.SalaryMax.Should().Be(55000m);
        offer.Location.Should().Be("Madrid");
        offer.WorkMode.Should().Be(WorkMode.Remote);
        offer.PublishedAt.Should().Be(new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SearchAsync_ConCamposNulos_AplicaLosFallbacks()
    {
        var provider = CreateProvider(FallbacksJson);

        var result = await provider.SearchAsync(new SearchFilter(["C# developer"], "España", null, null));

        result.Should().ContainSingle();

        var offer = result[0];
        offer.CompanyName.Should().Be("(empresa desconocida)");
        offer.Location.Should().Be("Alcalá de Henares");
        // job_is_remote=false NO fuerza Unknown: la descripción menciona
        // "híbrido" y la heurística compartida lo detecta.
        offer.WorkMode.Should().Be(WorkMode.Hybrid);
        offer.SalaryMin.Should().BeNull();
        offer.SalaryMax.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_SinJobCityNiJobLocation_UsaJobCountryComoUltimoFallback()
    {
        const string json = """
            {
              "data": {
                "jobs": [
                  {
                    "job_id": "noloc1",
                    "job_title": "Backend Developer",
                    "employer_name": "Acme Corp",
                    "job_apply_link": "https://jsearch.example/noloc1",
                    "job_city": null,
                    "job_country": "ES",
                    "job_location": null,
                    "job_is_remote": null
                  }
                ]
              }
            }
            """;
        var provider = CreateProvider(json);

        var result = await provider.SearchAsync(new SearchFilter(["backend"], "España", null, null));

        result.Should().ContainSingle();
        result[0].Location.Should().Be("ES");
    }

    [Fact]
    public async Task SearchAsync_SinApiKey_DevuelveVacio()
    {
        var provider = CreateProvider(SampleJson, apiKey: "");

        var result = await provider.SearchAsync(new SearchFilter(["x"], null, null, null));

        result.Should().BeEmpty();
    }
}
