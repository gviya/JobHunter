using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.API.Dto.Auth;
using RenewableEnergy.API.Extensions;
using RenewableEnergy.Application.Features.Auth.Commands.Login;

namespace RenewableEnergy.API.Controllers
{
    /// <summary>
    /// CONTROLLER de autenticación: credenciales → token JWT.
    ///
    /// FLUJO PARA PROBAR LA API (Swagger o Postman):
    /// 1. POST /api/v1/auth/login con demo@renovables.demo / Demo1234!
    ///    (usuario que crea el DatabaseSeeder en desarrollo).
    /// 2. Copia el token de la respuesta.
    /// 3. En Swagger: botón "Authorize" → pegar token.
    ///    En Postman: la colección lo guarda automáticamente en {{token}}.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    public sealed class AuthController(ISender mediator) : ControllerBase
    {
        /// <summary>Valida credenciales y devuelve un JWT (60 min por defecto).</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new LoginCommand(request.Email, request.Password), cancellationToken);

            return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
        }
    }
}
