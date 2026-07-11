using FluentValidation;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto
{
    /// <summary>Validación de entrada del command de actualización.</summary>
    public sealed class ActualizarProyectoValidator : AbstractValidator<ActualizarProyectoCommand>
    {
        public ActualizarProyectoValidator()
        {
            RuleFor(c => c.Id)
                .NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");

            RuleFor(c => c.Nombre)
                .NotEmpty().WithMessage("El nombre del proyecto es obligatorio.")
                .MaximumLength(200).WithMessage("El nombre no puede superar los 200 caracteres.");

            RuleFor(c => c.PotenciaKW)
                .GreaterThan(0).WithMessage("La potencia debe ser mayor que cero.")
                .LessThanOrEqualTo(PotenciaKW.Maximo).WithMessage($"La potencia no puede superar {PotenciaKW.Maximo:N0} kW.");

            RuleFor(c => c.Latitud)
                .InclusiveBetween(-90, 90).WithMessage("La latitud debe estar entre -90 y 90 grados.");

            RuleFor(c => c.Longitud)
                .InclusiveBetween(-180, 180).WithMessage("La longitud debe estar entre -180 y 180 grados.");

            RuleFor(c => c.Descripcion)
                .MaximumLength(2000).WithMessage("La descripción no puede superar los 2000 caracteres.");
        }
    }
}
