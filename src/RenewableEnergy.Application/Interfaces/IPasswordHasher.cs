namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Abstracción del hasheo de contraseñas. La implementación vive en
    /// Infrastructure/Identity (y en esta plantilla es un SHA-256 didáctico;
    /// en producción usarías BCrypt/Argon2 o ASP.NET Core Identity).
    /// Gracias a la interfaz, cambiar el algoritmo no toca ningún handler.
    /// </summary>
    public interface IPasswordHasher
    {
        /// <summary>Calcula el hash de una contraseña en claro.</summary>
        string Hash(string password);

        /// <summary>Comprueba si una contraseña corresponde a un hash.</summary>
        bool Verificar(string password, string hash);
    }
}
