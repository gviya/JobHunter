using FluentValidation;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto
{
    /// <summary>Validación de entrada: el Id no puede ser Guid.Empty.</summary>
    public sealed class ActivarProyectoValidator : AbstractValidator<ActivarProyectoCommand>
    {
        public ActivarProyectoValidator()
        {
            RuleFor(c => c.Id)
                .NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");
        }
    }
}
