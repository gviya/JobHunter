using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace RenewableEnergy.Application.Common.Behaviors
{
    /// <summary>
    /// PIPELINE BEHAVIOR: cronometra cada caso de uso y AVISA (warning) si
    /// tarda más de <see cref="UmbralMilisegundos"/>.
    ///
    /// ¿POR QUÉ EXISTE?
    /// Detección TEMPRANA de degradación: una query que empieza a tardar
    /// 800 ms (falta un índice, N+1...) aparece en los logs como warning
    /// mucho antes de que un cliente se queje. Con Serilog + Seq puedes
    /// crear una alerta sobre estos warnings.
    /// </summary>
    public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        /// <summary>Umbral a partir del cual un caso de uso se considera lento.</summary>
        private const int UmbralMilisegundos = 500;

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var cronometro = Stopwatch.StartNew();

            var respuesta = await next();

            cronometro.Stop();

            if (cronometro.ElapsedMilliseconds > UmbralMilisegundos)
            {
                logger.LogWarning(
                    "Caso de uso LENTO: {NombreRequest} ha tardado {Milisegundos} ms (umbral: {Umbral} ms)",
                    typeof(TRequest).Name,
                    cronometro.ElapsedMilliseconds,
                    UmbralMilisegundos);
            }

            return respuesta;
        }
    }
}
