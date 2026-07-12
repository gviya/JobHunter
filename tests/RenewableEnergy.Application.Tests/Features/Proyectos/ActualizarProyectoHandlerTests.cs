using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Commands.ActualizarProyecto;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del handler ActualizarProyecto: éxito con invalidación de caché,
    /// unicidad de nombre (excluyendo el propio) y proyecto en estado terminal.
    /// </summary>
    public sealed class ActualizarProyectoHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<IDistributedCache> _cacheMock = new();
        private readonly ActualizarProyectoHandler _handler;

        public ActualizarProyectoHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new ActualizarProyectoHandler(
                _repositorioMock.Object,
                _unitOfWorkMock.Object,
                _cacheMock.Object,
                mapper,
                NullLogger<ActualizarProyectoHandler>.Instance);
        }

        private static ActualizarProyectoCommand CommandValido(Guid id)
        {
            return new ActualizarProyectoCommand(
                id,
                "Nombre Actualizado",
                750m,
                41.65,
                -0.88,
                "Descripción nueva");
        }

        [Fact]
        public async Task Handle_ConDatosValidos_DeberiaActualizarEInvalidarCache()
        {
            var proyecto = new ProyectoRenovableBuilder().ConNombre("Nombre Original").Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(CommandValido(proyecto.Id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Nombre.Should().Be("Nombre Actualizado");
            result.Value.PotenciaKW.Should().Be(750m);
            result.Value.Latitud.Should().Be(41.65);

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _cacheMock.Verify(
                c => c.RemoveAsync($"proyecto:detalle:{proyecto.Id}", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ConNombreDeOtroProyecto_DeberiaDevolverConflict()
        {
            var proyecto = new ProyectoRenovableBuilder().Build();

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            // La comprobación de unicidad EXCLUYE al propio proyecto: si aun
            // así devuelve true, es que OTRO proyecto ya usa ese nombre.
            _repositorioMock
                .Setup(r => r.ExisteNombreAsync("Nombre Actualizado", proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _handler.Handle(CommandValido(proyecto.Id), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Codigo.Should().Be("Proyecto.NombreDuplicado");
        }

        [Fact]
        public async Task Handle_ConProyectoEnEstadoTerminal_DeberiaDevolverConflict()
        {
            var proyecto = new ProyectoRenovableBuilder().Build();
            proyecto.Cancelar();   // estado terminal: inmutable

            _repositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(CommandValido(proyecto.Id), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Codigo.Should().Be("Proyecto.OperacionInvalida");
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
