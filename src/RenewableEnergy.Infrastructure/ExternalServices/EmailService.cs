using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Interfaces;

namespace RenewableEnergy.Infrastructure.ExternalServices
{
    /// <summary>
    /// Implementación de IEmailService para DESARROLLO: escribe el email en
    /// el log en lugar de enviarlo.
    ///
    /// EN PRODUCCIÓN sustituirías el cuerpo por un proveedor real (SMTP,
    /// SendGrid, Azure Communication Services) SIN tocar ningún handler:
    /// solo esta clase y su registro en DependencyInjection. Esa es la
    /// ventaja de que Application dependa de la interfaz y no de esto.
    /// </summary>
    internal sealed class EmailService(ILogger<EmailService> logger) : IEmailService
    {
        public Task EnviarAsync(
            string destinatario,
            string asunto,
            string cuerpo,
            CancellationToken cancellationToken = default)
        {
            logger.LogInformation(
                "📧 [Email simulado] Para: {Destinatario} | Asunto: {Asunto} | Cuerpo: {Cuerpo}",
                destinatario, asunto, cuerpo);

            return Task.CompletedTask;
        }
    }
}
