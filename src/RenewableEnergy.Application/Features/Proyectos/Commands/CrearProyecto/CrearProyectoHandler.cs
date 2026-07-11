using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto
{
    /// <summary>
    /// HANDLER del caso de uso "Crear Proyecto". Aquí vive la ORQUESTACIÓN:
    /// comprobar unicidad → construir el agregado → persistir → responder.
    ///
    /// FÍJATE EN LO QUE *NO* HACE:
    /// - No valida la entrada (eso ya lo hizo ValidationBehavior).
    /// - No contiene reglas de negocio (viven en ProyectoRenovable.Create).
    /// - No conoce EF Core (habla con IProyectoRepository/IUnitOfWork).
    /// - No conoce HTTP (devuelve Result, no códigos de estado).
    ///
    /// Usa PRIMARY CONSTRUCTOR (C# 12+): las dependencias se declaran en la
    /// propia clase, sin el boilerplate de campos + constructor clásico.
    /// Es `internal sealed`: nadie fuera de Application lo invoca directamente
    /// (siempre a través de MediatR) y nadie hereda de él.
    /// </summary>
    internal sealed class CrearProyectoHandler(
        IProyectoRepository proyectoRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ILogger<CrearProyectoHandler> logger)
        : IRequestHandler<CrearProyectoCommand, Result<CrearProyectoResponse>>
    {
        public async Task<Result<CrearProyectoResponse>> Handle(
            CrearProyectoCommand request,
            CancellationToken cancellationToken)
        {
            // Regla de negocio esperable → patrón Result (no excepción):
            // el nombre del proyecto debe ser único en el sistema.
            if (await proyectoRepository.ExisteNombreAsync(request.Nombre, cancellationToken: cancellationToken))
            {
                logger.LogInformation("Intento de crear proyecto con nombre duplicado: {Nombre}", request.Nombre);

                return Result.Failure<CrearProyectoResponse>(Error.Conflict(
                    "Proyecto.NombreDuplicado",
                    $"Ya existe un proyecto con el nombre '{request.Nombre}'."));
            }

            // Los VALUE OBJECTS se construyen con sus factories: si llegara un
            // valor imposible (no debería: el validator ya filtró), el dominio
            // lanzaría DomainException — última línea de defensa.
            var potencia = PotenciaKW.Create(request.PotenciaKW);
            var ubicacion = Coordenadas.Create(request.Latitud, request.Longitud);

            // El AGREGADO nace por su factory. Internamente levanta el evento
            // ProyectoCreado, que el interceptor de EF copiará al outbox al
            // hacer SaveChanges (patrón Outbox: BD y evento, misma transacción).
            var proyecto = ProyectoRenovable.Create(
                request.Nombre,
                request.Tipo,
                potencia,
                ubicacion,
                currentUserService.UserId,
                request.Descripcion);

            proyectoRepository.Add(proyecto);

            // UNA transacción por caso de uso: el único SaveChanges está aquí.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Proyecto {ProyectoId} '{Nombre}' creado", proyecto.Id, proyecto.Nombre);

            // Entity -> DTO con AutoMapper. La conversión implícita de
            // Result<T> convierte la respuesta en Result.Success(...).
            return mapper.Map<CrearProyectoResponse>(proyecto);
        }
    }
}
