namespace RenewableEnergy.Infrastructure.Messaging.Outbox
{
    /// <summary>
    /// Fila de la tabla OUTBOX: un evento de dominio serializado, pendiente
    /// de publicar (o ya publicado, si ProcessedOnUtc tiene valor).
    ///
    /// Ciclo de vida:
    /// 1. La crea OutboxInterceptor en la misma transacción que el negocio.
    /// 2. La lee OutboxProcessor, publica el evento y la marca procesada.
    /// 3. Si la publicación falla, Error guarda el motivo (para diagnóstico
    ///    y reintentos manuales).
    /// </summary>
    public sealed class OutboxMessage(Guid id, DateTime occurredOnUtc, string type, string content)
    {
        /// <summary>Identificador único del mensaje.</summary>
        public Guid Id { get; init; } = id;

        /// <summary>Cuándo ocurrió el evento de negocio (UTC).</summary>
        public DateTime OccurredOnUtc { get; init; } = occurredOnUtc;

        /// <summary>Nombre del tipo del evento (para diagnóstico rápido en la tabla).</summary>
        public string Type { get; init; } = type;

        /// <summary>El evento serializado (JSON con metadatos de tipo).</summary>
        public string Content { get; init; } = content;

        /// <summary>Cuándo se procesó. Null = pendiente.</summary>
        public DateTime? ProcessedOnUtc { get; private set; }

        /// <summary>Error de la publicación, si la hubo.</summary>
        public string? Error { get; private set; }

        /// <summary>Marca el mensaje como procesado, con o sin error.</summary>
        public void MarcarComoProcesado(Exception? excepcion = null)
        {
            ProcessedOnUtc = DateTime.UtcNow;

            if (excepcion is not null)
            {
                Error = excepcion.ToString();
            }
        }
    }
}
