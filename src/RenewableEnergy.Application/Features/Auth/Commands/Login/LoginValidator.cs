using FluentValidation;

namespace RenewableEnergy.Application.Features.Auth.Commands.Login
{
    /// <summary>Validación de entrada del login.</summary>
    public sealed class LoginValidator : AbstractValidator<LoginCommand>
    {
        public LoginValidator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithMessage("El email es obligatorio.")
                .EmailAddress().WithMessage("El email no tiene un formato válido.");

            RuleFor(c => c.Password)
                .NotEmpty().WithMessage("La contraseña es obligatoria.");
        }
    }
}
