using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.Application.Common.Exceptions;
using RenewableEnergy.Domain.Common;

namespace RenewableEnergy.API.Middleware
{
    /// <summary>
    /// MANEJADOR GLOBAL DE ERRORES: la última red de seguridad de la API.
    ///
    /// Envuelve TODO el pipeline en un try/catch y traduce cada excepción a
    /// una respuesta ProblemDetails (RFC 7807), el formato estándar de error
    /// HTTP. Gracias a él:
    /// - Ningún stack trace llega jamás al cliente (seguridad).
    /// - Los controllers no necesitan try/catch (limpieza).
    /// - Cada tipo de excepción tiene SU código HTTP consistente:
    ///     ValidationException → 400 (con la lista de errores)
    ///     NotFoundException   → 404
    ///     ForbiddenException  → 403
    ///     DomainException     → 422 (regla de negocio violada)
    ///     resto               → 500 (mensaje genérico, detalle solo en logs)
    /// </summary>
    public sealed class ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (Exception excepcion)
            {
                logger.LogError(excepcion, "Excepción no controlada: {Mensaje}", excepcion.Message);

                var detalles = TraducirExcepcion(excepcion);

                var problemDetails = new ProblemDetails
                {
                    Status = detalles.Status,
                    Type = detalles.Type,
                    Title = detalles.Title,
                    Detail = detalles.Detail
                };

                if (detalles.Errores is not null)
                {
                    // Los errores campo a campo viajan como extensión del
                    // ProblemDetails: { ..., "errors": [ { propiedad, mensaje } ] }
                    problemDetails.Extensions["errors"] = detalles.Errores;
                }

                context.Response.StatusCode = detalles.Status;
                await context.Response.WriteAsJsonAsync(problemDetails);
            }
        }

        private static DetallesExcepcion TraducirExcepcion(Exception excepcion)
        {
            // SWITCH EXPRESSION con pattern matching: cada tipo de excepción
            // se traduce a su respuesta HTTP.
            return excepcion switch
            {
                ValidationException validacion => new DetallesExcepcion(
                    StatusCodes.Status400BadRequest,
                    "ValidationFailure",
                    "Error de validación",
                    "Se han producido uno o más errores de validación.",
                    validacion.Errors),

                NotFoundException noEncontrado => new DetallesExcepcion(
                    StatusCodes.Status404NotFound,
                    "NotFound",
                    "Recurso no encontrado",
                    noEncontrado.Message,
                    null),

                ForbiddenException prohibido => new DetallesExcepcion(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Acceso denegado",
                    prohibido.Message,
                    null),

                DomainException dominio => new DetallesExcepcion(
                    StatusCodes.Status422UnprocessableEntity,
                    "DomainRuleViolation",
                    "Regla de negocio violada",
                    dominio.Message,
                    null),

                _ => new DetallesExcepcion(
                    StatusCodes.Status500InternalServerError,
                    "ServerError",
                    "Error interno del servidor",
                    "Ha ocurrido un error inesperado. Inténtalo de nuevo más tarde.",
                    null)
            };
        }

        private sealed record DetallesExcepcion(
            int Status,
            string Type,
            string Title,
            string Detail,
            IEnumerable<object>? Errores);
    }
}
