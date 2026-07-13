using AwesomeAssertions;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class AdzunaProviderTests
{
    private const string SampleJson = """
        {
          "results": [
            {
              "id": "123",
              "title": "Backend .NET Engineer",
              "redirect_url": "https://adzuna.example/123",
              "description": "Buscamos experiencia en C# y Azure.",
              "created": "2026-01-15T10:00:00Z",
              "salary_min": 45000,
              "salary_max": 60000,
              "company": { "display_name": "Acme SL" },
              "location": { "display_name": "Madrid, España" }
            }
          ]
        }
        """;

    [Fact]
    public async Task SearchAsync_ConJsonDeEjemplo_DeserializaYMapea()
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(SampleJson));
        var options = Options.Create(new AdzunaOptions { AppId = "id", AppKey = "key", Country = "es" });
        var provider = new AdzunaProvider(factory, options, NullLogger<AdzunaProvider>.Instance);

        var filter = new SearchFilter(["backend .NET"], "España", null, null);

        var result = await provider.SearchAsync(filter);

        result.Should().ContainSingle();

        var offer = result[0];
        offer.ExternalId.Should().Be("123");
        offer.SourceProvider.Should().Be("Adzuna");
        offer.Title.Should().Be("Backend .NET Engineer");
        offer.Url.Should().Be("https://adzuna.example/123");
        offer.CompanyName.Should().Be("Acme SL");
        offer.SalaryMin.Should().Be(45000m);
        offer.SalaryMax.Should().Be(60000m);
        offer.Location.Should().Be("Madrid, España");
    }

    [Fact]
    public async Task SearchAsync_ConCharsetNoEstandarDeAdzuna_NoFalla()
    {
        // Regresión: la API real de Adzuna responde con
        // "Content-Type: application/json; charset=utf8" (sin guión), que
        // System.Net.Http.Json rechaza si se usa GetFromJsonAsync.
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(SampleJson, charSet: "utf8"));
        var options = Options.Create(new AdzunaOptions { AppId = "id", AppKey = "key", Country = "es" });
        var provider = new AdzunaProvider(factory, options, NullLogger<AdzunaProvider>.Instance);

        var result = await provider.SearchAsync(new SearchFilter(["backend .NET"], "España", null, null));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_SinCredenciales_DevuelveVacio()
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(SampleJson));
        var options = Options.Create(new AdzunaOptions()); // AppId/AppKey vacíos
        var provider = new AdzunaProvider(factory, options, NullLogger<AdzunaProvider>.Instance);

        var result = await provider.SearchAsync(new SearchFilter(["x"], null, null, null));

        result.Should().BeEmpty();
    }
}
