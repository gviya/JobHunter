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

    // Sin ciudad: el filtro de desplazamiento no aplica y todo es conmutable.
    // Los tests de ubicación lo sustituyen por PerfilMadrid().
    private UserProfile _profile = new()
    {
        SearchKeywords = ["backend .NET"],
        ReportWindowDays = 7,
    };

    private static UserProfile PerfilMadrid(bool acceptOnSite = true) => new()
    {
        SearchKeywords = ["backend .NET"],
        ReportWindowDays = 7,
        City = "Madrid",
        AcceptHybrid = true,
        AcceptOnSite = acceptOnSite,
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
        string title,
        string provider = "Test",
        string company = "Acme",
        string? description = null,
        string? location = null,
        WorkMode workMode = WorkMode.Unknown,
        DateTime? discoveredAt = null) => new()
    {
        ExternalId = Guid.NewGuid().ToString(),
        SourceProvider = provider,
        Title = title,
        CompanyName = company,
        Description = description,
        Location = location,
        WorkMode = workMode,
        DiscoveredAt = discoveredAt ?? DateTime.UtcNow,
        Url = "https://example.com",
    };

    private void OllamaCaido() =>
        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused (localhost:11434)"));

    // Ollama levantado: todas las ofertas reciben el mismo embedding y el
    // parecido entre dos cualesquiera es el indicado.
    private void OllamaConSimilitud(float similarity)
    {
        _embeddings.Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1f, 0f]);
        _embeddings.Setup(e => e.CosineSimilarity(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(similarity);
    }

    // Las lecturas por empresa y del top también salen del almacén simulado,
    // con los mismos filtros que JobRepository: así un test puede encadenar
    // varias ejecuciones y ver lo que la anterior dejó en BD.
    private void UsarAlmacenSimulado()
    {
        _repository.Setup(r => r.GetRecentByCompanyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string company, int days, CancellationToken _) =>
                [.. _saved.Where(o => o.CompanyNormalized == company && o.DiscoveredAt >= DateTime.UtcNow.AddDays(-days))]);
        _repository.Setup(r => r.GetTopScoredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime since, int count, CancellationToken _) =>
                [.. _saved
                    .Where(o => o.Score != null && !o.IsDuplicate && o.DiscoveredAt >= since)
                    .OrderByDescending(o => o.Score!.TotalScore)
                    .Take(count)]);
    }

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
        // Regresión (review: unknown-company-title-dedup): este test usaba
        // solo company "", que ningún provider envía, y pasaba por el motivo
        // equivocado. Jooble/Adzuna/JSearch ponen JobOffer.UnknownCompany,
        // que normaliza a una clave NO vacía, y las dos anónimas de Madrid y
        // Barcelona se fusionaban (se perdía la de Madrid).
        var acme = Offer("Backend Developer", "Adzuna", company: "Acme S.L.");
        var globex = Offer("Backend Developer", "Adzuna", company: "Globex");
        var anonimaMadrid = Offer("Backend Developer", "Jooble", company: JobOffer.UnknownCompany, location: "Madrid");
        var anonimaBarcelona = Offer("Backend Developer", "Jooble", company: JobOffer.UnknownCompany,
            description: "Snippet algo más largo", location: "Barcelona");
        var vacia1 = Offer("Backend Developer", "Jooble", company: "");
        var vacia2 = Offer("Backend Developer", "Jooble", company: "  ");

        var report = await RunAsync(Provider(filtersByKeyword: true,
            acme, globex, anonimaMadrid, anonimaBarcelona, vacia1, vacia2).Object);

        _saved.Should().OnlyContain(o => !o.IsDuplicate);
        report.TotalDuplicates.Should().Be(0);
        report.TotalAnalyzed.Should().Be(6);
    }

    [Fact]
    public async Task Handle_EmpresaDesconocidaConElMismoTituloEnDosEjecuciones_NoSeFusionan()
    {
        // Regresión (review: unknown-company-placeholder-merges): el marcador
        // "(empresa desconocida)" actuaba como una empresa real. En la 1ª
        // ejecución las dos anónimas del lote se fusionaban; en la 2ª, la
        // nueva casaba contra las guardadas y se marcaba duplicada. Durante
        // 30 días toda oferta anónima con un título genérico se perdía sin
        // analizar y sin llegar al email.
        OllamaCaido();
        UsarAlmacenSimulado();
        var jooble = Offer("Desarrollador Backend .NET", "Jooble", JobOffer.UnknownCompany,
            "Fintech de producto, 100% remoto", location: "España");
        var adzuna = Offer("Desarrollador Backend .NET", "Adzuna", JobOffer.UnknownCompany,
            "Consultora", location: "Valencia");

        var primera = await RunAsync(Provider(filtersByKeyword: true, jooble, adzuna).Object);

        var jsearch = Offer("Desarrollador Backend .NET", "JSearch", JobOffer.UnknownCompany, location: "Madrid");
        var segunda = await RunAsync(Provider(filtersByKeyword: true, jsearch).Object);

        _saved.Should().Equal(jooble, adzuna, jsearch);
        _saved.Should().OnlyContain(o => !o.IsDuplicate && o.IsAnalyzed);
        primera.TotalDuplicates.Should().Be(0);
        primera.TotalAnalyzed.Should().Be(2);
        segunda.TotalDuplicates.Should().Be(0);
        segunda.TotalAnalyzed.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OfertasSinTituloEnLaMismaEmpresa_NoSeFusionan()
    {
        // Regresión (review: unknown-company-placeholder-merges, parte del
        // título): Greenhouse/Lever/agregadores ponen "(sin título)" cuando la
        // fuente no da título, y dos ofertas así de la misma empresa
        // compartían clave, en el lote y contra BD.
        OllamaCaido();
        _repository.Setup(r => r.GetRecentByCompanyAsync("acme", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Offer(JobOffer.UntitledTitle, "Lever")]);
        var primera = Offer(JobOffer.UntitledTitle, "Greenhouse", description: "Oferta de pagos");
        var segunda = Offer(JobOffer.UntitledTitle, "Lever", description: "Oferta de plataforma, otra cosa");

        var report = await RunAsync(Provider(filtersByKeyword: true, primera, segunda).Object);

        _saved.Should().OnlyContain(o => !o.IsDuplicate);
        report.TotalDuplicates.Should().Be(0);
        report.TotalAnalyzed.Should().Be(2);
    }

    [Fact]
    public async Task Handle_MismoTituloEnOficinaConmutableYNoConmutable_SeQuedaLaConmutable()
    {
        // Regresión (review: title-dedup-ignores-location): las consultoras
        // publican el mismo título por oficina. El grupo del lote se quedaba
        // con la de descripción más larga (Barcelona) y marcaba duplicada la
        // de Madrid, la única que el candidato puede coger.
        // Regresión (review: r3-pipeline-1): la primera corrección separaba
        // conmutables y no conmutables en grupos distintos, y eso partía
        // también las copias de una misma remota. Ahora el grupo es uno y la
        // conmutable gana a la más informativa: Barcelona y la copia sin
        // ubicación quedan como duplicadas de la de Madrid.
        OllamaCaido();
        _profile = PerfilMadrid();
        var barcelona = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Snippet algo más largo de la oficina de Barcelona", location: "Barcelona, Cataluña");
        var madrid = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Snippet de Madrid", location: "Madrid, Comunidad de Madrid");
        var copiaSinUbicacion = Offer("Desarrollador .NET", "Jooble", "NTT Data", "Corto");

        var report = await RunAsync(Provider(filtersByKeyword: true, barcelona, madrid, copiaSinUbicacion).Object);

        madrid.IsDuplicate.Should().BeFalse();
        barcelona.IsDuplicate.Should().BeTrue();
        copiaSinUbicacion.IsDuplicate.Should().BeTrue();
        _ai.Verify(a => a.AnalyzeAsync(madrid, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalDuplicates.Should().Be(2);
    }

    [Theory]
    [InlineData("Barcelona, Cataluña", WorkMode.Unknown, "Madrid", false)]    // no conmutable: nunca suprime a la conmutable
    [InlineData("Barcelona, Cataluña", WorkMode.Unknown, "Barcelona", true)]  // copia de la no conmutable: sí casa
    [InlineData("Madrid", WorkMode.Unknown, "Barcelona, Cataluña", true)]     // conmutable: sí suprime a la no conmutable
    [InlineData("España", WorkMode.Remote, "Madrid", true)]                   // remota: conmutable aunque diga solo el país
    public async Task Handle_MismoTituloYaVistoEnBd_LaNoConmutableNuncaSuprimeALaConmutable(
        string storedLocation, WorkMode storedMode, string newLocation, bool expectedDuplicate)
    {
        // Regresión (review: title-dedup-ignores-location, paso contra BD):
        // una "Desarrollador .NET" de Barcelona vista en los últimos 30 días
        // suprimía la de Madrid de hoy.
        // Regresión (review: r3-pipeline-1): la clave simétrica exigía que
        // las dos tuvieran la misma conmutabilidad, así que tampoco casaba la
        // remota guardada con "España" con su copia de "Madrid" y esta se
        // analizaba y se enviaba otra vez. La regla es asimétrica: solo la
        // dirección no conmutable → conmutable está prohibida.
        OllamaCaido();
        _profile = PerfilMadrid();
        _repository.Setup(r => r.GetRecentByCompanyAsync("ntt data", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Offer("Desarrollador .NET", "Adzuna", "NTT DATA", location: storedLocation, workMode: storedMode)]);
        var nueva = Offer("Desarrollador .NET", "Jooble", "NTT Data", location: newLocation);

        var report = await RunAsync(Provider(filtersByKeyword: true, nueva).Object);

        nueva.IsDuplicate.Should().Be(expectedDuplicate);
        _ai.Verify(a => a.AnalyzeAsync(nueva, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()),
            expectedDuplicate ? Times.Never() : Times.Once());
        report.TotalDuplicates.Should().Be(expectedDuplicate ? 1 : 0);
    }

    [Theory]
    [InlineData("JSearch", "Madrid", "Jooble", "España")]
    [InlineData("Greenhouse", null, "Adzuna", "España")]
    [InlineData("Jooble", "España", "JSearch", "Madrid")]
    public async Task Handle_CopiasDeLaMismaRemotaConCiudadYConPais_SeFusionanEnElLote(
        string providerA, string? locationA, string providerB, string? locationB)
    {
        // Regresión (review: r3-pipeline-1): la clave (empresa, título,
        // conmutable) separaba las copias entre fuentes de la misma remota
        // cuando una fuente nombra la ciudad del perfil (o no da ubicación) y
        // otra solo el país: con City = Madrid, "Madrid" era conmutable y
        // "España" no. Cada copia se analizaba (segunda llamada de pago) y
        // las dos salían en el top.
        OllamaCaido();
        UsarAlmacenSimulado();
        _profile = PerfilMadrid();
        var a = Offer("Senior Backend Developer .NET", providerA, "acme",
            "Descripción completa con requisitos", locationA, WorkMode.Remote);
        var b = Offer("Senior Backend Developer .NET", providerB, "Acme", "Snippet", locationB, WorkMode.Remote);

        var report = await RunAsync(Provider(filtersByKeyword: true, a, b).Object);

        a.IsDuplicate.Should().BeFalse();
        b.IsDuplicate.Should().BeTrue();
        _ai.Verify(x => x.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalDuplicates.Should().Be(1);
        report.TotalAnalyzed.Should().Be(1);
        report.TopOffers.Should().Equal(a);
    }

    [Theory]
    [InlineData("Madrid", WorkMode.Remote, "España")]
    [InlineData(null, WorkMode.Remote, "España")]
    [InlineData("España", WorkMode.Remote, "Madrid")]
    [InlineData("España", WorkMode.Unknown, "Madrid")] // remota solo tras corregirla la IA
    public async Task Handle_CopiaDeLaMismaRemotaEnOtraEjecucion_SeMarcaDuplicadaYNoVuelveAlEmail(
        string? firstLocation, WorkMode firstMode, string? secondLocation)
    {
        // Regresión (review: r3-pipeline-1, entre ejecuciones): la copia del
        // segundo día no casaba con la guardada, volvía como "Nueva", ponía
        // HasNewTopOffers y el mismo puesto disparaba otro email. Con
        // "España" guardada, la fila solo es conmutable por ser remota (en la
        // última fila, por el modo que corrigió la IA y quedó en BD).
        OllamaCaido();
        UsarAlmacenSimulado();
        _profile = PerfilMadrid();
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#"], 80, [], [], "aplicar", [],
                WorkMode: WorkMode.Remote));
        var primera = Offer("Senior Backend Developer .NET", "JSearch",
            description: "Descripción completa con requisitos", location: firstLocation, workMode: firstMode);
        var primerInforme = await RunAsync(Provider(filtersByKeyword: true, primera).Object);

        var segunda = Offer("Senior Backend Developer .NET", "Jooble", location: secondLocation, workMode: WorkMode.Remote);
        var segundoInforme = await RunAsync(Provider(filtersByKeyword: true, segunda).Object);

        primerInforme.HasNewTopOffers().Should().BeTrue();
        _savedAsDuplicate.Should().Equal(segunda.Id);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        segundoInforme.TotalDuplicates.Should().Be(1);
        segundoInforme.TotalAnalyzed.Should().Be(0);
        segundoInforme.TopOffers.Should().Equal(primera);
        segundoInforme.HasNewTopOffers().Should().BeFalse();
    }

    [Theory]
    [InlineData(10, false, false, false)] // sin analizar y fuera de la ventana: ya no se reintenta
    [InlineData(1, false, true, false)]   // duplicada: nunca se analiza (cadenas de duplicadas)
    [InlineData(1, false, false, true)]   // pendiente dentro de la ventana: aún se analizará
    [InlineData(20, true, false, true)]   // analizada: ya salió en un informe
    public async Task Handle_TituloYaVistoEnBd_SoloCuentaSiLaFilaAunRepresentaALaOferta(
        int daysAgo, bool isAnalyzed, bool isDuplicate, bool expectedDuplicate)
    {
        // Regresión (review: dedup-against-dead-unanalyzed-original): el
        // lookback del dedup (30 días) es más largo que la ventana de
        // reintentos (7). Con la IA caída más de una semana, la original sin
        // analizar caducaba y aun así suprimía las copias nuevas: nada de esa
        // oferta llegaba a puntuarse nunca. Lo mismo con una fila duplicada,
        // que además se guarda con un DiscoveredAt nuevo y alargaba el plazo.
        OllamaCaido();
        var original = Offer("Backend Engineer", "Lever", discoveredAt: DateTime.UtcNow.AddDays(-daysAgo));
        original.IsAnalyzed = isAnalyzed;
        original.IsDuplicate = isDuplicate;
        _repository.Setup(r => r.GetRecentByCompanyAsync("acme", 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([original]);
        var copia = Offer("Backend Engineer", "Adzuna");

        var report = await RunAsync(Provider(filtersByKeyword: true, copia).Object);

        copia.IsDuplicate.Should().Be(expectedDuplicate);
        _ai.Verify(a => a.AnalyzeAsync(copia, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()),
            expectedDuplicate ? Times.Never() : Times.Once());
        report.TotalDuplicates.Should().Be(expectedDuplicate ? 1 : 0);
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

    // ── Dedup semántico: mismas reglas que el de título ─────────────────

    [Fact]
    public async Task Handle_GemelaDeBarcelonaConOllama_NoSuprimeLaVacanteDeMadrid()
    {
        // Regresión (review: r3-pipeline-2): el dedup semántico comparaba con
        // todas las filas de la empresa, sin la regla de conmutabilidad del de
        // título. El texto de plantilla de la vacante de Madrid supera el
        // umbral frente al de Barcelona: Madrid quedaba duplicada sin
        // analizar, Barcelona se descartaba tras el análisis (híbrida y no
        // conmutable) y el top se quedaba vacío.
        UsarAlmacenSimulado();
        OllamaConSimilitud(0.95f);
        _profile = PerfilMadrid();
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#"], 80, [], [], "aplicar", [],
                WorkMode: WorkMode.Hybrid));
        var barcelona = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Buscamos desarrollador .NET para proyecto en Barcelona", "Barcelona, Cataluña");
        await RunAsync(Provider(filtersByKeyword: true, barcelona).Object);

        var madrid = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Buscamos desarrollador .NET para proyecto en Madrid", "Madrid, Comunidad de Madrid");
        var report = await RunAsync(Provider(filtersByKeyword: true, madrid).Object);

        barcelona.IsAnalyzed.Should().BeTrue();
        barcelona.Score.Should().BeNull();
        madrid.IsDuplicate.Should().BeFalse();
        madrid.Score.Should().NotBeNull();
        report.TotalDuplicates.Should().Be(0);
        report.TopOffers.Should().Equal(madrid);
    }

    [Fact]
    public async Task Handle_GemelasDeOficinaEnElMismoLoteConOllama_LaRepresentanteEsLaConmutable()
    {
        // Regresión (review: r3-pipeline-2, dentro del lote): con la oficina
        // en el título el dedup por título no las casa y solo las ve el
        // semántico, que recorría el lote en orden de llegada: la de
        // Barcelona, primera, se quedaba y la de Madrid se marcaba duplicada
        // de ella.
        UsarAlmacenSimulado();
        OllamaConSimilitud(0.95f);
        _profile = PerfilMadrid();
        var barcelona = Offer("Desarrollador .NET - Barcelona", "Adzuna", "NTT DATA",
            "Buscamos desarrollador .NET para proyecto en Barcelona", "Barcelona, Cataluña");
        var madrid = Offer("Desarrollador .NET - Madrid", "Adzuna", "NTT DATA",
            "Buscamos desarrollador .NET para proyecto en Madrid", "Madrid, Comunidad de Madrid");

        var report = await RunAsync(Provider(filtersByKeyword: true, barcelona, madrid).Object);

        madrid.IsDuplicate.Should().BeFalse();
        barcelona.IsDuplicate.Should().BeTrue();
        _ai.Verify(a => a.AnalyzeAsync(madrid, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<JobOffer>(), It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalDuplicates.Should().Be(1);
        report.TopOffers.Should().Equal(madrid);
    }

    [Theory]
    [InlineData("Barcelona, Cataluña", WorkMode.Unknown, "Madrid", false)]    // no conmutable: nunca suprime a la conmutable
    [InlineData("Barcelona, Cataluña", WorkMode.Unknown, "Barcelona", true)]  // copia de la no conmutable: sí casa
    [InlineData("Madrid", WorkMode.Unknown, "Barcelona, Cataluña", true)]     // conmutable: sí suprime a la no conmutable
    [InlineData("España", WorkMode.Remote, "Madrid", true)]                   // remota: conmutable aunque diga solo el país
    public async Task Handle_SemanticoConOllama_LaNoConmutableNuncaSuprimeALaConmutable(
        string storedLocation, WorkMode storedMode, string newLocation, bool expectedDuplicate)
    {
        // Regresión (review: r3-pipeline-2): la misma regla asimétrica que el
        // dedup por título, en el semántico. Títulos distintos para que solo
        // el semántico pueda casarlas.
        OllamaConSimilitud(0.95f);
        _profile = PerfilMadrid();
        var guardada = Offer("Desarrollador .NET", "Adzuna", "NTT DATA", location: storedLocation, workMode: storedMode);
        guardada.Embedding = [1f, 0f];
        _repository.Setup(r => r.GetRecentByCompanyAsync("ntt data", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([guardada]);
        var nueva = Offer("Programador C# .NET", "Jooble", "NTT Data", location: newLocation);

        var report = await RunAsync(Provider(filtersByKeyword: true, nueva).Object);

        nueva.IsDuplicate.Should().Be(expectedDuplicate);
        _ai.Verify(a => a.AnalyzeAsync(nueva, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()),
            expectedDuplicate ? Times.Never() : Times.Once());
        report.TotalDuplicates.Should().Be(expectedDuplicate ? 1 : 0);
    }

    [Theory]
    [InlineData(10, false, false, false)] // sin analizar y fuera de la ventana: ya no se reintenta
    [InlineData(1, false, true, false)]   // duplicada: nunca se analiza
    [InlineData(1, false, false, true)]   // pendiente dentro de la ventana: aún se analizará
    [InlineData(20, true, false, true)]   // analizada: ya salió en un informe
    public async Task Handle_SemanticoConOllama_SoloCuentaSiLaFilaAunRepresentaALaOferta(
        int daysAgo, bool isAnalyzed, bool isDuplicate, bool expectedDuplicate)
    {
        // Regresión (review: r3-pipeline-2): el semántico no aplicaba el
        // filtro de representantes vivas del dedup por título. Una original
        // sin analizar caducada (o una fila duplicada) con embedding seguía
        // suprimiendo las copias nuevas y la oferta no se puntuaba nunca.
        // Títulos distintos: solo el semántico puede casarlas (con el mismo
        // título, las dos primeras filas acababan igual, porque el de título
        // ya las ignora).
        OllamaConSimilitud(0.95f);
        var original = Offer("Backend Engineer", "Lever", discoveredAt: DateTime.UtcNow.AddDays(-daysAgo));
        original.IsAnalyzed = isAnalyzed;
        original.IsDuplicate = isDuplicate;
        original.Embedding = [1f, 0f];
        _repository.Setup(r => r.GetRecentByCompanyAsync("acme", 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([original]);
        var copia = Offer("Ingeniero Backend", "Adzuna");

        var report = await RunAsync(Provider(filtersByKeyword: true, copia).Object);

        copia.IsDuplicate.Should().Be(expectedDuplicate);
        _ai.Verify(a => a.AnalyzeAsync(copia, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()),
            expectedDuplicate ? Times.Never() : Times.Once());
        report.TotalDuplicates.Should().Be(expectedDuplicate ? 1 : 0);
    }

    [Fact]
    public async Task Handle_SinEmpresaConOllama_ElSemanticoNoLasComparaEntreSi()
    {
        // Regresión (review: r3-pipeline-2): el semántico consultaba por la
        // clave del marcador "(empresa desconocida)" (o por la vacía) y
        // comparaba entre sí todas las ofertas anónimas: con textos de
        // plantilla parecidos, una anónima suprimía a otra de una empresa que
        // podía no tener nada que ver. Ni siquiera se pide su embedding.
        UsarAlmacenSimulado();
        OllamaConSimilitud(0.99f);
        var jooble = Offer("Desarrollador Backend .NET", "Jooble", JobOffer.UnknownCompany, "Fintech de producto");
        var adzuna = Offer("Backend Developer C#", "Adzuna", JobOffer.UnknownCompany, "Consultora");
        var vacia1 = Offer("Ingeniero .NET", "Jooble", "", "Producto");
        var vacia2 = Offer("Programador .NET", "Jooble", "  ", "Producto");

        var report = await RunAsync(Provider(filtersByKeyword: true, jooble, adzuna, vacia1, vacia2).Object);

        _saved.Should().OnlyContain(o => !o.IsDuplicate);
        _embeddings.Verify(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        report.TotalDuplicates.Should().Be(0);
        report.TotalAnalyzed.Should().Be(4);
    }

    // ── Modo de trabajo revelado por la IA ──────────────────────────────

    [Theory]
    [InlineData(WorkMode.OnSite, "Madrid")]              // modo que el perfil no acepta
    [InlineData(WorkMode.Hybrid, "Barcelona, Cataluña")] // modo aceptado, pero no conmutable
    public async Task Handle_IaRevelaUnModoQueElPerfilNoAcepta_SeDescartaTrasAnalizarSinPuntuar(
        WorkMode aiMode, string location)
    {
        // Regresión (review: ai-unacceptable-workmode-tops-report):
        // CommuteFilter solo veía el modo que estimó el provider. Si la IA
        // revelaba después un modo que el perfil rechaza, la oferta perdía
        // solo 30/25 puntos y, con buen salario, stack y cliente final,
        // encabezaba el top (99 pts, banda "high") y disparaba el email.
        OllamaCaido();
        UsarAlmacenSimulado();
        _profile = PerfilMadrid(acceptOnSite: false);
        var falsaRemota = Offer("Senior Backend Engineer (.NET)", "Greenhouse",
            description: "Descripción completa con requisitos", location: location, workMode: WorkMode.Remote);
        falsaRemota.SalaryMax = 75000;
        var remota = Offer("Desarrollador .NET", "Adzuna", "Globex", location: "España", workMode: WorkMode.Remote);
        _ai.Setup(a => a.AnalyzeAsync(falsaRemota, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#", ".NET"], 95, [], [], "aplicar", [],
                IsClientFinal: true, WorkMode: aiMode));

        var report = await RunAsync(Provider(filtersByKeyword: true, falsaRemota, remota).Object);

        // Analizada (la llamada a la IA ya se gastó y no debe repetirse) y
        // guardada con el modo corregido, pero sin puntuación...
        falsaRemota.IsAnalyzed.Should().BeTrue();
        falsaRemota.Score.Should().BeNull();
        falsaRemota.WorkMode.Should().Be(aiMode);
        _repository.Verify(r => r.UpdateAsync(falsaRemota, It.IsAny<CancellationToken>()), Times.Once);
        report.TotalAnalyzed.Should().Be(2);

        // ...así que nunca entra en el top (ni dispara el email por sí sola).
        report.TopOffers.Should().Equal(remota);

        // Y la siguiente ejecución no la reintenta.
        await RunAsync(Provider(filtersByKeyword: true).Object);
        _ai.Verify(a => a.AnalyzeAsync(falsaRemota, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IaRevelaUnModoAceptableYConmutable_SePuntuaNormal()
    {
        // Contrapunto del descarte: si el modo corregido por la IA sigue
        // siendo aceptable para el perfil, la oferta se puntúa como siempre.
        OllamaCaido();
        _profile = PerfilMadrid(acceptOnSite: false);
        var hibridaEnMadrid = Offer("Backend Engineer", "Greenhouse", location: "Madrid", workMode: WorkMode.Remote);
        _ai.Setup(a => a.AnalyzeAsync(hibridaEnMadrid, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult("resumen", "Senior", ["C#"], 80, [], [], "aplicar", [],
                WorkMode: WorkMode.Hybrid));

        var report = await RunAsync(Provider(filtersByKeyword: true, hibridaEnMadrid).Object);

        hibridaEnMadrid.IsAnalyzed.Should().BeTrue();
        hibridaEnMadrid.WorkMode.Should().Be(WorkMode.Hybrid);
        hibridaEnMadrid.Score.Should().NotBeNull();
        hibridaEnMadrid.Score!.RemoteScore.Should().Be(15);
        report.TotalAnalyzed.Should().Be(1);
    }
}
