using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Mediciones.Commands.RegistrarMedicion;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Mediciones
{
    /// <summary>
    /// Tests del handler RegistrarMedicion (la feature que construye paso a
    /// paso docs/GUIA_NUEVAS_FEATURES.md). Cubre el flujo feliz, el 404 y la
    /// traducción de DomainException a Result.Conflict.
    /// </summary>
    public sealed class RegistrarMedicionHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IDistributedCache> _cacheMock = new();
        private readonly RegistrarMedicionHandler _handler;

        public RegistrarMedicionHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new RegistrarMedicionHandler(
                _repositorioMock.Object,
                _unitOfWorkMock.Object,
                _cacheMock.Object,
                mapper,
                NullLogger<RegistrarMedicionHandler>.Instance);
        }

        [Fact]
        public async Task Handle_EnProyectoActivo_DeberiaRegistrarLaMedicion()
        {
            var proyecto = new ProyectoRenovableBuilder().Activado().Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var command = new RegistrarMedicionCommand(
                proyecto.Id,
                DateTime.UtcNow.AddHours(-1),
                1240.5m);

            var result = await _handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.EnergiaGeneradaKWh.Should().Be(1240.5m);
            result.Value.ProyectoId.Should().Be(proyecto.Id);

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

            // La caché del detalle debe invalidarse (contiene las mediciones).
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

            var command = new RegistrarMedicionCommand(Guid.NewGuid(), DateTime.UtcNow, 100m);

            var result = await _handler.Handle(command, CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.NotFound);
        }

        [Fact]
        public async Task Handle_EnProyectoBorrador_DeberiaDevolverConflict()
        {
            // Proyecto SIN activar: el dominio rechazará la medición.
            var proyecto = new ProyectoRenovableBuilder().Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var command = new RegistrarMedicionCommand(proyecto.Id, DateTime.UtcNow.AddHours(-1), 100m);

            var result = await _handler.Handle(command, CancellationToken.None);

            // La DomainException del agregado se tradujo a Result.Conflict.
            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.Conflict);
            result.Error.Codigo.Should().Be("Medicion.OperacionInvalida");

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
