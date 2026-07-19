using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class ReportHtmlTests
{
    private static JobOffer OfferWithScore(string title, string techList)
    {
        var offer = new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Test",
            Title = title,
            CompanyName = "Acme",
            Url = "https://example.com/1",
            WorkMode = WorkMode.Remote,
        };
        offer.Score = new JobScore
        {
            JobOfferId = offer.Id,
            TotalScore = 100,
            AiTechList = techList,
            AiRequirements = "Inglés B2 · 3 años de C#",
        };
        return offer;
    }

    [Fact]
    public void BuildHtml_ConOfertas_IncluyeElFiltroInteractivo()
    {
        var report = new DailyReport
        {
            TopOffers = [OfferWithScore("Backend .NET", "C#, .NET, SQL Server")],
        };

        var html = RunDailyPipeline.Handler.BuildHtml(report);

        // La barra de filtrado y el script deben estar presentes y las
        // tarjetas deben exponer las techs para los chips.
        html.Should().Contain("id=\"filter-exclude\"");
        html.Should().Contain("id=\"filter-require\"");
        html.Should().Contain("id=\"filter-chips\"");
        html.Should().Contain("data-techs=\"C#, .NET, SQL Server\"");
        html.Should().Contain("<script>");
        html.Should().Contain("normalize('NFD')");
    }

    [Fact]
    public void BuildHtml_SinOfertas_NoIncluyeLaBarraDeFiltro()
    {
        var html = RunDailyPipeline.Handler.BuildHtml(new DailyReport());

        html.Should().Contain("No hay ofertas puntuadas todavía");
        html.Should().NotContain("id=\"filter-exclude\"");
    }
}
