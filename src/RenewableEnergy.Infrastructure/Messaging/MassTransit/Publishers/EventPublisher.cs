using MassTransit;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Interfaces;

namespace RenewableEnergy.Infrastructure.Messaging.MassTransit.Publishers
{
    /// <summary>
    /// Implementación de IEventPublisher sobre MASSTRANSIT.
    ///
    /// IPublishEndpoint publica con semántica pub/sub: MassTransit crea un
    /// exchange por tipo de mensaje en RabbitMQ y CUALQUIER servicio con un
    /// consumer de ese tipo lo recibe. El publicador no sabe (ni le importa)
    /// quién escucha.
    ///
    /// Para cambiar a Azure Service Bus en producción NO se toca esta clase:
    /// solo la configuración del bus en DependencyInjection (UsingAzureServiceBus
    /// en lugar de UsingRabbitMq). Ese es el valor de MassTransit como abstracción.
    /// </summary>
    internal sealed class EventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<EventPublisher> logger) : IEventPublisher
    {
        public async Task PublicarAsync<TEvento>(TEvento evento, CancellationToken cancellationToken = default)
            where TEvento : class
        {
            logger.LogInformation("Publicando evento de integración {TipoEvento}", typeof(TEvento).Name);

            await publishEndpoint.Publish(evento, cancellationToken);
        }
    }

    /// <summary>
    /// Implementación NULA (patrón Null Object): se registra cuando
    /// Messaging:Enabled=false, para que la aplicación funcione SIN RabbitMQ
    /// (desarrollo rápido, tests). Loguea lo que habría publicado.
    /// </summary>
    internal sealed class NoOpEventPublisher(ILogger<NoOpEventPublisher> logger) : IEventPublisher
    {
        public Task PublicarAsync<TEvento>(TEvento evento, CancellationToken cancellationToken = default)
            where TEvento : class
        {
            logger.LogInformation(
                "Messaging deshabilitado: se omite la publicación de {TipoEvento}",
                typeof(TEvento).Name);

            return Task.CompletedTask;
        }
    }
}
