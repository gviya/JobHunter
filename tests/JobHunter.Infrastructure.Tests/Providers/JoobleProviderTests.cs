using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class JoobleProviderTests
{
    // El id de Jooble viene como NÚMERO (no string) y el snippet trae HTML
    // y entidades (&nbsp;), como en la API real.
    private const string SampleJson = """
        {
          "totalCount": 1,
          "jobs": [
            {
              "id": 1234567890123,
              "title": "Desarrollador Backend .NET",
              "company": "Beta Software",
              "link": "https://jooble.example/1234567890123",
              "snippet": "Buscamos <b>desarrollador</b> con&nbsp;experiencia en <strong>.NET</strong>.",
              "location": "Madrid",
              "salary": "35.000 € - 45.000 €",
              "updated": "2026-07-12T00:00:00Z",
              "type": "Trabajo remoto"
            }
          ]
        }
        """;

    private const string EmptyCompanyJson = """
        {
          "totalCount": 1,
          "jobs": [
            {
              "id": 42,
              "title": "Backend Developer",
              "company": "",
              "link": "https://jooble.example/42",
              "snippet": "Puesto presencial en oficina.",
              "location": "Madrid",
              "salary": "",
              "updated": "2026-07-12T00:00:00Z",
              "type": ""
            }
          ]
        }
        """;

    private static JoobleProvider CreateProvider(string json, string apiKey = "key")
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(json));
        var options = Options.Create(new JoobleOptions { ApiKey = apiKey });
        return new JoobleProvider(factory, options, NullLogger<JoobleProvider>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ConJsonDeEjemplo_DeserializaLimpiaHtmlYMapea()
    {
        var provider = CreateProvider(SampleJson);

        var result = await provider.SearchAsync(new SearchFilter(["backend .NET"], "España", null, null));

        result.Should().ContainSingle();

        var offer = result[0];
        offer.ExternalId.Should().Be("1234567890123"); // id numérico → string
        offer.SourceProvider.Should().Be("Jooble");
        offer.Title.Should().Be("Desarrollador Backend .NET");
        offer.CompanyName.Should().Be("Beta Software");
        offer.Url.Should().Be("https://jooble.example/1234567890123");
        offer.Description.Should().Be("Buscamos desarrollador con experiencia en .NET .");
        offer.DescriptionIsSnippet.Should().BeTrue(); // Jooble solo da un "snippet"
        offer.Location.Should().Be("Madrid");
        offer.WorkMode.Should().Be(WorkMode.Remote); // "Trabajo remoto" en type
        // El salary string de Jooble no se parsea todavía (TODO en el provider).
        offer.SalaryMin.Should().BeNull();
        offer.SalaryMax.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ConCompanyVacia_UsaElFallback()
    {
        var provider = CreateProvider(EmptyCompanyJson);

        var result = await provider.SearchAsync(new SearchFilter(["backend"], "España", null, null));

        result.Should().ContainSingle();
        result[0].CompanyName.Should().Be("(empresa desconocida)");
        result[0].WorkMode.Should().Be(WorkMode.OnSite); // "presencial" en el snippet
    }

    [Fact]
    public async Task SearchAsync_SinApiKey_DevuelveVacio()
    {
        var provider = CreateProvider(SampleJson, apiKey: "");

        var result = await provider.SearchAsync(new SearchFilter(["x"], null, null, null));

        result.Should().BeEmpty();
    }
}
