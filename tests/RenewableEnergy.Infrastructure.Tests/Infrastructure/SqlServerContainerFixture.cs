using Microsoft.EntityFrameworkCore;
using RenewableEnergy.Infrastructure.Persistence;
using RenewableEnergy.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;

namespace RenewableEnergy.Infrastructure.Tests.Infrastructure
{
    /// <summary>
    /// FIXTURE de xUnit: levanta UN contenedor de SQL Server 2022 para toda
    /// la clase de tests (IClassFixture) y lo destruye al final.
    ///
    /// ¿POR QUÉ TESTCONTAINERS?
    /// - La BD es REAL: el mismo motor que producción, no un InMemory que
    ///   acepta cosas que SQL Server rechazaría.
    /// - Es EFÍMERA: cada ejecución parte de cero, sin estado compartido
    ///   entre desarrolladores ni limpieza manual.
    /// </summary>
    public sealed class SqlServerContainerFixture : IAsyncLifetime
    {
        // Imagen FIJADA explícitamente: builds reproducibles (si mañana sale
        // SQL Server 2025, este test no cambia de motor sin que lo decidas).
        private readonly MsSqlContainer _contenedor =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        private bool _iniciado;

        public async Task InitializeAsync()
        {
            // Sin Docker no se intenta arrancar: los tests se omitirán solos
            // gracias a [DockerRequiredFact].
            if (!Docker.Disponible)
            {
                return;
            }

            await _contenedor.StartAsync();
            _iniciado = true;

            // Se aplican las MIGRACIONES reales: este fixture también valida
            // que la migración InitialCreate funciona contra SQL Server.
            await using var contexto = CrearContexto();
            await contexto.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            if (_iniciado)
            {
                await _contenedor.DisposeAsync();
            }
        }

        /// <summary>
        /// DbContext apuntando al contenedor, con los MISMOS interceptors que
        /// producción (auditoría + outbox): se prueba el pipeline completo.
        /// </summary>
        public ApplicationDbContext CrearContexto()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_contenedor.GetConnectionString())
                .AddInterceptors(new AuditInterceptor(), new OutboxInterceptor())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
