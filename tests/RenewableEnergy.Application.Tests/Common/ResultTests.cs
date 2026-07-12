using AwesomeAssertions;
using RenewableEnergy.Application.Common.Models;

namespace RenewableEnergy.Application.Tests.Common
{
    /// <summary>
    /// Tests del PATRÓN RESULT: sus invariantes internas ("éxito sin error",
    /// "fallo con error") y las factories de Error. Testear estos guardas
    /// importa: si el Result estuviera mal construido, TODOS los handlers
    /// heredarían el bug.
    /// </summary>
    public sealed class ResultTests
    {
        [Fact]
        public void Success_DeberiaSerExitoSinError()
        {
            var result = Result.Success();

            result.IsSuccess.Should().BeTrue();
            result.IsFailure.Should().BeFalse();
            result.Error.Should().BeNull();
        }

        [Fact]
        public void Failure_DeberiaExponerElError()
        {
            var error = Error.Conflict("Codigo.Prueba", "mensaje");

            var result = Result.Failure(error);

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(error);
        }

        [Fact]
        public void Value_EnUnResultadoFallido_DeberiaLanzar()
        {
            // Acceder a Value sin comprobar IsSuccess es un BUG del consumidor:
            // el Result lo hace explotar en lugar de devolver default silencioso.
            var result = Result.Failure<string>(Error.NotFound("X", "no existe"));

            var accion = () => result.Value;

            accion.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ConversionImplicita_DeberiaCrearUnSuccess()
        {
            // El azúcar sintáctico que usan los handlers: `return viewModel;`
            Result<int> result = 42;

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(42);
        }

        [Theory]
        [InlineData(ErrorType.Failure)]
        [InlineData(ErrorType.Validation)]
        [InlineData(ErrorType.NotFound)]
        [InlineData(ErrorType.Conflict)]
        [InlineData(ErrorType.Forbidden)]
        public void FactoriesDeError_DeberianAsignarElTipoCorrecto(ErrorType tipo)
        {
            var error = tipo switch
            {
                ErrorType.Failure => Error.Failure("C", "m"),
                ErrorType.Validation => Error.Validation("C", "m"),
                ErrorType.NotFound => Error.NotFound("C", "m"),
                ErrorType.Conflict => Error.Conflict("C", "m"),
                ErrorType.Forbidden => Error.Forbidden("C", "m"),
                _ => throw new ArgumentOutOfRangeException(nameof(tipo))
            };

            error.Tipo.Should().Be(tipo);
            error.Codigo.Should().Be("C");
            error.Mensaje.Should().Be("m");
        }

        [Fact]
        public void Errores_ConMismosDatos_SonIguales()
        {
            // Error es un record: igualdad por valor. Lo aprovecha, p. ej.,
            // el test del login que compara los dos errores de credenciales.
            Error.Forbidden("A", "b").Should().Be(Error.Forbidden("A", "b"));
        }
    }
}
