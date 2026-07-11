using AutoMapper;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto
{
    /// <summary>
    /// HANDLER de "Actualizar Proyecto". Muestra dos técnicas importantes:
    ///
    /// 1. TRADUCCIÓN DomainException → Result: la entidad defiende sus
    ///    invariantes lanzando; el handler, que sabe que "intentar editar un
    ///    proyecto completado" es un flujo esperable de la API, lo captura y
    ///    lo convierte en un Result.Conflict con código estable.
    ///
    /// 2. INVALIDACIÓN DE CACHÉ: el detalle del proyecto se cachea en
    ///    ObtenerProyectoPorIdHandler; cualquier command que lo modifique
    ///    debe borrar esa entrada o los clientes verían datos obsoletos.
    /// </summary>
    internal sealed class ActualizarProyectoHandler(
        IProyectoRepository proyectoRepository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache,
        IMapper mapper,
        ILogger<ActualizarProyectoHandler> logger)
        : IRequestHandler<ActualizarProyectoCommand, Result<ActualizarProyectoResponse>>
    {
        public async Task<Result<ActualizarProyectoResponse>> Handle(
            ActualizarProyectoCommand request,
            CancellationToken cancellationToken)
        {
            var proyecto = await proyectoRepository.GetByIdAsync(request.Id, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure<ActualizarProyectoResponse>(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.Id}'."));
            }

            // Unicidad de nombre, excluyendo el propio proyecto.
            if (await proyectoRepository.ExisteNombreAsync(request.Nombre, proyecto.Id, cancellationToken))
            {
                return Result.Failure<ActualizarProyectoResponse>(Error.Conflict(
                    "Proyecto.NombreDuplicado",
                    $"Ya existe otro proyecto con el nombre '{request.Nombre}'."));
            }

            try
            {
                var potencia = PotenciaKW.Create(request.PotenciaKW);
                var ubicacion = Coordenadas.Create(request.Latitud, request.Longitud);

                // La REGLA ("los proyectos terminales no se editan") vive en la
                // entidad, no aquí. El handler solo orquesta.
                proyecto.Actualizar(request.Nombre, request.Descripcion, potencia, ubicacion);
            }
            catch (DomainException ex)
            {
                return Result.Failure<ActualizarProyectoResponse>(Error.Conflict(
                    "Proyecto.OperacionInvalida", ex.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // El detalle cacheado quedó obsoleto: se elimina la entrada.
            await cache.RemoveAsync(CacheKeys.ProyectoDetalle(proyecto.Id), cancellationToken);

            logger.LogInformation("Proyecto {ProyectoId} actualizado", proyecto.Id);

            return mapper.Map<ActualizarProyectoResponse>(proyecto);
        }
    }
}
