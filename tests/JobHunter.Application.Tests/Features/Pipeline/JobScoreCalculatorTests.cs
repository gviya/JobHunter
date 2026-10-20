using AwesomeAssertions;
using JobHunter.Application.Features.Pipeline;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Tests.Features.Pipeline;

public sealed class JobScoreCalculatorTests
{
    // Descripción larga y detallada (>1200 chars) que solo menciona C#, .NET
    // y Docker como techs conocidas: representa una oferta "bien explicada".
    private const string DetailedBackendDescription = """
        Buscamos un desarrollador backend con sólida experiencia en C# y .NET
        para unirse a nuestro equipo de producto. Trabajarás en el diseño,
        desarrollo y mantenimiento de servicios backend robustos y escalables
        que dan soporte a miles de usuarios diarios. Entre tus responsabilidades
        estarán construir y evolucionar los microservicios del núcleo del
        sistema, diseñar el modelo de datos, optimizar el rendimiento de las
        consultas y garantizar la calidad mediante pruebas automatizadas.
        Valoramos la capacidad de escribir código limpio, mantenible y bien
        documentado, así como la colaboración estrecha con el resto del equipo
        en revisiones de código. El despliegue de los servicios se realiza
        mediante contenedores Docker en un entorno de integración y despliegue
        continuo. Ofrecemos un entorno de trabajo flexible, con posibilidad de
        teletrabajo, formación continua y participación en decisiones técnicas
        relevantes. El candidato ideal disfruta resolviendo problemas complejos,
        tiene mentalidad de mejora continua y quiere crecer profesionalmente en
        un equipo que cuida la calidad del software por encima de las entregas
        apresuradas. Si te apasiona el desarrollo backend y quieres formar parte
        de un proyecto ambicioso con impacto real, queremos conocerte.
        """;

    // Perfil de ejemplo (pesos: C# 8, .NET 8, Docker 6...) con el filtro de
    // ciudad activado, como el que usaría un candidato real de Madrid.
    private static UserProfile MadridProfile()
    {
        var defaults = UserProfile.CreateDefault();
        return new UserProfile
        {
            TechWeights = defaults.TechWeights,
            ExcludedTechs = defaults.ExcludedTechs,
            City = "Madrid",
            PreferredZones = ["Hortaleza", "28033"],
            ExcludedAreas = ["Alcobendas", "Getafe"],
        };
    }

    // Mismo perfil de Madrid con salarios y preferencia de cliente final a
    // medida, para comprobar que la puntuación sigue al perfil.
    private static UserProfile MadridProfileWith(
        decimal? minAcceptableSalary = null,
        decimal? targetSalary = null,
        bool preferClientFinal = true)
    {
        var madrid = MadridProfile();
        return new UserProfile
        {
            TechWeights = madrid.TechWeights,
            ExcludedTechs = madrid.ExcludedTechs,
            City = madrid.City,
            PreferredZones = madrid.PreferredZones,
            ExcludedAreas = madrid.ExcludedAreas,
            MinAcceptableSalary = minAcceptableSalary ?? madrid.MinAcceptableSalary,
            TargetSalary = targetSalary ?? madrid.TargetSalary,
            PreferClientFinal = preferClientFinal,
        };
    }

    private static JobOffer RemoteOffer(decimal? salaryMax = null, bool? isClientFinal = null) => new()
    {
        ExternalId = "salary",
        SourceProvider = "Test",
        Title = "Backend Developer",
        CompanyName = "Acme",
        Url = "https://example.com/salary",
        WorkMode = WorkMode.Remote,
        SalaryMax = salaryMax,
        IsClientFinal = isClientFinal,
    };

    private static AiAnalysisResult Analysis(int compatibility, string[]? importantTechs = null) =>
        new(
            Summary: "Resumen",
            Seniority: "Senior",
            ImportantTechs: importantTechs ?? [],
            Compatibility: compatibility,
            Pros: [],
            Cons: [],
            Recommendation: "Aplicar",
            Requirements: []);

    [Fact]
    public void Calculate_OfertaBackendBienExplicadaRemotaClienteFinal_ReflejaLosPesos()
    {
        var offer = new JobOffer
        {
            ExternalId = "1",
            SourceProvider = "Test",
            Title = "Desarrollador Backend Senior",
            CompanyName = "Acme",
            Url = "https://example.com/1",
            Description = DetailedBackendDescription,
            WorkMode = WorkMode.Remote,
            IsClientFinal = true,
            SalaryMax = 60000m,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(80), MadridProfile());

        score.RemoteScore.Should().Be(30);           // Remote
        score.ClientFinalScore.Should().Be(20);      // cliente final
        score.SalaryScore.Should().Be(20);           // SalaryMax >= 60k
        score.TechScore.Should().Be(22);             // C# (8) + .NET (8) + Docker (6)
        score.InfoQualityScore.Should().Be(8);       // descripción larga y detallada
        score.CompatibilityScore.Should().Be(80);

        // TotalScore = Compatibility/2 + location + clientFinal + salary + tech + info
        //            = 40 + 30 + 20 + 20 + 22 + 8 = 140
        score.TotalScore.Should().Be(140);
    }

    [Fact]
    public void Calculate_OfertaFullstackConVariasTechsDeFront_SeHunde()
    {
        var offer = new JobOffer
        {
            ExternalId = "2",
            SourceProvider = "Test",
            Title = "Desarrollador Fullstack Angular + .NET",
            CompanyName = "Consultora XYZ",
            Url = "https://example.com/2",
            Description = "Proyecto fullstack con Angular y React, algo de .NET.",
            WorkMode = WorkMode.Hybrid,
            Location = "Barcelona",
            IsClientFinal = false,
            SalaryMax = null,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(40, ["Angular", "React"]), MadridProfile());

        score.RemoteScore.Should().Be(-25);       // híbrido no conmutable (fuera de Madrid)
        score.ClientFinalScore.Should().Be(-10);  // consultora
        score.SalaryScore.Should().Be(-5);        // salario null
        // .NET conocida (+8); excluidas distintas = Fullstack, Angular, React
        // => -min(3*12, 36) = -36  =>  8 - 36 = -28
        score.TechScore.Should().Be(-28);
        score.InfoQualityScore.Should().Be(-25);  // descripción muy corta
        // Total = 20 + (-25) + (-10) + (-5) + (-28) + (-25) = -73
        score.TotalScore.Should().Be(-73);
    }

    [Fact]
    public void Calculate_OfertaSinDescripcion_PenalizaLaFaltaDeInformacion()
    {
        var offer = new JobOffer
        {
            ExternalId = "3",
            SourceProvider = "Test",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/3",
            Description = null,
            WorkMode = WorkMode.Remote,
            IsClientFinal = true,
            SalaryMax = 55000m,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile());

        score.InfoQualityScore.Should().Be(-25);  // sin descripción => máxima penalización
        // Total = 35 + 30 + 20 + 15 + 0 + (-25) = 75
        score.TotalScore.Should().Be(75);
    }

    [Fact]
    public void Calculate_MencionDeJavaScript_NoActivaLaExclusionDeJava()
    {
        var offer = new JobOffer
        {
            ExternalId = "4",
            SourceProvider = "Test",
            Title = "Backend .NET Developer",
            CompanyName = "Acme",
            Url = "https://example.com/4",
            Description = "Backend en .NET; algo de JavaScript en el lado cliente.",
            WorkMode = WorkMode.Remote,
            IsClientFinal = true,
            SalaryMax = 55000m,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile());

        // "JavaScript" NO debe disparar la exclusión de "Java": solo cuenta
        // .NET como conocida (+8), sin penalización de tech excluida.
        score.TechScore.Should().Be(8);
    }

    [Fact]
    public void Calculate_HibridoEnZonaPreferida_RecibeBonusSobreElHibridoNormal()
    {
        var enHortaleza = new JobOffer
        {
            ExternalId = "6",
            SourceProvider = "Test",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/6",
            WorkMode = WorkMode.Hybrid,
            Location = "Hortaleza, Madrid",
        };

        var score = JobScoreCalculator.Calculate(enHortaleza, Analysis(70), MadridProfile());

        score.RemoteScore.Should().Be(20);  // híbrido conmutable (15) + zona preferida (5)
    }

    [Fact]
    public void Calculate_SumaDePesosDeTechs_SeCapaEnCuarenta()
    {
        var offer = new JobOffer
        {
            ExternalId = "7",
            SourceProvider = "Test",
            Title = "Backend .NET",
            CompanyName = "Acme",
            Url = "https://example.com/7",
            // Menciona C# (8), .NET (8), ASP.NET Core (7... y .NET dentro ya contado),
            // Entity Framework (7), SQL Server (7), Docker (6), REST (6): suma > 40.
            Description = "C# y .NET con ASP.NET Core, Entity Framework, SQL Server, Docker y APIs REST.",
            WorkMode = WorkMode.Remote,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile());

        score.TechScore.Should().Be(40);
    }

    [Theory]
    [InlineData("Adzuna")]
    [InlineData("Jooble")]
    [InlineData("Greenhouse")]
    public void Calculate_ClienteFinal_DependeDelFlagDeLaOfertaNoDeLaFuente(string source)
    {
        // Regresión: solo Greenhouse/Lever marcaban cliente final, así que
        // cualquier oferta de agregador puntuaba -10 por su fuente. Ahora el
        // flag lo corrige la IA (AiSignals) y el cálculo solo mira el flag.
        JobOffer OfferFrom(bool isClientFinal) => new()
        {
            ExternalId = "8",
            SourceProvider = source,
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/8",
            WorkMode = WorkMode.Remote,
            IsClientFinal = isClientFinal,
        };

        JobScoreCalculator.Calculate(OfferFrom(true), Analysis(70), MadridProfile())
            .ClientFinalScore.Should().Be(20);
        JobScoreCalculator.Calculate(OfferFrom(false), Analysis(70), MadridProfile())
            .ClientFinalScore.Should().Be(-10);
    }

    [Fact]
    public void Calculate_ClienteFinalDesconocido_PuntuaNeutro()
    {
        // Ni el agregador ni la IA saben si es cliente final: no debe contar
        // como consultora (-10), que era el sesgo por fuente de antes.
        var offer = new JobOffer
        {
            ExternalId = "10",
            SourceProvider = "Jooble",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/10",
            WorkMode = WorkMode.Remote,
            IsClientFinal = null,
        };

        JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile())
            .ClientFinalScore.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Backend .NET.")]
    public void Calculate_DescripcionSnippet_InfoQualityNeutra(string? description)
    {
        // Regresión: Adzuna/Jooble solo dan un extracto y se llevaban -25/-10
        // fijos por la longitud, sin relación con la oferta real.
        var offer = new JobOffer
        {
            ExternalId = "9",
            SourceProvider = "Adzuna",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/9",
            Description = description,
            DescriptionIsSnippet = true,
            WorkMode = WorkMode.Remote,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile());

        score.InfoQualityScore.Should().Be(0);
    }

    [Fact]
    public void Calculate_EspaciosYSaltosDeRelleno_NoCuentanComoInformacion()
    {
        // ~300 caracteres de texto real repartidos con mucho relleno de
        // espacios/saltos: mide el texto limpio, no la maquetación.
        var padded = string.Join($"\n\n{new string(' ', 40)}\n", Enumerable.Repeat("Backend .NET con C# y SQL Server.", 9));
        padded.Length.Should().BeGreaterThan(600); // en bruto caería en el tramo de 0

        var offer = new JobOffer
        {
            ExternalId = "10",
            SourceProvider = "Greenhouse",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/10",
            Description = padded,
            WorkMode = WorkMode.Remote,
        };

        var score = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile());

        score.InfoQualityScore.Should().Be(-10); // 250-599 caracteres limpios
    }

    [Theory]
    [InlineData(WorkMode.Hybrid, -25)]
    [InlineData(WorkMode.OnSite, -30)]
    public void Calculate_ModoQueElPerfilNoAcepta_PuntuaComoNoConmutable(WorkMode mode, int expected)
    {
        // El pipeline descarta estos modos antes de analizar y también si la
        // IA los revela después; el calculador los penaliza igual como valor
        // defensivo: aunque la ubicación sea conmutable, no deben sumar como
        // aceptables.
        var defaults = MadridProfile();
        var remoteOnlyProfile = new UserProfile
        {
            TechWeights = defaults.TechWeights,
            ExcludedTechs = defaults.ExcludedTechs,
            City = defaults.City,
            PreferredZones = defaults.PreferredZones,
            ExcludedAreas = defaults.ExcludedAreas,
            AcceptHybrid = false,
            AcceptOnSite = false,
        };

        var offer = new JobOffer
        {
            ExternalId = "11",
            SourceProvider = "Test",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/11",
            WorkMode = mode,
            Location = "Hortaleza, Madrid", // conmutable y zona preferida
        };

        JobScoreCalculator.Calculate(offer, Analysis(70), remoteOnlyProfile)
            .RemoteScore.Should().Be(expected);

        // Con el perfil que sí acepta el modo, la misma oferta puntúa normal.
        JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile())
            .RemoteScore.Should().Be(mode == WorkMode.Hybrid ? 20 : -10);
    }

    [Fact]
    public void Calculate_CopiaRequisitosDelAnalisisAlScore()
    {
        var offer = new JobOffer
        {
            ExternalId = "5",
            SourceProvider = "Test",
            Title = "Backend Developer",
            CompanyName = "Acme",
            Url = "https://example.com/5",
            Description = DetailedBackendDescription,
            WorkMode = WorkMode.Remote,
            IsClientFinal = true,
            SalaryMax = 55000m,
        };

        var analysis = new AiAnalysisResult(
            Summary: "Resumen",
            Seniority: "Senior",
            ImportantTechs: [".NET"],
            Compatibility: 70,
            Pros: [],
            Cons: [],
            Recommendation: "Aplicar",
            Requirements: ["3 años con C#", "Experiencia con Docker", "Inglés B2"]);

        var score = JobScoreCalculator.Calculate(offer, analysis, MadridProfile());

        score.AiRequirements.Should().Be("3 años con C# · Experiencia con Docker · Inglés B2");
    }

    [Theory]
    [InlineData(70000, 25)]
    [InlineData(69999, 20)]
    [InlineData(60000, 20)]
    [InlineData(59999, 15)]
    [InlineData(50000, 15)]
    [InlineData(49999, 5)]
    [InlineData(40000, 5)]
    [InlineData(39999, -10)]
    [InlineData(null, -5)]
    public void Calculate_SalarioConPerfilPorDefecto_ReproduceLosTramosFijosDeSiempre(int? salaryMax, int expected)
    {
        // Mínimo 40k y objetivo 55k: los tramos relativos deben dar
        // exactamente lo mismo que los antiguos tramos fijos
        // (≥70k, ≥60k, ≥50k, ≥40k).
        JobScoreCalculator.Calculate(RemoteOffer(salaryMax), Analysis(70), MadridProfile())
            .SalaryScore.Should().Be(expected);
    }

    [Theory]
    [InlineData(53000, 25)]  // objetivo + 15k
    [InlineData(52999, 20)]
    [InlineData(43000, 20)]  // objetivo + 5k
    [InlineData(42999, 15)]
    [InlineData(33000, 15)]  // objetivo - 5k
    [InlineData(32999, 5)]
    [InlineData(30000, 5)]   // mínimo
    [InlineData(29999, -10)]
    [InlineData(null, -5)]
    public void Calculate_SalarioConPerfilPropio_LosTramosSeMuevenConElPerfil(int? salaryMax, int expected)
    {
        var profile = MadridProfileWith(minAcceptableSalary: 30000, targetSalary: 38000);

        JobScoreCalculator.Calculate(RemoteOffer(salaryMax), Analysis(70), profile)
            .SalaryScore.Should().Be(expected);
    }

    [Fact]
    public void Calculate_MismoSalarioConPerfilesDistintos_PuntuaDistinto()
    {
        // 45k es poco para el perfil por defecto (objetivo 55k) y bastante
        // por encima del objetivo de un perfil que pide 38k.
        var offer = RemoteOffer(45000m);

        JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfile())
            .SalaryScore.Should().Be(5);
        JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfileWith(minAcceptableSalary: 30000, targetSalary: 38000))
            .SalaryScore.Should().Be(20);
    }

    [Theory]
    [InlineData(35000, -10)] // con el objetivo tal cual (40k) sería "cerca del objetivo"
    [InlineData(45000, -10)] // mínimo - 5k: por debajo del mínimo
    [InlineData(49999, -10)]
    [InlineData(50000, 15)]  // el mínimo ya cuenta como "cerca del objetivo"
    [InlineData(55000, 20)]  // mínimo + 5k
    [InlineData(65000, 25)]  // mínimo + 15k
    public void Calculate_ObjetivoPorDebajoDelMinimo_ElMinimoMandaComoObjetivo(int salaryMax, int expected)
    {
        // Perfil mal configurado (objetivo 40k < mínimo 50k): el mínimo es el
        // límite duro, así que nada por debajo de él puede sumar.
        var profile = MadridProfileWith(minAcceptableSalary: 50000, targetSalary: 40000);

        JobScoreCalculator.Calculate(RemoteOffer(salaryMax), Analysis(70), profile)
            .SalaryScore.Should().Be(expected);
    }

    [Theory]
    [InlineData(38000, -10)] // objetivo - 5k caería aquí, pero está por debajo del mínimo
    [InlineData(40000, 15)]
    [InlineData(47000, 20)]
    [InlineData(57000, 25)]
    public void Calculate_ObjetivoPegadoAlMinimo_NoPremiaSalariosPorDebajoDelMinimo(int salaryMax, int expected)
    {
        var profile = MadridProfileWith(minAcceptableSalary: 40000, targetSalary: 42000);

        JobScoreCalculator.Calculate(RemoteOffer(salaryMax), Analysis(70), profile)
            .SalaryScore.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Calculate_SinPreferenciaPorClienteFinal_ClienteFinalPuntuaCero(bool? isClientFinal)
    {
        // Al candidato le da igual cliente final o consultora: el dato no
        // debe premiar ni castigar, y tampoco contar en el total.
        var offer = RemoteOffer(salaryMax: 55000m, isClientFinal: isClientFinal);

        var indifferent = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfileWith(preferClientFinal: false));
        var preferring = JobScoreCalculator.Calculate(offer, Analysis(70), MadridProfileWith(preferClientFinal: true));

        indifferent.ClientFinalScore.Should().Be(0);
        indifferent.TotalScore.Should().Be(preferring.TotalScore - preferring.ClientFinalScore);
    }

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, -10)]
    [InlineData(null, 0)]
    public void Calculate_ConPreferenciaPorClienteFinal_MantieneLosPesos(bool? isClientFinal, int expected)
    {
        JobScoreCalculator.Calculate(RemoteOffer(isClientFinal: isClientFinal), Analysis(70), MadridProfileWith(preferClientFinal: true))
            .ClientFinalScore.Should().Be(expected);
    }
}
