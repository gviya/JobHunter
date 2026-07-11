using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Infrastructure.Identity
{
    /// <summary>
    /// EMISOR de tokens JWT (la VALIDACIÓN la configura la API con JwtBearer;
    /// ambos lados comparten JwtOptions para que issuer/audience/clave casen).
    ///
    /// ANATOMÍA DEL TOKEN QUE EMITE:
    /// - sub / NameIdentifier → Id del usuario (lo lee CurrentUserService)
    /// - email               → email del usuario
    /// - name                → nombre para mostrar
    /// - exp                 → expiración (ahora + ExpirationInMinutes)
    /// Firmado con HMAC-SHA256 usando la clave secreta de configuración.
    /// </summary>
    internal sealed class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
    {
        public (string Token, DateTime ExpiraEnUtc) GenerarToken(Usuario usuario)
        {
            var opciones = options.Value;

            var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.Secret));
            var credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);
            var expiraEnUtc = DateTime.UtcNow.AddMinutes(opciones.ExpirationInMinutes);

            List<Claim> claims =
            [
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, usuario.Email.Valor),
                new(JwtRegisteredClaimNames.Name, usuario.Nombre),
                // Jti: id único del token (permite revocación/blacklisting).
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ];

            var token = new JwtSecurityToken(
                issuer: opciones.Issuer,
                audience: opciones.Audience,
                claims: claims,
                expires: expiraEnUtc,
                signingCredentials: credenciales);

            return (new JwtSecurityTokenHandler().WriteToken(token), expiraEnUtc);
        }
    }
}
