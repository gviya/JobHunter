using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Domain.DomainEvents
{
    /// <summary>
    /// EVENTO DE DOMINIO: "un proyecto ha pasado de Borrador a Activo".
    ///
    /// A partir de este momento el proyecto admite mediciones. Suscriptores
    /// típicos: notificar al propietario, dar de alta el proyecto en el
    /// sistema de monitorización, publicar un evento de integración para
    /// que otros microservicios reaccionen.
    /// </summary>
    public sealed record ProyectoActivado(
        Guid ProyectoId,
        string Nombre,
        DateTime OcurridoEnUtc) : IDomainEvent;
}
