using MediatR;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.IntegrationEvents;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.DomainEvents;

namespace RenewableEnergy.Application.Features.Proyectos.EventHandlers
{
    /// <summary>
    /// SUSCRIPTOR del evento de dominio <see cref="ProyectoCreado"/>.
    ///
    /// ¿CUÁNDO SE EJECUTA? (flujo Outbox completo)
    /// 1. CrearProyectoHandler guarda el proyecto → el interceptor de EF
    ///    serializa el evento a la tabla outbox EN LA MISMA TRANSACCIÓN.
    /// 2. El OutboxProcessor (background service) lee la tabla cada pocos
    ///    segundos y publica el evento vía MediatR.
    /// 3. MediatR entrega el evento a TODOS sus INotificationHandler — este.
    ///
    /// ¿POR QUÉ NO HACER ESTO DENTRO DE CrearProyectoHandler?
    /// Porque enviar emails y publicar en el bus NO es parte de "crear un
    /// proyecto": son REACCIONES. Si mañana hay que añadir "generar informe
    /// PDF", se añade otro handler sin tocar el caso de uso original
    /// (principio Open/Closed).
    /// </summary>
    internal sealed class ProyectoCreadoEventHandler(
        IEmailService emailService,
        IEventPublisher eventPublisher,
        ILogger<ProyectoCreadoEventHandler> logger)
        : INotificationHandler<ProyectoCreado>
    {
        public async Task Handle(ProyectoCreado notification, CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "Reaccionando a ProyectoCreado: {ProyectoId} '{Nombre}'",
                notification.ProyectoId, notification.Nombre);

            // Reacción 1: notificación interna por email.
            await emailService.EnviarAsync(
                "operaciones@renovables.demo",
                $"Nuevo proyecto renovable: {notification.Nombre}",
                $"Se ha creado el proyecto '{notification.Nombre}' ({notification.Tipo}) " +
                $"con id {notification.ProyectoId} el {notification.OcurridoEnUtc:dd/MM/yyyy HH:mm} UTC.",
                cancellationToken);

            // Reacción 2: traducir el evento de dominio (interno) a un evento
            // de INTEGRACIÓN (contrato público) y publicarlo en el bus para
            // que otros servicios reaccionen.
            await eventPublisher.PublicarAsync(
                new ProyectoCreadoIntegrationEvent(
                    notification.ProyectoId,
                    notification.Nombre,
                    notification.Tipo.ToString(),
                    notification.OcurridoEnUtc),
                cancellationToken);
        }
    }
}
