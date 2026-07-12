using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActivarProyecto;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del handler ActivarProyecto: el flujo feliz, el 404 y — lo más
    /// interesante — la TRADUCCIÓN de DomainException a Result.Conflict
    /// cuando la transición de estado no es válida.
    /// </summary>
    public sealed class ActivarProyectoHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IDistributedCache> _cacheMock = new();
        private readonly ActivarProyectoHandler _handler;

        public ActivarProyectoHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new ActivarProyectoHandler(
                _repositorioMock.Object,
                _unitOfWorkMock.Object,
                _cacheMock.Object,
                mapper,
                NullLogger<ActivarProyectoHandler>.Instance);
        }

        [Fact]
        public async Task Handle_ConProyectoEnBorrador_DeberiaActivarlo()
        {
            var proyecto = new ProyectoRenovableBuilder().Build();   // Borrador

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(new ActivarProyectoCommand(proyecto.Id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Estado.Should().Be(nameof(EstadoProyecto.Activo));
            result.Value.FechaActivacion.Should().NotBeNull();

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

            // El detalle cacheado quedó obsoleto: debe invalidarse.
            _cacheMock.Verify(
                c => c.RemoveAsync($"proyecto:detalle:{proyecto.Id}", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ConProyectoInexistente_DeberiaDevolverNotFound()
        {
            _repositorioMock
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProyectoRenovable?)null);

            var result = await _handler.Handle(new ActivarProyectoCommand(Guid.NewGuid()), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.NotFound);
        }

        [Fact]
        public async Task Handle_ConProyectoYaActivo_DeberiaDevolverConflict()
        {
            // El dominio lanzará DomainException; el handler la traduce.
            var proyecto = new ProyectoRenovableBuilder().Activado().Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(new ActivarProyectoCommand(proyecto.Id), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.Conflict);
            result.Error.Codigo.Should().Be("Proyecto.TransicionInvalida");

            // Nada que guardar si la transición no fue válida.
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
