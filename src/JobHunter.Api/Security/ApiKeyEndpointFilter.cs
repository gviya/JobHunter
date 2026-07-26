using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace JobHunter.Api.Security;

/// <summary>
/// Protege los endpoints sensibles (disparar el pipeline, ver el informe).
///
/// Política: las peticiones LOCALES (loopback / misma máquina) se permiten
/// siempre — cómodo para uso en local, que es el caso normal. Desde fuera se
/// exige la cabecera <c>X-Api-Key</c> con el valor de <c>Security:ApiKey</c>.
/// Si no hay clave configurada, SOLO se admite acceso local: así el servicio
/// nunca queda abierto por accidente al exponerlo en una red.
/// </summary>
internal sealed class ApiKeyEndpointFilter(
    IConfiguration configuration,
    ILogger<ApiKeyEndpointFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (RequestAuthorizer.IsLocal(http) ||
            RequestAuthorizer.HasValidApiKey(configuration, http.Request.Headers[HeaderName]))
        {
            return await next(context);
        }

        logger.LogWarning(
            "Acceso rechazado a {Path} desde {Ip}: falta X-Api-Key válida.",
            http.Request.Path, http.Connection.RemoteIpAddress);

        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "No autorizado",
            detail: "Esta operación requiere la cabecera X-Api-Key.");
    }
}

/// <summary>
/// Lógica compartida de autorización (endpoints + dashboard de Hangfire):
/// decide si una petición es local y compara la API key en tiempo constante.
/// </summary>
internal static class RequestAuthorizer
{
    public static bool IsLocal(HttpContext http)
    {
        var remote = http.Connection.RemoteIpAddress;

        // Sin IP remota = petición en proceso (tests, health interno): local.
        if (remote is null)
        {
            return true;
        }

        if (IPAddress.IsLoopback(remote))
        {
            return true;
        }

        // Cliente y servidor en la misma IP (misma máquina).
        var local = http.Connection.LocalIpAddress;
        return local is not null && remote.Equals(local);
    }

    public static bool HasValidApiKey(IConfiguration configuration, string? provided)
    {
        var configured = configuration["Security:ApiKey"];

        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Comparación en tiempo constante para no filtrar la clave por timing.
        var a = Encoding.UTF8.GetBytes(configured);
        var b = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
