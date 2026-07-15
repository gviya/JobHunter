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

    /// <summary>
    /// Tecnologías/roles que descartan la oferta: restan puntos. El candidato
    /// es backend puro y apenas tiene conocimiento de frontend, así que se
    /// listan los frameworks de front y las señales de rol "fullstack"
    /// (frontend obligatorio), además de stacks de otro lenguaje.
    ///
    /// A propósito NO se incluyen "JavaScript"/"TypeScript" sueltos: muchas
    /// ofertas de backend .NET los mencionan de pasada como "nice to have" y
    /// no deben penalizarse por eso. El matiz "el rol EXIGE frontend" lo juzga
    /// la IA vía el prompt (baja la compatibilidad), que sí entiende contexto.
    /// </summary>
    public string[] ExcludedTechs { get; init; } =
    [
        "Angular", "React", "Vue", "Svelte", "Next.js", "Nextjs",
        "Frontend", "Front-end", "Fullstack", "Full-stack", "Full Stack",
        "PHP", "Java", "Python", "Ruby", "Golang"
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
