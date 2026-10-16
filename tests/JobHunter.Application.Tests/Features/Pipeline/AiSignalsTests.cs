using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class AiSignalsTests
{
    private static JobOffer Offer(string source, WorkMode mode, bool isClientFinal, string? location = null) => new()
    {
        ExternalId = "1",
        SourceProvider = source,
        Title = "Backend .NET Developer",
        CompanyName = "Acme",
        Url = "https://example.com/1",
        WorkMode = mode,
        IsClientFinal = isClientFinal,
        Location = location,
    };

    private static AiAnalysisResult Analysis(bool? isClientFinal = null, WorkMode? workMode = null) =>
        new(
            Summary: "Resumen",
            Seniority: "Senior",
            ImportantTechs: [],
            Compatibility: 70,
            Pros: [],
            Cons: [],
            Recommendation: "Aplicar",
            Requirements: [],
            IsClientFinal: isClientFinal,
            WorkMode: workMode);

    [Theory]
    [InlineData(WorkMode.Remote, WorkMode.Hybrid)]
    [InlineData(WorkMode.Unknown, WorkMode.OnSite)]
    [InlineData(WorkMode.OnSite, WorkMode.Remote)]
    public void Apply_ConWorkModeDefinido_SobrescribeElDelProvider(WorkMode fromProvider, WorkMode fromAi)
    {
        var offer = Offer("Adzuna", fromProvider, isClientFinal: false);

        AiSignals.Apply(offer, Analysis(workMode: fromAi));

        offer.WorkMode.Should().Be(fromAi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WorkMode.Unknown)]
    public void Apply_ConWorkModeNullOUnknown_ConservaElDelProvider(WorkMode? fromAi)
    {
        var offer = Offer("Lever", WorkMode.Remote, isClientFinal: true);

        AiSignals.Apply(offer, Analysis(workMode: fromAi));

        offer.WorkMode.Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void Apply_IaDiceClienteFinalEnUnAgregador_LoMarcaComoClienteFinal()
    {
        // Regresión: los agregadores nunca marcaban cliente final y toda
        // oferta suya puntuaba -10 por la fuente.
        var offer = Offer("Jooble", WorkMode.Remote, isClientFinal: false);

        AiSignals.Apply(offer, Analysis(isClientFinal: true));

        offer.IsClientFinal.Should().BeTrue();
    }

    [Fact]
    public void Apply_IaDiceConsultoraEnUnAts_LoDesmarca()
    {
        // Algunas "empresas objetivo" de Greenhouse son consultoras: "ATS
        // propio = cliente final" no es fiable.
        var offer = Offer("Greenhouse", WorkMode.Remote, isClientFinal: true);

        AiSignals.Apply(offer, Analysis(isClientFinal: false));

        offer.IsClientFinal.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_ConIsClientFinalNull_ConservaElDelProvider(bool fromProvider)
    {
        var offer = Offer("Greenhouse", WorkMode.Remote, isClientFinal: fromProvider);

        AiSignals.Apply(offer, Analysis(isClientFinal: null));

        offer.IsClientFinal.Should().Be(fromProvider);
    }

    [Fact]
    public void Apply_AntesDePuntuar_LaPuntuacionUsaLosValoresCorregidos()
    {
        // Oferta de Adzuna marcada "remota" por la heurística que la IA lee
        // como híbrida fuera de la ciudad del candidato y de cliente final.
        var offer = Offer("Adzuna", WorkMode.Remote, isClientFinal: false, location: "Barcelona");
        var profile = new UserProfile { City = "Madrid" };
        var analysis = Analysis(isClientFinal: true, workMode: WorkMode.Hybrid);

        AiSignals.Apply(offer, analysis);
        var score = JobScoreCalculator.Calculate(offer, analysis, profile);

        score.RemoteScore.Should().Be(-25);       // híbrido no conmutable, no +30
        score.ClientFinalScore.Should().Be(20);   // cliente final, no -10
    }
}
