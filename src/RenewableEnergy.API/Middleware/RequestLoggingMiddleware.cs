using System.Diagnostics;

namespace RenewableEnergy.API.Middleware
{
    /// <summary>
    /// MIDDLEWARE de logging de peticiones: método, ruta, código de estado y
    /// duración de CADA request.
    ///
    /// NOTA DIDÁCTICA: Serilog trae UseSerilogRequestLogging() que hace esto
    /// mismo de serie. Se implementa a mano para que veas la anatomía de un
    /// middleware de ASP.NET Core: recibe el RequestDelegate `next`, hace
    /// algo ANTES de la petición, llama a next(), y hace algo DESPUÉS (aquí,
    /// medir y loguear). El orden de registro en el pipeline importa: este
    /// va tras el ExceptionHandlingMiddleware para loguear también el status
    /// final de las peticiones que fallaron.
    /// </summary>
    public sealed class RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var cronometro = Stopwatch.StartNew();

            try
            {
                await next(context);
            }
            finally
            {
                cronometro.Stop();

                logger.LogInformation(
                    "HTTP {Metodo} {Ruta} → {CodigoEstado} en {Milisegundos} ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    cronometro.ElapsedMilliseconds);
            }
        }
    }
}
