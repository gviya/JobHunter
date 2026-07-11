using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Infrastructure.Messaging.Outbox;

namespace RenewableEnergy.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// INTERCEPTOR que implementa la mitad "escritura" del PATRÓN OUTBOX.
    ///
    /// ¿QUÉ PROBLEMA RESUELVE EL OUTBOX? (pregunta estrella de entrevista)
    /// Si el handler guardara en BD y DESPUÉS publicara el evento en RabbitMQ,
    /// podría ocurrir que la BD confirme y el bus falle (o al revés):
    /// sistemas INCONSISTENTES. El outbox lo evita guardando el evento EN LA
    /// MISMA TRANSACCIÓN que los datos, en una tabla local (OutboxMessages).
    /// Un proceso aparte (OutboxProcessor) los publica después con reintentos.
    /// Resultado: consistencia "at-least-once" sin transacciones distribuidas.
    ///
    /// ¿QUÉ HACE ESTE INTERCEPTOR EXACTAMENTE?
    /// Justo antes de cada SaveChanges: recorre las entidades rastreadas,
    /// recoge sus eventos de dominio pendientes, los limpia de la entidad y
    /// los serializa como filas de OutboxMessage que viajan en la MISMA
    /// transacción que el cambio de negocio.
    /// </summary>
    public sealed class OutboxInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            VolcarEventosAlOutbox(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            VolcarEventosAlOutbox(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void VolcarEventosAlOutbox(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            var mensajes = context.ChangeTracker
                .Entries<BaseEntity>()
                .Select(entry => entry.Entity)
                .SelectMany(entidad =>
                {
                    var eventos = entidad.GetDomainEvents();

                    // Se limpian para no volver a procesarlos en el siguiente save.
                    entidad.ClearDomainEvents();

                    return eventos;
                })
                .Select(OutboxSerializer.Serializar)
                .ToList();

            if (mensajes.Count != 0)
            {
                context.Set<OutboxMessage>().AddRange(mensajes);
            }
        }
    }
}
