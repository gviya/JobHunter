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

    // ── Remoto completo explícito frente a "híbrido" (revisión: hybrid-beats-explicit-full-remote) ──
    //
    // Un falso híbrido lo descarta el filtro de desplazamiento ANTES de que la
    // IA lo vea; un falso remoto lo corrige la IA. Ante la duda, remoto.

    [Theory]
    // Regresión: cualquier "híbrido" no técnico del texto ganaba a un "100%
    // remoto / fully remote" explícito y la oferta remota se descartaba.
    [InlineData("Stack: C#, .NET 8, Blazor Hybrid, MAUI. Puesto 100% remoto.")]
    [InlineData("Nice to have: .NET MAUI / Blazor Hybrid. Fully remote.")]
    [InlineData("public, private and hybrid clouds. 100% remote.")]
    [InlineData("Azure Hybrid Connections ... Fully remote role.")]
    [InlineData("Entra ID hybrid identity. Fully remote role.")]
    [InlineData("Modelo de despliegue híbrido (on-premise + Azure). Puesto en remoto.")]
    [InlineData("enfoque híbrido entre producto y consultoría. Teletrabajo 100%.")]
    [InlineData("Benefits: hybrid working model, gym. Note: this specific role is fully remote.")]
    [InlineData("100% remoto. Si lo prefieres, puedes venir a la oficina 2 días a la semana.")]
    [InlineData("Trabajo 100% remoto. Ofrecemos horario flexible y modelo de trabajo híbrido si vives cerca de la oficina.")]
    [InlineData("Experience with hybrid cloud (Azure Arc) and hybrid networking. Fully remote role.")]
    [InlineData("Azure, hybrid workloads, Azure Arc. 100% remote.")]
    [InlineData("We work in hybrid teams across Europe. This position is fully remote.")]
    public void FromText_RemotoCompletoExplicitoJuntoAHibrido_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión: los usos técnicos de "hybrid" que quedaban fuera de la lista
    // de exclusiones convertían en híbrida una oferta que solo dice "remoto"
    // (sin "100%"): ahora no dan modo y decide el remoto.
    [InlineData("Experiencia con Blazor Hybrid y .NET MAUI. Trabajo en remoto.")]
    [InlineData("Conocimientos de Azure Hybrid Benefit. Remoto.")]
    [InlineData("Buscamos un perfil híbrido backend/DevOps. Puesto en remoto.")]
    [InlineData("Hybrid identity (Entra Connect) y hybrid join. Remote.")]
    public void FromText_HibridoTecnicoEnOfertaRemota_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseConBlazorHybridYLocalizacionDeCiudad_EsRemoto()
    {
        // Regresión (de extremo a extremo en la revisión): Greenhouse solo da
        // la ciudad, así que decide la descripción; salía Hybrid y el filtro
        // de desplazamiento la descartaba por no estar en Madrid.
        var description = HtmlText.ToPlainText(
            "&lt;p&gt;Stack: C#, .NET 8, Blazor Hybrid, MAUI.&lt;/p&gt;&lt;p&gt;Puesto 100% remoto.&lt;/p&gt;");

        WorkModeHeuristics.FromText(title: "Senior .NET Developer", location: "Barcelona, Spain", description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Un remoto completo NEGADO no se cuenta como remoto completo: las
    // negaciones se borran antes de buscarlo.
    [InlineData("Modelo híbrido, no 100% teletrabajo: 2 días en oficina", WorkMode.Hybrid)]
    [InlineData("Hybrid role: this position isn't fully remote.", WorkMode.Hybrid)]
    [InlineData("Hybrid role: this position isn’t fully remote.", WorkMode.Hybrid)] // apóstrofo tipográfico
    [InlineData("Hybrid. Fully remote is not an option.", WorkMode.Hybrid)]
    [InlineData("No es un trabajo 100% en remoto: 3 días en la oficina", WorkMode.Hybrid)]
    [InlineData("No es posible trabajar 100% en remoto.", WorkMode.OnSite)]
    public void FromText_RemotoCompletoNegado_NoGana(string text, WorkMode expected)
    {
        FromDescription(text).Should().Be(expected);
    }

    // ── Negaciones acotadas por geografía (revisión: negation-anywhere-forces-onsite) ──

    [Theory]
    // Regresión: cualquier negación de remoto en el campo forzaba OnSite (y
    // con AcceptOnSite=false la oferta se descartaba), aunque el texto dijera
    // "100% remoto" o la "negación" solo limitara el país.
    [InlineData("Teletrabajo 100% desde cualquier punto de España (no se permite teletrabajar desde el extranjero).")]
    [InlineData("100% remoto (no es posible teletrabajar desde fuera de España)")]
    [InlineData("Remoto (imprescindible residir en España, no remoto internacional)")]
    [InlineData("This is a fully remote role, but not remote from anywhere: you must live in Spain.")]
    [InlineData("Fully remote within Spain. No remote work from outside the EU.")]
    [InlineData("Remote-first company. Not open to remote candidates outside Spain.")]
    [InlineData("Remote: no relocation needed")]
    [InlineData("Trabajo 100% en remoto. Sin teletrabajo desde el extranjero.")]
    [InlineData("Puesto 100% remoto desde España. No remote outside Spain.")]
    [InlineData("Remote within Spain. Not eligible for remote work outside Spain.")]
    public void FromText_RemotoConRestriccionGeografica_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseTeletrabajoSoloDesdeEspana_EsRemoto()
    {
        // Regresión (de extremo a extremo en la revisión): salía OnSite.
        var description = HtmlText.ToPlainText(
            "&lt;p&gt;Teletrabajo 100% desde cualquier punto de Espa&amp;ntilde;a (no se permite teletrabajar desde el extranjero).&lt;/p&gt;");

        WorkModeHeuristics.FromText(title: "Backend Developer", location: "Spain", description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // La restricción geográfica se borra SIN contar como remoto: no convierte
    // en remota una oferta que es presencial por otra frase.
    [InlineData("Puesto presencial en Madrid. No remoto internacional.", WorkMode.OnSite)]
    [InlineData("Sin teletrabajo. No remote work from abroad.", WorkMode.OnSite)]
    // "Teletrabajo: No" sigue siendo negación al final de línea o de frase.
    [InlineData("Teletrabajo: No\nSalario: 35.000 €", WorkMode.OnSite)]
    [InlineData("Remote: no. Office in Madrid.", WorkMode.OnSite)]
    public void FromText_RestriccionGeograficaONegacionAlFinal_MantieneElModo(string text, WorkMode expected)
    {
        FromDescription(text).Should().Be(expected);
    }

    // ── Remoto como una de varias opciones (revisión: remote-or-hybrid-too-narrow) ──

    [Theory]
    // Regresión: la excepción "remoto o híbrido" solo aceptaba las dos
    // palabras pegadas con "/", "|", "o" u "or".
    [InlineData("Ofrecemos: 100% remoto o modelo híbrido según preferencia.")]
    [InlineData("Teletrabajo 100% u oficina híbrida.")]
    [InlineData("Remote and hybrid options available")]
    [InlineData("Modalidad remota e híbrida")]
    [InlineData("Remoto, híbrido o presencial: tú eliges")]
    [InlineData("Teletrabajo 100% o híbrido, como prefieras")]
    [InlineData("Remote (Spain) or Hybrid (Barcelona)")]
    [InlineData("modelo de trabajo híbrido o 100% remoto")]
    [InlineData("Hybrid and remote options available")]
    [InlineData("Hybrid and fully remote positions")]
    public void FromText_RemotoEntreVariasOpciones_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión: Greenhouse junta varias sedes en la localización, que va por
    // delante de la descripción; salía Hybrid y, sin "Madrid", se descartaba.
    [InlineData("Remote, Spain; Hybrid, Barcelona")]
    [InlineData("Hybrid - Barcelona / Remote - Spain")]
    [InlineData("Remote - Spain / Hybrid - Barcelona")]
    [InlineData("Barcelona (Hybrid) | Remote (Spain)")]
    public void FromText_LocalizacionMultisedeConOpcionRemota_EsRemoto(string location)
    {
        WorkModeHeuristics.FromText(title: "Backend Engineer", location: location, description: "Modelo híbrido.")
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Un remoto PARCIAL junto a "híbrido" no es una alternativa: sigue siendo
    // híbrido (antes "Híbrido / teletrabajo 2 días" salía remoto).
    [InlineData("Híbrido / teletrabajo 2 días")]
    [InlineData("Modelo híbrido y teletrabajo 2 días a la semana")]
    [InlineData("Modelo híbrido y teletrabajo flexible")]
    [InlineData("Trabajarás 2 días remoto, híbrido el resto")]
    [InlineData("Teletrabajo parcial o híbrido")]
    public void FromText_RemotoParcialJuntoAHibrido_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Theory]
    // Un "/" entre cifras no separa sedes: "2/3 días" sigue siendo híbrido.
    [InlineData("Madrid (teletrabajo 2/3 días)")]
    [InlineData("Madrid (2 días/semana teletrabajo)")]
    [InlineData("Madrid - Hybrid; Valencia - Hybrid")]
    // Regresión (revisión: digit-guard-untested): con espacios alrededor del
    // "/" sí se ejercita la guarda de cifras; sin ella el campo se partía en
    // "Madrid (teletrabajo 2", que es remoto, y salía Remote.
    [InlineData("Madrid (teletrabajo 2 / 3 días)")]
    public void FromText_LocalizacionHibridaSinSedeRemota_EsHibrido(string location)
    {
        WorkModeHeuristics.FromText(title: null, location: location, description: null)
            .Should().Be(WorkMode.Hybrid);
    }

    // ── "<" y ">" literales en texto plano (revisión: normalize-strips-lt-gt-spans) ──

    [Theory]
    // Regresión: Normalize quitaba todo lo que pareciera "<...>" y se comía la
    // frase del modo entre un "<10 personas" y un ">3 años".
    [InlineData("Equipo pequeño (<10 personas). Teletrabajo 100%. >3 años de experiencia. Incorporación presencial el primer día.")]
    [InlineData("APIs con latencias <50 ms ... Puesto 100% remoto. Requisitos: experiencia >3 años con C#. Onboarding presencial en Barcelona")]
    [InlineData("Salario <45k. Modalidad: 100% remoto. Requisitos: >3 años")]
    [InlineData("Equipo pequeño (&lt;10 personas). Teletrabajo 100%. &gt;3 años de experiencia. Incorporación presencial el primer día.")]
    public void FromText_MenorYMayorQueLiterales_NoSeTomanPorEtiquetas(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseConMenorYMayorQueEscapados_EsRemoto()
    {
        // Regresión (de extremo a extremo en la revisión): HtmlText deja "<10
        // personas" y ">3 años" literales; Normalize los tomaba por etiqueta.
        var description = HtmlText.ToPlainText(
            "&lt;p&gt;Equipo peque&amp;ntilde;o (&amp;lt;10 personas). Teletrabajo 100%.&lt;/p&gt;"
            + "&lt;ul&gt;&lt;li&gt;&amp;gt;3 a&amp;ntilde;os de experiencia.&lt;/li&gt;&lt;/ul&gt;"
            + "&lt;p&gt;Incorporaci&amp;oacute;n presencial el primer d&amp;iacute;a.&lt;/p&gt;");

        description.Should().Contain("<10 personas").And.Contain(">3 años");
        WorkModeHeuristics.FromText(title: "Backend Developer", location: "Spain", description: description)
            .Should().Be(WorkMode.Remote);
    }

    // ── Variantes de negación y de remoto parcial (revisión: common-variants-fall-to-remote) ──

    [Theory]
    // Regresión: variantes cercanas de negaciones que la heurística ya decía
    // cubrir salían como remotas (sin filtro de desplazamiento y gastando IA).
    [InlineData("Remote work isn't possible for this role.")]
    [InlineData("Remote work isn’t possible for this role.")] // apóstrofo tipográfico
    [InlineData("El teletrabajo no es posible en este puesto.")]
    [InlineData("No se puede teletrabajar.")]
    [InlineData("We don't offer remote work for this position.")]
    [InlineData("We do not offer remote work for this position.")]
    [InlineData("This is an on-site position. Remote applications will not be considered.")]
    public void FromText_VarianteDeNegacionDeRemoto_EsPresencial(string text)
    {
        FromDescription(text).Should().Be(WorkMode.OnSite);
    }

    [Theory]
    // Regresión: frecuencias, porcentajes y días de la semana que indican
    // remoto PARCIAL salían como remoto completo.
    [InlineData("Teletrabajo dos veces por semana.")]
    [InlineData("Work from home twice a week.")]
    [InlineData("Teletrabajo 60%")]
    [InlineData("Viernes teletrabajo y jornada intensiva en verano.")]
    [InlineData("You will work 3 days a week in our Barcelona office and remotely the rest of the week.")]
    [InlineData("Office attendance: 3 days per week (Barcelona HQ). Remote otherwise.")]
    [InlineData("50% del tiempo en remoto")]
    public void FromText_VarianteDeRemotoParcial_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Theory]
    // Las variantes nuevas no deben convertir en híbrida una oferta remota:
    // la semana entera, un porcentaje casi total, los viajes o una visita
    // mensual a la oficina no son híbrido.
    [InlineData("De lunes a viernes en remoto")]
    [InlineData("Teletrabajo 90%, alguna visita a cliente")]
    [InlineData("Remote (20% travel)")]
    [InlineData("Remote once onboarded")]
    [InlineData("Remote; office twice a month")]
    [InlineData("Remoto, una vez al mes en la oficina")]
    public void FromText_RemotoQueNoEsParcial_SigueRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Un tope de días de teletrabajo no niega el remoto: es un híbrido. Como
    // negación saldría OnSite y, con AcceptOnSite=false, se descartaría.
    [InlineData("No se puede teletrabajar más de 2 días a la semana")]
    [InlineData("No se permite teletrabajar más de 2 días por semana")]
    public void FromText_TopeDeDiasDeTeletrabajo_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Fact]
    public void FromText_TextoLargoYPatologico_TerminaRapido()
    {
        // Las descripciones pueden ser muy largas: ningún patrón debe
        // retroceder de forma catastrófica con entradas repetitivas.
        var parts = new[]
        {
            new string('<', 20_000),
            string.Concat(Enumerable.Repeat("<a ", 5_000)),
            string.Concat(Enumerable.Repeat("2 días de la semana del mes ", 2_000)),
            string.Concat(Enumerable.Repeat("remoto o ", 3_000)),
            string.Concat(Enumerable.Repeat("no se permite ", 3_000)),
            string.Concat(Enumerable.Repeat("hybrid ", 3_000)),
            new string(' ', 20_000) + "dias",
            // Regresión (revisión: whitespace-backtracking): rachas de espacios
            // o saltos de línea tras una palabra remota. Los \s* opcionales
            // seguidos se repartían la racha de todas las formas posibles
            // (unos 100 s con 50.000 espacios).
            "remoto" + new string(' ', 20_000) + "x hibrido",
            "remoto" + new string(' ', 20_000) + "x",
            "remote" + new string('\n', 20_000) + "x hybrid",
            "teletrabajo" + new string('\t', 20_000) + "x",
            "remoto (" + new string(' ', 20_000) + "x hibrido",
            "remoto" + string.Concat(Enumerable.Repeat(" \n", 10_000)) + "x hibrido",
            "no se puede teletrabajar" + new string(' ', 20_000) + "x",
            "hibrido o remoto" + new string(' ', 20_000) + "x",
            // Regresión (revisión: r4-workmode-1): la cantidad semanal tras
            // "más de" se busca dentro de un lookahead; con el bucle perezoso
            // el código generado no terminaba nunca.
            string.Concat(Enumerable.Repeat("no se puede teletrabajar mas de un par de meses al ano ", 2_000)),
            string.Concat(Enumerable.Repeat("we don't allow remote work for more than 4 weeks a year ", 2_000)),
            string.Concat(Enumerable.Repeat("teletrabajo y modelo hibrido: ", 3_000)),
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var part in parts)
        {
            _ = FromDescription(part);
        }

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Theory]
    // Colapsar las rachas de espacios no cambia el modo: el "\n" (también el
    // "\r\n" de Windows) sigue cerrando el "Teletrabajo: No", y los
    // tabuladores o espacios duros siguen separando palabras.
    [InlineData("Teletrabajo: No\r\n\r\nSalario: 35.000 €", WorkMode.OnSite)]
    [InlineData("Remoto   o \t  híbrido", WorkMode.Remote)]
    [InlineData("Teletrabajo\t\t2\u00A0días", WorkMode.Hybrid)]
    public void FromText_RachasDeEspacios_NoCambianElModo(string text, WorkMode expected)
    {
        FromDescription(text).Should().Be(expected);
    }

    // ── Negaciones de un remoto concreto (revisión: negation-of-specific-remote-forces-onsite) ──

    [Theory]
    // Regresión: una negación que solo excluye un país o región con nombre,
    // un tipo de contrato o un beneficio condicional forzaba OnSite (y con
    // AcceptOnSite=false la oferta remota se descartaba). Ahora, si sobrevive
    // un remoto positivo y no hay exigencia explícita de presencia, es remoto.
    [InlineData("Remote role within Spain. We cannot offer remote contracts in Portugal or France.")]
    [InlineData("Teletrabajo desde cualquier punto de España. No se puede teletrabajar desde Canarias, Ceuta ni Melilla.")]
    [InlineData("Puesto en remoto. Ayuda para coworking si no puedes teletrabajar desde casa.")]
    [InlineData("Remote-first company. We do not support remote work from Russia or Belarus.")]
    [InlineData("Remote (EU). We don't allow remote work from the US due to tax reasons.")]
    [InlineData("Remote within the EU. Remote work isn't possible from non-EU countries.")]
    [InlineData("Remote\n\nWe can't offer remote work to candidates based in the US.")]
    [InlineData("Location: Remote (Spain)\nPlease note: we do not support remote employment in Germany.")]
    [InlineData("Remote role. Note: we don't provide a remote work allowance.")]
    // Una visita o una semana de incorporación presenciales no son una
    // exigencia de presencia del puesto.
    [InlineData("Remote role within Spain. We cannot offer remote contracts in Portugal. Presencialidad obligatoria una vez al trimestre.")]
    [InlineData("Remote role within Spain. We cannot offer remote contracts in Portugal. On-site onboarding week in Madrid.")]
    public void FromText_NegacionDeUnRemotoConcreto_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión (de extremo a extremo en la revisión): Greenhouse solo da el
    // país o la ciudad, así que decide la descripción; salía OnSite.
    [InlineData("Spain", "&lt;p&gt;Remote role within Spain.&lt;/p&gt;&lt;p&gt;We cannot offer remote contracts in Portugal or France.&lt;/p&gt;")]
    [InlineData("España", "&lt;p&gt;Teletrabajo desde cualquier punto de Espa&amp;ntilde;a.&lt;/p&gt;&lt;p&gt;No se puede teletrabajar desde Canarias, Ceuta ni Melilla.&lt;/p&gt;")]
    [InlineData("Barcelona, Spain", "&lt;p&gt;Puesto en remoto.&lt;/p&gt;&lt;p&gt;Ayuda para coworking si no puedes teletrabajar desde casa.&lt;/p&gt;")]
    public void FromText_GreenhouseRemotoConNegacionDeUnRemotoConcreto_EsRemoto(string location, string content)
    {
        var description = HtmlText.ToPlainText(content);

        WorkModeHeuristics.FromText(title: "Backend Developer", location: location, description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Con una exigencia EXPLÍCITA de presencia, el remoto que sobrevive a la
    // negación no cuenta: sigue siendo presencial.
    [InlineData("Puesto 100% presencial. No se puede teletrabajar; teletrabajo solo en casos excepcionales.")]
    [InlineData("Presencial obligatorio en Madrid. No se puede teletrabajar salvo urgencias; teletrabajo con autorización.")]
    [InlineData("Fully on-site role. No remote work. Remote-friendly culture events.")]
    // Sin remoto positivo que sobreviva, la negación sigue decidiendo.
    [InlineData("Puesto presencial en Madrid. No remoto.")]
    [InlineData("Trabajo presencial; no es posible el teletrabajo.")]
    [InlineData("Sin opción de trabajo remoto")]
    public void FromText_NegacionSinRemotoQueSobreviva_EsPresencial(string text)
    {
        FromDescription(text).Should().Be(WorkMode.OnSite);
    }

    // ── Topes al teletrabajo (revisión: capped-negation-forces-onsite) ──

    [Theory]
    // Regresión: una negación que solo pone un TOPE al teletrabajo (días,
    // "todos los días", días de la semana, "al 100%") borraba el remoto sin
    // dejar señal híbrida y salía OnSite; una oferta híbrida en Madrid se
    // descartaba con AcceptOnSite=false.
    [InlineData("No se puede teletrabajar más que dos días por semana.")]
    [InlineData("No se puede teletrabajar los lunes (día de equipo en la oficina).")]
    [InlineData("En este puesto no se puede teletrabajar al 100%.")]
    [InlineData("No puedes teletrabajar todos los días: los martes vamos a la oficina.")]
    [InlineData("We don't support remote work for more than 2 days a week.")]
    [InlineData("We don't allow remote work more than 2 days per week.")]
    [InlineData("We do not offer remote work on Mondays and Tuesdays (team days).")]
    [InlineData("El teletrabajo no es posible más de dos días a la semana.")]
    [InlineData("Remote work isn't possible every day: Tuesdays at the office.")]
    [InlineData("No remote work on Fridays.")]
    [InlineData("No es posible el teletrabajo al 100%.")]
    [InlineData("We cannot offer remote work full-time.")]
    [InlineData("Sin teletrabajo los viernes")]
    [InlineData("No hay trabajo remoto los viernes")]
    public void FromText_NegacionConTopeDeTeletrabajo_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    // ── Límites que no cuentan la semana (revisión: r4-workmode-1) ──

    [Theory]
    // Regresión: todo "más de / more than" tras una negación se leía como
    // tope SEMANAL y salía Hybrid; un puesto remoto que limita el trabajo
    // desde el EXTRANJERO por año o por mes se descartaba fuera de Madrid.
    [InlineData("Puesto en remoto desde España. No se puede teletrabajar más de 3 meses al año desde el extranjero.")]
    [InlineData("Trabajo en remoto. No se puede teletrabajar más de 30 días al año desde fuera de España.")]
    [InlineData("Teletrabajo desde cualquier punto de España. No se puede trabajar en remoto más de 4 semanas al año desde otro país.")]
    [InlineData("Teletrabajo. No es posible teletrabajar más de un mes seguido desde el extranjero.")]
    [InlineData("Remote-first: no se puede teletrabajar más de 2 meses fuera de España.")]
    // La misma regla en inglés y con una negación que también casa como fija.
    [InlineData("We don't allow remote work for more than 4 weeks a year from abroad.")]
    [InlineData("Trabajo en remoto. No hay trabajo remoto más de 30 días al año desde fuera de España.")]
    public void FromText_LimiteAnualOMensualDesdeElExtranjero_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseRemotoConLimiteAnualDesdeElExtranjero_EsRemoto()
    {
        // Regresión (r4-workmode-1, de extremo a extremo en la revisión): con
        // la localización "Spain" decide la descripción; salía Hybrid.
        var description = HtmlText.ToPlainText(
            "&lt;p&gt;Puesto en remoto desde Espa&amp;ntilde;a.&lt;/p&gt;"
            + "&lt;p&gt;No se puede teletrabajar m&amp;aacute;s de 3 meses al a&amp;ntilde;o desde el extranjero.&lt;/p&gt;");

        WorkModeHeuristics.FromText(title: "Backend Developer", location: "Spain", description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión (r4-workmode-1): con una cantidad SEMANAL detrás, "más de"
    // sigue siendo un tope y el puesto, híbrido.
    [InlineData("No se puede teletrabajar más de 2 días por semana")]
    [InlineData("No se puede teletrabajar más del 40% de la jornada.")]
    [InlineData("No se puede teletrabajar más de un par de días a la semana.")]
    [InlineData("No se puede teletrabajar más de la mitad de la semana.")]
    [InlineData("No se puede teletrabajar más que los viernes.")]
    [InlineData("We don't offer remote work for more than 16 hours per week.")]
    public void FromText_MasDeConCantidadSemanal_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Theory]
    // Sin tope detrás, o con el "100%" DELANTE (niega el modo completo, no
    // lo acota), sigue siendo una negación.
    [InlineData("No se puede teletrabajar de lunes a viernes.")]
    [InlineData("No es remoto al 100%.")]
    [InlineData("No es posible trabajar 100% en remoto.")]
    [InlineData("This position isn't fully remote.")]
    public void FromText_NegacionSinTope_SiguePresencial(string text)
    {
        FromDescription(text).Should().Be(WorkMode.OnSite);
    }

    // ── Porcentajes tras "remoto" (revisión: workload-split-read-as-partial-remote) ──

    [Theory]
    // Regresión: un porcentaje tras "remoto/remote" se leía como tiempo en
    // remoto aunque repartiera TAREAS; salía Hybrid (en el título, que manda
    // sobre todo) y se descartaba fuera de Madrid.
    [InlineData("Desarrollador Fullstack Remoto (70% backend, 30% frontend)", null, null)]
    [InlineData("Fullstack Developer - Remote (70% backend / 30% frontend)", null, null)]
    [InlineData("Desarrollador Fullstack Remoto (70% backend, 30% frontend)", "España", null)]
    [InlineData("Backend Developer", null, "Puesto remoto (80% desarrollo, 20% soporte).")]
    [InlineData("Backend Developer", null, "Remote (50% coding, 50% architecture)")]
    [InlineData("Backend Developer", null, "Remote: 60% engineering, 40% management.")]
    [InlineData("Backend Developer", null, "Remote (30% on-call rotation)")]
    public void FromText_PorcentajeQueRepartePorTareas_SigueRemoto(string title, string? location, string? description)
    {
        WorkModeHeuristics.FromText(title: title, location: location, description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Un porcentaje sin nada detrás, con una palabra de tiempo o con el
    // resto del reparto sí es tiempo en remoto: híbrido.
    [InlineData("Remoto 60% del tiempo")]
    [InlineData("Remote 50% of the time")]
    [InlineData("Teletrabajo al 40%, resto en oficina")]
    [InlineData("Remoto (60%)")]
    [InlineData("Teletrabajo 60% y 40% presencial")]
    public void FromText_PorcentajeDeTiempoEnRemoto_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    // ── "Híbrido o remoto" seguido de una cantidad (revisión: partial-tail-any-quantity) ──

    [Theory]
    // Regresión: cualquier palabra de cantidad tras "híbrido o remoto"
    // ("some travel", "one of the two", "part of a team") volvía parcial el
    // remoto y salía Hybrid; en la localización eso manda sobre la descripción.
    [InlineData("Hybrid or remote - some travel required.")]
    [InlineData("Hybrid or remote: one of the two, your choice.")]
    [InlineData("Híbrido o remoto - algunas visitas a cliente.")]
    [InlineData("Hybrid or remote - part of a growing team.")]
    [InlineData("Hybrid or remote - a few trips per year.")]
    [InlineData("Hybrid or remote - occasional travel.")]
    public void FromText_HibridoORemotoConCantidadQueNoCuentaDias_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_LocalizacionHibridaORemotaConViajes_EsRemoto()
    {
        // Regresión (de extremo a extremo en la revisión): salía Hybrid.
        WorkModeHeuristics.FromText(
                title: "Backend Engineer",
                location: "Hybrid or Remote - Some travel to Madrid required",
                description: "C#.")
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Si la cantidad cuenta días, el remoto sí es la parte en casa del híbrido.
    [InlineData("Hybrid or remote some days")]
    [InlineData("Híbrido o remoto algunos días")]
    [InlineData("Hybrid or remote - two days a week")]
    [InlineData("Hybrid or remote part of the week")]
    [InlineData("Híbrido o teletrabajo ocasional")]
    public void FromText_HibridoORemotoConDiasContados_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    // ── Rangos de días con guion (revisión: weekday-range-with-dash) ──

    [Theory]
    // Regresión: "lunes-viernes" (con guion o raya) se leía como un solo día
    // de teletrabajo y salía Hybrid; es la semana entera.
    [InlineData("Horario: lunes-viernes remoto")]
    [InlineData("Horario de L-V. Lunes–viernes remoto.")]
    [InlineData("Teletrabajo lunes-viernes")]
    [InlineData("Teletrabajo lunes – viernes")]
    public void FromText_RangoDeDiasConGuion_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    // ── Híbridos que descartan el remoto completo (revisión: full-remote-negation-variants) ──

    [Theory]
    // Regresión: las formas habituales de decir que el remoto completo NO
    // está disponible no se reconocían como negación; el "100% remoto /
    // fully remote" sobrevivía y la oferta híbrida salía Remote.
    [InlineData("This is a hybrid role and is not eligible for fully remote work.")]
    [InlineData("Modelo híbrido (no hay opción de 100% remoto). 3 días en oficina.")]
    [InlineData("Puesto híbrido, no se contempla el 100% remoto.")]
    [InlineData("Modelo híbrido; no ofrecemos 100% remoto.")]
    [InlineData("Hybrid. We're unable to offer fully remote.")]
    [InlineData("Hybrid role. Not looking for 100% remote candidates.")]
    // "Teletrabajo y modelo híbrido" describe el propio híbrido: "y" tras
    // "teletrabajo" no es una alternativa.
    [InlineData("Teletrabajo y modelo híbrido: 2 días en oficina.")]
    public void FromText_HibridoQueDescartaElRemotoCompleto_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
    }

    [Theory]
    // Tras "teletrabajo" siguen valiendo los separadores de alternativa y la
    // coma de una lista de opciones.
    [InlineData("Teletrabajo / híbrido")]
    [InlineData("Teletrabajo, híbrido o presencial: tú eliges")]
    public void FromText_TeletrabajoComoAlternativaAHibrido_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    // ── "Teletrabajo y/e híbrido" como lista de opciones (revisión: r4-workmode-2) ──

    [Theory]
    // Regresión: tras "teletrabajo" solo valían "/", "|", ",", "o", "u" y
    // "or", así que una lista de modalidades con "y"/"e" salía Hybrid y se
    // descartaba fuera de Madrid. Sin calendario detrás de "híbrido", es una
    // alternativa: ante la duda, remoto.
    [InlineData("Modalidades: teletrabajo e híbrido, a elegir.")]
    [InlineData("Posibilidad de teletrabajo y modelo híbrido, a elegir.")]
    [InlineData("Opciones de teletrabajo e híbrido según preferencia")]
    [InlineData("Modalidad: teletrabajo y híbrido")]
    [InlineData("Teletrabajo (España) y híbrido (Madrid)")]
    [InlineData("Teletrabajo & híbrido")]
    [InlineData("Teletrabajo + híbrido")]
    // Siguen siendo remotos como antes.
    [InlineData("Remoto y híbrido")]
    [InlineData("Teletrabajo o híbrido")]
    public void FromText_TeletrabajoYHibridoComoOpciones_EsRemoto(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión (r4-workmode-2): en los campos cortos, que mandan sobre la
    // descripción, también salía Hybrid.
    [InlineData("Desarrollador .NET - Teletrabajo e Híbrido", null)]
    [InlineData("Desarrollador .NET", "Madrid (Teletrabajo e híbrido)")]
    public void FromText_TeletrabajoEHibridoEnCampoCorto_EsRemoto(string title, string? location)
    {
        WorkModeHeuristics.FromText(title: title, location: location, description: "Modelo híbrido.")
            .Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseTituloTeletrabajoEHibrido_EsRemoto()
    {
        // Regresión (r4-workmode-2, de extremo a extremo en la revisión): con
        // la localización "España" el título decide; salía Hybrid.
        WorkModeHeuristics.FromText(
                title: "Desarrollador .NET - Teletrabajo e Híbrido",
                location: "España",
                description: HtmlText.ToPlainText("&lt;p&gt;Equipo de plataforma.&lt;/p&gt;"))
            .Should().Be(WorkMode.Remote);
    }

    [Fact]
    public void FromText_GreenhouseModalidadesTeletrabajoEHibrido_EsRemoto()
    {
        // Regresión (r4-workmode-2, de extremo a extremo en la revisión): con
        // la localización "Barcelona, Spain" decide la descripción; salía Hybrid.
        var description = HtmlText.ToPlainText(
            "&lt;p&gt;Modalidades: teletrabajo e h&amp;iacute;brido, a elegir.&lt;/p&gt;");

        WorkModeHeuristics.FromText(title: "Backend Developer", location: "Barcelona, Spain", description: description)
            .Should().Be(WorkMode.Remote);
    }

    [Theory]
    // Regresión (r4-workmode-2): con el calendario del híbrido detrás, la
    // frase describe UN modelo y sigue siendo híbrida.
    [InlineData("Teletrabajo y modelo híbrido (3 días en oficina).")]
    [InlineData("Teletrabajo e híbrido - dos días presenciales.")]
    [InlineData("Teletrabajo y modelo híbrido con 2 días en la oficina.")]
    [InlineData("Teletrabajo y modelo híbrido: los viernes en la oficina.")]
    public void FromText_TeletrabajoYHibridoConSuCalendario_EsHibrido(string text)
    {
        FromDescription(text).Should().Be(WorkMode.Hybrid);
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
