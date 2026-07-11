using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Infrastructure.Persistence;

namespace RenewableEnergy.Infrastructure.Messaging.Outbox
{
    /// <summary>
    /// Implementación de IOutboxService: encolado MANUAL de eventos.
    /// El mensaje queda pendiente en el ChangeTracker y se confirmará con el
    /// SaveChanges del caso de uso (misma transacción — esa es la gracia).
    /// </summary>
    internal sealed class OutboxService(ApplicationDbContext context) : IOutboxService
    {
        public void Encolar(IDomainEvent evento)
        {
            context.OutboxMessages.Add(OutboxSerializer.Serializar(evento));
        }
    }
}
