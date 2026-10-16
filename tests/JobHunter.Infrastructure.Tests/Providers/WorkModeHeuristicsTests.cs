using AwesomeAssertions;
using JobHunter.Domain.Entities;
using JobHunter.Infrastructure.Providers;

namespace JobHunter.Infrastructure.Tests.Providers;

public sealed class WorkModeHeuristicsTests
{
    // Atajo para los casos de un solo texto: va en la descripción, el campo
    // de menor prioridad, que es donde suelen aparecer estas frases.
    private static WorkMode FromDescription(string? description)
        => WorkModeHeuristics.FromText(title: null, location: null, description: description);

    [Theory]
    // Regresión: "teletrabajo" se buscaba primero en todo el texto y estas
    // ofertas híbridas salían como remotas (+30 y sin filtro de desplazamiento).
    [InlineData("Modelo híbrido con 2 días de teletrabajo")]
    [InlineData("Ofrecemos 2 días de teletrabajo a la semana")]
    [InlineData("Teletrabajo parcial")]
    [InlineData("Flexibilidad para teletrabajar algunos días")]
    [InlineData("Posibilidad de teletrabajo ocasional")]
    [InlineData("Trabajarás 3 días en remoto y 2 en la oficina")]
    [InlineData("Ofrecemos días en remoto")]
    [InlineData("Teletrabajo 2 días/semana")]
    [InlineData("Teletrabajo: parcial")]
    [InlineData("Teletrabajo los viernes")]
    [InlineData("Presencialidad: 3 días a la semana")]
    [InlineData("3 días en la oficina de Madrid")]
    [InlineData("Puesto semipresencial")]
    [InlineData("Modalidad semi-presencial")]
    [InlineData("Modalidad híbrida")]
    [InlineData("HIBRIDO")]
    [InlineData("hybrid")]
    [InlineData("2 days remote, 3 in the office")]
    [InlineData("Remote 2-3 days a week")]
    [InlineData("Work from home 2 days a week")]
    [InlineData("Partially remote")]
    [InlineData("Remote on Fridays")]
    [InlineData("Puesto presencial con posibilidad de teletrabajo")]
    [InlineData("On-site with some remote work")]
    // Un "no es remoto" junto a una señal híbrida aclara que no es remoto
    // COMPLETO, no que sea presencial.
    [InlineData("Modelo híbrido: no es un puesto 100% remoto")]
    public void FromText_SenalHibridaOParcial_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Theory]
    [InlineData("100% remoto")]
    [InlineData("100% teletrabajo")]
    [InlineData("Teletrabajo 100%")]
    [InlineData("Full remote")]
    [InlineData("Fully remote")]
    [InlineData("Somos una empresa remote-first")]
    [InlineData("Trabajo en remoto desde cualquier lugar de España")]
    [InlineData("Remote")]
    [InlineData("Remoto")]
    [InlineData("Modalidad remota")]
    [InlineData("Teletrabajo")]
    [InlineData("Teletrabajo: Sí")]
    [InlineData("Work from home")]
    [InlineData("Trabajo no presencial")]
    [InlineData("Remote part-time developer")] // jornada parcial, pero en remoto
    public void FromText_RemotoCompleto_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    [InlineData("Puesto no remoto")]
    [InlineData("No es remoto")]
    [InlineData("No es 100% remoto, se trabaja en nuestras oficinas")]
    [InlineData("Sin teletrabajo")]
    [InlineData("Sin posibilidad de teletrabajo")]
    [InlineData("No hay opción de teletrabajo")]
    [InlineData("Teletrabajo: No")]
    [InlineData("This is not a remote position")]
    [InlineData("Not remote")]
    [InlineData("No remote work")]
    [InlineData("Remote work is not possible for this role")]
    [InlineData("100% presencial")]
    [InlineData("100% presencial, quizá teletrabajo en el futuro")]
    [InlineData("No es híbrido ni remoto: presencial en Madrid")]
    public void FromText_RemotoNegado_EsPresencial(string text)
    {
        FromDescription(text).Should().Be(WorkMode.OnSite);
    }

    [Theory]
    [InlineData("Puesto presencial en oficina")]
    [InlineData("PRESENCIAL")]
    [InlineData("On-site")]
    [InlineData("Onsite")]
    [InlineData("On site in Madrid")]
    [InlineData("In office")]
    [InlineData("In-office role")]
    [InlineData("Office-based position")]
    [InlineData("5 días presenciales")]
    public void FromText_SenalPresencial_EsPresencial(string text)
    {
        FromDescription(text).Should().Be(WorkMode.OnSite);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Buscamos desarrollador .NET con experiencia en Azure")]
    [InlineData("Entrevista presencial en Madrid")] // el proceso, no el puesto
    [InlineData("Experiencia con remote debugging y acceso remoto a servidores")]
    [InlineData("Arquitectura cloud híbrida y entornos híbridos")]
    [InlineData("Focus on site reliability")] // SRE, no "on site"
    public void FromText_SinSenalDeModo_EsDesconocido(string? text)
    {
        FromDescription(text).Should().Be(WorkMode.Unknown);
    }

    [Fact]
    public void FromText_TodosLosCamposVacios_EsDesconocido()
    {
        WorkModeHeuristics.FromText(title: null, location: "", description: "  ", extra: null)
            .Should().Be(WorkMode.Unknown);
    }

    [Theory]
    [InlineData("Teletrabajo los Miércoles")]
    [InlineData("teletrabajo los miercoles")]
    [InlineData("HÍBRIDO")]
    [InlineData("Híbrida")]
    public void FromText_IgnoraTildesYMayusculas(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    // ── Prioridad por campo ─────────────────────────────────────────────

    [Fact]
    public void FromText_TituloRemotoYDescripcionHibrida_GanaElTitulo()
    {
        var mode = WorkModeHeuristics.FromText(
            title: "Backend Developer (Remote)",
            location: "Madrid",
            description: "Tenemos oficina en Madrid, híbrido opcional.");

        mode.Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_LocalizacionHibridaYDescripcionRemota_GanaLaLocalizacion()
    {
        var mode = WorkModeHeuristics.FromText(
            title: "Backend Developer",
            location: "Madrid (Hybrid)",
            description: "Join our remote-friendly team.");

        mode.Should().Be(WorkMode.Hybrid);
    }

    [Fact]
    public void FromText_LocalizacionRemota_EsRemoto()
    {
        var mode = WorkModeHeuristics.FromText(
            title: "Backend Developer",
            location: "Remote - Spain",
            description: "Buscamos desarrollador .NET.");

        mode.Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_ExtraEstructurado_GanaALocalizacionYDescripcion()
    {
        // El "type" de Jooble va por delante de la localización y la descripción.
        var mode = WorkModeHeuristics.FromText(
            title: "Desarrollador .NET",
            location: "Madrid (presencial)",
            description: "Modelo híbrido.",
            extra: "Trabajo remoto");

        mode.Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_TituloConModo_GanaAlExtra()
    {
        var mode = WorkModeHeuristics.FromText(
            title: "Desarrollador .NET - Híbrido",
            location: null,
            description: null,
            extra: "Trabajo remoto");

        mode.Should().Be(WorkMode.Hybrid);
    }

    [Fact]
    public void FromText_CamposCortosSinSenal_UsaLaDescripcion()
    {
        var mode = WorkModeHeuristics.FromText(
            title: "C# Developer",
            location: "Madrid",
            description: "Modelo híbrido: 2 días de oficina.",
            extra: "Jornada completa");

        mode.Should().Be(WorkMode.Hybrid);
    }

    [Fact]
    public void FromText_TituloConHibridoTecnico_NoDecideYPasaALaDescripcion()
    {
        // "Hybrid Cloud" es tecnología: el modo lo da la descripción.
        var mode = WorkModeHeuristics.FromText(
            title: "Hybrid Cloud Engineer",
            location: "Spain",
            description: "Puesto 100% remoto.");

        mode.Should().Be(WorkMode.Remote);
    }

    // ── Casos límite dentro de un mismo texto ───────────────────────────

    [Fact]
    public void FromText_CloudHibridoEnOfertaRemota_EsRemoto()
    {
        // Al comprobar lo híbrido antes que lo remoto, "cloud híbrido" no
        // puede convertir en híbrida una oferta 100% remota.
        FromDescription("Experiencia en cloud híbrido (Azure + on-premise). Trabajo 100% remoto.")
            .Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_AccesoRemotoEnOfertaPresencial_EsPresencial()
    {
        FromDescription("Soporte con acceso remoto a clientes. Puesto presencial.")
            .Should().Be(WorkMode.OnSite);
    }

    [Theory]
    [InlineData("Modalidad: remoto o híbrido")]
    [InlineData("Desarrollador .NET (Híbrido / Remoto)")]
    [InlineData("Hybrid or fully remote")]
    public void FromText_RemotoComoAlternativaAHibrido_EsRemoto(string text)
    {
        // La oferta ADMITE remoto completo: no debe tratarse como híbrida
        // (el filtro de desplazamiento la descartaría fuera de la ciudad).
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_FraseCortadaPorPunto_NoUneDiasConTeletrabajo()
    {
        // "4 días" y "Teletrabajo" son frases distintas: no es "4 días de teletrabajo".
        FromDescription("Formación inicial: 4 días. Teletrabajo 100%.")
            .Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_ContenidoHtmlEscapadoDeGreenhouse_DecodificaAntesDeBuscar()
    {
        // Greenhouse manda el content HTML-escapado y con las tildes como entidades.
        FromDescription("&lt;p&gt;Modelo h&amp;iacute;brido en Madrid&lt;/p&gt;")
            .Should().Be(WorkMode.Hybrid);
    }

    // ── Lever ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("remote", WorkMode.Remote)]
    [InlineData("Remote", WorkMode.Remote)]
    [InlineData("hybrid", WorkMode.Hybrid)]
    [InlineData("on-site", WorkMode.OnSite)]
    [InlineData("onsite", WorkMode.OnSite)]
    // Regresión: "unspecified" se mapeaba a OnSite (-10 y filtro de
    // desplazamiento) cuando la empresa simplemente no lo indicó.
    [InlineData("unspecified", WorkMode.Unknown)]
    [InlineData(null, WorkMode.Unknown)]
    [InlineData("", WorkMode.Unknown)]
    public void FromLeverWorkplaceType_MapeaAlEnum(string? workplaceType, WorkMode expected)
    {
        WorkModeHeuristics.FromLeverWorkplaceType(workplaceType).Should().Be(expected);
    }
}
