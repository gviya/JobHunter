using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Infrastructure.Persistence.Repositories
{
    /// <summary>Implementación EF Core del repositorio de usuarios.</summary>
    internal sealed class UsuarioRepository(ApplicationDbContext context)
        : RepositoryBase<Usuario>(context), IUsuarioRepository
    {
        public async Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            // El VO Email tiene un value converter a string: EF traduce la
            // igualdad "propiedad == parámetro" aplicando el converter al
            // parámetro, así que la comparación ocurre en SQL.
            var emailVo = Domain.ValueObjects.Email.Create(email);

            return await Context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == emailVo, cancellationToken);
        }
    }
}
