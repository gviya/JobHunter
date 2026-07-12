using Azure.Identity;
using RenewableEnergy.API.Extensions;
using RenewableEnergy.Application;
using RenewableEnergy.Infrastructure;
using Serilog;

// ==================================================================
// Program.cs — PUNTO DE ENTRADA y COMPOSITION ROOT de la aplicación.
//
// Fíjate en lo CORTO que es: cada capa registra sus servicios en su
// propio método de extensión (AddApplication / AddInfrastructure /
// AddApi) y el pipeline HTTP se monta en UseApi(). Este archivo solo
// ORQUESTA el arranque.
// ==================================================================

// LOGGER DE ARRANQUE (bootstrap): captura errores que ocurran ANTES de
// que la configuración completa de Serilog esté lista.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── AZURE KEY VAULT (solo producción) ───────────────────────────
    // Si hay una URL de Key Vault configurada, los secretos (cadena de
    // conexión, JWT_SECRET...) se cargan como configuración. La identidad
    // la resuelve DefaultAzureCredential: Managed Identity en Azure,
    // az login / variables de entorno en local.
    var keyVaultUrl = builder.Configuration["AzureKeyVault:Url"];

    if (!string.IsNullOrWhiteSpace(keyVaultUrl))
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());
    }

    // ── SERILOG como proveedor de logging ───────────────────────────
    // La configuración (sinks, niveles) vive en appsettings.json: cambiar
    // el nivel de log en producción NO requiere recompilar.
    builder.Host.UseSerilog((context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Aplicacion", "RenewableEnergy.API"));

    // ── REGISTRO DE SERVICIOS: una llamada por capa ─────────────────
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApi(builder.Configuration);

    var app = builder.Build();

    // ── ARRANQUE EN DESARROLLO: migraciones + datos demo ────────────
    if (app.Environment.IsDevelopment())
    {
        await app.ApplyMigrationsAndSeedAsync();
    }

    // ── PIPELINE HTTP (orden explicado en WebApplicationExtensions) ─
    app.UseApi();

    Log.Information("RenewableEnergy API arrancando en entorno {Entorno}", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // El filtro `when` deja pasar HostAbortedException: la lanzan a propósito
    // las herramientas que interceptan el arranque (dotnet-ef en tiempo de
    // diseño, WebApplicationFactory en los tests E2E). Sin el filtro, esas
    // herramientas fallarían con "the entry point exited without building a host".
    Log.Fatal(ex, "La aplicación no pudo arrancar");

    // Se RELANZA: un fallo de arranque debe tumbar el proceso con código de
    // error (el orquestador — Kubernetes, App Service — necesita saberlo
    // para no enrutar tráfico a un pod muerto).
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Clase parcial vacía: convierte el Program implícito (top-level statements)
/// en un tipo PÚBLICO accesible, requisito de WebApplicationFactory&lt;Program&gt;
/// en los tests E2E de la API.
/// </summary>
public partial class Program;
