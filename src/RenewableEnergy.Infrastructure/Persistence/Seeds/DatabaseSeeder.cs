using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Entities;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Domain.ValueObjects;

namespace RenewableEnergy.Infrastructure.Persistence.Seeds
{
    /// <summary>
    /// SEEDER: datos de ejemplo realistas para desarrollo.
    ///
    /// Lo invoca la API al arrancar SOLO en Development (ver
    /// WebApplicationExtensions.ApplyMigrationsAndSeedAsync). Es idempotente:
    /// si ya hay datos, no hace nada.
    ///
    /// USUARIO DEMO para probar el login en Swagger/Postman:
    ///   email:    demo@renovables.demo
    ///   password: Demo1234!
    /// </summary>
    public sealed class DatabaseSeeder(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<DatabaseSeeder> logger)
    {
        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            if (await context.Proyectos.AnyAsync(cancellationToken))
            {
                logger.LogInformation("La base de datos ya tiene datos: seed omitido");
                return;
            }

            logger.LogInformation("Sembrando datos de ejemplo...");

            // ── Usuario demo (para el endpoint de login) ────────────────
            var usuarioDemo = Usuario.Create(
                "Usuaria Demo",
                Email.Create("demo@renovables.demo"),
                passwordHasher.Hash("Demo1234!"));

            context.Usuarios.Add(usuarioDemo);

            // ── Proyectos de ejemplo en distintos estados ───────────────
            // Se usan las factories del DOMINIO: los seeds también respetan
            // las reglas de negocio (y levantan eventos, que irán al outbox).
            var parqueSolar = ProyectoRenovable.Create(
                "Parque Solar Madrid Norte",
                TipoEnergia.Solar,
                PotenciaKW.Create(500m),
                Coordenadas.Create(40.4168, -3.7038),
                usuarioDemo.Id,
                "Planta fotovoltaica de 500 kW en la sierra norte de Madrid.");

            parqueSolar.AgregarInstalacion("String fotovoltaico A", PotenciaKW.Create(250m));
            parqueSolar.AgregarInstalacion("String fotovoltaico B", PotenciaKW.Create(250m));
            parqueSolar.Activar();
            parqueSolar.RegistrarMedicion(DateTime.UtcNow.AddDays(-2), 1240.5m);
            parqueSolar.RegistrarMedicion(DateTime.UtcNow.AddDays(-1), 1315.2m);

            var parqueEolico = ProyectoRenovable.Create(
                "Parque Eólico Galicia Costa",
                TipoEnergia.Eolica,
                PotenciaKW.Create(2000m),
                Coordenadas.Create(43.3623, -8.4115),
                usuarioDemo.Id,
                "Cuatro aerogeneradores de 500 kW frente a la costa de A Coruña.");

            parqueEolico.AgregarInstalacion("Aerogenerador AG-01", PotenciaKW.Create(500m));
            parqueEolico.Activar();
            parqueEolico.RegistrarMedicion(DateTime.UtcNow.AddHours(-6), 3890.0m);

            var centralHidraulica = ProyectoRenovable.Create(
                "Minihidráulica Pirineo Aragonés",
                TipoEnergia.Hidraulica,
                PotenciaKW.Create(750m),
                Coordenadas.Create(42.5701, -0.5488),
                usuarioDemo.Id,
                "Central de agua fluyente en el río Gállego. Pendiente de activación.");

            context.Proyectos.AddRange(parqueSolar, parqueEolico, centralHidraulica);

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Seed completado: 3 proyectos y el usuario demo (demo@renovables.demo / Demo1234!)");
        }
    }
}
