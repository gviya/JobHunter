using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Repositorio de SOLO LECTURA de mediciones (lado query de CQRS).
    /// La escritura de mediciones pasa por el agregado ProyectoRenovable.
    /// </summary>
    internal sealed class MedicionRepository(ApplicationDbContext context) : IMedicionRepository
    {
        public async Task<IReadOnlyList<Medicion>> GetByProyectoAsync(
            Guid proyectoId,
            CancellationToken cancellationToken = default)
        {
            return await context.Mediciones
                .AsNoTracking()
                .Where(m => m.ProyectoRenovableId == proyectoId)
                .OrderByDescending(m => m.FechaMedicion)
                .ToListAsync(cancellationToken);
        }
    }
}
