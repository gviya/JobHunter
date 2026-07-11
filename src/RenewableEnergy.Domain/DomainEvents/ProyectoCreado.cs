using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Domain.DomainEvents
{
    /// <summary>
    /// EVENTO DE DOMINIO: "se ha creado un proyecto renovable".
    ///
    /// Lo levanta <c>ProyectoRenovable.Create()</c>. Nadie lo "envía"
    /// activamente: la entidad lo acumula y, al hacer SaveChanges, el
    /// interceptor de Infrastructure lo serializa a la tabla Outbox.
    /// Después el OutboxProcessor lo publica vía MediatR (handlers internos,
    /// p. ej. enviar email de bienvenida) y/o MassTransit (otros servicios).
    ///
    /// Es un `record` INMUTABLE: los hechos del pasado no se editan.
    /// Transporta SOLO los datos necesarios (no la entidad entera) para que
    /// los suscriptores no dependan del agregado completo.
    /// </summary>
    public sealed record ProyectoCreado(
        Guid ProyectoId,
        string Nombre,
        TipoEnergia Tipo,
        DateTime OcurridoEnUtc) : IDomainEvent;
}
