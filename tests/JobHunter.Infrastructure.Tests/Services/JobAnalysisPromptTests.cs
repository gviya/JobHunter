using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Services;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class JobAnalysisPromptTests
{
    // Respuesta con el formato ANTERIOR (sin clientFinal ni workMode): debe
    // seguir parseando, con las señales nuevas a null.
    private const string LegacyResponse = """
        {
          "summary": "Backend .NET en producto.",
          "seniority": "Senior",
          "importantTechs": ["C#", ".NET"],
          "requirements": ["5 años con C#"],
          "compatibility": 85,
          "pros": ["Remoto"],
          "cons": [],
          "recommendation": "Aplicar"
        }
        """;

    private static string ResponseWith(string clientFinal, string workMode) => $$"""
        {
          "summary": "Resumen",
          "seniority": "Mid",
          "importantTechs": [],
          "requirements": [],
          "compatibility": 70,
          "pros": [],
          "cons": [],
          "recommendation": "Aplicar",
          "clientFinal": {{clientFinal}},
          "workMode": {{workMode}}
        }
        """;

    private static JobOffer Offer(bool descriptionIsSnippet = false) => new()
    {
        ExternalId = "1",
        SourceProvider = descriptionIsSnippet ? "Adzuna" : "Greenhouse",
        Title = "Backend .NET Developer",
        CompanyName = "Acme",
        Url = "https://example.com/1",
        Description = "Buscamos backend con C# y .NET.",
        DescriptionIsSnippet = descriptionIsSnippet,
        WorkMode = WorkMode.Remote,
    };

    [Fact]
    public void TryParse_ConClientFinalYWorkMode_LeeLasSenales()
    {
        var ok = JobAnalysisPrompt.TryParse(ResponseWith("false", "\"Hybrid\""), out var result);

        ok.Should().BeTrue();
        result.IsClientFinal.Should().BeFalse();
        result.WorkMode.Should().Be(WorkMode.Hybrid);
        result.Compatibility.Should().Be(70);
    }

    [Fact]
    public void TryParse_SinLosCamposNuevos_ParseaYLosDejaANull()
    {
        var ok = JobAnalysisPrompt.TryParse(LegacyResponse, out var result);

        ok.Should().BeTrue();
        result.Compatibility.Should().Be(85);
        result.Requirements.Should().Equal("5 años con C#");
        result.IsClientFinal.Should().BeNull();
        result.WorkMode.Should().BeNull();
    }

    [Theory]
    [InlineData("\"remote\"", WorkMode.Remote)]
    [InlineData("\"HYBRID\"", WorkMode.Hybrid)]
    [InlineData("\"OnSite\"", WorkMode.OnSite)]
    [InlineData("\"On-site\"", WorkMode.OnSite)]
    [InlineData("\"on site\"", WorkMode.OnSite)]
    public void TryParse_WorkMode_SinDistinguirMayusculasNiSeparadores(string json, WorkMode expected)
    {
        JobAnalysisPrompt.TryParse(ResponseWith("null", json), out var result).Should().BeTrue();

        result.WorkMode.Should().Be(expected);
    }

    [Theory]
    [InlineData("\"Unknown\"")]  // "no lo dice": null, igual que si faltara
    [InlineData("\"Remoto\"")]
    [InlineData("\"Remote, Hybrid\"")]
    [InlineData("\"2\"")]
    [InlineData("2")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{}")]
    public void TryParse_WorkModeDesconocidoOInvalido_DevuelveNullSinFallar(string json)
    {
        JobAnalysisPrompt.TryParse(ResponseWith("true", json), out var result).Should().BeTrue();

        result.WorkMode.Should().BeNull();
        result.IsClientFinal.Should().BeTrue(); // el resto de la respuesta se conserva
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"False\"", false)]
    public void TryParse_ClientFinal_AceptaBooleanosYSuFormaEnTexto(string json, bool expected)
    {
        JobAnalysisPrompt.TryParse(ResponseWith(json, "\"Remote\""), out var result).Should().BeTrue();

        result.IsClientFinal.Should().Be(expected);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"quizás\"")]
    [InlineData("1")]
    [InlineData("[]")]
    public void TryParse_ClientFinalNullOInvalido_DevuelveNullSinFallar(string json)
    {
        JobAnalysisPrompt.TryParse(ResponseWith(json, "\"Remote\""), out var result).Should().BeTrue();

        result.IsClientFinal.Should().BeNull();
        result.WorkMode.Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void Build_PideClientFinalYWorkModeYMantieneLaGuardaAntiInyeccion()
    {
        var prompt = JobAnalysisPrompt.Build(Offer(), UserProfile.CreateDefault());

        prompt.Should().Contain("\"clientFinal\"");
        prompt.Should().Contain("\"workMode\": \"Remote|Hybrid|OnSite|Unknown\"");
        prompt.Should().Contain("consultora");
        // El modo detectado es una suposición del provider, no un dato.
        prompt.Should().Contain("Modo de trabajo detectado (estimación heurística, puede ser errónea): Remote");
        prompt.Should().Contain("<<<OFERTA>>>").And.Contain("<<<FIN_OFERTA>>>");
        prompt.Should().Contain("NO confiable");
    }

    [Fact]
    public void Build_ConDescripcionSnippet_AvisaDeQueEsUnExtractoTruncado()
    {
        var prompt = JobAnalysisPrompt.Build(Offer(descriptionIsSnippet: true), UserProfile.CreateDefault());

        prompt.Should().Contain("EXTRACTO TRUNCADO");
        prompt.Should().Contain("NO bajes la compatibility por falta");
        prompt.Should().Contain("- Descripción (extracto truncado de la fuente): Buscamos backend");

        // El aviso va en las instrucciones (confiables), no dentro de la oferta.
        prompt.IndexOf("EXTRACTO TRUNCADO", StringComparison.Ordinal)
            .Should().BeLessThan(prompt.IndexOf("<<<OFERTA>>>", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_SinSnippet_NoIncluyeElAvisoDeExtracto()
    {
        var prompt = JobAnalysisPrompt.Build(Offer(descriptionIsSnippet: false), UserProfile.CreateDefault());

        prompt.Should().NotContain("EXTRACTO TRUNCADO");
        prompt.Should().NotContain("extracto truncado");
        prompt.Should().Contain("- Descripción: Buscamos backend");
    }
}
