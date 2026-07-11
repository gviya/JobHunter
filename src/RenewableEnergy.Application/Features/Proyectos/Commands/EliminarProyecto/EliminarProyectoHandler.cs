using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto
{
    /// <summary>
    /// HANDLER de "Eliminar Proyecto". La regla "¿se puede borrar?" NO está
    /// aquí: la expone la entidad (<c>proyecto.PuedeSerEliminado</c>). Solo
    /// los proyectos que nunca produjeron (Borrador/Cancelado) se borran
    /// físicamente; el resto conserva su histórico de mediciones.
    /// </summary>
    internal sealed class EliminarProyectoHandler(
        IProyectoRepository proyectoRepository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache,
        ILogger<EliminarProyectoHandler> logger)
        : IRequestHandler<EliminarProyectoCommand, Result>
    {
        public async Task<Result> Handle(EliminarProyectoCommand request, CancellationToken cancellationToken)
        {
            var proyecto = await proyectoRepository.GetByIdAsync(request.Id, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.Id}'."));
            }

            if (!proyecto.PuedeSerEliminado)
            {
                return Result.Failure(Error.Conflict(
                    "Proyecto.NoEliminable",
                    $"El proyecto '{proyecto.Nombre}' está en estado {proyecto.Estado} y no puede eliminarse. " +
                    "Solo se eliminan proyectos en Borrador o Cancelados."));
            }

            proyectoRepository.Delete(proyecto);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await cache.RemoveAsync(CacheKeys.ProyectoDetalle(proyecto.Id), cancellationToken);

            logger.LogInformation("Proyecto {ProyectoId} '{Nombre}' eliminado", proyecto.Id, proyecto.Nombre);

            return Result.Success();
        }
    }
}
