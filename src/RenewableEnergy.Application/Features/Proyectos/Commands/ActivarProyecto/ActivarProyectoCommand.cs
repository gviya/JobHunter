using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto
{
    /// <summary>
    /// COMMAND: transición de estado Borrador → Activo.
    ///
    /// Fíjate en que NO es un "PUT estado=Activo" genérico: las transiciones
    /// de estado se modelan como OPERACIONES DE NEGOCIO con nombre propio
    /// (POST /api/v1/proyectos/{id}/activar). Así la API expresa intención
    /// y el dominio puede validar cada transición por separado.
    /// </summary>
    public sealed record ActivarProyectoCommand(Guid Id) : IRequest<Result<ActivarProyectoResponse>>;
}
