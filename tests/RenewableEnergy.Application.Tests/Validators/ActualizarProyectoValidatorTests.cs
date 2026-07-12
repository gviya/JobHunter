using AwesomeAssertions;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto;

namespace RenewableEnergy.Application.Tests.Validators
{
    /// <summary>Tests del validator de actualización (mismas reglas que crear + Id obligatorio).</summary>
    public sealed class ActualizarProyectoValidatorTests
    {
        private readonly ActualizarProyectoValidator _validator = new();

        private static ActualizarProyectoCommand CommandValido()
        {
            return new ActualizarProyectoCommand(
                Guid.NewGuid(),
                "Parque Actualizado",
                750m,
                41.65,
                -0.88,
                "Descripción");
        }

        [Fact]
        public void Validate_ConCommandValido_NoDeberiaTenerErrores()
        {
            _validator.Validate(CommandValido()).IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_ConIdVacio_DeberiaFallar()
        {
            var command = CommandValido() with { Id = Guid.Empty };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
            resultado.Errors.Should().Contain(e => e.PropertyName == nameof(ActualizarProyectoCommand.Id));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_ConNombreVacio_DeberiaFallar(string nombreInvalido)
        {
            var command = CommandValido() with { Nombre = nombreInvalido };

            _validator.Validate(command).IsValid.Should().BeFalse();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void Validate_ConPotenciaNoPositiva_DeberiaFallar(decimal potenciaInvalida)
        {
            var command = CommandValido() with { PotenciaKW = potenciaInvalida };

            _validator.Validate(command).IsValid.Should().BeFalse();
        }

        [Theory]
        [InlineData(91, 0)]      // latitud fuera de rango
        [InlineData(0, 181)]     // longitud fuera de rango
        public void Validate_ConCoordenadasFueraDeRango_DeberiaFallar(double latitud, double longitud)
        {
            var command = CommandValido() with { Latitud = latitud, Longitud = longitud };

            _validator.Validate(command).IsValid.Should().BeFalse();
        }

        [Fact]
        public void Validate_ConDescripcionDemasiadoLarga_DeberiaFallar()
        {
            var command = CommandValido() with { Descripcion = new string('x', 2001) };

            _validator.Validate(command).IsValid.Should().BeFalse();
        }
    }
}
