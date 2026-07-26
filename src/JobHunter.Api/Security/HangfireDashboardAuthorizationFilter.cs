using Hangfire.Dashboard;

namespace JobHunter.Api.Security;

/// <summary>
/// Autoriza el dashboard de Hangfire (<c>/hangfire</c>). Por defecto Hangfire
/// deja el dashboard ABIERTO a cualquiera que lo alcance; detrás de un proxy
/// inverso o bindeado a 0.0.0.0 eso expone encolar/borrar jobs y ver sus
/// argumentos.
///
/// Aquí se permite solo acceso LOCAL (loopback) o, desde fuera, aportar la
/// clave <c>Security:ApiKey</c> como query string <c>?apiKey=...</c> (el
/// navegador no puede mandar cabeceras personalizadas al abrir una URL).
/// </summary>
internal sealed class HangfireDashboardAuthorizationFilter(IConfiguration configuration)
    : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();

        return RequestAuthorizer.IsLocal(http) ||
               RequestAuthorizer.HasValidApiKey(configuration, http.Request.Query["apiKey"]);
    }
}
