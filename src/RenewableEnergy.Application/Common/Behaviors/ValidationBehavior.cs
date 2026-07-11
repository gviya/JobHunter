using FluentValidation;
using MediatR;

namespace RenewableEnergy.Application.Common.Behaviors
{
    /// <summary>
    /// PIPELINE BEHAVIOR de MediatR: valida el Command/Query ANTES de que se
    /// ejecute su handler.
    ///
    /// ¿QUÉ ES UN PIPELINE BEHAVIOR?
    /// Un "middleware" del mediador. Cada Send() atraviesa la cadena:
    ///
    ///   Controller → Logging → Performance → Validation → Handler
    ///
    /// Cada behavior decide si continúa (llamando a next()) o corta la
    /// cadena. Así los cross-cutting concerns (validar, loguear, medir) no
    /// se repiten en cada handler: se escriben UNA vez aquí.
    ///
    /// ¿CÓMO ENCUENTRA LOS VALIDATORS?
    /// Por inyección: el contenedor entrega TODOS los IValidator&lt;TRequest&gt;
    /// registrados (AddValidatorsFromAssembly los descubre automáticamente).
    /// Si el request no tiene validator, la colección llega vacía y se pasa
    /// directo al handler.
    /// </summary>
    public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (!validators.Any())
            {
                return await next();
            }

            var contexto = new ValidationContext<TRequest>(request);

            // Se ejecutan TODOS los validators (puede haber varios por request)
            // y se agregan sus errores en una sola respuesta.
            var resultados = await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(contexto, cancellationToken)));

            var errores = resultados
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .Select(f => new Exceptions.ValidationError(f.PropertyName, f.ErrorMessage))
                .ToList();

            if (errores.Count != 0)
            {
                // El handler NUNCA llega a ejecutarse. El middleware de la API
                // convertirá esta excepción en un HTTP 400 con los detalles.
                throw new Exceptions.ValidationException(errores);
            }

            return await next();
        }
    }
}
