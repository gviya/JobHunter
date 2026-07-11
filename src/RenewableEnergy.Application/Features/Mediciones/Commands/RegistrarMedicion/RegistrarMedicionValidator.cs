using FluentValidation;

namespace RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion
{
    /// <summary>Validación de entrada del registro de mediciones.</summary>
    public sealed class RegistrarMedicionValidator : AbstractValidator<RegistrarMedicionCommand>
    {
        public RegistrarMedicionValidator()
        {
            RuleFor(c => c.ProyectoId)
                .NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");

            RuleFor(c => c.FechaMedicion)
                .NotEmpty().WithMessage("La fecha de la medición es obligatoria.")
                .LessThanOrEqualTo(_ => DateTime.UtcNow)
                .WithMessage("La fecha de la medición no puede ser futura.");

            RuleFor(c => c.EnergiaGeneradaKWh)
                .GreaterThanOrEqualTo(0).WithMessage("La energía generada no puede ser negativa.");
        }
    }
}
