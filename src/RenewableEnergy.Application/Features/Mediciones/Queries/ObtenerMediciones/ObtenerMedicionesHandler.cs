using AutoMapper;
using MediatR;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones
{
    /// <summary>
    /// HANDLER del listado de mediciones. Lado QUERY puro: usa el repositorio
    /// de solo lectura IMedicionRepository sin cargar el agregado entero.
    /// </summary>
    internal sealed class ObtenerMedicionesHandler(
        IProyectoRepository proyectoRepository,
        IMedicionRepository medicionRepository,
        IMapper mapper)
        : IRequestHandler<ObtenerMedicionesQuery, Result<IReadOnlyList<MedicionViewModel>>>
    {
        public async Task<Result<IReadOnlyList<MedicionViewModel>>> Handle(
            ObtenerMedicionesQuery request,
            CancellationToken cancellationToken)
        {
            // Distinguir "proyecto sin mediciones" (200 + lista vacía) de
            // "proyecto inexistente" (404) es parte del contrato de la API.
            var proyecto = await proyectoRepository.GetByIdAsync(request.ProyectoId, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure<IReadOnlyList<MedicionViewModel>>(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.ProyectoId}'."));
            }

            var mediciones = await medicionRepository.GetByProyectoAsync(request.ProyectoId, cancellationToken);

            IReadOnlyList<MedicionViewModel> viewModels = mapper.Map<List<MedicionViewModel>>(mediciones);

            return Result.Success(viewModels);
        }
    }
}
