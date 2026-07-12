using System.Text;
using System.Text.Json;
using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RenewableEnergy.Application.Common.Models;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Interfaces;

namespace RenewableEnergy.Application.Tests.Features.Proyectos
{
    /// <summary>
    /// Tests del handler de detalle: cómo se testea el patrón CACHE-ASIDE
    /// con Moq. Las extensiones GetStringAsync/SetStringAsync de
    /// IDistributedCache llaman por debajo a GetAsync/SetAsync (bytes):
    /// eso es lo que se mockea y verifica.
    /// </summary>
    public sealed class ObtenerProyectoPorIdHandlerTests
    {
        private readonly Mock<IProyectoRepository> _repositorioMock = new();
        private readonly Mock<IDistributedCache> _cacheMock = new();
        private readonly ObtenerProyectoPorIdHandler _handler;

        public ObtenerProyectoPorIdHandlerTests()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<ProyectoMappingProfile>())
                .CreateMapper();

            _handler = new ObtenerProyectoPorIdHandler(
                _repositorioMock.Object,
                _cacheMock.Object,
                mapper,
                NullLogger<ObtenerProyectoPorIdHandler>.Instance);
        }

        [Fact]
        public async Task Handle_ConCacheMiss_DeberiaLeerDeBdYGuardarEnCache()
        {
            // Moq devuelve null por defecto en GetAsync → cache MISS.
            var proyecto = new ProyectoRenovableBuilder().ConNombre("Proyecto Cacheable").Build();

            _repositorioMock
                .Setup(r => r.GetByIdConDetalleAsync(proyecto.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(proyecto);

            var result = await _handler.Handle(new ObtenerProyectoPorIdQuery(proyecto.Id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Nombre.Should().Be("Proyecto Cacheable");

            // Tras el miss, el ViewModel debe quedar escrito en la caché.
            _cacheMock.Verify(
                c => c.SetAsync(
                    $"proyecto:detalle:{proyecto.Id}",
                    It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ConCacheHit_DeberiaResponderSinTocarLaBaseDeDatos()
        {
            var id = Guid.NewGuid();
            var viewModelCacheado = new ProyectoDetalleViewModel { Id = id, Nombre = "Desde la caché" };

            // Se simula el HIT: la caché ya tiene el JSON serializado.
            _cacheMock
                .Setup(c => c.GetAsync($"proyecto:detalle:{id}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(viewModelCacheado)));

            var result = await _handler.Handle(new ObtenerProyectoPorIdQuery(id), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Nombre.Should().Be("Desde la caché");

            // LO IMPORTANTE: con hit, el repositorio NUNCA se consulta.
            _repositorioMock.Verify(
                r => r.GetByIdConDetalleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_ConProyectoInexistente_DeberiaDevolverNotFoundSinCachear()
        {
            _repositorioMock
                .Setup(r => r.GetByIdConDetalleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProyectoRenovable?)null);

            var result = await _handler.Handle(new ObtenerProyectoPorIdQuery(Guid.NewGuid()), CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            result.Error!.Tipo.Should().Be(ErrorType.NotFound);

            // Los "no encontrado" NO se cachean (cachearlos requeriría
            // invalidación también en la creación — decisión documentada).
            _cacheMock.Verify(
                c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
