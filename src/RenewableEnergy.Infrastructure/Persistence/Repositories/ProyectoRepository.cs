using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.Specifications;

namespace RenewableEnergy.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// IMPLEMENTACIÓN del contrato IProyectoRepository (definido en Domain).
    /// Aquí SÍ vive EF Core: Includes, AsNoTracking, paginación con SQL...
    /// Los handlers nunca ven este código: programan contra la interfaz.
    /// </summary>
    internal sealed class ProyectoRepository(ApplicationDbContext context)
        : RepositoryBase<ProyectoRenovable>(context), IProyectoRepository
    {
        public async Task<ProyectoRenovable?> GetByIdConDetalleAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            // Include: carga EAGER de las colecciones hijas del agregado.
            // Sin esto, Mediciones/Instalaciones llegarían vacías.
            return await Context.Proyectos
                .Include(p => p.Mediciones)
                .Include(p => p.Instalaciones)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<ProyectoRenovable>> GetBySpecificationAsync(
            Specification<ProyectoRenovable> specification,
            CancellationToken cancellationToken = default)
        {
            // La Specification se traduce a SQL: el filtro ocurre en la BD,
            // no en memoria. AsNoTracking porque es lectura pura (más rápido).
            return await Context.Proyectos
                .AsNoTracking()
                .Where(specification.ToExpression())
                .ToListAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<ProyectoRenovable> Items, int Total)> GetPaginadoAsync(
            Specification<ProyectoRenovable>? specification,
            int pagina,
            int tamanoPagina,
            CancellationToken cancellationToken = default)
        {
            var query = Context.Proyectos.AsNoTracking();

            if (specification is not null)
            {
                query = query.Where(specification.ToExpression());
            }

            // Dos consultas: COUNT para el total y la página de datos.
            var total = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public async Task<bool> ExisteNombreAsync(
            string nombre,
            Guid? excluirId = null,
            CancellationToken cancellationToken = default)
        {
            return await Context.Proyectos
                .AsNoTracking()
                .AnyAsync(
                    p => p.Nombre == nombre && (excluirId == null || p.Id != excluirId),
                    cancellationToken);
        }
    }
}
