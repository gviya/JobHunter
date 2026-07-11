using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Trace;
using RenewableEnergy.API.Configuration;
using RenewableEnergy.API.Filters;
using RenewableEnergy.Infrastructure.Identity;

namespace RenewableEnergy.API.Extensions
{
    /// <summary>
    /// REGISTRO DE SERVICIOS propios de la capa API: controllers, versionado,
    /// Swagger, autenticación JWT, rate limiting y OpenTelemetry.
    /// Program.cs queda limpio: solo llama a AddApi(...).
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
        {
            AddControllersConJson(services);
            AddVersionado(services);
            AddSwagger(services);
            AddAutenticacionJwt(services, configuration);
            AddRateLimiting(services, configuration);
            AddObservabilidad(services);

            // El filtro de API Key se resuelve por DI ([ServiceFilter]).
            services.AddScoped<ApiKeyAuthFilter>();

            return services;
        }

        private static void AddControllersConJson(IServiceCollection services)
        {
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    // Los enums viajan como string en el JSON: el cliente envía
                    // "tipo": "Solar" en lugar del críptico "tipo": 1.
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });
        }

        /// <summary>
        /// VERSIONADO DE API: la versión viaja en la RUTA (/api/v1/...).
        /// Publicar una v2 = añadir un controller con [ApiVersion("2.0")]
        /// sin romper a los clientes de la v1.
        /// </summary>
        private static void AddVersionado(IServiceCollection services)
        {
            services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                // Devuelve las versiones soportadas en la cabecera api-supported-versions.
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                // Formato del nombre de grupo en Swagger: v1, v2...
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });
        }

        /// <summary>
        /// SWAGGER con soporte de JWT: el candado de la UI permite pegar el
        /// token del login y probar los endpoints protegidos.
        /// </summary>
        private static void AddSwagger(IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "RenewableEnergy API",
                    Version = "v1",
                    Description = "Plantilla educativa de Clean Architecture en .NET 10 — "
                        + "dominio de energías renovables. Usuario demo: demo@renovables.demo / Demo1234!"
                });

                // Esquema de seguridad Bearer: pulsa "Authorize" en Swagger UI
                // y pega el token devuelto por POST /api/v1/auth/login.
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Introduce SOLO el token (Swagger añade el prefijo 'Bearer')."
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        []
                    }
                });
            });
        }

        /// <summary>
        /// VALIDACIÓN de JWT (la emisión vive en Infrastructure/Identity).
        /// Ambos lados comparten la sección "Jwt" de configuración.
        /// </summary>
        private static void AddAutenticacionJwt(IServiceCollection services, IConfiguration configuration)
        {
            var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                ?? throw new InvalidOperationException("Falta la sección 'Jwt' en la configuración.");

            if (string.IsNullOrWhiteSpace(jwtOptions.Secret) || jwtOptions.Secret.Length < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:Secret debe tener al menos 32 caracteres. En local se define con la "
                    + "variable de entorno JWT_SECRET o en appsettings.Development.json; en "
                    + "producción viene de Azure Key Vault.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        // Se valida TODO: emisor, audiencia, caducidad y firma.
                        ValidateIssuer = true,
                        ValidIssuer = jwtOptions.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwtOptions.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                        // Sin margen extra sobre la expiración (default: 5 min).
                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.AddAuthorization();
        }

        /// <summary>Rate limiting: política global "Fixed" y "PerUser" (por usuario/IP).</summary>
        private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<RateLimitSettings>(configuration.GetSection("RateLimiting"));

            var settings = configuration.GetSection("RateLimiting").Get<RateLimitSettings>()
                ?? new RateLimitSettings();

            services.AddRateLimiter(options =>
            {
                // 429 en lugar del 503 por defecto cuando se supera el cupo.
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddFixedWindowLimiter("Fixed", limiter =>
                {
                    limiter.PermitLimit = settings.PermitLimit;
                    limiter.Window = TimeSpan.FromSeconds(settings.WindowSeconds);
                    limiter.QueueLimit = settings.QueueLimit;
                });

                // Partición por usuario autenticado (o IP si es anónimo):
                // cada cliente tiene SU propio cupo.
                options.AddPolicy("PerUser", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.User.Identity?.Name
                            ?? context.Connection.RemoteIpAddress?.ToString()
                            ?? "anonimo",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.PermitLimit,
                            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                            QueueLimit = settings.QueueLimit
                        }));
            });
        }

        /// <summary>
        /// OPENTELEMETRY: trazas distribuidas de ASP.NET Core y HttpClient.
        /// Sin exporter por defecto; en producción añadirías OTLP hacia
        /// Application Insights, Jaeger, Grafana Tempo...
        /// </summary>
        private static void AddObservabilidad(IServiceCollection services)
        {
            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation());
        }
    }
}
