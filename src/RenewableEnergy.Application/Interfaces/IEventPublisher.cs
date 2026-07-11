namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Contrato para publicar EVENTOS DE INTEGRACIÓN hacia otros sistemas
    /// (RabbitMQ vía MassTransit, Azure Service Bus...).
    ///
    /// DIFERENCIA CLAVE (cae en entrevistas):
    /// - EVENTO DE DOMINIO: hecho interno, viaja DENTRO del proceso vía
    ///   MediatR. Puede cambiar libremente al refactorizar.
    /// - EVENTO DE INTEGRACIÓN: contrato PÚBLICO con otros servicios, viaja
    ///   por el bus. Cambiarlo rompe consumidores: se versiona con cuidado.
    ///
    /// La implementación real es EventPublisher (Infrastructure/Messaging,
    /// usa IPublishEndpoint de MassTransit). Cuando el messaging está
    /// deshabilitado (Messaging:Enabled=false) se registra una implementación
    /// no-op que solo loguea — la app funciona sin RabbitMQ.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>Publica un evento de integración en el bus de mensajes.</summary>
        Task PublicarAsync<TEvento>(TEvento evento, CancellationToken cancellationToken = default)
            where TEvento : class;
    }
}
