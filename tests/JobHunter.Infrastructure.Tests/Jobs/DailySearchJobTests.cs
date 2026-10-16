using System.Reflection;
using AwesomeAssertions;
using Hangfire;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Jobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JobHunter.Infrastructure.Tests.Jobs;

public sealed class DailySearchJobTests
{
    /// <summary>Informe con una oferta en el top puntuada hace <paramref name="scoredAgo"/>.</summary>
    private static DailyReport ReportWithOffer(TimeSpan scoredAgo)
    {
        var report = new DailyReport { TotalNew = 1 };
        var offer = new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Test",
            Title = "t",
            CompanyName = "c",
            Url = "u",
        };
        offer.Score = new JobScore { JobOfferId = offer.Id, ScoredAt = report.GeneratedAt - scoredAgo };
        report.TopOffers = [offer];
        return report;
    }

    private static DailyReport ReportWithOffers() => ReportWithOffer(scoredAgo: TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_SiFallaElEmail_NoPropagaLaExcepcion()
    {
        // Regresión: un fallo de email NO debe re-lanzarse, porque Hangfire
        // reintentaría el job entero y re-ejecutaría el pipeline caro.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<RunDailyPipeline.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReportWithOffers());

        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendReportAsync(It.IsAny<DailyReport>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP 535: credenciales rechazadas"));

        var job = new DailySearchJob(sender.Object, email.Object, NullLogger<DailySearchJob>.Instance);

        var act = async () => await job.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        email.Verify(e => e.SendReportAsync(It.IsAny<DailyReport>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_SinOfertasEnElTop_NoIntentaEnviarEmail()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<RunDailyPipeline.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyReport()); // TopOffers vacío

        var email = new Mock<IEmailService>();

        var job = new DailySearchJob(sender.Object, email.Object, NullLogger<DailySearchJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        email.Verify(
            e => e.SendReportAsync(It.IsAny<DailyReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_TopSinNadaNuevo_NoIntentaEnviarEmail()
    {
        // El top abarca varios días: si todo se puntuó en ejecuciones
        // anteriores, el email repetiría lo ya enviado.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<RunDailyPipeline.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReportWithOffer(scoredAgo: TimeSpan.FromDays(1)));

        var email = new Mock<IEmailService>();

        var job = new DailySearchJob(sender.Object, email.Object, NullLogger<DailySearchJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        email.Verify(
            e => e.SendReportAsync(It.IsAny<DailyReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void RunAsync_NoSeReintentaAutomaticamenteNiSeEjecutaEnParalelo()
    {
        // Cada ejecución gasta cuota de APIs y análisis de IA: Hangfire no
        // debe repetirla por su cuenta (por defecto reintenta hasta 10 veces)
        // ni lanzar dos a la vez (cron + /jobs/run-now).
        var method = typeof(DailySearchJob).GetMethod(nameof(DailySearchJob.RunAsync))!;

        method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts.Should().Be(0);
        method.GetCustomAttribute<DisableConcurrentExecutionAttribute>().Should().NotBeNull();
    }
}
