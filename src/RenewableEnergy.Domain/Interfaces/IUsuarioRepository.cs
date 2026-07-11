using RenewableEnergy.Domain.Entities;

namespace RenewableEnergy.Domain.Interfaces
{
    /// <summary>
    /// Contrato del repositorio de usuarios. Lo consume el flujo de
    /// autenticación (login → validar credenciales → emitir JWT).
    /// </summary>
    public interface IUsuarioRepository
    {
        /// <summary>Busca un usuario por email normalizado. Null si no existe.</summary>
        Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>Marca el usuario para inserción (guarda el IUnitOfWork).</summary>
        void Add(Usuario usuario);
    }
}
