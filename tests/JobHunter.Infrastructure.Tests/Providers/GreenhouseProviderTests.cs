using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class GreenhouseProviderTests
{
    // Forma real de /v1/boards/{token}/jobs?content=true: "content" es HTML
    // ESCAPADO (con las entidades del original doblemente escapadas).
    private const string SampleJson = """
        {
          "jobs": [
            {
              "id": 4567890,
              "title": "Senior Backend Engineer (.NET)",
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/4567890",
              "updated_at": "2026-07-10T08:00:00-04:00",
              "location": { "name": "Madrid, Spain" },
              "content": "&lt;div class=&quot;content-intro&quot;&gt;&lt;p&gt;En Acme construimos&amp;nbsp;nuestro propio producto.&lt;/p&gt;&lt;/div&gt;&lt;h3&gt;&lt;strong&gt;Requisitos&lt;/strong&gt;&lt;/h3&gt;&lt;ul&gt;&lt;li&gt;5+ años con C# y .NET&lt;/li&gt;&lt;li&gt;SQL Server &amp;amp; Docker&lt;/li&gt;&lt;/ul&gt;"
            }
          ]
        }
        """;

    private static GreenhouseProvider CreateProvider(string json, params string[] tokens)
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(json));
        var options = Options.Create(new TargetCompaniesOptions { Greenhouse = tokens });
        return new GreenhouseProvider(factory, options, NullLogger<GreenhouseProvider>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ConContentEscapado_GuardaLaDescripcionComoTextoLimpio()
    {
        var provider = CreateProvider(SampleJson, "acme");

        var result = await provider.SearchAsync(new SearchFilter(["backend .NET"], "España", null, null));

        result.Should().ContainSingle();

        var offer = result[0];
        offer.ExternalId.Should().Be("4567890");
        offer.SourceProvider.Should().Be("Greenhouse");
        offer.Title.Should().Be("Senior Backend Engineer (.NET)");
        offer.CompanyName.Should().Be("acme");
        offer.Url.Should().Be("https://boards.greenhouse.io/acme/jobs/4567890");
        offer.Location.Should().Be("Madrid, Spain");

        // Regresión: antes se guardaba el HTML escapado tal cual.
        offer.Description.Should().Be(
            "En Acme construimos nuestro propio producto.\nRequisitos\n- 5+ años con C# y .NET\n- SQL Server & Docker");
        offer.Description.Should().NotContain("&lt;").And.NotContain("<");

        // ATS propio: cliente final por defecto (la IA lo puede corregir) y
        // descripción completa, no un extracto.
        offer.IsClientFinal.Should().BeTrue();
        offer.DescriptionIsSnippet.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_SinEmpresasObjetivo_DevuelveVacio()
    {
        var provider = CreateProvider(SampleJson);

        var result = await provider.SearchAsync(new SearchFilter(["x"], null, null, null));

        result.Should().BeEmpty();
    }
}
