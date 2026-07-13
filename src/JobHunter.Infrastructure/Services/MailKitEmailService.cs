using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using JobHunter.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JobHunter.Infrastructure.Services;

/// <summary>
/// Envía el informe diario por SMTP con MailKit. Si el SMTP no está
/// configurado (host/usuario vacíos), lo registra y no envía nada — así el
/// pipeline puede correr en local sin credenciales.
/// </summary>
public sealed class MailKitEmailService(
    IOptions<EmailOptions> options,
    ILogger<MailKitEmailService> logger) : IEmailService
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendReportAsync(DailyReport report, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost) ||
            string.IsNullOrWhiteSpace(_options.From) ||
            string.IsNullOrWhiteSpace(_options.To))
        {
            logger.LogWarning("Email sin configurar (SmtpHost/From/To vacíos): no se envía el informe.");
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(_options.To));
        message.Subject = $"JobHunter AI — {report.GeneratedAt:yyyy-MM-dd} — {report.TotalNew} ofertas nuevas";
        message.Body = new BodyBuilder { HtmlBody = report.HtmlContent }.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, SecureSocketOptions.StartTls, ct);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Informe enviado a {To} ({Count} ofertas nuevas).", _options.To, report.TotalNew);
    }
}
