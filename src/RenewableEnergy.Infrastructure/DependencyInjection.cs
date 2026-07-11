using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using RenewableEnergy.Application.Interfaces;
using RenewableEnergy.Domain.Interfaces;
using RenewableEnergy.Infrastructure.ExternalServices;
using RenewableEnergy.Infrastructure.Identity;
using RenewableEnergy.Infrastructure.Messaging.MassTransit.Consumers;
using RenewableEnergy.Infrastructure.Messaging.MassTransit.Publishers;
using RenewableEnergy.Infrastructure.Messaging.Outbox;
using RenewableEnergy.Infrastructure.Persistence;
using RenewableEnergy.Infrastructure.Persistence.Interceptors;
using RenewableEnergy.Infrastructure.Persistence.Repositories;
using RenewableEnergy.Infrastructure.Persistence.Seeds;

namespace RenewableEnergy.Infrastructure
{
    /// <summary>
    /// REGISTRO DE DEPENDENCIAS de Infrastructure: aquí se conectan las
    /// interfaces (Domain/Application) con sus implementaciones concretas.
    /// Es el ÚNICO lugar del sistema que conoce ambas mitades — la esencia
    /// de la inversión de dependencias hecha práctica.
    /// </summary>
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            AddPersistence(services, configuration);
            AddCaching(services, configuration);
            AddMessaging(services, configuration);
            AddOutbox(services, configuration);
            AddIdentity(services, configuration);
            AddServiciosExternos(services);
            AddInfrastructureHealthChecks(services);

            return services;
        }

        /// <summary>EF Core + interceptors + repositorios + Unit of Work.</summary>
        private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
        {
            // Los interceptors son singleton (no guardan estado) y se
            // inyectan en el DbContext al configurarlo.
            services.AddSingleton<AuditInterceptor>();
            services.AddSingleton<OutboxInterceptor>();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                // Proveedor por defecto: SQL SERVER.
                options.UseSqlServer(connectionString);

                // ── ALTERNATIVA POSTGRESQL ──────────────────────────────
                // 1. Comenta UseSqlServer y descomenta esta línea.
                // 2. Regenera las migraciones (ver docs/SETUP.md).
                // options.UseNpgsql(configuration.GetConnectionString("PostgreSql"));

                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditInterceptor>(),
                    serviceProvider.GetRequiredService<OutboxInterceptor>());
            });

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IProyectoRepository, ProyectoRepository>();
            services.AddScoped<IMedicionRepository, MedicionRepository>();
            services.AddScoped<IUsuarioRepository, UsuarioRepository>();
            services.AddScoped<IOutboxService, OutboxService>();
            services.AddScoped<DatabaseSeeder>();
        }

        /// <summary>
        /// Caché distribuida: REDIS si está habilitado; memoria si no.
        /// Los handlers usan IDistributedCache y no saben cuál hay detrás.
        /// </summary>
        private static void AddCaching(IServiceCollection services, IConfiguration configuration)
        {
            if (configuration.GetValue<bool>("Redis:Enabled"))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = configuration["Redis:ConnectionString"];
                    options.InstanceName = "RenewableEnergy:";
                });
            }
            else
            {
                // Fallback para desarrollo/tests sin Redis.
                services.AddDistributedMemoryCache();
            }
        }

        /// <summary>
        /// MASSTRANSIT + RabbitMQ. Deshabilitado (Messaging:Enabled=false)
        /// se registra un publisher nulo y la app funciona sin broker.
        /// </summary>
        private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
        {
            if (!configuration.GetValue<bool>("Messaging:Enabled"))
            {
                services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
                return;
            }

            services.AddMassTransit(bus =>
            {
                // Descubre el consumer y le crea su cola automáticamente.
                bus.AddConsumer<ProyectoCreadoConsumer>();

                bus.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(
                        configuration["MassTransit:Host"] ?? "localhost",
                        configuration["MassTransit:VirtualHost"] ?? "/",
                        host =>
                        {
                            host.Username(configuration["MassTransit:Username"] ?? "guest");
                            host.Password(configuration["MassTransit:Password"] ?? "guest");
                        });

                    cfg.ConfigureEndpoints(context);
                });

                // ── ALTERNATIVA AZURE SERVICE BUS (producción) ──────────
                // Mismo código de publishers/consumers; solo cambia el transporte:
                // bus.UsingAzureServiceBus((context, cfg) =>
                // {
                //     cfg.Host(configuration["AzureServiceBus:ConnectionString"]);
                //     cfg.ConfigureEndpoints(context);
                // });
            });

            services.AddScoped<IEventPublisher, EventPublisher>();
        }

        /// <summary>Opciones del outbox + worker de procesamiento.</summary>
        private static void AddOutbox(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));

            // El worker se puede apagar por configuración (tests, workers
            // dedicados que procesan el outbox en otro proceso...).
            if (configuration.GetValue("Outbox:Enabled", defaultValue: true))
            {
                services.AddHostedService<OutboxProcessor>();
            }
        }

        /// <summary>Usuario actual, hashing de contraseñas y emisión de JWT.</summary>
        private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        }

        /// <summary>Servicios externos: email + ejemplo de HttpClient resiliente.</summary>
        private static void AddServiciosExternos(IServiceCollection services)
        {
            services.AddTransient<IEmailService, EmailService>();

            // EJEMPLO DE POLLY SOBRE HTTPCLIENT: cualquier servicio que llame
            // a una API externa inyecta IHttpClientFactory y crea el cliente
            // "ExternalApi" — cada petición reintenta 3 veces con backoff
            // exponencial ante errores transitorios (5xx, 408, fallos de red).
            services.AddHttpClient("ExternalApi")
                .AddTransientHttpErrorPolicy(policy =>
                    policy.WaitAndRetryAsync(
                        3,
                        intento => TimeSpan.FromMilliseconds(200 * Math.Pow(2, intento))));
        }

        /// <summary>
        /// Health check de la base de datos, etiquetado "ready": el endpoint
        /// /health/ready lo usa Kubernetes para decidir si enviar tráfico.
        /// </summary>
        private static void AddInfrastructureHealthChecks(IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddDbContextCheck<ApplicationDbContext>("database", tags: ["ready"]);
        }
    }
}
