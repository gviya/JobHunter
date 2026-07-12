using AwesomeAssertions;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Tests.ValueObjects
{
    /// <summary>Tests del VO PotenciaKW: rango válido (0, 10M] kW.</summary>
    public sealed class PotenciaKWTests
    {
        [Fact]
        public void Create_ConValorValido_DeberiaCrearPotencia()
        {
            var potencia = PotenciaKW.Create(500.5m);

            potencia.Valor.Should().Be(500.5m);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Create_ConValorNoPositivo_DeberiaLanzarDomainException(decimal valorInvalido)
        {
            var accion = () => PotenciaKW.Create(valorInvalido);

            accion.Should().Throw<DomainException>()
                .WithMessage("*mayor que cero*");
        }

        [Fact]
        public void Create_SuperandoElMaximo_DeberiaLanzarDomainException()
        {
            var accion = () => PotenciaKW.Create(PotenciaKW.Maximo + 1);

            accion.Should().Throw<DomainException>();
        }
    }
}
