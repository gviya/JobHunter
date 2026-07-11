using FluentValidation;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto
{
    /// <summary>Validación de entrada del borrado.</summary>
    public sealed class EliminarProyectoValidator : AbstractValidator<EliminarProyectoCommand>
    {
        public EliminarProyectoValidator()
        {
            RuleFor(c => c.Id)
                .NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");
        }
    }
}
