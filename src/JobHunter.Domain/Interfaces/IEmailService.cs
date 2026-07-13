using JobHunter.Domain.Entities;

namespace JobHunter.Domain.Interfaces;

/// <summary>
/// Envía el informe diario por email. Lo implementa Infrastructure con
/// MailKit (SMTP).
/// </summary>
public interface IEmailService
{
    Task SendReportAsync(DailyReport report, CancellationToken ct = default);
}
