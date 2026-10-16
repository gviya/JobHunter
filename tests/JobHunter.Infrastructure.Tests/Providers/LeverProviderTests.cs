using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Providers;
using JobHunter.Infrastructure.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class LeverProviderTests
{
    // Forma real de /v0/postings/{token}?mode=json: la presentación va en
    // description/descriptionPlain, pero los requisitos viven en "lists" (con
    // el contenido en HTML) y el cierre en additional/additionalPlain.
    private const string SampleJson = """
        [
          {
            "id": "5f1c2a9e-0b7d-4c1e-9a3f-2d8e6b4c1a00",
            "text": "Backend Engineer - .NET",
            "hostedUrl": "https://jobs.lever.co/acme/5f1c2a9e-0b7d-4c1e-9a3f-2d8e6b4c1a00",
            "description": "<div><b>Acme</b> construye software de logística.</div>",
            "descriptionPlain": "Acme construye software de logística.\n",
            "lists": [
              {
                "text": "Requirements",
                "content": "<li>5+ years of experience with C# and .NET</li><li>SQL Server &amp; Entity Framework</li>"
              },
              {
                "text": "Nice to have:",
                "content": "<li>Azure Service Bus</li>"
              },
              {
                "text": "Vacía",
                "content": ""
              }
            ],
            "additional": "<div>Salario 55-65k. Equipo 100% remoto.</div>",
            "additionalPlain": "Salario 55-65k. Equipo 100% remoto.",
            "workplaceType": "remote",
            "createdAt": 1783670400000,
            "categories": { "location": "Madrid", "team": "Engineering", "commitment": "Full-time" }
          }
        ]
        """;

    // Sin descriptionPlain (ni lists/additional): se usa description pasada a texto.
    private const string HtmlOnlyJson = """
        [
          {
            "id": "abc",
            "text": "Backend Developer",
            "hostedUrl": "https://jobs.lever.co/acme/abc",
            "description": "<p>Backend en <b>.NET</b></p><p>Equipo&nbsp;pequeño</p>",
            "workplaceType": "hybrid",
            "categories": { "location": "Madrid" }
          }
        ]
        """;

    private static LeverProvider CreateProvider(string json, params string[] tokens)
    {
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(json));
        var options = Options.Create(new TargetCompaniesOptions { Lever = tokens });
        return new LeverProvider(factory, options, NullLogger<LeverProvider>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ConListsYAdditional_LosIncluyeEnLaDescripcion()
    {
        var provider = CreateProvider(SampleJson, "acme");

        var result = await provider.SearchAsync(new SearchFilter(["backend .NET"], "España", null, null));

        result.Should().ContainSingle();

        var offer = result[0];
        offer.ExternalId.Should().Be("5f1c2a9e-0b7d-4c1e-9a3f-2d8e6b4c1a00");
        offer.SourceProvider.Should().Be("Lever");
        offer.CompanyName.Should().Be("acme");
        offer.Location.Should().Be("Madrid");
        offer.WorkMode.Should().Be(WorkMode.Remote);

        // Regresión: solo se guardaba descriptionPlain y la IA nunca veía los
        // requisitos. Las listas vacías se omiten y el ":" no se duplica.
        offer.Description.Should().Be(
            """
            Acme construye software de logística.

            Requirements:
            - 5+ years of experience with C# and .NET
            - SQL Server & Entity Framework

            Nice to have:
            - Azure Service Bus

            Salario 55-65k. Equipo 100% remoto.
            """.ReplaceLineEndings("\n"));

        offer.IsClientFinal.Should().BeTrue();
        offer.DescriptionIsSnippet.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_SinDescriptionPlain_UsaLaDescripcionHtmlComoTexto()
    {
        var provider = CreateProvider(HtmlOnlyJson, "acme");

        var result = await provider.SearchAsync(new SearchFilter(["backend"], "España", null, null));

        result.Should().ContainSingle();
        result[0].Description.Should().Be("Backend en .NET\nEquipo pequeño");
    }

    [Fact]
    public async Task SearchAsync_SinEmpresasObjetivo_DevuelveVacio()
    {
        var provider = CreateProvider(SampleJson);

        var result = await provider.SearchAsync(new SearchFilter(["x"], null, null, null));

        result.Should().BeEmpty();
    }
}
