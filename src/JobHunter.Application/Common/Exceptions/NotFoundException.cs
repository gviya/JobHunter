namespace JobHunter.Application.Common.Exceptions
{
    /// <summary>
    /// Excepción para "el recurso pedido no existe".
    ///
    /// NOTA SOBRE LA CONVENCIÓN DE ESTE PROYECTO: los handlers devuelven
    /// Result con Error.NotFound (flujo esperable, sin excepciones). Esta
    /// excepción existe para los puntos donde no hay Result disponible
    /// (servicios internos, seeds, middleware) y para ilustrar la
    /// alternativa "throw" que verás en muchos proyectos reales.
    /// El ExceptionHandlingMiddleware la traduce a HTTP 404.
    /// </summary>
    public sealed class NotFoundException(string nombreEntidad, object clave)
        : Exception($"La entidad '{nombreEntidad}' con clave '{clave}' no existe.")
    {
    }
}
