using AutoMapper;
using AwesomeAssertions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Mediciones.Queries.ObtenerMediciones;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Mediciones
{
    /// <summary>
    /// Tests del listado de mediciones. Detalle de diseño que se prueba:
    /// distinguir "proyecto sin mediciones" (lista vacía) de "proyecto
    /// inexistente" (NotFound) es parte del contrato de la API.
    /// </summary>
    public sealed class ObtenerMedicionesHandlerTests
    {
        private readonly Mock<IProyectoRepository> _proyectoRepositorioMock = new();
        private readonly Mock<IMedicionRepository> _medicionRepositorioMock = new();
        private readonly ObtenerMedicionesHandler _handler;

        public ObtenerMedicionesHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new ObtenerMedicionesHandler(
                _proyectoRepositorioMock.Object,
                _medicionRepositorioMock.Object,
                mapper);
        }

        [Fact]
        public async Task Handle_ConProyectoConMediciones_DeberiaDevolverlasMapeadas()
        {
            // La factory de Medicion es internal: la única forma de crear
            // mediciones (también en tests) es a través del agregado. Eso
            // NO es una molestia: es la encapsulación funcionando.
            var proyecto = new ProyectoRenovableBuilder().Activado().Build();
            proyecto.RegistrarMedicion(DateTime.UtcNow.AddDays(-1), 100.5m);
            proyecto.RegistrarMedicion(DateTime.UtcNow.AddHours(-2), 200.5m);

            _proyectoRepositorioMock
                .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            _medicionRepositorioMock
                .Setup(r => r.GetByProyectoAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([.. proyecto.Mediciones]);

            var result = await _handler.Handle(new ObtenerMedicionesQuery(proyecto.Id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Sum(m => m.EnergiaGeneradaKWh).Should().Be(301m);
        }

        [Fact]
        public async Task Handle_ConProyectoInexistente_DeberiaDevolverNotFound()
        {
            _proyectoRepositorioMock
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProyectoRenovable?)null);

            var result = await _handler.Handle(new ObtenerMedicionesQuery(Guid.NewGuid()), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.NotFound);

            // Si el proyecto no existe, ni se consulta el repo de mediciones.
            _medicionRepositorioMock.Verify(
                r => r.GetByProyectoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
