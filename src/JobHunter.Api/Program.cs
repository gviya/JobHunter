using Hangfire;
using Hangfire.Storage.SQLite;
using JobHunter.Application;
using JobHunter.Application.Features.Reports;
using JobHunter.Infrastructure;
using JobHunter.Infrastructure.Jobs;
using JobHunter.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ==================================================================
// Program.cs — PUNTO DE ENTRADA y COMPOSITION ROOT de JobHunter AI.
// Registra las capas, configura Hangfire (server + dashboard), aplica
// migraciones al arrancar y expone los endpoints mínimos.
// ==================================================================

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configuración personal PRIVADA (perfil del candidato, filtros de
    // ubicación, pesos de tecnologías...). El fichero está en .gitignore;
    // hay un ejemplo público en appsettings.Local.template.json.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

    // Las BD SQLite viven en ./data — hay que crear la carpeta antes de que
    // EF o Hangfire intenten abrir los ficheros.
    Directory.CreateDirectory("data");

    builder.Host.UseSerilog((context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Aplicacion", "JobHunter.Api"));

    // ── Capas ───────────────────────────────────────────────────────
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── Hangfire (scheduler) sobre SQLite ───────────────────────────
    var hangfireDb = SqlitePath(builder.Configuration.GetConnectionString("Hangfire"), "data/hangfire.db");
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSQLiteStorage(hangfireDb));
    builder.Services.AddHangfireServer();

    var app = builder.Build();

    // ── Migración automática al arrancar ────────────────────────────
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<JobHunterDbContext>();
        await db.Database.MigrateAsync();
    }

    // ── Dashboard de Hangfire ───────────────────────────────────────
    app.UseHangfireDashboard("/hangfire");

    // ── Job recurrente diario ───────────────────────────────────────
    var cron = app.Configuration["Schedule:CronExpression"] ?? "0 8 * * *";
    RecurringJob.AddOrUpdate<DailySearchJob>(
        "daily-search",
        job => job.RunAsync(CancellationToken.None),
        cron);

    // ── Endpoints ───────────────────────────────────────────────────
    app.MapPost("/jobs/run-now", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<DailySearchJob>(job => job.RunAsync(CancellationToken.None));
        return Results.Accepted($"/hangfire/jobs/details/{id}", new { jobId = id });
    });

    app.MapGet("/reports/latest", async (ISender sender) =>
    {
        var report = await sender.Send(new GetLatestReport.Query());
        return report is null
            ? Results.NotFound(new { message = "Todavía no hay ningún informe." })
            : Results.Content(report.HtmlContent, "text/html");
    });

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // Traza qué perfil se ha cargado: permite detectar de un vistazo si se
    // está usando appsettings.Local.json o el perfil de ejemplo por defecto.
    var perfil = app.Services.GetRequiredService<JobHunter.Domain.Entities.UserProfile>();
    Log.Information(
        "Perfil del candidato: {Techs} techs con peso, {Excluidas} excluidas, ciudad \"{Ciudad}\", {Zonas} zonas preferidas, {Areas} áreas excluidas.",
        perfil.TechWeights.Count, perfil.ExcludedTechs.Length, perfil.City,
        perfil.PreferredZones.Length, perfil.ExcludedAreas.Length);

    Log.Information("JobHunter AI arrancando en entorno {Entorno}", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La aplicación no pudo arrancar");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Extrae la ruta del fichero SQLite de una cadena "Data Source=...".
static string SqlitePath(string? connectionString, string fallback)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return fallback;
    }

    var trimmed = connectionString.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
    return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
}

/// <summary>
/// Clase parcial vacía que expone el Program implícito como tipo público:
/// los tests de arquitectura lo usan como ancla del assembly de la API.
/// </summary>
public partial class Program;
