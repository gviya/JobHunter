using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto
{
    /// <summary>
    /// COMMAND: borrado físico de un proyecto.
    ///
    /// Devuelve Result SIN valor (IRequest&lt;Result&gt;): un DELETE con éxito
    /// no tiene nada que devolver — la API responderá 204 No Content.
    /// </summary>
    public sealed record EliminarProyectoCommand(Guid Id) : IRequest<Result>;
}
