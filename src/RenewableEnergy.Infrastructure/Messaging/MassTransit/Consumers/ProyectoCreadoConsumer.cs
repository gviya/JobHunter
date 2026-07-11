using MassTransit;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.IntegrationEvents;

namespace RenewableEnergy.Infrastructure.Messaging.MassTransit.Consumers
{
    /// <summary>
    /// CONSUMER de MassTransit: recibe ProyectoCreadoIntegrationEvent desde
    /// RabbitMQ.
    ///
    /// En un sistema real este consumer viviría en OTRO microservicio
    /// (facturación, notificaciones...). Aquí está en el mismo proceso con
    /// fin DIDÁCTICO: al crear un proyecto con la API (con Messaging:Enabled
    /// y RabbitMQ arrancado) verás en los logs el ciclo completo:
    /// publicación → cola → consumo.
    ///
    /// MassTransit crea automáticamente la cola, deserializa el mensaje y
    /// gestiona reintentos/errores (colas _error) sin código adicional.
    /// </summary>
    internal sealed class ProyectoCreadoConsumer(ILogger<ProyectoCreadoConsumer> logger)
        : IConsumer<ProyectoCreadoIntegrationEvent>
    {
        public Task Consume(ConsumeContext<ProyectoCreadoIntegrationEvent> context)
        {
            var evento = context.Message;

            logger.LogInformation(
                "📨 Consumido ProyectoCreadoIntegrationEvent: {ProyectoId} '{Nombre}' ({Tipo})",
                evento.ProyectoId, evento.Nombre, evento.TipoEnergia);

            // Aquí iría la reacción del servicio consumidor (crear factura,
            // dar de alta monitorización...). Los consumers deben ser
            // IDEMPOTENTES: el mismo evento puede llegar más de una vez.
            return Task.CompletedTask;
        }
    }
}
