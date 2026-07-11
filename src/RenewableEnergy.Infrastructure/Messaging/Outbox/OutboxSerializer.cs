using Newtonsoft.Json;
using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Infrastructure.Messaging.Outbox
{
    /// <summary>
    /// Serialización de eventos de dominio hacia/desde la tabla outbox.
    ///
    /// DETALLE TÉCNICO: se usa Newtonsoft.Json con TypeNameHandling.All
    /// porque el JSON debe guardar QUÉ TIPO CONCRETO es el evento
    /// (ProyectoCreado, MedicionRegistrada...) para poder deserializarlo
    /// como IDomainEvent polimórfico al procesarlo. System.Text.Json exige
    /// registrar cada tipo a mano; Newtonsoft lo resuelve con metadatos "$type".
    /// </summary>
    internal static class OutboxSerializer
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        /// <summary>Evento de dominio → fila de outbox.</summary>
        internal static OutboxMessage Serializar(IDomainEvent evento)
        {
            return new OutboxMessage(
                Guid.NewGuid(),
                evento.OcurridoEnUtc,
                evento.GetType().Name,
                JsonConvert.SerializeObject(evento, Settings));
        }

        /// <summary>Fila de outbox → evento de dominio (polimórfico).</summary>
        internal static IDomainEvent? Deserializar(OutboxMessage mensaje)
        {
            return JsonConvert.DeserializeObject<IDomainEvent>(mensaje.Content, Settings);
        }
    }
}
