using AwesomeAssertions;
using RenewableEnergy.Domain.Common;
using RenewableEnergy.Domain.DomainEvents;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Domain.Tests.Entities
{
    /// <summary>
    /// Tests de la LÓGICA DE DOMINIO del agregado ProyectoRenovable.
    ///
    /// Qué se prueba aquí (y solo aquí):
    /// - Que las factories crean objetos en estado válido.
    /// - Que las transiciones de estado respetan las reglas.
    /// - Que los EVENTOS DE DOMINIO se levantan cuando toca.
    /// Sin mocks ni infraestructura: el dominio es C# puro.
    ///
    /// Convención de nombres: Metodo_Escenario_ResultadoEsperado.
    /// </summary>
    public sealed class ProyectoRenovableTests
    {
        // Helper: proyecto válido recién creado (estado Borrador).
        private static ProyectoRenovable CrearProyectoValido()
        {
            return ProyectoRenovable.Create(
                "Parque Solar Test",
                TipoEnergia.Solar,
                PotenciaKW.Create(500m),
                Coordenadas.Create(40.4168, -3.7038));
        }

        [Fact]
        public void Create_ConDatosValidos_DeberiaCrearEnEstadoBorrador()
        {
            var proyecto = CrearProyectoValido();

            proyecto.Nombre.Should().Be("Parque Solar Test");
            proyecto.Tipo.Should().Be(TipoEnergia.Solar);
            proyecto.Estado.Should().Be(EstadoProyecto.Borrador);
            proyecto.Potencia.Valor.Should().Be(500m);
            proyecto.Id.Should().NotBeEmpty();
        }

        [Fact]
        public void Create_ConDatosValidos_DeberiaLevantarEventoProyectoCreado()
        {
            var proyecto = CrearProyectoValido();

            var eventos = proyecto.GetDomainEvents();

            eventos.Should().ContainSingle()
                .Which.Should().BeOfType<ProyectoCreado>()
                .Which.ProyectoId.Should().Be(proyecto.Id);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_ConNombreVacio_DeberiaLanzarDomainException(string nombreInvalido)
        {
            var accion = () => ProyectoRenovable.Create(
                nombreInvalido,
                TipoEnergia.Eolica,
                PotenciaKW.Create(100m),
                Coordenadas.Create(0, 0));

            accion.Should().Throw<DomainException>()
                .WithMessage("*nombre*obligatorio*");
        }

        [Fact]
        public void Activar_DesdeBorrador_DeberiaCambiarAActivoYLevantarEvento()
        {
            var proyecto = CrearProyectoValido();
            proyecto.ClearDomainEvents();

            proyecto.Activar();

            proyecto.Estado.Should().Be(EstadoProyecto.Activo);
            proyecto.FechaActivacion.Should().NotBeNull();
            proyecto.GetDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<ProyectoActivado>();
        }

        [Fact]
        public void Activar_CuandoYaEstaActivo_DeberiaLanzarDomainException()
        {
            var proyecto = CrearProyectoValido();
            proyecto.Activar();

            var accion = () => proyecto.Activar();

            accion.Should().Throw<DomainException>()
                .WithMessage("*Borrador*");
        }

        [Fact]
        public void RegistrarMedicion_EnProyectoActivo_DeberiaCrearMedicionYLevantarEvento()
        {
            var proyecto = CrearProyectoValido();
            proyecto.Activar();
            proyecto.ClearDomainEvents();

            var medicion = proyecto.RegistrarMedicion(DateTime.UtcNow.AddHours(-1), 1240.5m);

            medicion.EnergiaGeneradaKWh.Should().Be(1240.5m);
            medicion.ProyectoRenovableId.Should().Be(proyecto.Id);
            proyecto.Mediciones.Should().ContainSingle();
            proyecto.GetDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<MedicionRegistrada>();
        }

        [Fact]
        public void RegistrarMedicion_EnProyectoBorrador_DeberiaLanzarDomainException()
        {
            var proyecto = CrearProyectoValido();

            var accion = () => proyecto.RegistrarMedicion(DateTime.UtcNow, 100m);

            accion.Should().Throw<DomainException>()
                .WithMessage("*Activos*");
        }

        [Fact]
        public void RegistrarMedicion_ConFechaFutura_DeberiaLanzarDomainException()
        {
            var proyecto = CrearProyectoValido();
            proyecto.Activar();

            var accion = () => proyecto.RegistrarMedicion(DateTime.UtcNow.AddDays(1), 100m);

            accion.Should().Throw<DomainException>()
                .WithMessage("*futura*");
        }

        [Fact]
        public void Actualizar_EnEstadoTerminal_DeberiaLanzarDomainException()
        {
            var proyecto = CrearProyectoValido();
            proyecto.Cancelar();

            var accion = () => proyecto.Actualizar(
                "Nuevo Nombre",
                null,
                PotenciaKW.Create(100m),
                Coordenadas.Create(0, 0));

            accion.Should().Throw<DomainException>()
                .WithMessage("*terminal*");
        }

        [Fact]
        public void Completar_DesdeActivo_DeberiaCambiarACompletado()
        {
            var proyecto = CrearProyectoValido();
            proyecto.Activar();

            proyecto.Completar();

            proyecto.Estado.Should().Be(EstadoProyecto.Completado);
        }

        [Theory]
        [InlineData(EstadoProyecto.Borrador, true)]
        [InlineData(EstadoProyecto.Activo, false)]
        public void PuedeSerEliminado_SegunEstado_DeberiaAplicarLaRegla(
            EstadoProyecto estadoObjetivo,
            bool esperado)
        {
            var proyecto = CrearProyectoValido();

            if (estadoObjetivo == EstadoProyecto.Activo)
            {
                proyecto.Activar();
            }

            proyecto.PuedeSerEliminado.Should().Be(esperado);
        }

        [Fact]
        public void AgregarInstalacion_EnProyectoNoTerminal_DeberiaAnadirla()
        {
            var proyecto = CrearProyectoValido();

            var instalacion = proyecto.AgregarInstalacion("String A", PotenciaKW.Create(250m));

            proyecto.Instalaciones.Should().ContainSingle();
            instalacion.Nombre.Should().Be("String A");
        }
    }
}
