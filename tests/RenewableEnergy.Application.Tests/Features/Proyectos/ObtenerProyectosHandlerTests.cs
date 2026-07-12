using AutoMapper;
using AwesomeAssertions;
using Moq;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectos;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Domain.Specifications;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del handler de listado: paginación, mapeo a ViewModel y
    /// composición de Specifications según los filtros.
    /// </summary>
    public sealed class ObtenerProyectosHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly ObtenerProyectosHandler _handler;

        public ObtenerProyectosHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new ObtenerProyectosHandler(_repositorioMock.Object, mapper);
        }

        [Fact]
        public async Task Handle_SinFiltros_DeberiaDevolverPaginaConMetadatos()
        {
            // Arrange: el repositorio devuelve 2 elementos de un total de 5.
            IReadOnlyList<ProyectoRenovable> proyectos =
            [
                new ProyectoRenovableBuilder().ConNombre("Solar A").Build(),
                new ProyectoRenovableBuilder().ConNombre("Eólico B").ConTipo(TipoEnergia.Eolica).Build()
            ];

            _repositorioMock
                .Setup(r => r.GetPaginadoAsync(null, 1, 2, It.IsAny<CancellationToken>()))
                .ReturnsAsync((proyectos, 5));

            // Act
            var result = await _handler.Handle(
                new ObtenerProyectosQuery(Pagina: 1, TamanoPagina: 2),
                CancellationToken.None);

            // Assert: metadatos de paginación calculados correctamente.
            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().HaveCount(2);
            result.Value.TotalCount.Should().Be(5);
            result.Value.TotalPages.Should().Be(3);
            result.Value.HasNextPage.Should().BeTrue();
            result.Value.HasPreviousPage.Should().BeFalse();
            result.Value.Items[0].Nombre.Should().Be("Solar A");
            result.Value.Items[0].Tipo.Should().Be(nameof(TipoEnergia.Solar));
        }

        [Fact]
        public async Task Handle_ConFiltroDeTipo_DeberiaPasarUnaSpecificationAlRepositorio()
        {
            Specification<ProyectoRenovable>? specRecibida = null;

            _repositorioMock
                .Setup(r => r.GetPaginadoAsync(
                    It.IsAny<Specification<ProyectoRenovable>?>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Specification<ProyectoRenovable>?, int, int, CancellationToken>(
                    (spec, _, _, _) => specRecibida = spec)
                .ReturnsAsync(([], 0));

            await _handler.Handle(
                new ObtenerProyectosQuery(Tipo: TipoEnergia.Solar, SoloActivos: true),
                CancellationToken.None);

            // La spec combinada (tipo AND activos) debe llegar al repositorio.
            specRecibida.Should().NotBeNull();

            // Verificación semántica: la spec solo acepta solares ACTIVOS.
            var solarActivo = new ProyectoRenovableBuilder().ConTipo(TipoEnergia.Solar).Activado().Build();
            var solarBorrador = new ProyectoRenovableBuilder().ConTipo(TipoEnergia.Solar).Build();

            specRecibida!.IsSatisfiedBy(solarActivo).Should().BeTrue();
            specRecibida.IsSatisfiedBy(solarBorrador).Should().BeFalse();
        }

        [Fact]
        public async Task Handle_SinResultados_DeberiaDevolverPaginaVacia()
        {
            _repositorioMock
                .Setup(r => r.GetPaginadoAsync(null, 1, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(([], 0));

            var result = await _handler.Handle(new ObtenerProyectosQuery(), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().BeEmpty();
            result.Value.TotalCount.Should().Be(0);
        }
    }
}
