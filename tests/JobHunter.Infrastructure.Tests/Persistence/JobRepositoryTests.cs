using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Persistence;
using JobHunter.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests de integración de la persistencia sobre SQLite en memoria (con la
/// conexión mantenida abierta para que la BD sobreviva entre contextos).
/// Verifican que la migración aplica, que la relación 1:1 JobOffer↔JobScore
/// se inserta bien y que las consultas del repositorio funcionan.
/// </summary>
public sealed class JobRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private JobHunterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new JobHunterDbContext(options);
    }

    private static JobOffer NewOffer(string externalId, string company, DateTime? discoveredAt = null) => new()
    {
        DiscoveredAt = discoveredAt ?? DateTime.UtcNow,
        ExternalId = externalId,
        SourceProvider = "Test",
        Title = "Backend .NET",
        CompanyName = company,
        CompanyNormalized = company.ToLowerInvariant(),
        Url = $"https://example.com/{externalId}",
        WorkMode = WorkMode.Remote,
        PublishedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Save_AsignarScore_Update_PersisteLaRelacion1a1()
    {
        Guid offerId;

        // ── Contexto de escritura ───────────────────────────────────
        await using (var db = CreateContext())
        {
            var repo = new JobRepository(db);
            var offer = NewOffer("1", "acme");
            offerId = offer.Id;

            await repo.SaveAsync(offer);

            offer.Score = new JobScore
            {
                JobOfferId = offer.Id,
                TotalScore = 119,
                CompatibilityScore = 80,
                RemoteScore = 30,
                ClientFinalScore = 20,
                SalaryScore = 20,
                TechScore = 9,
                AiSummary = "buena oferta",
            };
            offer.IsAnalyzed = true;

            await repo.UpdateAsync(offer);
        }

        // ── Contexto de lectura nuevo (sin identity map) ────────────
        await using (var db = CreateContext())
        {
            var repo = new JobRepository(db);
            var top = await repo.GetTopScoredAsync(DateTime.UtcNow.AddDays(-7));

            top.Should().ContainSingle();
            top[0].Id.Should().Be(offerId);
            top[0].IsAnalyzed.Should().BeTrue();
            top[0].Score.Should().NotBeNull();
            top[0].Score!.TotalScore.Should().Be(119);
        }
    }

    [Fact]
    public async Task Embedding_SePersisteYRecupera()
    {
        Guid offerId;

        await using (var db = CreateContext())
        {
            var repo = new JobRepository(db);
            var offer = NewOffer("emb", "acme");
            offerId = offer.Id;
            await repo.SaveAsync(offer);

            offer.Embedding = [0.1f, 0.2f, 0.3f];
            await repo.UpdateAsync(offer);
        }

        await using (var db = CreateContext())
        {
            var stored = await db.JobOffers.SingleAsync(o => o.Id == offerId);
            stored.Embedding.Should().Equal(0.1f, 0.2f, 0.3f);
        }
    }

    [Fact]
    public async Task ExistsAsync_TrasGuardar_DistingueClaves()
    {
        await using var db = CreateContext();
        var repo = new JobRepository(db);
        await repo.SaveAsync(NewOffer("42", "acme"));

        (await repo.ExistsAsync("42", "Test")).Should().BeTrue();
        (await repo.ExistsAsync("42", "Otro")).Should().BeFalse();
        (await repo.ExistsAsync("99", "Test")).Should().BeFalse();
    }

    [Fact]
    public async Task SaveReport_Then_GetLatest_DevuelveElMasReciente()
    {
        await using var db = CreateContext();
        var repo = new JobRepository(db);

        await repo.SaveReportAsync(new DailyReport
        {
            TotalNew = 3,
            HtmlContent = "<b>viejo</b>",
            GeneratedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        await repo.SaveReportAsync(new DailyReport
        {
            TotalNew = 7,
            HtmlContent = "<b>nuevo</b>",
            GeneratedAt = DateTime.UtcNow,
        });

        var latest = await repo.GetLatestReportAsync();

        latest.Should().NotBeNull();
        latest!.TotalNew.Should().Be(7);
    }

    [Fact]
    public async Task GetTopScoredAsync_SoloIncluyeLoDescubiertoDentroDeLaVentana()
    {
        // Regresión: sin ventana el informe "diario" era el top histórico y
        // repetía cada día las mismas ofertas, aunque ya hubieran caducado.
        await using var db = CreateContext();
        var repo = new JobRepository(db);

        var reciente = NewOffer("reciente", "acme", DateTime.UtcNow.AddDays(-2));
        var antigua = NewOffer("antigua", "acme", DateTime.UtcNow.AddDays(-30));

        foreach (var (offer, total) in new[] { (reciente, 60), (antigua, 140) })
        {
            await repo.SaveAsync(offer);
            offer.Score = new JobScore { JobOfferId = offer.Id, TotalScore = total };
            offer.IsAnalyzed = true;
            await repo.UpdateAsync(offer);
        }

        var top = await repo.GetTopScoredAsync(DateTime.UtcNow.AddDays(-7));

        // La antigua puntúa más, pero queda fuera de la ventana.
        top.Select(o => o.ExternalId).Should().Equal("reciente");
    }

    [Fact]
    public async Task GetUnanalyzedAsync_DevuelvePendientesDeLaVentana_SinAnalizadasNiDuplicadas()
    {
        await using var db = CreateContext();
        var repo = new JobRepository(db);

        var pendienteAyer = NewOffer("ayer", "acme", DateTime.UtcNow.AddDays(-1));
        var pendienteHoy = NewOffer("hoy", "acme");
        var caducada = NewOffer("caducada", "acme", DateTime.UtcNow.AddDays(-30));
        var analizada = NewOffer("analizada", "acme");
        analizada.IsAnalyzed = true;
        var duplicada = NewOffer("duplicada", "acme");
        duplicada.IsDuplicate = true;

        foreach (var offer in new[] { pendienteHoy, pendienteAyer, caducada, analizada, duplicada })
        {
            await repo.SaveAsync(offer);
        }

        var pending = await repo.GetUnanalyzedAsync(DateTime.UtcNow.AddDays(-7));

        // Ordenadas de más antigua a más reciente.
        pending.Select(o => o.ExternalId).Should().Equal("ayer", "hoy");
    }
}
