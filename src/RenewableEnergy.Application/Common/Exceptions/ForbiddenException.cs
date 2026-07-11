namespace RenewableEnergy.Application.Common.Exceptions
{
    /// <summary>
    /// Excepción para "el usuario está autenticado pero NO tiene permiso"
    /// (distinto de 401, que es "no sabemos quién eres").
    ///
    /// La lanza, por ejemplo, el ApiKeyAuthFilter cuando la clave no es
    /// válida. El ExceptionHandlingMiddleware la traduce a HTTP 403.
    /// </summary>
    public sealed class ForbiddenException(string mensaje) : Exception(mensaje)
    {
    }
}
