using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// INTERCEPTOR de EF Core que rellena la AUDITORÍA automáticamente:
    /// - Entidad nueva (Added)     → CreatedAt = ahora (UTC)
    /// - Entidad editada (Modified) → UpdatedAt = ahora (UTC)
    ///
    /// ¿POR QUÉ UN INTERCEPTOR?
    /// La alternativa sería que cada handler recordara poner las fechas — y
    /// alguno lo olvidaría. El interceptor se engancha al SaveChanges del
    /// DbContext y lo hace SIEMPRE, para TODAS las entidades que hereden de
    /// BaseEntity. El dominio ni se entera (sus setters son privados; aquí
    /// se escriben a través del ChangeTracker de EF).
    /// </summary>
    public sealed class AuditInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            AplicarAuditoria(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            AplicarAuditoria(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void AplicarAuditoria(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            var ahoraUtc = DateTime.UtcNow;

            foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Se escribe a través de EF, no de la entidad: el setter
                    // es privado a propósito (el dominio no gestiona auditoría).
                    entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = ahoraUtc;
                }

                if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = ahoraUtc;
                }
            }
        }
    }
}
