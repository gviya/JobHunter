using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class RelevanceFilterTests
{
    private static readonly string[] Keywords = ["backend .NET", "C# backend developer", "programador de .NET"];

    private static readonly UserProfile Profile = new()
    {
        TechWeights = new Dictionary<string, int> { ["C#"] = 8, [".NET"] = 8, ["Azure"] = 5, ["REST"] = 6 },
    };

    [Theory]
    [InlineData("Senior Software Engineer")]
    [InlineData("Backend Developer")]
    [InlineData("Desarrollador/a Backend")]
    [InlineData("Ingeniera de Datos")]
    [InlineData("Ingeniería de Plataforma")]
    [InlineData("Arquitecto Cloud")]
    [InlineData("Engineering Manager")]
    [InlineData("SRE")]
    [InlineData("Senior .NET Dev")]
    [InlineData("Consultor C#")] // por tech del perfil
    [InlineData("Especialista ASP.NET")] // ".NET" casa dentro de "ASP.NET"
    public void IsRelevant_TituloTecnico_Pasa(string title)
        => RelevanceFilter.IsRelevant(title, Keywords, Profile).Should().BeTrue();

    [Theory]
    [InlineData("Account Executive")]
    [InlineData("Business Development Representative")]
    [InlineData("Director de Ventas")] // "de" es palabra vacía: no cuenta como keyword
    [InlineData("Restaurant Manager")] // "REST" solo casa como palabra propia
    [InlineData("Responsable de Devoluciones")] // "dev" solo como sigla completa
    [InlineData("Office Manager")]
    [InlineData("")]
    public void IsRelevant_TituloNoTecnico_SeDescarta(string title)
        => RelevanceFilter.IsRelevant(title, Keywords, Profile).Should().BeFalse();

    [Fact]
    public void IsRelevant_TerminoDeLasKeywords_PasaAunqueNoSeaUnRolGenerico()
    {
        // El usuario busca algo que no está en la lista de roles genéricos.
        RelevanceFilter.IsRelevant("Analista Programación Mainframe", ["mainframe"], new UserProfile())
            .Should().BeTrue();
    }

    [Fact]
    public void Apply_DejaSoloLasOfertasRelevantes()
    {
        JobOffer Offer(string title) => new()
        {
            ExternalId = title,
            SourceProvider = "Greenhouse",
            Title = title,
            CompanyName = "acme",
            Url = "https://example.com",
        };

        var result = RelevanceFilter.Apply(
            [Offer("Backend Engineer"), Offer("Account Executive"), Offer("People Partner")],
            Keywords,
            Profile);

        result.Select(o => o.Title).Should().Equal("Backend Engineer");
    }
}
