namespace JobHunter.Domain.Entities;

/// <summary>
/// Perfil de preferencias del candidato. Es una clase de configuración
/// (se registra como singleton y puede sobrescribirse desde appsettings
/// mediante el patrón Options): alimenta tanto el prompt de la IA como el
/// cálculo de la puntuación.
/// </summary>
public sealed class UserProfile
{
    /// <summary>Tecnologías que domina: suman puntos por coincidencia.</summary>
    public string[] KnownTechs { get; init; } =
    [
        "C#", ".NET", "ASP.NET Core", "Entity Framework Core", "SQL Server",
        "Docker", "Clean Architecture", "MediatR", "Azure", "REST API", "CQRS"
    ];

    /// <summary>Tecnologías que le gustaría aprender (contexto para la IA).</summary>
    public string[] DesiredTechs { get; init; } =
    [
        ".NET Aspire", "Dapr", "Azure Service Bus", "Kubernetes", "Redis", "gRPC"
    ];

    /// <summary>Tecnologías que descartan la oferta: restan puntos.</summary>
    public string[] ExcludedTechs { get; init; } =
    [
        "Angular", "React", "Vue", "Frontend", "PHP", "Java"
    ];

    /// <summary>Modo de trabajo preferido.</summary>
    public WorkMode PreferredWorkMode { get; init; } = WorkMode.Remote;

    /// <summary>
    /// Acepta híbrido, pero solo puntúa positivo cuando la localización
    /// contiene "Madrid" (ver el cálculo de puntuación en el pipeline).
    /// </summary>
    public bool AcceptHybrid { get; init; } = true;

    /// <summary>Prefiere cliente final frente a consultora.</summary>
    public bool PreferClientFinal { get; init; } = true;

    /// <summary>Salario mínimo aceptable (EUR/año).</summary>
    public decimal MinAcceptableSalary { get; init; } = 40000;

    /// <summary>Salario objetivo (EUR/año).</summary>
    public decimal TargetSalary { get; init; } = 55000;
}
