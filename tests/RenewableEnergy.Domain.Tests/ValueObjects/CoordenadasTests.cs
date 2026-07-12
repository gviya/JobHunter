using AwesomeAssertions;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Tests.ValueObjects
{
    /// <summary>
    /// Tests del VALUE OBJECT Coordenadas: validación de rangos e IGUALDAD
    /// POR VALOR (la característica que define a un VO).
    /// </summary>
    public sealed class CoordenadasTests
    {
        [Fact]
        public void Create_ConValoresValidos_DeberiaCrearCoordenadas()
        {
            var coordenadas = Coordenadas.Create(40.4168, -3.7038);

            coordenadas.Latitud.Should().Be(40.4168);
            coordenadas.Longitud.Should().Be(-3.7038);
        }

        [Theory]
        [InlineData(-91)]
        [InlineData(91)]
        public void Create_ConLatitudFueraDeRango_DeberiaLanzarDomainException(double latitudInvalida)
        {
            var accion = () => Coordenadas.Create(latitudInvalida, 0);

            accion.Should().Throw<DomainException>()
                .WithMessage("*latitud*");
        }

        [Theory]
        [InlineData(-181)]
        [InlineData(181)]
        public void Create_ConLongitudFueraDeRango_DeberiaLanzarDomainException(double longitudInvalida)
        {
            var accion = () => Coordenadas.Create(0, longitudInvalida);

            accion.Should().Throw<DomainException>()
                .WithMessage("*longitud*");
        }

        [Fact]
        public void Equals_ConMismosValores_DeberianSerIguales()
        {
            // IGUALDAD POR VALOR: dos VOs con los mismos datos SON el mismo
            // valor, aunque sean instancias distintas (lo genera el record).
            var a = Coordenadas.Create(40.0, -3.0);
            var b = Coordenadas.Create(40.0, -3.0);

            a.Should().Be(b);
            (a == b).Should().BeTrue();
        }
    }
}
