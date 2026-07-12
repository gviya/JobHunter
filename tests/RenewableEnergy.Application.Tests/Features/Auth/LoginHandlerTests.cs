using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Features.Auth.Commands.Login;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Application.Tests.Features.Auth
{
    /// <summary>
    /// Tests del login. La aserción MÁS importante es la de seguridad:
    /// "usuario inexistente" y "contraseña incorrecta" devuelven EXACTAMENTE
    /// el mismo error — la API no debe revelar qué emails están registrados
    /// (enumeración de usuarios).
    /// </summary>
    public sealed class LoginHandlerTests
    {
        private readonly Mock<IUsuarioRepository> _usuarioRepositorioMock = new();
        private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
        private readonly Mock<IJwtTokenGenerator> _tokenGeneratorMock = new();
        private readonly LoginHandler _handler;

        public LoginHandlerTests()
        {
            _handler = new LoginHandler(
                _usuarioRepositorioMock.Object,
                _passwordHasherMock.Object,
                _tokenGeneratorMock.Object,
                NullLogger<LoginHandler>.Instance);
        }

        private static Usuario UsuarioDemo()
        {
            return Usuario.Create("Usuaria Demo", Email.Create("demo@renovables.demo"), "hash-guardado");
        }

        [Fact]
        public async Task Handle_ConCredencialesValidas_DeberiaDevolverElToken()
        {
            var usuario = UsuarioDemo();
            var expiracion = DateTime.UtcNow.AddHours(1);

            _usuarioRepositorioMock
                .Setup(r => r.GetByEmailAsync("demo@renovables.demo", It.IsAny<CancellationToken>()))
                .ReturnsAsync(usuario);

            _passwordHasherMock
                .Setup(h => h.Verificar("Demo1234!", "hash-guardado"))
                .Returns(true);

            _tokenGeneratorMock
                .Setup(g => g.GenerarToken(usuario))
                .Returns(("jwt-de-prueba", expiracion));

            var result = await _handler.Handle(
                new LoginCommand("demo@renovables.demo", "Demo1234!"),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Token.Should().Be("jwt-de-prueba");
            result.Value.ExpiraEnUtc.Should().Be(expiracion);
            result.Value.Email.Should().Be("demo@renovables.demo");
        }

        [Fact]
        public async Task Handle_NormalizaElEmail_AntesDeBuscarlo()
        {
            // "  DEMO@Renovables.DEMO " debe buscarse como "demo@renovables.demo".
            _usuarioRepositorioMock
                .Setup(r => r.GetByEmailAsync("demo@renovables.demo", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Usuario?)null);

            await _handler.Handle(
                new LoginCommand("  DEMO@Renovables.DEMO ", "loquesea"),
                CancellationToken.None);

            _usuarioRepositorioMock.Verify(
                r => r.GetByEmailAsync("demo@renovables.demo", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_UsuarioInexistenteYPasswordIncorrecta_DevuelvenElMismoError()
        {
            // Caso A: el email no existe.
            _usuarioRepositorioMock
                .Setup(r => r.GetByEmailAsync("noexiste@renovables.demo", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Usuario?)null);

            var resultadoEmailInexistente = await _handler.Handle(
                new LoginCommand("noexiste@renovables.demo", "da igual"),
                CancellationToken.None);

            // Caso B: el email existe pero la contraseña falla.
            var usuario = UsuarioDemo();
            _usuarioRepositorioMock
                .Setup(r => r.GetByEmailAsync("demo@renovables.demo", It.IsAny<CancellationToken>()))
                .ReturnsAsync(usuario);
            _passwordHasherMock
                .Setup(h => h.Verificar(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            var resultadoPasswordMala = await _handler.Handle(
                new LoginCommand("demo@renovables.demo", "incorrecta"),
                CancellationToken.None);

            // MISMO código y MISMO mensaje: no se filtra qué emails existen.
            resultadoEmailInexistente.IsFailure.Should().BeTrue();
            resultadoPasswordMala.IsFailure.Should().BeTrue();
            resultadoEmailInexistente.Error.Should().Be(resultadoPasswordMala.Error);
        }

        [Fact]
        public async Task Handle_ConUsuarioDesactivado_DeberiaRechazarElLogin()
        {
            var usuario = UsuarioDemo();
            usuario.Desactivar();

            _usuarioRepositorioMock
                .Setup(r => r.GetByEmailAsync("demo@renovables.demo", It.IsAny<CancellationToken>()))
                .ReturnsAsync(usuario);

            var result = await _handler.Handle(
                new LoginCommand("demo@renovables.demo", "Demo1234!"),
                CancellationToken.None);

            result.IsFailure.Should().BeTrue();

            // Ni siquiera se comprueba la contraseña de un usuario inactivo.
            _passwordHasherMock.Verify(
                h => h.Verificar(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }
    }
}
