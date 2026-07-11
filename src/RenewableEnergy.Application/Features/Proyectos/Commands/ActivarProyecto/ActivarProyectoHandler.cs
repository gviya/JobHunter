using AutoMapper;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto
{
    /// <summary>
    /// HANDLER de "Activar Proyecto". La transición la ejecuta la ENTIDAD
    /// (proyecto.Activar()): si el proyecto no está en Borrador, el dominio
    /// lanza DomainException y aquí se traduce a Result.Conflict.
    ///
    /// Al activar, la entidad levanta ProyectoActivado: tras SaveChanges el
    /// evento queda en el outbox y el OutboxProcessor lo publicará.
    /// </summary>
    internal sealed class ActivarProyectoHandler(
        IProyectoRepository proyectoRepository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache,
        IMapper mapper,
        ILogger<ActivarProyectoHandler> logger)
        : IRequestHandler<ActivarProyectoCommand, Result<ActivarProyectoResponse>>
    {
        public async Task<Result<ActivarProyectoResponse>> Handle(
            ActivarProyectoCommand request,
            CancellationToken cancellationToken)
        {
            var proyecto = await proyectoRepository.GetByIdAsync(request.Id, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure<ActivarProyectoResponse>(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.Id}'."));
            }

            try
            {
                proyecto.Activar();
            }
            catch (DomainException ex)
            {
                return Result.Failure<ActivarProyectoResponse>(Error.Conflict(
                    "Proyecto.TransicionInvalida", ex.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            await cache.RemoveAsync(CacheKeys.ProyectoDetalle(proyecto.Id), cancellationToken);

            logger.LogInformation("Proyecto {ProyectoId} activado", proyecto.Id);

            return mapper.Map<ActivarProyectoResponse>(proyecto);
        }
    }
}
