using MediatR;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Features.Auth.Commands.Login
{
    /// <summary>
    /// HANDLER del login. Buenas prácticas de seguridad que ilustra:
    /// - El mensaje de error es IDÉNTICO para "usuario no existe" y
    ///   "contraseña incorrecta": no se filtra qué emails están registrados.
    /// - Se usa ErrorType.Failure con código genérico (la API responderá 401
    ///   desde el controller para este código concreto).
    /// - La contraseña nunca se loguea.
    /// </summary>
    internal sealed class LoginHandler(
        IUsuarioRepository usuarioRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        ILogger<LoginHandler> logger)
        : IRequestHandler<LoginCommand, Result<LoginResponse>>
    {
        public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var usuario = await usuarioRepository.GetByEmailAsync(
                request.Email.Trim().ToLowerInvariant(),
                cancellationToken);

            // Mismo error para ambos casos: no revelar si el email existe.
            var credencialesInvalidas = Error.Forbidden(
                "Auth.CredencialesInvalidas",
                "Email o contraseña incorrectos.");

            if (usuario is null || !usuario.Activo)
            {
                logger.LogInformation("Login fallido para {Email}", request.Email);
                return Result.Failure<LoginResponse>(credencialesInvalidas);
            }

            if (!passwordHasher.Verificar(request.Password, usuario.PasswordHash))
            {
                logger.LogInformation("Login fallido para {Email}", request.Email);
                return Result.Failure<LoginResponse>(credencialesInvalidas);
            }

            var (token, expiraEnUtc) = tokenGenerator.GenerarToken(usuario);

            logger.LogInformation("Login correcto de {Email}", usuario.Email.Valor);

            return new LoginResponse
            {
                Token = token,
                ExpiraEnUtc = expiraEnUtc,
                Email = usuario.Email.Valor,
                Nombre = usuario.Nombre
            };
        }
    }
}
