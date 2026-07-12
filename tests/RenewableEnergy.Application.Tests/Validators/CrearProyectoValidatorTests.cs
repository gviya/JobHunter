using AwesomeAssertions;
using RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto;
using RenewableEnergy.Domain.Enums;

namespace RenewableEnergy.Application.Tests.Validators
{
    /// <summary>
    /// Tests del VALIDATOR de CrearProyecto. Los validators se prueban
    /// directamente (sin pipeline ni mocks): son funciones puras
    /// command → lista de errores.
    /// </summary>
    public sealed class CrearProyectoValidatorTests
    {
        private readonly CrearProyectoValidator _validator = new();

        private static CrearProyectoCommand CommandValido()
        {
            return new CrearProyectoCommand(
                "Parque Solar Madrid Norte",
                TipoEnergia.Solar,
                500m,
                40.4168,
                -3.7038);
        }

        [Fact]
        public void Validate_ConCommandValido_NoDeberiaTenerErrores()
        {
            var resultado = _validator.Validate(CommandValido());

            resultado.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_ConNombreVacio_DeberiaFallar(string nombreInvalido)
        {
            var command = CommandValido() with { Nombre = nombreInvalido };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
            resultado.Errors.Should().Contain(e => e.PropertyName == nameof(CrearProyectoCommand.Nombre));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void Validate_ConPotenciaNoPositiva_DeberiaFallar(decimal potenciaInvalida)
        {
            var command = CommandValido() with { PotenciaKW = potenciaInvalida };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
            resultado.Errors.Should().Contain(e => e.PropertyName == nameof(CrearProyectoCommand.PotenciaKW));
        }

        [Fact]
        public void Validate_ConLatitudFueraDeRango_DeberiaFallar()
        {
            var command = CommandValido() with { Latitud = 120 };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
            resultado.Errors.Should().Contain(e => e.PropertyName == nameof(CrearProyectoCommand.Latitud));
        }

        [Fact]
        public void Validate_ConTipoDeEnergiaInvalido_DeberiaFallar()
        {
            // Un valor fuera del enum (nunca definimos el 99): IsInEnum lo caza.
            var command = CommandValido() with { Tipo = (TipoEnergia)99 };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
            resultado.Errors.Should().Contain(e => e.PropertyName == nameof(CrearProyectoCommand.Tipo));
        }

        [Fact]
        public void Validate_ConNombreDemasiadoLargo_DeberiaFallar()
        {
            var command = CommandValido() with { Nombre = new string('x', 201) };

            var resultado = _validator.Validate(command);

            resultado.IsValid.Should().BeFalse();
        }
    }
}
