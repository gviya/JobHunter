using MediatR;
using Microsoft.Extensions.Logging;

namespace RenewableEnergy.Application.Common.Behaviors
{
    /// <summary>
    /// PIPELINE BEHAVIOR: loguea la entrada y salida de CADA caso de uso.
    ///
    /// Es el behavior MÁS EXTERNO (se registra el primero), de modo que
    /// envuelve a todos los demás y al handler: si algo falla en cualquier
    /// punto, este log lo registra con el nombre del request.
    ///
    /// FÍJATE en el logging ESTRUCTURADO de Serilog: {NombreRequest} no es
    /// interpolación de strings, es una PROPIEDAD indexable. En Seq podrás
    /// filtrar "todos los logs de CrearProyectoCommand" en un clic.
    /// </summary>
    public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var nombreRequest = typeof(TRequest).Name;

            logger.LogInformation("Iniciando caso de uso {NombreRequest}", nombreRequest);

            try
            {
                var respuesta = await next();

                logger.LogInformation("Completado caso de uso {NombreRequest}", nombreRequest);

                return respuesta;
            }
            catch (Exception ex)
            {
                // Se loguea y se RELANZA: este behavior observa, no decide.
                // Quien traduce errores a HTTP es el ExceptionHandlingMiddleware.
                logger.LogError(ex, "Error en caso de uso {NombreRequest}", nombreRequest);
                throw;
            }
        }
    }
}
