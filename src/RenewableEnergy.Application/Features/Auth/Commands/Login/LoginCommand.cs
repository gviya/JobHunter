using MediatR;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Features.Auth.Commands.Login
{
    /// <summary>
    /// COMMAND de autenticación: credenciales → JWT.
    ///
    /// Fíjate en que el login también pasa por MediatR: el AuthController no
    /// toca repositorios (regla verificada por los tests de arquitectura) y
    /// el flujo se beneficia del pipeline (logging, validación).
    /// </summary>
    public sealed record LoginCommand(string Email, string Password) : IRequest<Result<LoginResponse>>;
}
