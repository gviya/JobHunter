using MediatR;

namespace RenewableEnergy.Domain.Common
{
    /// <summary>
    /// Interfaz base de todos los EVENTOS DE DOMINIO.
    ///
    /// ¿QUÉ ES UN EVENTO DE DOMINIO?
    /// Un hecho de negocio que YA HA OCURRIDO, expresado en pasado:
    /// "ProyectoCreado", "MedicionRegistrada", "ProyectoActivado".
    ///
    /// ¿POR QUÉ EXISTE?
    /// Permite DESACOPLAR: cuando se crea un proyecto, la entidad no sabe
    /// (ni debe saber) que hay que enviar un email o publicar en RabbitMQ.
    /// La entidad solo "levanta" el evento; otros componentes reaccionan.
    ///
    /// Hereda de <see cref="INotification"/> (MediatR.Contracts) para que los
    /// handlers de Application puedan suscribirse con INotificationHandler&lt;T&gt;.
    /// MediatR.Contracts contiene SOLO interfaces, por eso se tolera en Domain.
    /// </summary>
    public interface IDomainEvent : INotification
    {
        /// <summary>Momento (UTC) en el que ocurrió el hecho de negocio.</summary>
        DateTime OcurridoEnUtc { get; }
    }
}
