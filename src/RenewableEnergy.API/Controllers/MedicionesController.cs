using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.API.Dto.Mediciones;
using RenewableEnergy.API.Extensions;
using RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion;
using RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones;

namespace RenewableEnergy.API.Controllers
{
    /// <summary>
    /// CONTROLLER de mediciones como RECURSO ANIDADO REST:
    /// /api/v1/proyectos/{proyectoId}/mediciones — la URL ya expresa que una
    /// medición no existe sin su proyecto (igual que en el dominio, donde
    /// Medicion es hija del agregado ProyectoRenovable).
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/proyectos/{proyectoId:guid}/mediciones")]
    public sealed class MedicionesController(ISender mediator) : ControllerBase
    {
        /// <summary>Mediciones del proyecto, más recientes primero.</summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IReadOnlyList<MedicionViewModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ObtenerMediciones(Guid proyectoId, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ObtenerMedicionesQuery(proyectoId), cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }

        /// <summary>Registra una lectura de energía. El proyecto debe estar Activo. Requiere JWT.</summary>
        [HttpPost]
        [Authorize]
        [ProducesResponseType(typeof(RegistrarMedicionResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RegistrarMedicion(
            Guid proyectoId,
            [FromBody] RegistrarMedicionRequest request,
            CancellationToken cancellationToken)
        {
            var command = new RegistrarMedicionCommand(
                proyectoId,
                request.FechaMedicion,
                request.EnergiaGeneradaKWh);

            var result = await mediator.Send(command, cancellationToken);

            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            return CreatedAtAction(
                nameof(ObtenerMediciones),
                new { proyectoId, version = "1" },
                result.Value);
        }
    }
}
