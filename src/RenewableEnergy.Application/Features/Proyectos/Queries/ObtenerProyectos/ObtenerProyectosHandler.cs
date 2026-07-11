using AutoMapper;
using MediatR;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.Specifications;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos
{
    /// <summary>
    /// HANDLER del listado de proyectos. Demuestra el PATRÓN SPECIFICATION
    /// en acción: los filtros de la query se traducen a Specifications con
    /// nombre de negocio, combinables con And(). El SQL final lo genera
    /// EF Core en Infrastructure a partir de la expresión.
    /// </summary>
    internal sealed class ObtenerProyectosHandler(
        IProyectoRepository proyectoRepository,
        IMapper mapper)
        : IRequestHandler<ObtenerProyectosQuery, Result<PaginatedList<ProyectoListadoViewModel>>>
    {
        public async Task<Result<PaginatedList<ProyectoListadoViewModel>>> Handle(
            ObtenerProyectosQuery request,
            CancellationToken cancellationToken)
        {
            // Composición de Specifications según los filtros recibidos.
            // null = sin filtro (el repositorio devuelve todo paginado).
            Specification<ProyectoRenovable>? spec = null;

            if (request.Tipo.HasValue)
            {
                spec = new ProyectosPorTipoSpec(request.Tipo.Value);
            }

            if (request.SoloActivos)
            {
                var activos = new ProyectosActivosSpec();
                spec = spec is null ? activos : spec.And(activos);
            }

            var (items, total) = await proyectoRepository.GetPaginadoAsync(
                spec,
                request.Pagina,
                request.TamanoPagina,
                cancellationToken);

            var viewModels = mapper.Map<List<ProyectoListadoViewModel>>(items);

            return new PaginatedList<ProyectoListadoViewModel>(
                viewModels,
                total,
                request.Pagina,
                request.TamanoPagina);
        }
    }
}
