using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Infrastructure.Persistence
{
    /// <summary>
    /// Implementación del UNIT OF WORK sobre EF Core.
    ///
    /// EF Core YA implementa el patrón internamente (el DbContext rastrea
    /// cambios y SaveChanges los confirma en una transacción). Esta clase es
    /// un ADAPTADOR fino que lo expone tras la interfaz del dominio, de modo
    /// que Application no dependa de EF.
    ///
    /// Al compartir todos los repositorios y este UnitOfWork el MISMO
    /// DbContext (scoped por request), un SaveChangesAsync confirma TODO el
    /// trabajo del caso de uso de una vez — incluidos los OutboxMessages que
    /// añadió el interceptor.
    /// </summary>
    internal sealed class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
    }
}
