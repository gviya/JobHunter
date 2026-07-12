using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.Specifications;
using RenewableEnergy.Domain.ValueObjects;
using RenewableEnergy.Infrastructure.Persistence.Repositories;
using RenewableEnergy.Infrastructure.Tests.Infrastructure;

namespace RenewableEnergy.Infrastructure.Tests.Repositories
{
    /// <summary>
    /// Tests de INTEGRACIÓN del ProyectoRepository contra SQL Server real
    /// (Testcontainers). Verifican el "roundtrip" completo: dominio →
    /// EF Core → SQL → EF Core → dominio, incluidos value converters,
    /// owned types, interceptors y la traducción de Specifications a SQL.
    ///
    /// Patrón: cada operación usa un DbContext NUEVO, igual que en
    /// producción (un contexto por request). Leer con el mismo contexto que
    /// escribió puede dar falsos positivos (lee de la caché de EF, no de SQL).
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class ProyectoRepositoryTests(SqlServerContainerFixture fixture)
        : IClassFixture<SqlServerContainerFixture>
    {
        private static ProyectoRenovable CrearProyecto(
            TipoEnergia tipo = TipoEnergia.Solar,
            bool activar = false)
        {
            // Nombre único: todos los tests comparten la misma BD del contenedor.
            var proyecto = ProyectoRenovable.Create(
                $"Proyecto {tipo} {Guid.NewGuid():N}",
                tipo,
                PotenciaKW.Create(500m),
                Coordenadas.Create(40.4168, -3.7038),
                descripcion: "Proyecto de test de integración");

            if (activar)
            {
                proyecto.Activar();
            }

            return proyecto;
        }

        [DockerRequiredFact]
        public async Task Add_DeberiaPersistirElProyectoConSusValueObjects()
        {
            var proyecto = CrearProyecto();

            // ESCRITURA con un contexto...
            await using (var contexto = fixture.CrearContexto())
            {
                new ProyectoRepository(contexto).Add(proyecto);
                await contexto.SaveChangesAsync();
            }

            // ...LECTURA con otro (roundtrip real por SQL).
            await using var contextoLectura = fixture.CrearContexto();
            var recuperado = await new ProyectoRepository(contextoLectura).GetByIdAsync(proyecto.Id);

            recuperado.Should().NotBeNull();
            recuperado!.Nombre.Should().Be(proyecto.Nombre);
            recuperado.Potencia.Valor.Should().Be(500m);
            recuperado.Ubicacion.Latitud.Should().Be(40.4168);
            recuperado.Estado.Should().Be(EstadoProyecto.Borrador);

            // El AuditInterceptor debe haber rellenado CreatedAt.
            recuperado.CreatedAt.Should().NotBe(default);
        }

        [DockerRequiredFact]
        public async Task GetByIdAsync_CuandoNoExiste_DeberiaDevolverNull()
        {
            await using var contexto = fixture.CrearContexto();

            var resultado = await new ProyectoRepository(contexto).GetByIdAsync(Guid.NewGuid());

            resultado.Should().BeNull();
        }

        [DockerRequiredFact]
        public async Task GetBySpecification_DeberiaFiltrarEnSql()
        {
            // Arrange: una geotérmica activa, una geotérmica en borrador y
            // una biomasa activa. Geotermica se usa solo en este test para
            // no interferir con los datos de los demás.
            var geotermicaActiva = CrearProyecto(TipoEnergia.Geotermica, activar: true);
            var geotermicaBorrador = CrearProyecto(TipoEnergia.Geotermica);
            var biomasaActiva = CrearProyecto(TipoEnergia.Biomasa, activar: true);

            await using (var contexto = fixture.CrearContexto())
            {
                var repositorio = new ProyectoRepository(contexto);
                repositorio.Add(geotermicaActiva);
                repositorio.Add(geotermicaBorrador);
                repositorio.Add(biomasaActiva);
                await contexto.SaveChangesAsync();
            }

            // Act: Specification COMBINADA (tipo AND activos), traducida a SQL.
            var spec = new ProyectosPorTipoSpec(TipoEnergia.Geotermica)
                .And(new ProyectosActivosSpec());

            await using var contextoLectura = fixture.CrearContexto();
            var resultado = await new ProyectoRepository(contextoLectura).GetBySpecificationAsync(spec);

            resultado.Should().ContainSingle()
                .Which.Id.Should().Be(geotermicaActiva.Id);
        }

        [DockerRequiredFact]
        public async Task ExisteNombreAsync_DeberiaDetectarDuplicadosYRespetarLaExclusion()
        {
            var proyecto = CrearProyecto();

            await using (var contexto = fixture.CrearContexto())
            {
                new ProyectoRepository(contexto).Add(proyecto);
                await contexto.SaveChangesAsync();
            }

            await using var contextoLectura = fixture.CrearContexto();
            var repositorio = new ProyectoRepository(contextoLectura);

            (await repositorio.ExisteNombreAsync(proyecto.Nombre)).Should().BeTrue();

            // Excluyendo el propio proyecto (caso "actualizar sin cambiar nombre").
            (await repositorio.ExisteNombreAsync(proyecto.Nombre, proyecto.Id)).Should().BeFalse();

            (await repositorio.ExisteNombreAsync("nombre-que-no-existe")).Should().BeFalse();
        }

        [DockerRequiredFact]
        public async Task SaveChanges_DeberiaVolcarLosEventosDeDominioAlOutbox()
        {
            // El proyecto levanta ProyectoCreado al nacer; el OutboxInterceptor
            // debe convertirlo en una fila de OutboxMessages EN LA MISMA
            // transacción. Esta es la mitad "escritura" del patrón Outbox.
            var proyecto = CrearProyecto();

            await using (var contexto = fixture.CrearContexto())
            {
                new ProyectoRepository(contexto).Add(proyecto);
                await contexto.SaveChangesAsync();
            }

            await using var contextoLectura = fixture.CrearContexto();

            var mensaje = await contextoLectura.OutboxMessages
                .Where(m => m.Type == "ProyectoCreado" && m.Content.Contains(proyecto.Id.ToString()))
                .FirstOrDefaultAsync();

            mensaje.Should().NotBeNull();
            mensaje!.ProcessedOnUtc.Should().BeNull("el mensaje aún no lo procesó el OutboxProcessor");

            // Y la entidad quedó sin eventos pendientes (el interceptor los limpió).
            var recuperado = await contextoLectura.Proyectos.FirstAsync(p => p.Id == proyecto.Id);
            recuperado.GetDomainEvents().Should().BeEmpty();
        }

        [DockerRequiredFact]
        public async Task GetByIdConDetalleAsync_DeberiaCargarMedicionesEInstalaciones()
        {
            var proyecto = CrearProyecto(activar: true);
            proyecto.AgregarInstalacion("String A", PotenciaKW.Create(250m));
            proyecto.RegistrarMedicion(DateTime.UtcNow.AddHours(-2), 1240.5m);

            await using (var contexto = fixture.CrearContexto())
            {
                new ProyectoRepository(contexto).Add(proyecto);
                await contexto.SaveChangesAsync();
            }

            await using var contextoLectura = fixture.CrearContexto();
            var recuperado = await new ProyectoRepository(contextoLectura)
                .GetByIdConDetalleAsync(proyecto.Id);

            recuperado.Should().NotBeNull();
            recuperado!.Instalaciones.Should().ContainSingle();
            recuperado.Mediciones.Should().ContainSingle()
                .Which.EnergiaGeneradaKWh.Should().Be(1240.5m);
        }
    }
}
