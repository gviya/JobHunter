using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.API.Dto.Proyectos;
using RenewableEnergy.API.Extensions;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto;
using RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.API.Controllers
{
    /// <summary>
    /// CONTROLLER de proyectos renovables. Fíjate en lo FINO que es: cada
    /// acción construye un Command/Query, lo envía por MediatR y traduce el
    /// Result a HTTP. CERO lógica de negocio (regla verificada por los tests
    /// de arquitectura: los controllers no tocan repositorios ni entidades).
    ///
    /// Seguridad: las LECTURAS son públicas; las ESCRITURAS exigen JWT
    /// (obtenlo en POST /api/v1/auth/login con el usuario demo).
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/proyectos")]
    public sealed class ProyectosController(ISender mediator) : ControllerBase
    {
        /// <summary>Listado paginado con filtros opcionales.</summary>
        /// <remarks>GET /api/v1/proyectos?pagina=1&amp;tamanoPagina=10&amp;tipo=Solar&amp;soloActivos=true</remarks>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(PaginatedList<ProyectoListadoViewModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ObtenerProyectos(
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 10,
            [FromQuery] TipoEnergia? tipo = null,
            [FromQuery] bool soloActivos = false,
            CancellationToken cancellationToken = default)
        {
            var query = new ObtenerProyectosQuery(pagina, tamanoPagina, tipo, soloActivos);

            var result = await mediator.Send(query, cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }

        /// <summary>Detalle completo de un proyecto (con mediciones e instalaciones).</summary>
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ProyectoDetalleViewModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ObtenerProyectoPorId(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ObtenerProyectoPorIdQuery(id), cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }

        /// <summary>Crea un proyecto (nace en estado Borrador). Requiere JWT.</summary>
        [HttpPost]
        [Authorize]
        [ProducesResponseType(typeof(CrearProyectoResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CrearProyecto(
            [FromBody] CrearProyectoRequest request,
            CancellationToken cancellationToken)
        {
            // El controller TRADUCE el DTO público al command interno.
            var command = new CrearProyectoCommand(
                request.Nombre,
                request.Tipo,
                request.PotenciaKW,
                request.Coordenadas.Latitud,
                request.Coordenadas.Longitud,
                request.Descripcion);

            var result = await mediator.Send(command, cancellationToken);

            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            // 201 Created + cabecera Location apuntando al detalle (REST).
            return CreatedAtAction(
                nameof(ObtenerProyectoPorId),
                new { id = result.Value.Id, version = "1" },
                result.Value);
        }

        /// <summary>Actualiza los datos editables de un proyecto. Requiere JWT.</summary>
        [HttpPut("{id:guid}")]
        [Authorize]
        [ProducesResponseType(typeof(ActualizarProyectoResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ActualizarProyecto(
            Guid id,
            [FromBody] ActualizarProyectoRequest request,
            CancellationToken cancellationToken)
        {
            // El Id de la RUTA + el body del DTO forman el command completo.
            var command = new ActualizarProyectoCommand(
                id,
                request.Nombre,
                request.PotenciaKW,
                request.Coordenadas.Latitud,
                request.Coordenadas.Longitud,
                request.Descripcion);

            var result = await mediator.Send(command, cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }

        /// <summary>Transición Borrador → Activo. Operación de negocio explícita. Requiere JWT.</summary>
        [HttpPost("{id:guid}/activar")]
        [Authorize]
        [ProducesResponseType(typeof(ActivarProyectoResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ActivarProyecto(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ActivarProyectoCommand(id), cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }

        /// <summary>Elimina un proyecto (solo Borrador/Cancelado). Requiere JWT.</summary>
        [HttpDelete("{id:guid}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> EliminarProyecto(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new EliminarProyectoCommand(id), cancellationToken);

            return result.IsSuccess ? NoContent() : result.ToProblem();
        }
    }
}
