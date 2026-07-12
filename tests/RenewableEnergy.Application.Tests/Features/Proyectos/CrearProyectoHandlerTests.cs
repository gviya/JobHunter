using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Commands.CrearProyecto;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests UNITARIOS del handler CrearProyecto. Todo lo externo se MOCKEA
    /// (repositorio, unit of work, usuario actual): aquí se prueba SOLO la
    /// orquestación del caso de uso. El mapper es real a propósito: valida
    /// de paso que el Profile cubre este mapeo.
    /// </summary>
    public sealed class CrearProyectoHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _usuarioActualMock = new();
        private readonly CrearProyectoHandler _handler;

        private static readonly CrearProyectoCommand CommandValido = new(
            "Parque Solar Madrid Norte",
            TipoEnergia.Solar,
            500m,
            40.4168,
            -3.7038,
            "Descripción de prueba");

        public CrearProyectoHandlerTests()
        {
            // AutoMapper REAL con el Profile de producción.
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new CrearProyectoHandler(
                _repositorioMock.Object,
                _unitOfWorkMock.Object,
                _usuarioActualMock.Object,
                mapper,
                NullLogger<CrearProyectoHandler>.Instance);
        }

        [Fact]
        public async Task Handle_ConCommandValido_DeberiaCrearProyecto()
        {
            // Arrange: el nombre no existe todavía.
            _repositorioMock
                .Setup(r => r.ExisteNombreAsync(CommandValido.Nombre, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _handler.Handle(CommandValido, CancellationToken.None);

            // Assert: éxito, DTO correcto y colaboraciones esperadas.
            result.IsSuccess.Should().BeTrue();
            result.Value.Nombre.Should().Be("Parque Solar Madrid Norte");
            result.Value.Estado.Should().Be(nameof(EstadoProyecto.Borrador));
            result.Value.PotenciaKW.Should().Be(500m);

            _repositorioMock.Verify(r => r.Add(It.IsAny<ProyectoRenovable>()), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ConNombreDuplicado_DeberiaDevolverConflict()
        {
            _repositorioMock
                .Setup(r => r.ExisteNombreAsync(CommandValido.Nombre, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _handler.Handle(CommandValido, CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.Conflict);
            result.Error.Codigo.Should().Be("Proyecto.NombreDuplicado");

            // Si el nombre está repetido, NO se debe persistir nada.
            _repositorioMock.Verify(r => r.Add(It.IsAny<ProyectoRenovable>()), Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_ConUsuarioAutenticado_DeberiaAsignarPropietario()
        {
            var usuarioId = Guid.NewGuid();
            _usuarioActualMock.Setup(u => u.UserId).Returns(usuarioId);

            // Se CAPTURA la entidad que el handler entrega al repositorio
            // para inspeccionar el agregado construido.
            ProyectoRenovable? proyectoCreado = null;
            _repositorioMock
                .Setup(r => r.Add(It.IsAny<ProyectoRenovable>()))
                .Callback<ProyectoRenovable>(p => proyectoCreado = p);

            await _handler.Handle(CommandValido, CancellationToken.None);

            proyectoCreado.Should().NotBeNull();
            proyectoCreado!.PropietarioId.Should().Be(usuarioId);
        }
    }
}
