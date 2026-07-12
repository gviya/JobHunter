using AwesomeAssertions;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.Specifications;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Tests.Specifications
{
    /// <summary>
    /// Tests del PATRÓN SPECIFICATION: criterios individuales y su
    /// COMPOSICIÓN con And/Or/Not evaluada en memoria (IsSatisfiedBy).
    /// La traducción a SQL la verifica el test de integración del repositorio.
    /// </summary>
    public sealed class SpecificationTests
    {
        private static ProyectoRenovable CrearProyecto(TipoEnergia tipo, bool activar = false)
        {
            var proyecto = ProyectoRenovable.Create(
                $"Proyecto {tipo} {Guid.NewGuid():N}",
                tipo,
                PotenciaKW.Create(100m),
                Coordenadas.Create(0, 0));

            if (activar)
            {
                proyecto.Activar();
            }

            return proyecto;
        }

        [Fact]
        public void ProyectosActivosSpec_DeberiaAceptarSoloActivos()
        {
            var spec = new ProyectosActivosSpec();

            spec.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar, activar: true)).Should().BeTrue();
            spec.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar)).Should().BeFalse();
        }

        [Fact]
        public void And_CombinandoDosSpecs_DeberiaExigirAmbas()
        {
            var solaresActivos = new ProyectosPorTipoSpec(TipoEnergia.Solar)
                .And(new ProyectosActivosSpec());

            solaresActivos.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar, activar: true)).Should().BeTrue();
            solaresActivos.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar)).Should().BeFalse();
            solaresActivos.IsSatisfiedBy(CrearProyecto(TipoEnergia.Eolica, activar: true)).Should().BeFalse();
        }

        [Fact]
        public void Or_CombinandoDosTipos_DeberiaAceptarCualquiera()
        {
            var solarOEolica = new ProyectosPorTipoSpec(TipoEnergia.Solar)
                .Or(new ProyectosPorTipoSpec(TipoEnergia.Eolica));

            solarOEolica.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar)).Should().BeTrue();
            solarOEolica.IsSatisfiedBy(CrearProyecto(TipoEnergia.Eolica)).Should().BeTrue();
            solarOEolica.IsSatisfiedBy(CrearProyecto(TipoEnergia.Biomasa)).Should().BeFalse();
        }

        [Fact]
        public void Not_NegandoUnaSpec_DeberiaInvertirla()
        {
            var noSolares = new ProyectosPorTipoSpec(TipoEnergia.Solar).Not();

            noSolares.IsSatisfiedBy(CrearProyecto(TipoEnergia.Eolica)).Should().BeTrue();
            noSolares.IsSatisfiedBy(CrearProyecto(TipoEnergia.Solar)).Should().BeFalse();
        }
    }
}
