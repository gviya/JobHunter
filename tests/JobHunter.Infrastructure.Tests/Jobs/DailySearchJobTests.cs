using AwesomeAssertions;
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
    private static DailyReport ReportWithOffers() => new()
    {
        TotalNew = 1,
        TopOffers = [new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Test",
            Title = "t",
            CompanyName = "c",
            Url = "u",
        }],
    };

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
}
