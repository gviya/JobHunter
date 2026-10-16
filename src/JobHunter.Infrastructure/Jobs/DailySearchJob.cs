using Hangfire;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JobHunter.Infrastructure.Jobs;

/// <summary>
/// Job que orquesta Hangfire: ejecuta el pipeline diario vía MediatR y, si
/// el top trae alguna oferta nueva, envía el informe por email. Es el punto
/// de entrada tanto del job recurrente como del endpoint /jobs/run-now.
/// </summary>
public sealed class DailySearchJob(
    ISender sender,
    IEmailService emailService,
    ILogger<DailySearchJob> logger)
{
    // AutomaticRetry(0): por defecto Hangfire reintenta un job fallido hasta
    // 10 veces, y cada reintento repite búsquedas en APIs con cuota y el
    // análisis de IA. Los fallos recuperables ya se absorben dentro (provider
    // caído, oferta que no se analiza, email); lo que llegue aquí suele ser
    // persistente (BD, configuración). El cron de mañana lo vuelve a intentar
    // y las ofertas sin analizar se reintentan entonces, así que no se pierde
    // nada. El job fallido queda visible en /hangfire para relanzarlo a mano.
    //
    // DisableConcurrentExecution: el cron y /jobs/run-now pueden coincidir;
    // dos pipelines a la vez duplicarían el gasto y competirían por la misma
    // BD SQLite. El segundo espera a que acabe el primero (hasta 30 min, un
    // margen holgado para un análisis largo con Ollama) y si no, falla.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task RunAsync(CancellationToken ct)
    {
        var report = await sender.Send(new RunDailyPipeline.Command(), ct);

        // El top abarca varios días: si nada es nuevo, el email repetiría lo
        // ya enviado. El informe queda guardado igualmente en /reports/latest.
        if (!report.HasNewTopOffers())
        {
            logger.LogInformation("El informe no trae ofertas nuevas en el top: no se envía email.");
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
