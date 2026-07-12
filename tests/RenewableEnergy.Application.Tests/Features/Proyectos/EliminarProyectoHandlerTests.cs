using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Commands.EliminarProyecto;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del handler EliminarProyecto: la regla "solo Borrador/Cancelado
    /// se borran" la expone la ENTIDAD (PuedeSerEliminado) y el handler la
    /// respeta — aquí se comprueban ambos lados de esa regla.
    /// </summary>
    public sealed class EliminarProyectoHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IDistributedCache> _cacheMock = new();
        private readonly EliminarProyectoHandler _handler;

        public EliminarProyectoHandlerTests()
        {
            _handler = new EliminarProyectoHandler(
                _repositorioMock.Object,
                _unitOfWorkMock.Object,
                _cacheMock.Object,
                NullLogger<EliminarProyectoHandler>.Instance);
        }

        [Fact]
        public async Task Handle_ConProyectoEnBorrador_DeberiaEliminarlo()
        {
            var proyecto = new ProyectoRenovableBuilder().Build();   // Borrador

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(new EliminarProyectoCommand(proyecto.Id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            _repositorioMock.Verify(r => r.Delete(proyecto), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(
                c => c.RemoveAsync($"proyecto:detalle:{proyecto.Id}", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ConProyectoActivo_DeberiaDevolverConflict()
        {
            // Un proyecto que ya produjo energía conserva su histórico.
            var proyecto = new ProyectoRenovableBuilder().Activado().Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(new EliminarProyectoCommand(proyecto.Id), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Codigo.Should().Be("Proyecto.NoEliminable");
            _repositorioMock.Verify(r => r.Delete(It.IsAny<ProyectoRenovable>()), Times.Never);
        }

        [Fact]
        public async Task Handle_ConProyectoInexistente_DeberiaDevolverNotFound()
        {
            _repositorioMock
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProyectoRenovable?)null);

            var result = await _handler.Handle(new EliminarProyectoCommand(Guid.NewGuid()), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.NotFound);
        }
    }
}
