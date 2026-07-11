using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Abstracción de la emisión de tokens JWT. Application decide CUÁNDO
    /// emitir un token (LoginHandler); Infrastructure sabe CÓMO firmarlo
    /// (clave, issuer, audience, expiración — ver Identity/JwtTokenGenerator).
    /// </summary>
    public interface IJwtTokenGenerator
    {
        /// <summary>Genera un JWT firmado para el usuario. Devuelve el token y su expiración UTC.</summary>
        (string Token, DateTime ExpiraEnUtc) GenerarToken(Usuario usuario);
    }
}
