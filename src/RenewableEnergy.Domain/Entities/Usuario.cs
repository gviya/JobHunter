using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Entities
{
    /// <summary>
    /// AGREGADO que representa a un usuario de la plataforma.
    ///
    /// Existe para demostrar dos cosas:
    /// 1. El Value Object <see cref="Email"/> integrado en una entidad
    ///    (y persistido como columna con conversión en Fluent API).
    /// 2. La autenticación JWT de la API (AuthController valida credenciales
    ///    contra esta entidad y emite el token).
    ///
    /// NOTA DIDÁCTICA: en un producto real la identidad se delega en
    /// ASP.NET Core Identity, Auth0, Entra ID... Aquí se implementa a mano
    /// (hash SHA-256) SOLO para que el flujo completo sea visible y estudiable.
    /// </summary>
    public sealed class Usuario : AggregateRoot
    {
        /// <summary>Nombre completo para mostrar.</summary>
        public string Nombre { get; private set; } = string.Empty;

        /// <summary>Email de acceso. Value Object: siempre válido y normalizado.</summary>
        public Email Email { get; private set; } = null!;

        /// <summary>Hash de la contraseña. NUNCA se guarda la contraseña en claro.</summary>
        public string PasswordHash { get; private set; } = string.Empty;

        /// <summary>Permite desactivar usuarios sin borrarlos (soft-disable).</summary>
        public bool Activo { get; private set; }

        // Constructor privado para EF Core.
        private Usuario()
        {
        }

        /// <summary>Factory: único punto de creación de usuarios.</summary>
        public static Usuario Create(string nombre, Email email, string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new DomainException("El nombre del usuario es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new DomainException("El hash de contraseña es obligatorio.");
            }

            return new Usuario
            {
                Nombre = nombre.Trim(),
                Email = email,
                PasswordHash = passwordHash,
                Activo = true
            };
        }

        /// <summary>Desactiva el acceso del usuario sin destruir su histórico.</summary>
        public void Desactivar()
        {
            Activo = false;
        }
    }
}
