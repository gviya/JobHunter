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
///
/// El query string solo llega en la petición de entrada: el dashboard hace
/// decenas de peticiones AJAX internas (/hangfire/stats, polling...) que no
/// lo arrastran. Al validar la clave se deja una cookie de sesión HttpOnly
/// limitada a /hangfire y las peticiones siguientes se autorizan con ella.
/// </summary>
internal sealed class HangfireDashboardAuthorizationFilter(IConfiguration configuration)
    : IDashboardAuthorizationFilter
{
    internal const string CookieName = "jobhunter-hangfire-key";

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();

        if (RequestAuthorizer.IsLocal(http))
        {
            return true;
        }

        string? fromQuery = http.Request.Query["apiKey"];
        if (RequestAuthorizer.HasValidApiKey(configuration, fromQuery))
        {
            http.Response.Cookies.Append(CookieName, fromQuery!, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = http.Request.IsHttps,
                Path = "/hangfire",
            });

            return true;
        }

        return RequestAuthorizer.HasValidApiKey(configuration, http.Request.Cookies[CookieName]);
    }
}
