using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones
{
    /// <summary>QUERY: mediciones de un proyecto, más recientes primero.</summary>
    public sealed record ObtenerMedicionesQuery(Guid ProyectoId)
        : IRequest<Result<IReadOnlyList<MedicionViewModel>>>;
}
