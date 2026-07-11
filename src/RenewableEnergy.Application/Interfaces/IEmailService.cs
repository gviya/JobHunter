namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Contrato de envío de emails. Application define QUÉ necesita
    /// ("envía este email") e Infrastructure decide CÓMO (SMTP, SendGrid,
    /// Azure Communication Services... o un simple log en desarrollo).
    ///
    /// Lo consumen los HANDLERS DE EVENTOS de dominio: p. ej. cuando llega
    /// un ProyectoCreado, ProyectoCreadoEventHandler envía la notificación.
    /// Así "crear proyecto" y "notificar" quedan desacoplados.
    /// </summary>
    public interface IEmailService
    {
        /// <summary>Envía un email. En desarrollo la implementación solo loguea.</summary>
        Task EnviarAsync(string destinatario, string asunto, string cuerpo, CancellationToken cancellationToken = default);
    }
}
