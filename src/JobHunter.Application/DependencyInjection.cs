using FluentValidation;
using JobHunter.Application.Common.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Application;

/// <summary>
/// REGISTRO DE DEPENDENCIAS de la capa Application.
///
/// Cada capa registra LO SUYO en un método de extensión propio
/// (AddApplication, AddInfrastructure) y Program.cs solo los encadena.
/// Así el arranque de la API queda limpio y cada capa es dueña de su
/// configuración.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // MediatR: descubre TODOS los handlers (IRequestHandler,
        // INotificationHandler) de este assembly.
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // El ORDEN de registro ES el orden de ejecución del pipeline:
            //   Logging (externo) → Performance → Validation → Handler
            // Logging va primero para capturar también los fallos de
            // validación; Validation va último para que el handler reciba
            // siempre requests válidos.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // FluentValidation: descubre todos los AbstractValidator<T>.
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
