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

    // Almacén simulado del repositorio: lo guardado y los ids que ya llegaron
    // marcados como duplicados a SaveAsync (no después: en BD real un cambio
    // posterior sin UpdateAsync no se persistiría).
    private readonly List<JobOffer> _saved = [];
    private readonly HashSet<Guid> _savedAsDuplicate = [];

    private readonly UserProfile _profile = new()
    {
        SearchKeywords = ["backend .NET"],
        ReportWindowDays = 7,
    };

    public RunDailyPipelineTests()
    {
        _repository.Setup(r => r.SaveAsync(It.IsAny<JobOffer>(), It.IsAny<CancellationToken>()))
            .Callback<JobOffer, CancellationToken>((offer, _) =>
            {
                _saved.Add(offer);
                if (offer.IsDuplicate)
                {
                    _savedAsDuplicate.Add(offer.Id);
                }
            })
            .Returns(Task.CompletedTask);

        // Mismo filtro que JobRepository (sin analizar y no duplicada): así
        // los tests ven si una duplicada llegaría al análisis de IA.
        _repository.Setup(r => r.GetUnanalyzedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. _saved.Where(o => !o.IsAnalyzed && !o.IsDuplicate)]);
        _repository.Setup(r => r.GetRecentByCompanyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repository.Setup(r => r.GetTopScoredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#"], 80, [], [], "aplicar", []));
    }

    private static JobOffer Offer(
        string title, string provider = "Test", string company = "Acme", string? description = null) => new()
    {
        ExternalId = Guid.NewGuid().ToString(),
        SourceProvider = provider,
        Title = title,
        CompanyName = company,
        Description = description,
        Url = "https://example.com",
    };

    private void OllamaCaido() =>
        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused (localhost:11434)"));

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

    // ── Dedup determinista por empresa + título ─────────────────────────

    [Fact]
    public async Task Handle_MismaOfertaEnVariasFuentesSinOllama_SeGuardaComoDuplicadaYSeAnalizaUnaVez()
    {
        // Regresión: sin Ollama el dedup semántico se salta entero, y la
        // misma oferta de Adzuna + JSearch + Jooble se analizaba (IA de pago)
        // y salía en el top tres veces.
        OllamaCaido();
        var adzuna = Offer("Desarrollador/a Backend .NET", "Adzuna", description: "Snippet corto");
        var jsearch = Offer("Desarrollador Backend .NET", "JSearch", description: "Descripción completa con requisitos");
        var jooble = Offer("DESARROLLADOR BACKEND .NET (m/f/d)", "Jooble", description: "Otro snippet");

        var report = await RunAsync(
            Provider(filtersByKeyword: true, adzuna).Object,
            Provider(filtersByKeyword: true, jsearch).Object,
            Provider(filtersByKeyword: true, jooble).Object);

        // Las tres se guardan (mañana el dedup exacto las reconoce), las
        // duplicadas ya marcadas al guardarse...
        _saved.Should().Equal(adzuna, jsearch, jooble);
        _savedAsDuplicate.Should().BeEquivalentTo([adzuna.Id, jooble.Id]);

        // ...y solo una pasa por la IA.
        _ai.Verify(a => a.AnalyzeAsync(jsearch, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalNew.Should().Be(3);
        report.TotalDuplicates.Should().Be(2);
        report.TotalAnalyzed.Should().Be(1);
    }

    [Fact]
    public async Task Handle_DuplicadasEnElLote_SeQuedaLaDeDescripcionMasLarga()
    {
        // La descripción completa del ATS vale más que el snippet del
        // agregador, aunque llegue la última.
        var snippet = Offer("Backend Engineer", "Adzuna", description: "Buscamos backend...");
        var sinDescripcion = Offer("Backend Engineer", "Jooble");
        var completa = Offer("Backend Engineer (m/f/d)", "Lever",
            description: "Buscamos backend engineer para nuestro equipo de pagos. Requisitos: C#, .NET, SQL...");

        await RunAsync(Provider(filtersByKeyword: true, snippet, sinDescripcion, completa).Object);

        completa.IsDuplicate.Should().BeFalse();
        snippet.IsDuplicate.Should().BeTrue();
        sinDescripcion.IsDuplicate.Should().BeTrue();
        _ai.Verify(a => a.AnalyzeAsync(completa, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicadasConDescripcionIgualDeLarga_SeQuedaLaPrimeraVista()
    {
        var primera = Offer("Backend Engineer", "Adzuna", description: "misma longitud");
        var segunda = Offer("Backend Engineer", "JSearch", description: "MISMA LONGITUD");

        await RunAsync(Provider(filtersByKeyword: true, primera, segunda).Object);

        primera.IsDuplicate.Should().BeFalse();
        segunda.IsDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OfertaYaVistaEnBdConOtroId_SeGuardaComoDuplicadaYNoSeAnaliza()
    {
        // La empresa la republica con otro id, o la trae otra fuente días
        // después: el dedup exacto por (ExternalId, SourceProvider) no la ve.
        OllamaCaido();
        var anterior = Offer("Backend Engineer (m/f/d)", "Lever");
        _repository.Setup(r => r.GetRecentByCompanyAsync("acme", 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([anterior]);
        var republicada = Offer("Backend Engineer", "Adzuna");

        var report = await RunAsync(Provider(filtersByKeyword: true, republicada).Object);

        _savedAsDuplicate.Should().Equal(republicada.Id);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
        report.TotalDuplicates.Should().Be(1);
    }

    [Fact]
    public async Task Handle_TitulosDistintosEnLaMismaEmpresa_NoSeFusionan()
    {
        _repository.Setup(r => r.GetRecentByCompanyAsync("acme", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Offer("Data Engineer")]);
        var senior = Offer("Backend Developer (Senior)");
        var junior = Offer("Backend Developer (Junior)");
        var frontend = Offer("Frontend Developer");

        var report = await RunAsync(Provider(filtersByKeyword: true, senior, junior, frontend).Object);

        _saved.Should().OnlyContain(o => !o.IsDuplicate);
        report.TotalDuplicates.Should().Be(0);
        report.TotalAnalyzed.Should().Be(3);
    }

    [Fact]
    public async Task Handle_MismoTituloEnEmpresasDistintasOSinEmpresa_NoSeFusionan()
    {
        // Sin empresa no hay clave fiable: dos "Backend Developer" de empresa
        // desconocida no son necesariamente la misma oferta.
        var acme = Offer("Backend Developer", "Adzuna", company: "Acme S.L.");
        var globex = Offer("Backend Developer", "Adzuna", company: "Globex");
        var anonima1 = Offer("Backend Developer", "Jooble", company: "");
        var anonima2 = Offer("Backend Developer", "Jooble", company: "");

        var report = await RunAsync(Provider(filtersByKeyword: true, acme, globex, anonima1, anonima2).Object);

        _saved.Should().OnlyContain(o => !o.IsDuplicate);
        report.TotalDuplicates.Should().Be(0);
        report.TotalAnalyzed.Should().Be(4);
    }

    [Fact]
    public async Task Handle_DuplicadaPorTitulo_NoPideEmbeddingAOllama()
    {
        // Con Ollama levantado, la duplicada ya marcada no gasta una llamada
        // de embeddings: solo la conservada pasa por el dedup semántico.
        var conservada = Offer("Backend Engineer", "Adzuna", description: "Descripción completa");
        var duplicada = Offer("Backend Engineer", "JSearch");

        await RunAsync(Provider(filtersByKeyword: true, conservada, duplicada).Object);

        _embeddings.Verify(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.UpdateAsync(duplicada, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConsultaLasOfertasRecientesUnaVezPorEmpresa()
    {
        // Ollama caído: el semántico no consulta nada, así que todas las
        // llamadas son del dedup por título.
        OllamaCaido();

        await RunAsync(Provider(filtersByKeyword: true,
            Offer("Backend Engineer"),
            Offer("Frontend Engineer"),
            Offer("Platform Engineer"),
            Offer("Backend Engineer", company: "Globex")).Object);

        _repository.Verify(r => r.GetRecentByCompanyAsync("acme", 30, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetRecentByCompanyAsync("globex", 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TotalDuplicates_SumaLasDeTituloYLasSemanticas()
    {
        // Globex ya tiene en BD la misma oferta con otro título: solo la ve
        // el semántico. Las dos de Acme las resuelve el dedup por título.
        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1f, 0f]);
        _embeddings.Setup(e => e.CosineSimilarity(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(0.99f);
        var conocida = Offer("Senior .NET Developer", company: "Globex");
        conocida.Embedding = [1f, 0f];
        _repository.Setup(r => r.GetRecentByCompanyAsync("globex", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([conocida]);

        var acme = Offer("Backend Engineer", "Adzuna", description: "Descripción completa");
        var acmeRepetida = Offer("Backend Engineer", "JSearch");
        var globex = Offer("Desarrollador .NET Senior", "Jooble", company: "Globex");

        var report = await RunAsync(Provider(filtersByKeyword: true, acme, acmeRepetida, globex).Object);

        acmeRepetida.IsDuplicate.Should().BeTrue();
        globex.IsDuplicate.Should().BeTrue();
        acme.IsDuplicate.Should().BeFalse();
        report.TotalDuplicates.Should().Be(2);
        report.TotalAnalyzed.Should().Be(1);
    }
}
