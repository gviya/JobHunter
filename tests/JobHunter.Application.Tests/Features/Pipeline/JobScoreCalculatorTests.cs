using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class JobScoreCalculatorTests
{
    [Fact]
    public void Calculate_OfertaRemotaClienteFinal60kConTechsConocidas_ReflejaLosPesos()
    {
        // Arrange: oferta remota, cliente final, salario 60k y tres techs
        // conocidas (C#, .NET, Docker) sin ninguna excluida.
        var offer = new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Test",
            Title = "Desarrollador Backend",
            CompanyName = "Acme",
            Url = "https://example.com/1",
            Description = "Buscamos experiencia en C#, .NET y Docker.",
            WorkMode = WorkMode.Remote,
            IsClientFinal = true,
            SalaryMax = 60000m,
        };

        var analysis = new AiAnalysisResult(
            Summary: "Buena oferta",
            Seniority: "Senior",
            ImportantTechs: [],
            Compatibility: 80,
            Pros: [],
            Cons: [],
            Recommendation: "Aplicar");

        var profile = new UserProfile();

        // Act
        var score = JobScoreCalculator.Calculate(offer, analysis, profile);

        // Assert — componentes según los pesos definidos.
        score.RemoteScore.Should().Be(30);          // Remote
        score.ClientFinalScore.Should().Be(20);      // cliente final
        score.SalaryScore.Should().Be(20);           // SalaryMax >= 60k
        score.TechScore.Should().Be(9);              // 3 techs conocidas * 3
        score.CompatibilityScore.Should().Be(80);

        // TotalScore = Compatibility/2 + remote + clientFinal + salary + tech
        //            = 40 + 30 + 20 + 20 + 9 = 119
        score.TotalScore.Should().Be(119);
    }

    [Fact]
    public void Calculate_HibridoFueraDeMadridSinSalarioYTechExcluida_Penaliza()
    {
        var offer = new JobOffer
        {
            ExternalId = "2",
            SourceProvider = "Test",
            Title = "Fullstack Angular + .NET",
            CompanyName = "Consultora XYZ",
            Url = "https://example.com/2",
            Description = "Proyecto con Angular y algo de .NET.",
            WorkMode = WorkMode.Hybrid,
            Location = "Barcelona",
            IsClientFinal = false,
            SalaryMax = null,
        };

        var analysis = new AiAnalysisResult(
            Summary: "Regular",
            Seniority: "Mid",
            ImportantTechs: ["Angular"],
            Compatibility: 40,
            Pros: [],
            Cons: [],
            Recommendation: "Dudoso");

        var score = JobScoreCalculator.Calculate(offer, analysis, new UserProfile());

        score.RemoteScore.Should().Be(-5);       // híbrido fuera de Madrid
        score.ClientFinalScore.Should().Be(-10); // consultora
        score.SalaryScore.Should().Be(-5);       // salario null
        // .NET conocida (+3) pero aparece Angular (excluida) => 3 - 15 = -12
        score.TechScore.Should().Be(-12);
        // Total = 20 + (-5) + (-10) + (-5) + (-12) = -12
        score.TotalScore.Should().Be(-12);
    }
}
