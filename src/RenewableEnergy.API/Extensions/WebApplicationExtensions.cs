using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RenewableEnergy.API.Configuration;
using RenewableEnergy.API.Middleware;
using RenewableEnergy.Domain.Enums;
using RenewableEnergy.Infrastructure.Persistence;
using RenewableEnergy.Infrastructure.Persistence.Seeds;

namespace RenewableEnergy.API.Extensions
{
    /// <summary>
    /// CONFIGURACIÓN DEL PIPELINE HTTP y utilidades de arranque.
    /// El ORDEN de los middleware importa (se explica en UseApi).
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// Monta el pipeline de la petición. Orden (de fuera hacia dentro):
        ///
        ///   ExceptionHandling → RequestLogging → HTTPS → RateLimiter →
        ///   AuthN → AuthZ → Controllers / Minimal APIs / Health
        ///
        /// ExceptionHandling va PRIMERO para capturar errores de TODO lo
        /// demás; Authentication antes que Authorization (hay que saber quién
        /// eres antes de decidir qué puedes hacer).
        /// </summary>
        public static WebApplication UseApi(this WebApplication app)
        {
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                // Swagger solo en desarrollo: no expongas el contrato interno
                // en producción salvo decisión consciente.
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "RenewableEnergy API v1");
                });
            }

            app.UseHttpsRedirection();

            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            var rateLimitSettings = app.Services
                .GetRequiredService<IOptions<RateLimitSettings>>()
                .Value;

            app.MapControllers()
                .RequireRateLimiting(rateLimitSettings.Policy);

            MapHealthEndpoints(app);
            MapCatalogoEndpoints(app);

            return app;
        }

        /// <summary>
        /// HEALTH CHECKS con la semántica que espera Kubernetes:
        /// - /health/live  → ¿el proceso responde? (liveness probe)
        /// - /health/ready → ¿puede atender tráfico? Incluye la BD (readiness)
        /// - /health       → estado completo (humanos y monitorización)
        /// </summary>
        private static void MapHealthEndpoints(WebApplication app)
        {
            app.MapHealthChecks("/health");

            // live: sin checks — si el proceso contesta, está vivo.
            app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = _ => false
            });

            // ready: solo los checks etiquetados "ready" (la base de datos).
            app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready")
            });
        }

        /// <summary>
        /// MINIMAL APIs de ejemplo (la alternativa moderna a los Controllers,
        /// ideal para endpoints simples sin lógica). Compáralas con
        /// ProyectosController: mismas capacidades, menos ceremonia.
        /// </summary>
        private static void MapCatalogoEndpoints(WebApplication app)
        {
            var grupo = app.MapGroup("/api/v1/catalogo")
                .WithTags("Catalogo");

            grupo.MapGet("/tipos-energia", () => Results.Ok(Enum.GetNames<TipoEnergia>()))
                .WithName("ObtenerTiposEnergia")
                .WithSummary("Tipos de energía soportados (enum del dominio).");

            grupo.MapGet("/estados-proyecto", () => Results.Ok(Enum.GetNames<EstadoProyecto>()))
                .WithName("ObtenerEstadosProyecto")
                .WithSummary("Estados del ciclo de vida de un proyecto.");
        }

        /// <summary>
        /// ARRANQUE EN DESARROLLO: aplica migraciones pendientes y siembra
        /// datos demo. En PRODUCCIÓN las migraciones se aplican desde el
        /// pipeline de CI/CD (paso controlado), nunca al arrancar el pod.
        /// </summary>
        public static async Task ApplyMigrationsAndSeedAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.MigrateAsync();
                logger.LogInformation("Migraciones aplicadas correctamente");

                var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
                await seeder.SeedAsync();
            }
            catch (Exception ex)
            {
                // No tumbamos la app en dev: se loguea para que el desarrollador
                // vea que la BD no está disponible (¿docker compose arrancado?).
                logger.LogError(ex, "Error aplicando migraciones o seed. ¿Está la base de datos levantada?");
            }
        }
    }
}
