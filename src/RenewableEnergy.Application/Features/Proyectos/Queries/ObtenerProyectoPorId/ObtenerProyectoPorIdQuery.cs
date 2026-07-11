using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId
{
    /// <summary>QUERY: detalle completo de un proyecto (con mediciones e instalaciones).</summary>
    public sealed record ObtenerProyectoPorIdQuery(Guid Id) : IRequest<Result<ProyectoDetalleViewModel>>;
}
