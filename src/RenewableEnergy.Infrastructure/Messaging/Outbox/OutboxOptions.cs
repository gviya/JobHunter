namespace RenewableEnergy.Infrastructure.Messaging.Outbox
{
    /// <summary>
    /// Opciones del procesador de outbox (patrón IOptions: se bindean desde
    /// la sección "Outbox" de appsettings.json).
    /// </summary>
    public sealed class OutboxOptions
    {
        /// <summary>Permite apagar el procesador (p. ej. en tests).</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Cada cuántos segundos se busca mensajes pendientes.</summary>
        public int IntervalInSeconds { get; init; } = 10;

        /// <summary>Máximo de mensajes procesados por pasada.</summary>
        public int BatchSize { get; init; } = 20;
    }
}
