namespace RenewableEnergy.Domain.Common
{
    /// <summary>
    /// Excepción que lanza el DOMINIO cuando se viola una regla de negocio
    /// (una "invariante"): activar un proyecto que no está en borrador,
    /// registrar una medición con fecha futura, una potencia negativa...
    ///
    /// ¿POR QUÉ UNA EXCEPCIÓN PROPIA?
    /// - Distingue "bug del programador" (NullReferenceException) de
    ///   "regla de negocio violada" (DomainException).
    /// - El ExceptionHandlingMiddleware de la API la traduce a un HTTP 409/422
    ///   con ProblemDetails, sin que el dominio sepa nada de HTTP.
    ///
    /// NOTA: para flujos de negocio ESPERABLES (p. ej. "el nombre ya existe")
    /// preferimos el patrón Result en Application. La excepción se reserva
    /// para violaciones de invariantes que no deberían ocurrir si el caso de
    /// uso hizo bien su trabajo. Ver docs/PATRONES.md (Result vs Exception).
    /// </summary>
    public sealed class DomainException : Exception
    {
        public DomainException(string message) : base(message)
        {
        }
    }
}
