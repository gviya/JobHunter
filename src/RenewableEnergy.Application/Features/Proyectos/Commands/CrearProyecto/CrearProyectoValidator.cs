using FluentValidation;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto
{
    /// <summary>
    /// Reglas de VALIDACIÓN DE ENTRADA del command (FluentValidation).
    ///
    /// ¿VALIDACIÓN AQUÍ Y TAMBIÉN EN EL DOMINIO? SÍ, y no es redundancia:
    /// - AQUÍ se valida la FORMA de la petición (campos obligatorios, rangos,
    ///   longitudes) para devolver un 400 amable con todos los errores juntos.
    /// - El DOMINIO defiende sus INVARIANTES (PotenciaKW.Create lanza si es
    ///   negativa) pase lo que pase: es la última línea de defensa si alguien
    ///   llega al dominio por otro camino.
    ///
    /// Lo ejecuta automáticamente el ValidationBehavior ANTES del handler.
    /// </summary>
    public sealed class CrearProyectoValidator : AbstractValidator<CrearProyectoCommand>
    {
        public CrearProyectoValidator()
        {
            RuleFor(c => c.Nombre)
                .NotEmpty().WithMessage("El nombre del proyecto es obligatorio.")
                .MaximumLength(200).WithMessage("El nombre no puede superar los 200 caracteres.");

            RuleFor(c => c.Tipo)
                .IsInEnum().WithMessage("El tipo de energía no es válido. Valores: Solar, Eolica, Hidraulica, Biomasa, Geotermica.");

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
