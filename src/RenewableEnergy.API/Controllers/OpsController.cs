using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using RenewableEnergy.API.Filters;

namespace RenewableEnergy.API.Controllers
{
    /// <summary>
    /// CONTROLLER de operaciones internas: demuestra el ApiKeyAuthFilter.
    /// No usa JWT — está pensado para llamadas MÁQUINA-A-MÁQUINA
    /// (monitorización, scripts de operaciones) que se autentican con la
    /// cabecera X-Api-Key.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/ops")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public sealed class OpsController(IWebHostEnvironment environment) : ControllerBase
    {
        /// <summary>Información del despliegue. Requiere cabecera X-Api-Key.</summary>
        [HttpGet("info")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public IActionResult ObtenerInfo()
        {
            return Ok(new
            {
                Aplicacion = "RenewableEnergy API",
                Entorno = environment.EnvironmentName,
                Version = typeof(OpsController).Assembly.GetName().Version?.ToString() ?? "desconocida",
                HoraServidorUtc = DateTime.UtcNow
            });
        }
    }
}
