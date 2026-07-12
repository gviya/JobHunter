using AwesomeAssertions;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Tests.ValueObjects
{
    /// <summary>Tests del VO Email: formato y normalización.</summary>
    public sealed class EmailTests
    {
        [Fact]
        public void Create_ConEmailValido_DeberiaNormalizarlo()
        {
            var email = Email.Create("  Demo@Renovables.DEMO ");

            email.Valor.Should().Be("demo@renovables.demo");
        }

        [Theory]
        [InlineData("")]
        [InlineData("sin-arroba")]
        [InlineData("dos@@arrobas.com")]
        [InlineData("sin@dominio")]
        public void Create_ConFormatoInvalido_DeberiaLanzarDomainException(string emailInvalido)
        {
            var accion = () => Email.Create(emailInvalido);

            accion.Should().Throw<DomainException>();
        }
    }
}
