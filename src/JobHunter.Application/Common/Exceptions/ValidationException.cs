namespace JobHunter.Application.Common.Exceptions
{
    /// <summary>Un error de validación concreto: campo + mensaje.</summary>
    public sealed record ValidationError(string Propiedad, string Mensaje);

    /// <summary>
    /// Excepción que lanza el <c>ValidationBehavior</c> del pipeline de MediatR
    /// cuando un Command/Query no pasa sus reglas de FluentValidation.
    ///
    /// FLUJO COMPLETO:
    /// 1. El controller envía el command con _mediator.Send(...).
    /// 2. ValidationBehavior ejecuta los validators ANTES del handler.
    /// 3. Si hay errores, lanza ESTA excepción (el handler nunca se ejecuta).
    /// 4. ExceptionHandlingMiddleware la captura y responde HTTP 400 con la
    ///    lista de errores en formato ProblemDetails.
    ///
    /// El handler puede asumir que el command YA es válido: separación clara
    /// entre validación de entrada (aquí) y reglas de negocio (dominio).
    /// </summary>
    public sealed class ValidationException(IEnumerable<ValidationError> errors)
        : Exception("Se han producido uno o más errores de validación.")
    {
        /// <summary>Errores campo a campo, para construir la respuesta 400.</summary>
        public IReadOnlyList<ValidationError> Errors { get; } = [.. errors];
    }
}
