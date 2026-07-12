using AutoMapper;
using AwesomeAssertions;
using RenewableEnergy.Application.Features.Proyectos.Queries.ObtenerProyectoPorId;
using RenewableEnergy.Application.Mappings;
using RenewableEnergy.Application.Tests.Builders;

namespace RenewableEnergy.Application.Tests.Mappings
{
    /// <summary>
    /// Tests del perfil de AutoMapper. El primero es ORO: valida TODA la
    /// configuración de mapeos de una vez — si un ViewModel gana una
    /// propiedad y nadie la mapea, este test falla en CI antes de que el
    /// bug llegue a producción como un campo silenciosamente vacío.
    /// </summary>
    public sealed class ProyectoMappingProfileTests
    {
        private readonly MapperConfiguration _configuration = new(cfg =>
            cfg.AddProfile<ProyectoMappingProfile>());

        [Fact]
        public void Configuracion_DeberiaSerValida()
        {
            // Verifica que TODOS los miembros de TODOS los destinos tienen
            // origen definido. Barato y de altísimo valor.
            _configuration.AssertConfigurationIsValid();
        }

        [Fact]
        public void Map_ProyectoADetalle_DeberiaAplanarVOsYCalcularTotal()
        {
            var mapper = _configuration.CreateMapper();

            var proyecto = new ProyectoRenovableBuilder()
                .ConNombre("Parque Test")
                .ConPotencia(750m)
                .Activado()
                .Build();

            proyecto.RegistrarMedicion(DateTime.UtcNow.AddDays(-2), 100.5m);
            proyecto.RegistrarMedicion(DateTime.UtcNow.AddDays(-1), 200.5m);

            var viewModel = mapper.Map<ProyectoDetalleViewModel>(proyecto);

            // Value Objects APLANADOS a primitivos.
            viewModel.PotenciaKW.Should().Be(750m);
            viewModel.Latitud.Should().Be(40.4168);

            // Campo CALCULADO en el mapping.
            viewModel.TotalEnergiaGeneradaKWh.Should().Be(301m);
            viewModel.Mediciones.Should().HaveCount(2);
        }
    }
}
