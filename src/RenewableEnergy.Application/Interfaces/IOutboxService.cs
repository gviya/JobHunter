using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Puerta de entrada MANUAL al patrón Outbox.
    ///
    /// NORMALMENTE NO SE USA: los eventos de dominio llegan al outbox de forma
    /// automática (el interceptor de EF Core los serializa al hacer
    /// SaveChanges). Esta interfaz existe como "escape hatch" para los casos
    /// en que un handler quiere encolar un evento que NO nace de una entidad
    /// (p. ej. un proceso batch que quiere notificar su finalización) con la
    /// misma garantía transaccional.
    ///
    /// El mensaje se guarda en la MISMA transacción que el resto de cambios
    /// (lo confirma el IUnitOfWork) y el OutboxProcessor lo publicará después.
    /// </summary>
    public interface IOutboxService
    {
        /// <summary>Encola un evento en la tabla outbox (pendiente de SaveChanges).</summary>
        void Encolar(IDomainEvent evento);
    }
}
