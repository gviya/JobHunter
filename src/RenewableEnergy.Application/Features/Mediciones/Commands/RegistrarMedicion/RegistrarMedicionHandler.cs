using AutoMapper;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion
{
    /// <summary>
    /// HANDLER de "Registrar Medición". PUNTO CLAVE de DDD: la medición NO se
    /// crea con "new Medicion(...)" (su factory es internal, ni siquiera
    /// compilaría) — se pide al AGREGADO: proyecto.RegistrarMedicion(...).
    /// Es la raíz quien valida "solo proyectos activos miden" y levanta el
    /// evento MedicionRegistrada.
    /// </summary>
    internal sealed class RegistrarMedicionHandler(
        IProyectoRepository proyectoRepository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache,
        IMapper mapper,
        ILogger<RegistrarMedicionHandler> logger)
        : IRequestHandler<RegistrarMedicionCommand, Result<RegistrarMedicionResponse>>
    {
        public async Task<Result<RegistrarMedicionResponse>> Handle(
            RegistrarMedicionCommand request,
            CancellationToken cancellationToken)
        {
            var proyecto = await proyectoRepository.GetByIdAsync(request.ProyectoId, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure<RegistrarMedicionResponse>(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.ProyectoId}'."));
            }

            Domain.Entities.Medicion medicion;

            try
            {
                // El agregado aplica sus reglas y devuelve la medición creada.
                medicion = proyecto.RegistrarMedicion(request.FechaMedicion, request.EnergiaGeneradaKWh);
            }
            catch (DomainException ex)
            {
                return Result.Failure<RegistrarMedicionResponse>(Error.Conflict(
                    "Medicion.OperacionInvalida", ex.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // El detalle del proyecto incluye sus mediciones: invalidar caché.
            await cache.RemoveAsync(CacheKeys.ProyectoDetalle(proyecto.Id), cancellationToken);

            logger.LogInformation(
                "Medición {MedicionId} registrada en el proyecto {ProyectoId}: {Energia} kWh",
                medicion.Id, proyecto.Id, medicion.EnergiaGeneradaKWh);

            return mapper.Map<RegistrarMedicionResponse>(medicion);
        }
    }
}
