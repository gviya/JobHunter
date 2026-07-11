namespace RenewableEnergy.API.Configuration
{
    /// <summary>
    /// Opciones de RATE LIMITING (limitación de peticiones), bindeadas desde
    /// la sección "RateLimiting" de appsettings.json.
    ///
    /// ¿PARA QUÉ SIRVE? Protege la API frente a abuso (accidental o
    /// malicioso): cada cliente tiene un cupo de peticiones por ventana de
    /// tiempo; superado el cupo recibe HTTP 429 Too Many Requests.
    /// </summary>
    public sealed class RateLimitSettings
    {
        /// <summary>Política activa: "Fixed" (global) o "PerUser" (por usuario/IP).</summary>
        public string Policy { get; init; } = "Fixed";

        /// <summary>Peticiones permitidas por ventana.</summary>
        public int PermitLimit { get; init; } = 100;

        /// <summary>Duración de la ventana en segundos.</summary>
        public int WindowSeconds { get; init; } = 60;

        /// <summary>Peticiones que esperan en cola cuando se agota el cupo.</summary>
        public int QueueLimit { get; init; } = 5;
    }
}
