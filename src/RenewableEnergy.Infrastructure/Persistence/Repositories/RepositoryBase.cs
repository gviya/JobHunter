using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Base genérica de los repositorios: el CRUD común escrito UNA vez.
    ///
    /// FÍJATE: Add/Delete NO llaman a SaveChanges. Los repositorios solo
    /// marcan cambios en el ChangeTracker; quien confirma la transacción es
    /// el IUnitOfWork al final del caso de uso. Un caso de uso = un commit.
    /// </summary>
    internal abstract class RepositoryBase<TEntity>(ApplicationDbContext context)
        where TEntity : BaseEntity
    {
        /// <summary>El DbContext compartido por todos los repos del scope actual.</summary>
        protected ApplicationDbContext Context { get; } = context;

        /// <summary>Búsqueda por Id con tracking (para poder modificar la entidad).</summary>
        public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await Context.Set<TEntity>()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        /// <summary>Marca la entidad para inserción (pendiente de SaveChanges).</summary>
        public void Add(TEntity entity)
        {
            Context.Set<TEntity>().Add(entity);
        }

        /// <summary>Marca la entidad para borrado (pendiente de SaveChanges).</summary>
        public void Delete(TEntity entity)
        {
            Context.Set<TEntity>().Remove(entity);
        }
    }
}
