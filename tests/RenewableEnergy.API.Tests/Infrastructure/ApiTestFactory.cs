using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenewableEnergy.Infrastructure.Persistence;
using RenewableEnergy.Infrastructure.Persistence.Interceptors;
using RenewableEnergy.Infrastructure.Persistence.Seeds;

namespace RenewableEnergy.API.Tests.Infrastructure
{
    /// <summary>
    /// FACTORY de la API para tests E2E. Arranca la aplicación real con tres
    /// diferencias controladas respecto a producción:
    ///
    /// 1. Entorno "Testing": no se ejecuta el arranque de Development
    ///    (migraciones + seed automáticos) — lo controla el propio factory.
    /// 2. Configuración en memoria: secretos de test, outbox y messaging
    ///    apagados (los tests no necesitan workers de fondo).
    /// 3. SQLite EN MEMORIA en lugar de SQL Server: la BD vive mientras la
    ///    conexión maestra esté abierta y desaparece al cerrar (aislamiento
    ///    total entre ejecuciones). Los interceptors (auditoría, outbox)
    ///    siguen activos: se prueba el pipeline de verdad.
    /// </summary>
    public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        // Mientras esta conexión siga abierta, la BD en memoria existe.
        private readonly SqliteConnection _conexion = new("DataSource=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // OJO, detalle importante de WebApplicationFactory con minimal
            // hosting: ConfigureAppConfiguration se aplicaría DESPUÉS de que
            // Program.cs lea la configuración durante el registro de
            // servicios. UseSetting, en cambio, alimenta la configuración
            // INICIAL del WebApplicationBuilder — es la vía correcta para
            // valores que el arranque necesita (como Jwt:Secret).
            var configuracionDeTests = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "RenewableEnergyAPI",
                ["Jwt:Audience"] = "RenewableEnergyClient",
                ["Jwt:Secret"] = "clave_secreta_solo_para_tests_32_caracteres!",
                ["Jwt:ExpirationInMinutes"] = "60",
                ["ApiKey"] = "test-api-key",
                ["Outbox:Enabled"] = "false",
                ["Messaging:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["RateLimiting:Policy"] = "Fixed",
                ["RateLimiting:PermitLimit"] = "1000",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:QueueLimit"] = "100"
            };

            foreach (var (clave, valor) in configuracionDeTests)
            {
                builder.UseSetting(clave, valor);
            }

            builder.ConfigureTestServices(services =>
            {
                // Se QUITA el DbContext de SQL Server y se registra el de
                // SQLite, conservando los interceptors de producción.
                // Desde EF Core 9, AddDbContext registra la configuración como
                // IDbContextOptionsConfiguration<T>: hay que retirarla TAMBIÉN,
                // o quedarían dos proveedores (SqlServer + Sqlite) y EF lanza
                // "services for database providers ... have been registered".
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<ApplicationDbContext>));
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));

                services.AddDbContext<ApplicationDbContext>((sp, options) =>
                {
                    options.UseSqlite(_conexion);
                    options.AddInterceptors(
                        sp.GetRequiredService<AuditInterceptor>(),
                        sp.GetRequiredService<OutboxInterceptor>());
                });
            });
        }

        /// <summary>Arranca la app, crea el esquema y siembra los datos demo.</summary>
        public async Task InitializeAsync()
        {
            await _conexion.OpenAsync();

            // CreateClient fuerza la construcción del host.
            _ = CreateClient();

            using var scope = Services.CreateScope();
            var contexto = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // EnsureCreated (no Migrate): las migraciones son de SQL Server;
            // aquí el esquema se genera directamente del modelo.
            await contexto.Database.EnsureCreatedAsync();

            // El MISMO seeder de producción: usuario demo + 3 proyectos.
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync();
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await _conexion.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
