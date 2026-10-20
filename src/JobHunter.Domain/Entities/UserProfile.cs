namespace JobHunter.Domain.Entities;

/// <summary>
/// Perfil de preferencias del candidato. Se registra como singleton y se
/// carga desde la sección "UserProfile" de la configuración (típicamente
/// <c>appsettings.Local.json</c>, que está en .gitignore para que las
/// preferencias personales no acaben en el repositorio). Si esa sección no
/// existe se usa <see cref="CreateDefault"/>, un perfil de ejemplo genérico.
///
/// Alimenta tanto el prompt de la IA como el cálculo de la puntuación y los
/// filtros de ubicación/desplazamiento del pipeline.
/// </summary>
public sealed class UserProfile
{
    /// <summary>
    /// Tecnologías que conoce con su nivel de experiencia en escala 0-10:
    /// 8-10 = experiencia alta, 4-7 = media, 1-3 = básica. Cada tecnología
    /// que aparezca en la oferta suma su peso a la puntuación (con tope).
    /// </summary>
    public Dictionary<string, int> TechWeights { get; init; } = [];

    /// <summary>Tecnologías que le gustaría aprender (contexto para la IA).</summary>
    public string[] DesiredTechs { get; init; } = [];

    /// <summary>
    /// Tecnologías/roles que descartan la oferta: restan puntos con fuerza.
    /// Útil para señales de frontend/fullstack cuando se busca backend puro,
    /// o stacks de otro lenguaje.
    ///
    /// A propósito NO conviene incluir "JavaScript"/"TypeScript" sueltos:
    /// muchas ofertas de backend .NET los mencionan de pasada como "nice to
    /// have". El matiz "el rol EXIGE frontend" lo juzga la IA vía el prompt.
    /// </summary>
    public string[] ExcludedTechs { get; init; } = [];

    /// <summary>Modo de trabajo preferido.</summary>
    public WorkMode PreferredWorkMode { get; init; } = WorkMode.Remote;

    /// <summary>Acepta ofertas híbridas (sujetas al filtro de ciudad/zonas).</summary>
    public bool AcceptHybrid { get; init; } = true;

    /// <summary>Acepta ofertas presenciales (sujetas al filtro de ciudad/zonas).</summary>
    public bool AcceptOnSite { get; init; } = true;

    /// <summary>
    /// Ciudad donde vive el candidato (p. ej. "Madrid"). Si se deja vacía no
    /// se aplica ningún filtro de desplazamiento: cualquier híbrido/presencial
    /// que pase el filtro de país se acepta. Si se rellena, las ofertas
    /// híbridas/presenciales cuya localización no mencione la ciudad (o una
    /// de <see cref="PreferredZones"/>) se descartan antes de analizar.
    /// </summary>
    public string City { get; init; } = string.Empty;

    /// <summary>
    /// Zonas/distritos/códigos postales bien comunicados con casa (p. ej.
    /// "Hortaleza", "28033", "Chamartín"). Dan un pequeño bonus de puntuación
    /// y cuentan como ubicación válida para híbrido/presencial.
    /// </summary>
    public string[] PreferredZones { get; init; } = [];

    /// <summary>
    /// Municipios/zonas que se descartan para híbrido/presencial aunque el
    /// texto mencione también la ciudad (p. ej. "Alcobendas, Madrid"):
    /// típicamente los que solo tienen cercanías o metro ligero.
    /// </summary>
    public string[] ExcludedAreas { get; init; } = [];

    /// <summary>
    /// Prefiere cliente final frente a consultora. Con true, la puntuación
    /// suma +20 a cliente final, resta -10 a consultora/agencia y deja 0 si no
    /// se sabe. Con false (le da igual) ese componente vale siempre 0.
    /// </summary>
    public bool PreferClientFinal { get; init; } = true;

    /// <summary>
    /// Salario mínimo aceptable (EUR/año, bruto). Además de dar contexto a la
    /// IA, es el límite duro de la puntuación por salario: una oferta cuyo
    /// techo (SalaryMax) no llega a este mínimo resta, y ningún tramo que
    /// premie queda por debajo de él.
    /// </summary>
    public decimal MinAcceptableSalary { get; init; } = 40000;

    /// <summary>
    /// Salario objetivo (EUR/año, bruto). Centra los tramos de la puntuación
    /// por salario: cuanto más supera el techo de la oferta a este objetivo,
    /// más suma. Si se configura por debajo de
    /// <see cref="MinAcceptableSalary"/>, la puntuación lo trata como igual al
    /// mínimo.
    /// </summary>
    public decimal TargetSalary { get; init; } = 55000;

    /// <summary>
    /// Keywords que se lanzan contra los providers. Pocas y de backend: cada
    /// keyword consume cuota diaria en providers con límite (JSearch: 200
    /// peticiones/mes). Si se deja vacío, el pipeline usa sus keywords por
    /// defecto para no quedarse sin búsqueda.
    /// </summary>
    public string[] SearchKeywords { get; init; } = [];

    /// <summary>
    /// Ubicación que se pasa a los providers que filtran en origen (Adzuna).
    /// Conviene dejar "España" (no la ciudad): restringir en origen a la
    /// ciudad perdería ofertas 100% remotas publicadas en otro sitio. El
    /// afinado por ciudad/zona lo hacen los filtros del pipeline.
    /// </summary>
    public string SearchLocation { get; init; } = "España";

    /// <summary>Máximo de resultados a pedir a cada provider.</summary>
    public int MaxResultsPerProvider { get; init; } = 50;

    /// <summary>
    /// Ventana en días del informe: el top solo incluye ofertas descubiertas
    /// en los últimos N días (las de esta ejecución van marcadas como
    /// "Nueva"), y las que fallaron al analizarse se reintentan durante ese
    /// mismo plazo — pasado, se dan por caducadas.
    /// </summary>
    public int ReportWindowDays { get; init; } = 7;

    /// <summary>
    /// Perfil de ejemplo (backend .NET genérico) usado cuando no hay sección
    /// "UserProfile" en la configuración. Sirve también como referencia de
    /// cómo rellenar appsettings.Local.json — ver appsettings.Local.template.json.
    /// </summary>
    public static UserProfile CreateDefault() => new()
    {
        TechWeights = new Dictionary<string, int>
        {
            ["C#"] = 8,
            [".NET"] = 8,
            ["ASP.NET Core"] = 7,
            ["Entity Framework"] = 7,
            ["SQL Server"] = 7,
            ["Clean Architecture"] = 6,
            ["Docker"] = 6,
            ["REST"] = 6,
            ["CQRS"] = 5,
            ["MediatR"] = 5,
            ["Azure"] = 5,
        },
        DesiredTechs = [".NET Aspire", "Dapr", "Azure Service Bus", "Kubernetes", "Redis", "gRPC"],
        ExcludedTechs =
        [
            "Angular", "React", "Vue", "Svelte", "Next.js", "Nextjs",
            "Frontend", "Front-end", "Fullstack", "Full-stack", "Full Stack",
            "PHP", "Java", "Python", "Ruby", "Golang",
        ],
        SearchKeywords = ["backend .NET", "C# backend developer", "desarrollador backend .NET"],
    };
}
