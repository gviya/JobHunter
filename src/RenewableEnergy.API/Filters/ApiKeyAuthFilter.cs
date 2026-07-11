using Microsoft.AspNetCore.Mvc.Filters;
using RenewableEnergy.Application.Common.Exceptions;

namespace RenewableEnergy.API.Filters
{
    /// <summary>
    /// FILTRO de autenticación por API KEY (cabecera X-Api-Key).
    ///
    /// ¿CUÁNDO API KEY EN VEZ DE JWT? (pregunta de entrevista)
    /// - JWT: usuarios humanos, identidad rica (claims), expira.
    /// - API Key: comunicación MÁQUINA-A-MÁQUINA (webhooks, jobs, endpoints
    ///   de operaciones) donde no hay "usuario" que loguearse.
    ///
    /// USO: decora el controller o la acción con
    ///   [ServiceFilter(typeof(ApiKeyAuthFilter))]
    /// (ver OpsController). La clave válida viene de configuración
    /// ("ApiKey", que en local llega por la variable de entorno API_KEY).
    ///
    /// Lanza ForbiddenException si falta o no coincide: el
    /// ExceptionHandlingMiddleware la traduce a HTTP 403.
    /// </summary>
    public sealed class ApiKeyAuthFilter(IConfiguration configuration) : IAuthorizationFilter
    {
        /// <summary>Nombre de la cabecera esperada.</summary>
        public const string NombreCabecera = "X-Api-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var claveConfigurada = configuration["ApiKey"];

            if (string.IsNullOrWhiteSpace(claveConfigurada))
            {
                throw new ForbiddenException(
                    "El endpoint requiere API Key pero el servidor no tiene ninguna configurada.");
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(NombreCabecera, out var claveRecibida)
                || claveRecibida != claveConfigurada)
            {
                throw new ForbiddenException($"API Key inválida o ausente (cabecera {NombreCabecera}).");
            }
        }
    }
}
