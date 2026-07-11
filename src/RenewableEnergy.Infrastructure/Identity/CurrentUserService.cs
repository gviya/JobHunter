using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RenewableEnergy.Application.Interfaces;

namespace RenewableEnergy.Infrastructure.Identity
{
    /// <summary>
    /// Implementación de ICurrentUserService: extrae la identidad del JWT de
    /// la petición HTTP actual a través de IHttpContextAccessor.
    ///
    /// ¿POR QUÉ EN INFRASTRUCTURE?
    /// Porque depende de HttpContext (tecnología web). Si mañana la app
    /// corre como worker sin HTTP, se registra otra implementación y los
    /// handlers de Application ni se enteran.
    /// </summary>
    internal sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
    {
        private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

        public Guid? UserId
        {
            get
            {
                // El claim "sub"/NameIdentifier lo puso JwtTokenGenerator al
                // emitir el token en el login.
                var valor = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                return Guid.TryParse(valor, out var id) ? id : null;
            }
        }

        public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

        public bool EstaAutenticado => Principal?.Identity?.IsAuthenticated ?? false;
    }
}
