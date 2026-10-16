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
    public async Task Handle_MismoTituloEnOficinaConmutableYNoConmutable_NoSeFusionan()
    {
        // Regresión (review: title-dedup-ignores-location): las consultoras
        // publican el mismo título por oficina. El grupo del lote se quedaba
        // con la de descripción más larga (Barcelona) y marcaba duplicada la
        // de Madrid, la única que el candidato puede coger.
        OllamaCaido();
        _profile = PerfilMadrid();
        var barcelona = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Snippet algo más largo de la oficina de Barcelona", location: "Barcelona, Cataluña");
        var madrid = Offer("Desarrollador .NET", "Adzuna", "NTT DATA",
            "Snippet de Madrid", location: "Madrid, Comunidad de Madrid");
        var copiaSinUbicacion = Offer("Desarrollador .NET", "Jooble", "NTT Data", "Corto");

        var report = await RunAsync(Provider(filtersByKeyword: true, barcelona, madrid, copiaSinUbicacion).Object);

        barcelona.IsDuplicate.Should().BeFalse();
        madrid.IsDuplicate.Should().BeFalse();
        // Sin ubicación cuenta como conmutable: las copias entre fuentes de la
        // oferta de Madrid se siguen fusionando.
        copiaSinUbicacion.IsDuplicate.Should().BeTrue();
        _ai.Verify(a => a.AnalyzeAsync(madrid, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalDuplicates.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MismoTituloYaVistoEnBdEnOficinaNoConmutable_NoSuprimeLaConmutable()
    {
        // Regresión (review: title-dedup-ignores-location, paso contra BD):
        // una "Desarrollador .NET" de Barcelona vista en los últimos 30 días
        // suprimía la de Madrid de hoy. Una copia de la de Barcelona sí debe
        // seguir casando con ella.
        OllamaCaido();
        _profile = PerfilMadrid();
        _repository.Setup(r => r.GetRecentByCompanyAsync("ntt data", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Offer("Desarrollador .NET", "Adzuna", "NTT DATA", location: "Barcelona, Cataluña")]);
        var madrid = Offer("Desarrollador .NET", "Jooble", "NTT Data", location: "Madrid");
        var copiaDeBarcelona = Offer("Desarrollador .NET", "JSearch", "NTT DATA", location: "Barcelona");

        var report = await RunAsync(Provider(filtersByKeyword: true, madrid, copiaDeBarcelona).Object);

        madrid.IsDuplicate.Should().BeFalse();
        copiaDeBarcelona.IsDuplicate.Should().BeTrue();
        _ai.Verify(a => a.AnalyzeAsync(madrid, It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        report.TotalDuplicates.Should().Be(1);
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
