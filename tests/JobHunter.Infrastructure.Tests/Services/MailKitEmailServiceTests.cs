using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Configuration;
using JobHunter.Infrastructure.Services;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunter.Infrastructure.Tests.Services;

public sealed class MailKitEmailServiceTests
{
    [Fact]
    public void ResolveSocketOptions_Puerto465_UsaTlsImplicito()
    {
        // Regresión: con StartTls fijo, el 465 (SMTPS) esperaba un saludo en
        // claro que nunca llega y la conexión fallaba.
        var options = MailKitEmailService.ResolveSocketOptions(465);

        options.Should().Be(SecureSocketOptions.SslOnConnect);
    }

    [Theory]
    [InlineData(587)]
    [InlineData(25)]
    [InlineData(2525)]
    public void ResolveSocketOptions_PuertoDeSubmissionOClaro_ExigeStartTls(int port)
    {
        var options = MailKitEmailService.ResolveSocketOptions(port);

        options.Should().Be(SecureSocketOptions.StartTls);
    }

    [Theory]
    [InlineData(465)]
    [InlineData(587)]
    [InlineData(25)]
    public void ResolveSocketOptions_CualquierPuerto_NuncaPermiteTextoPlano(int port)
    {
        // Auto/StartTlsWhenAvailable/None seguirían sin cifrar si el servidor
        // no ofrece TLS: credenciales e informe viajarían en claro.
        var options = MailKitEmailService.ResolveSocketOptions(port);

        options.Should().NotBe(SecureSocketOptions.Auto)
            .And.NotBe(SecureSocketOptions.StartTlsWhenAvailable)
            .And.NotBe(SecureSocketOptions.None);
    }

    [Theory]
    [InlineData("", "yo@example.com", "yo@example.com")]
    [InlineData("127.0.0.1", "", "yo@example.com")]
    [InlineData("127.0.0.1", "yo@example.com", "")]
    public async Task SendReportAsync_SmtpIncompleto_NoIntentaConectar(string host, string from, string to)
    {
        // Puerto 1 en loopback: si el guard dejara pasar la llamada, la
        // conexión se rechazaría al instante y el test fallaría sin red.
        var service = new MailKitEmailService(
            Options.Create(new EmailOptions { SmtpHost = host, SmtpPort = 1, From = from, To = to }),
            NullLogger<MailKitEmailService>.Instance);

        var act = () => service.SendReportAsync(new DailyReport());

        await act.Should().NotThrowAsync();
    }
}
