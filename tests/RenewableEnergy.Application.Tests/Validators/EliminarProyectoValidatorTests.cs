using AwesomeAssertions;
using RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto;

namespace RenewableEnergy.Application.Tests.Validators
{
    /// <summary>Tests del validator de borrado: el Id no puede ser Guid.Empty.</summary>
    public sealed class EliminarProyectoValidatorTests
    {
        private readonly EliminarProyectoValidator _validator = new();

        [Fact]
        public void Validate_ConIdValido_NoDeberiaTenerErrores()
        {
            var resultado = _validator.Validate(new EliminarProyectoCommand(Guid.NewGuid()));

            resultado.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ConIdVacio_DeberiaFallar()
        {
            var resultado = _validator.Validate(new EliminarProyectoCommand(Guid.Empty));

            resultado.IsValid.Should().BeFalse();
        }
    }
}
