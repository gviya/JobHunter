using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId
{
    /// <summary>
    /// HANDLER del detalle de proyecto. Implementa el patrón CACHE-ASIDE:
    ///
    ///   1. ¿Está en caché? → devolver directamente (ni toca la BD).
    ///   2. No está → leer de BD, mapear y GUARDAR en caché con TTL.
    ///   3. Los commands que modifican el proyecto INVALIDAN la entrada
    ///      (ver ActualizarProyectoHandler y compañía).
    ///
    /// La caché es IDistributedCache: Redis en producción, memoria en
    /// desarrollo/tests. El handler no sabe cuál — otra vez inversión de
    /// dependencias.
    /// </summary>
    internal sealed class ObtenerProyectoPorIdHandler(
        IProyectoRepository proyectoRepository,
        IDistributedCache cache,
        IMapper mapper,
        ILogger<ObtenerProyectoPorIdHandler> logger)
        : IRequestHandler<ObtenerProyectoPorIdQuery, Result<ProyectoDetalleViewModel>>
    {
        /// <summary>Tiempo de vida de la entrada de caché.</summary>
        private static readonly TimeSpan TiempoDeVida = TimeSpan.FromMinutes(2);

        public async Task<Result<ProyectoDetalleViewModel>> Handle(
            ObtenerProyectoPorIdQuery request,
            CancellationToken cancellationToken)
        {
            var claveCache = CacheKeys.ProyectoDetalle(request.Id);

            // 1. Intento de lectura de caché (cache HIT).
            var enCache = await cache.GetStringAsync(claveCache, cancellationToken);

            if (enCache is not null)
            {
                logger.LogDebug("Cache HIT para {ClaveCache}", claveCache);
                return JsonSerializer.Deserialize<ProyectoDetalleViewModel>(enCache)!;
            }

            // 2. Cache MISS: se lee de la base de datos con el agregado completo.
            var proyecto = await proyectoRepository.GetByIdConDetalleAsync(request.Id, cancellationToken);

            if (proyecto is null)
            {
                return Result.Failure<ProyectoDetalleViewModel>(Error.NotFound(
                    "Proyecto.NoEncontrado",
                    $"No existe ningún proyecto con id '{request.Id}'."));
            }

            var viewModel = mapper.Map<ProyectoDetalleViewModel>(proyecto);

            // 3. Se guarda en caché con expiración: si nadie la invalida antes,
            //    caduca sola (protección frente a invalidaciones olvidadas).
            await cache.SetStringAsync(
                claveCache,
                JsonSerializer.Serialize(viewModel),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TiempoDeVida },
                cancellationToken);

            return viewModel;
        }
    }
}
