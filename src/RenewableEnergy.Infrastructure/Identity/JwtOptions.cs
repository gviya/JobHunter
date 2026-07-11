namespace RenewableEnergy.Infrastructure.Identity
{
    /// <summary>
    /// Opciones del JWT, bindeadas desde la sección "Jwt" de appsettings.
    /// El SECRET nunca va en appsettings.json: llega por variable de entorno
    /// (JWT_SECRET) en local/Docker o desde Azure Key Vault en producción.
    /// </summary>
    public sealed class JwtOptions
    {
        /// <summary>Nombre de la sección de configuración.</summary>
        public const string SectionName = "Jwt";

        /// <summary>Quién emite el token (esta API).</summary>
        public string Issuer { get; init; } = string.Empty;

        /// <summary>Para quién es válido el token (los clientes de esta API).</summary>
        public string Audience { get; init; } = string.Empty;

        /// <summary>Clave de firma HMAC-SHA256. Mínimo 32 caracteres.</summary>
        public string Secret { get; init; } = string.Empty;

        /// <summary>Minutos de validez del token.</summary>
        public int ExpirationInMinutes { get; init; } = 60;
    }
}
