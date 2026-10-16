using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JobHunter.Application.Tests.Features.Pipeline;

/// <summary>
/// Tests del handler completo con dependencias simuladas: verifican la
/// orquestación (qué se guarda, qué se analiza, qué ventana se pide), no la
/// puntuación, que ya cubre JobScoreCalculatorTests.
/// </summary>
public sealed class RunDailyPipelineTests
{
    private readonly Mock<IJobRepository> _repository = new();
    private readonly Mock<IAiAnalysisService> _ai = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();

    private readonly UserProfile _profile = new()
    {
        SearchKeywords = ["backend .NET"],
        ReportWindowDays = 7,
    };

    public RunDailyPipelineTests()
    {
        _repository.Setup(r => r.GetUnanalyzedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(r => r.GetRecentByCompanyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(r => r.GetTopScoredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#"], 80, [], [], "aplicar", []));
    }

    private static JobOffer Offer(string title, string provider = "Test") => new()
    {
        ExternalId = Guid.NewGuid().ToString(),
        SourceProvider = provider,
        Title = title,
        CompanyName = "Acme",
        Url = "https://example.com",
    };

    private static Mock<IJobProvider> Provider(bool filtersByKeyword, params JobOffer[] offers)
    {
        var provider = new Mock<IJobProvider>();
        provider.SetupGet(p => p.ProviderName).Returns("Test");
        provider.SetupGet(p => p.FiltersByKeyword).Returns(filtersByKeyword);
        provider.Setup(p => p.SearchAsync(It.IsAny<SearchFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(offers);
        return provider;
    }

    private Task<DailyReport> RunAsync(params IJobProvider[] providers)
    {
        var handler = new RunDailyPipeline.Handler(
            providers,
            _repository.Object,
            _ai.Object,
            _embeddings.Object,
            _profile,
            NullLogger<RunDailyPipeline.Handler>.Instance);

        return handler.Handle(new RunDailyPipeline.Command(), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ReintentaLasOfertasQueQuedaronSinAnalizarEnEjecucionesAnteriores()
    {
        // Regresión: antes solo se analizaban las nuevas del día, y una
        // oferta cuyo análisis falló (IA caída) no se volvía a intentar nunca.
        var pendiente = Offer("Backend .NET de la semana pasada");
        _repository.Setup(r => r.GetUnanalyzedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pendiente]);

        var report = await RunAsync(Provider(filtersByKeyword: true).Object);

        _ai.Verify(a => a.AnalyzeAsync(pendiente, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        pendiente.IsAnalyzed.Should().BeTrue();
        pendiente.Score.Should().NotBeNull();
        report.TotalAnalyzed.Should().Be(1);
    }

    [Fact]
    public async Task Handle_PideReintentosYTopDentroDeLaVentanaDelPerfil()
    {
        var report = await RunAsync(Provider(filtersByKeyword: true).Object);

        var expectedSince = report.GeneratedAt.AddDays(-7);
        _repository.Verify(r => r.GetUnanalyzedAsync(expectedSince, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetTopScoredAsync(expectedSince, 15, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProviderSinFiltroPorKeyword_NoGuardaLosPuestosNoTecnicos()
    {
        var backend = Offer("Backend Engineer", "Greenhouse");
        var ventas = Offer("Account Executive", "Greenhouse");

        var report = await RunAsync(Provider(filtersByKeyword: false, backend, ventas).Object);

        _repository.Verify(r => r.SaveAsync(backend, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveAsync(ventas, It.IsAny<CancellationToken>()), Times.Never);
        report.TotalFound.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ProviderQueFiltraPorKeyword_NoAplicaElFiltroDeTitulo()
    {
        // Adzuna/JSearch ya buscaron por keyword: un título poco explícito
        // puede venir de una descripción relevante y no se descarta.
        var consultor = Offer("Consultor Microsoft Dynamics", "Adzuna");

        await RunAsync(Provider(filtersByKeyword: true, consultor).Object);

        _repository.Verify(r => r.SaveAsync(consultor, It.IsAny<CancellationToken>()), Times.Once);
    }
}
