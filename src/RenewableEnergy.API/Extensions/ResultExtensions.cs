using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.API.Extensions
{
    /// <summary>
    /// Traducción del PATRÓN RESULT a HTTP: el único punto donde el
    /// ErrorType de Application se convierte en códigos de estado.
    ///
    /// Application dice "Conflict" (lenguaje de negocio); esta extensión
    /// dice "409" (lenguaje HTTP). Si mañana la app tuviera un frontal gRPC,
    /// tendría su propia traducción sin tocar Application.
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>Convierte un Result fallido en ProblemDetails con su código HTTP.</summary>
        public static IActionResult ToProblem(this Result result)
        {
            if (result.IsSuccess)
            {
                throw new InvalidOperationException("No se puede convertir un resultado de éxito en problema.");
            }

            var error = result.Error!;

            var statusCode = error.Tipo switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };

            // Caso especial: credenciales inválidas es 401 (no autenticado),
            // no 403 (autenticado sin permisos).
            if (error.Codigo == "Auth.CredencialesInvalidas")
            {
                statusCode = StatusCodes.Status401Unauthorized;
            }

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = error.Codigo,
                Detail = error.Mensaje
            };

            return new ObjectResult(problemDetails) { StatusCode = statusCode };
        }
    }
}
