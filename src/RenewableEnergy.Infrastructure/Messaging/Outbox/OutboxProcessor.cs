using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RenewableEnergy.Infrastructure.Persistence;

namespace RenewableEnergy.Infrastructure.Messaging.Outbox
{
    /// <summary>
    /// WORKER (BackgroundService) que implementa la mitad "lectura" del
    /// PATRÓN OUTBOX: cada N segundos busca mensajes pendientes, los
    /// deserializa y los publica vía MediatR para que reaccionen los
    /// INotificationHandler de Application (emails, eventos de integración...).
    ///
    /// GARANTÍA: entrega AT-LEAST-ONCE. Si el proceso muere tras publicar
    /// pero antes de marcar el mensaje, se volverá a publicar: los handlers
    /// de eventos deben ser IDEMPOTENTES (procesar dos veces = mismo efecto).
    ///
    /// RESILIENCIA con POLLY: cada publicación se envuelve en una pipeline de
    /// reintentos con backoff exponencial — un fallo transitorio del bus no
    /// marca el mensaje como fallido a la primera.
    /// </summary>
    internal sealed class OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger) : BackgroundService
    {
        // Pipeline de Polly v8: 3 reintentos con backoff exponencial
        // (200ms, 400ms, 800ms). Es estática: se construye una sola vez.
        private static readonly ResiliencePipeline Reintentos = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "OutboxProcessor iniciado: cada {Intervalo}s, lotes de {Batch}",
                options.Value.IntervalInSeconds, options.Value.BatchSize);

            using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalInSeconds));

            try
            {
                while (await temporizador.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await ProcesarLoteAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Un fallo de pasada NO tumba el worker: se loguea y se
                        // reintenta en el siguiente tick.
                        logger.LogError(ex, "Error procesando el lote de outbox");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Apagado normal de la aplicación.
            }
        }

        private async Task ProcesarLoteAsync(CancellationToken cancellationToken)
        {
            // BackgroundService es singleton pero DbContext es scoped:
            // se crea un scope por pasada.
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

            var pendientes = await dbContext.OutboxMessages
                .Where(m => m.ProcessedOnUtc == null)
                .OrderBy(m => m.OccurredOnUtc)
                .Take(options.Value.BatchSize)
                .ToListAsync(cancellationToken);

            if (pendientes.Count == 0)
            {
                return;
            }

            logger.LogInformation("Procesando {Cantidad} mensajes de outbox", pendientes.Count);

            foreach (var mensaje in pendientes)
            {
                Exception? fallo = null;

                try
                {
                    var eventoDominio = OutboxSerializer.Deserializar(mensaje)
                        ?? throw new InvalidOperationException(
                            $"No se pudo deserializar el mensaje {mensaje.Id} ({mensaje.Type}).");

                    // Publicación con reintentos: MediatR entrega el evento a
                    // todos sus INotificationHandler (ProyectoCreadoEventHandler...).
                    await Reintentos.ExecuteAsync(
                        async token => await publisher.Publish(eventoDominio, token),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Fallo publicando el mensaje de outbox {MensajeId}", mensaje.Id);
                    fallo = ex;
                }

                mensaje.MarcarComoProcesado(fallo);
            }

            // Un único SaveChanges marca todo el lote (procesados y fallidos).
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
