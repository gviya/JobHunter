using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Jobs;

/// <summary>
/// Job que orquesta Hangfire: ejecuta el pipeline diario vía MediatR y, si
/// hay ofertas en el top, envía el informe por email. Es el punto de
/// entrada tanto del job recurrente como del endpoint /jobs/run-now.
/// </summary>
public sealed class DailySearchJob(
    ISender sender,
    IEmailService emailService,
    ILogger<DailySearchJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var report = await sender.Send(new RunDailyPipeline.Command(), ct);

        if (report.TopOffers.Count == 0)
        {
            logger.LogInformation("El informe no tiene ofertas en el top: no se envía email.");
            return;
        }

        // El envío del email es "best effort" y va en su propio try/catch: si
        // falla (credenciales SMTP incorrectas, red...), NO debe re-lanzarse
        // porque Hangfire reintentaría el JOB ENTERO — re-ejecutando un
        // pipeline caro (búsqueda + embeddings + análisis con IA) solo por un
        // fallo de notificación. El informe ya está guardado y accesible en
        // /reports/latest pase lo que pase con el correo.
        try
        {
            await emailService.SendReportAsync(report, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo enviar el informe por email: {Error}", ex.Message);
        }
    }
}
