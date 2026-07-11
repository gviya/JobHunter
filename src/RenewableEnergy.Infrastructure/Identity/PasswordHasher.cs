using System.Security.Cryptography;
using System.Text;
using RenewableEnergy.Application.Interfaces;

namespace RenewableEnergy.Infrastructure.Identity
{
    /// <summary>
    /// Implementación DIDÁCTICA de IPasswordHasher con SHA-256.
    ///
    /// ⚠️ ADVERTENCIA DE SEGURIDAD (y tema de entrevista): SHA-256 "a pelo"
    /// NO es adecuado para contraseñas en producción porque es demasiado
    /// rápido (facilita ataques de fuerza bruta) y no lleva salt. En un
    /// producto real usarías BCrypt, Argon2 o el PasswordHasher de ASP.NET
    /// Core Identity (PBKDF2 con salt e iteraciones). Se implementa así para
    /// que el flujo sea legible sin dependencias extra — cambiarlo solo
    /// requiere tocar esta clase gracias a la interfaz.
    /// </summary>
    internal sealed class PasswordHasher : IPasswordHasher
    {
        public string Hash(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));

            return Convert.ToHexString(bytes);
        }

        public bool Verificar(string password, string hash)
        {
            // Comparación en TIEMPO CONSTANTE: evita "timing attacks" en los
            // que el atacante deduce el hash midiendo cuánto tarda en fallar.
            var candidato = Encoding.UTF8.GetBytes(Hash(password));
            var esperado = Encoding.UTF8.GetBytes(hash);

            return CryptographicOperations.FixedTimeEquals(candidato, esperado);
        }
    }
}
