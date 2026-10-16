using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JobHunter.Domain.Entities;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Heurística compartida para deducir el modo de trabajo a partir de texto
/// cuando la fuente no lo indica de forma estructurada.
///
/// Los errores NO cuestan lo mismo:
///  - Un falso <see cref="WorkMode.Remote"/> se guarda y se analiza, y la IA
///    lo corrige después (AiSignals sobrescribe el modo antes de puntuar). Su
///    coste es cuota de IA y, si la IA no devuelve modo, un +30 inmerecido.
///  - Un falso <see cref="WorkMode.Hybrid"/>/<see cref="WorkMode.OnSite"/> lo
///    descarta el filtro de desplazamiento ANTES de que la IA lo vea (y con
///    AcceptOnSite=false cae todo lo presencial): la oferta se pierde en
///    silencio y para siempre.
/// Por eso, ante un texto ambiguo entre remoto e híbrido/presencial se
/// prefiere remoto (o desconocido), y solo se devuelve híbrido/presencial con
/// señales claras. Aun así "teletrabajo" a secas no basta para remoto: una
/// oferta típica española dice "modelo híbrido con 2 días de teletrabajo".
///
/// Reglas:
///  1. Prioridad por CAMPO: título &gt; pista estructurada del provider
///     (<c>extra</c>, p. ej. el "type" de Jooble) &gt; localización &gt;
///     descripción. Los campos cortos los rellena la empresa a propósito; la
///     descripción es texto libre que mezcla cultura de empresa, beneficios y
///     tecnologías ("cloud híbrido", "acceso remoto"...). Gana el primer
///     campo que dé un modo concreto. Un campo corto con varias sedes
///     ("Remote, Spain; Hybrid, Barcelona") es remoto si alguna lo es.
///  2. Dentro de un mismo texto, en este orden:
///     a. Las restricciones geográficas de un remoto ("no remoto
///        internacional", "not remote from outside the EU") se borran: dicen
///        DESDE DÓNDE, no que no haya remoto.
///     b. Las negaciones ("sin teletrabajo", "not remote") se borran.
///     c. Un remoto COMPLETO explícito que sobreviva ("100% remoto", "fully
///        remote") o el remoto como alternativa ("remoto o híbrido") es
///        remoto, aunque el texto también diga "híbrido" o restrinja el país.
///     d. Lo híbrido/parcial se comprueba ANTES que el remoto a secas.
///     e. Una negación de remoto sin señal híbrida es presencial.
///  3. Comparación sin mayúsculas ni tildes ("Híbrido" == "hibrido") y con
///     límites de palabra, para que "presencial" no case en "semipresencial".
/// </summary>
internal static partial class WorkModeHeuristics
{
    /// <summary>
    /// Deduce el modo de trabajo evaluando los campos en orden de prioridad:
    /// <paramref name="title"/>, <paramref name="extra"/>,
    /// <paramref name="location"/> y, por último, <paramref name="description"/>.
    /// Los parámetros son explícitos (no un <c>params</c>) para que cada
    /// provider diga qué texto es cuál: el orden de los argumentos importa.
    /// </summary>
    /// <param name="extra">
    /// Campo corto y estructurado propio del provider que hable del modo de
    /// trabajo (el "type" de Jooble: "Trabajo remoto", "Jornada completa"...).
    /// </param>
    public static WorkMode FromText(string? title, string? location, string? description, string? extra = null)
    {
        // Solo la descripción es texto libre: en ella un "/" o un ";" separan
        // frases ("2 días/semana"), no sedes alternativas.
        (string? Text, bool IsShort)[] fieldsByPriority =
            [(title, true), (extra, true), (location, true), (description, false)];

        foreach (var (field, isShort) in fieldsByPriority)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var mode = isShort ? ClassifyShortField(field) : Classify(field);
            if (mode != WorkMode.Unknown)
            {
                return mode;
            }
        }

        return WorkMode.Unknown;
    }

    /// <summary>
    /// Mapea el <c>workplaceType</c> de Lever a nuestro enum. "unspecified"
    /// significa que la empresa no lo indicó: es <see cref="WorkMode.Unknown"/>,
    /// no presencial (eso penalizaba la oferta y la sometía al filtro de
    /// desplazamiento sin motivo).
    /// </summary>
    public static WorkMode FromLeverWorkplaceType(string? workplaceType) => workplaceType?.ToLowerInvariant() switch
    {
        "remote" => WorkMode.Remote,
        "hybrid" => WorkMode.Hybrid,
        "on-site" or "onsite" => WorkMode.OnSite,
        _ => WorkMode.Unknown,
    };

    /// <summary>
    /// Clasifica un campo corto. Greenhouse y compañía juntan varias sedes en
    /// la localización ("Remote, Spain; Hybrid, Barcelona", "Hybrid -
    /// Barcelona / Remote - Spain"): el texto entero sale híbrido, pero la
    /// oferta ADMITE remoto completo, así que basta con que una sede lo sea.
    /// </summary>
    private static WorkMode ClassifyShortField(string raw)
    {
        var mode = Classify(raw);
        if (mode is not (WorkMode.Hybrid or WorkMode.OnSite))
        {
            return mode;
        }

        var segments = SegmentSeparatorRegex().Split(raw);
        return segments.Length > 1 && segments.Any(s => Classify(s) == WorkMode.Remote)
            ? WorkMode.Remote
            : mode;
    }

    /// <summary>Clasifica UN texto suelto (un único campo).</summary>
    private static WorkMode Classify(string raw)
    {
        var text = Normalize(raw);

        // Las negaciones se borran del texto para que "sin teletrabajo" no
        // cuente después como remoto ni "no es híbrido" como híbrido. Toda
        // restricción geográfica empieza por una negación, así que sin
        // negación no hay nada más que buscar (y se ahorran dos pasadas sobre
        // descripciones largas).
        var negatesRemote = NegatedRemoteRegex().IsMatch(text);
        if (negatesRemote)
        {
            // Las restricciones geográficas ("no remoto internacional", "no se
            // permite teletrabajar desde el extranjero") no niegan el remoto:
            // lo acotan. Se borran ANTES y sin contar como ninguna señal: así
            // "Remoto (no remoto internacional)" sigue siendo remoto y
            // "Presencial. No remoto internacional" sigue presencial.
            text = GeoRestrictedRemoteRegex().Replace(text, " ");
            negatesRemote = NegatedRemoteRegex().IsMatch(text);
            if (negatesRemote)
            {
                text = NegatedRemoteRegex().Replace(text, " ");
            }
        }

        text = NegatedHybridRegex().Replace(text, " ");

        // Un remoto COMPLETO explícito que ha sobrevivido a las negaciones
        // ("100% remoto", "fully remote") gana a cualquier "híbrido" del texto
        // (beneficios de la empresa, "Blazor Hybrid", "venir a la oficina si lo
        // prefieres") y a una negación que quede en otra frase. Lo mismo si el
        // remoto es una alternativa ("remoto o híbrido"): la oferta lo admite.
        // Equivocarse aquí hacia remoto lo corrige la IA; hacia híbrido, no.
        if (FullRemoteRegex().IsMatch(text) || RemoteOrHybridRegex().IsMatch(text))
        {
            return WorkMode.Remote;
        }

        if (HybridRegex().IsMatch(text))
        {
            return WorkMode.Hybrid;
        }

        if (negatesRemote)
        {
            return WorkMode.OnSite;
        }

        if (RemoteRegex().IsMatch(text))
        {
            return WorkMode.Remote;
        }

        return OnSiteRegex().IsMatch(text) ? WorkMode.OnSite : WorkMode.Unknown;
    }

    /// <summary>
    /// Minúsculas, sin tildes y sin HTML. Greenhouse manda el content
    /// HTML-escapado ("&amp;lt;p&amp;gt;h&amp;amp;iacute;brido"), así que se
    /// decodifica dos veces antes de quitar etiquetas: si no, "h&amp;iacute;brido"
    /// nunca casaría con "hibrido". Los apóstrofos tipográficos ("don’t",
    /// "isn’t", habituales tras decodificar &amp;rsquo;) pasan a "'" para que
    /// las negaciones en inglés no dependan del editor de la empresa.
    /// </summary>
    private static string Normalize(string text)
    {
        if (text.Contains('&'))
        {
            text = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text));
        }

        if (text.Contains('<'))
        {
            text = HtmlTagRegex().Replace(text, " ");
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (c is '\u2019' or '\u2018' or '\u00B4' or '`')
            {
                sb.Append('\'');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ── Piezas de los patrones (el texto ya llega normalizado) ──────────

    // Número de días "parcial" (1-4, rangos "2-3" / "dos o tres"): 5 días ya
    // sería la semana completa, así que no indica híbrido.
    private const string DayCount =
        @"(?:[1-4](?:\s*(?:-|/|o|y|a|or|to)\s*[1-4])?"
        + @"|(?:un|uno|una|dos|tres|cuatro|one|two|three|four)(?:\s+(?:o|y|a|or|to)\s+(?:dos|tres|cuatro|two|three|four))?)";

    private const string Days = @"(?:dias?|days?)";

    // Frecuencia parcial: "2 días", "dos veces por semana", "3 times a week",
    // "twice a week". Las veces exigen la SEMANA detrás: "remote once
    // onboarded" no habla de días, y "una vez al mes a la oficina" es una
    // visita puntual de un puesto remoto (ante la duda, remoto).
    private const string DayFreq =
        @"(?:" + DayCount + @"\s+" + Days
        + @"|(?:" + DayCount + @"\s+(?:ve(?:z|ces)|times)|twice|once)\s+(?:(?:a\s+la|a|al|por|cada|per|each|every)\s+(?:semana|week)|semanal(?:es|mente)?|weekly))";

    // Separadores entre palabras de una misma frase. Sin '.', ',' ni ';': así
    // "4 días. Teletrabajo 100%" no se lee como "4 días de teletrabajo".
    private const string Sep = @"[\s/:(\-]+";

    // Palabras de relleno entre la cifra y el modo ("2 días a la semana en
    // la oficina", "2 days per week from home").
    private const string Filler =
        @"(?:a|al|la|las|el|semana|semanales?|por|per|each|week|weekly|mes|month|de|del|en|desde|from|of|in|at|work|working|trabajo|the|nuestra|nuestras|nuestro|our)";

    private const string RemoteTarget = @"(?:teletrabaj\w*|remot[oa]|remote(?:ly)?|casa|home|wfh)";

    private const string OfficeTarget = @"(?:oficinas?|office|presencial\w*|on[\s-]?site|sede)";

    // "Híbrido" sin el uso técnico: "cloud híbrido", "nube pública e híbrida",
    // "hybrid cloud", "Blazor Hybrid", "Azure Hybrid Connections", "hybrid
    // identity", "perfil híbrido backend/DevOps"... aparecen en descripciones
    // de puestos 100% remotos y no dicen nada del modo de trabajo. Solo
    // singular: "entornos híbridos" es casi siempre tecnología. La palabra va
    // primero y la exclusión de delante se comprueba DESPUÉS con un lookbehind
    // que la incluye: así el motor solo la evalúa donde ya hay "híbrido".
    private const string HybridWord =
        @"\b(?:hibrid[oa]|hybrid)\b"
        + @"(?<!\b(?:cloud|clouds|nube|nubes|entorno|aplicacion|app|arquitectura|infraestructura|solucion|sistema|vehiculo|coche|motor|desarrollo|integracion|red|privad[oa]|public[oa]|private|public|azure|blazor|maui|ionic|despliegue|enfoque|perfil|identidad)\s+(?:(?:e|y|o|and|or)\s+)?(?:hibrid[oa]|hybrid))"
        + @"(?![\s/-]+(?:(?:and|or|y|o)[\s/-]+)?(?:multi\w*|clouds?|nubes?|apps?|applications?|mobile|infrastructure|environments?|architectures?|solutions?|search|integrations?|vehicles?|cars?|it|networks?|networking|deployments?|kubernetes|data|storage|engines?|on[\s-]?prem\w*|workloads?|identity|identities|connections?|connectivity|join(?:ed)?|servers?|setups?|systems?|profiles?|approach(?:es)?|development|frameworks?)\b)";

    // "Remoto" sin el uso técnico ("acceso remoto", "remote debugging", "git
    // remote"...). Solo singular en español: "equipos remotos" o "servidores
    // remotos" no describen el puesto.
    private const string RemoteWord =
        @"(?<!\b(?:acceso|escritorio|control|servidor|repositorio|conexion|depuracion|mando|sensor|monitorizacion|administracion|gestion|ejecucion|maquina|sesion|procedimiento|lectura|telemetria|actualizacion|instalacion|configuracion)\s+)"
        + @"\bremot(?:[oa]|amente)\b"
        + @"|(?<!\bgit\s+)\bremote(?:ly)?\b"
        + @"(?![\s-]+(?:desktops?|debug\w*|access|control\w*|sensing|procedures?|repositor(?:y|ies)|repos?|servers?|branch(?:es)?|connections?|monitoring|management|execution|administration|calls?|apis?|hosts?|machines?|sessions?|backups?|configuration|installation|updates?)\b)";

    // ── Negaciones (se neutralizan antes de clasificar) ─────────────────

    /// <summary>
    /// Negaciones del remoto: "no (es) (100%) remoto", "sin (posibilidad de)
    /// teletrabajo", "no se puede teletrabajar", "el teletrabajo no es
    /// posible", "Teletrabajo: No", "not (a) remote", "isn't fully remote",
    /// "no remote", "remote work isn't possible", "we don't offer remote
    /// work", "remote applications will not be considered"...
    /// "Teletrabajo: No" exige fin de frase detrás: "Remote: no relocation
    /// needed" no niega nada.
    /// </summary>
    private const string NegatedRemotePhrase =
        @"(?:\b(?:no|ni|sin)\s+(?:es\s+|sera\s+|se\s+trata\s+de\s+)?(?:un\s+puesto\s+|una\s+posicion\s+|una\s+oferta\s+|un\s+trabajo\s+|trabajo\s+)?(?:100\s*%\s*|totalmente\s+|completamente\s+)?(?:en\s+)?remot[oa]\b"
        // "No se puede teletrabajar MÁS DE 2 días" pone un tope a un híbrido,
        // no niega el remoto: el \b impide que "teletrabaj\w*" se recorte
        // para saltarse la comprobación.
        + @"|\b(?:no|ni|sin)\s+(?:(?:hay|es|se\s+ofrece|ofrecemos|se\s+permite|se\s+admite|admite|permite|es\s+posible|se\s+puede|puede|puedes|podras|incluye|existe|contempla|se\s+contempla)\s+)?(?:el\s+|la\s+)?(?:(?:posibilidad|opcion)\s+(?:de|a)\s+)?(?:100\s*%\s*(?:de\s+)?|totalmente\s+|completamente\s+)?(?:teletrabaj\w*|trabaj\w*\s+(?:100\s*%\s*|totalmente\s+|completamente\s+)?(?:en\s+)?remoto|trabaj\w*\s+a\s+distancia)\b(?!\s+(?:mas\s+del?|more\s+than)\b)"
        + @"|\b(?:teletrabajo|teletrabajable|remot[oa]|remote(?:\s+work)?|trabajo\s+(?:en\s+)?remoto)\s*:\s*no\b(?=[^\S\n]*(?:[\n.,;:|/()!\-]|$))"
        + @"|\b(?:teletrabajo|trabajo\s+(?:en\s+)?remoto|remoto)(?:\s*:\s*|\s+)no\s+(?:es\s+|esta\s+)?(?:disponible|posible|permitido|contemplado|aplica)\b"
        + @"|\b(?:not|isn'?t|aren'?t)\s+(?:a\s+|an\s+)?(?:fully[\s-]+|full[\s-]+|100\s*%\s*|entirely\s+|completely\s+)?remote\b"
        + @"|\bno\s+(?:remote|wfh)\b"
        + @"|\bnon[\s-]?remote\b"
        + @"|\bremote(?:\s+work(?:ing)?|\s+option)?(?:\s*:\s*|\s+)(?:is\s+not|isn'?t|not)\s+(?:possible|available|allowed|offered|supported|an\s+option)\b"
        + @"|\bnot\s+(?:open\s+to|eligible\s+for|available\s+for|offering|offered\s+as)\s+(?:a\s+)?remote\b"
        + @"|\b(?:cannot|can'?t|can\s+not)\s+be\s+(?:done\s+|performed\s+|worked\s+)?remote(?:ly)?\b"
        + @"|\b(?:do\s+not|don'?t|does\s+not|doesn'?t|cannot|can'?t|can\s+not)\s+(?:offer|support|allow|provide)\s+(?:a\s+|any\s+)?(?:fully[\s-]+|full[\s-]+|100\s*%\s*)?remote\b"
        + @"|\bremote\s+(?:applications?|applicants?|candidates?|candidacies)\s+(?:will\s+not|won'?t|would\s+not|are\s+not|aren'?t|is\s+not|isn'?t|cannot|can'?t|can\s+not)\s+(?:be\s+)?(?:considered|accepted|reviewed)\b"
        + @")";

    // Calificador geográfico tras una negación de remoto: "(no remoto)
    // internacional", "(no remote work) from outside the EU", "(no se permite
    // teletrabajar) desde el extranjero", "(not open to remote) candidates
    // outside Spain". Empieza por \s+, así que el \w* de "teletrabaj\w*" no
    // puede recortarse para colarlo a media palabra.
    private const string GeoQualifier =
        @"\s+(?:(?:work(?:ing)?|candidates?|applicants?|applications?|employees?|hires?|hiring|workers?|contractors?|trabajadores?|candidat(?:os?|as?|uras?)|empleados?|perfiles?)\s+)?"
        + @"(?:desde\s+(?:el\s+extranjero|fuera|otros?\s+pais(?:es)?|cualquier)"
        + @"|fuera\s+de|en\s+el\s+extranjero|internacional(?:es|mente)?|international(?:ly)?|worldwide|global(?:ly|mente)?"
        + @"|from\s+(?:abroad|outside|other\s+countr\w*|anywhere|any\s+(?:country|location))|outside|abroad|overseas|anywhere"
        + @"|(?:for|to)\s+(?:candidates?|applicants?|residents?|people|anyone)\s+(?:outside|abroad|from\s+outside|(?:living|based|located)\s+outside))\b";

    /// <summary>Negación de remoto acotada por geografía (ver <see cref="GeoQualifier"/>).</summary>
    [GeneratedRegex(NegatedRemotePhrase + GeoQualifier, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeoRestrictedRemoteRegex();

    /// <summary>
    /// Negaciones de remoto (<see cref="NegatedRemotePhrase"/>) y exigencias
    /// explícitas de presencia: "100% presencial", "fully on-site"...
    /// </summary>
    [GeneratedRegex(
        NegatedRemotePhrase
        + @"|(?:\b100\s*%|\btotalmente|\bcompletamente|\bintegramente)\s*presencial\b|\bpresencial\s*(?:al\s+)?100\s*%"
        + @"|(?:\b100\s*%|\bfully|\bentirely)\s*(?:on[\s-]?site|in[\s-]office|office[\s-]based)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegatedRemoteRegex();

    /// <summary>"No es híbrido", "ni híbrido", "not hybrid", "Híbrido: No".</summary>
    [GeneratedRegex(
        @"\b(?:no|ni|sin)\s+(?:es\s+)?(?:un\s+puesto\s+|una\s+posicion\s+|modelo\s+|modalidad\s+|trabajo\s+)?(?:hibrid[oa]|hybrid|semi[\s-]?presencial)\b"
        + @"|\bnot\s+(?:a\s+)?hybrid\b"
        + @"|\b(?:hibrid[oa]|hybrid)\s*:\s*no\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegatedHybridRegex();

    // ── Señales de modo ─────────────────────────────────────────────────

    /// <summary>
    /// Remoto COMPLETO explícito: "100% remoto", "100% en remoto", "100% de
    /// teletrabajo", "teletrabajo (al) 100%", "remote 100%", "full(y)
    /// remote", "remote-only", "totalmente/completamente (en) remoto",
    /// "entirely remote", "teletrabajo total/completo". Se evalúa tras borrar
    /// las negaciones, así que "no es 100% remoto" ya no está.
    /// </summary>
    [GeneratedRegex(
        @"\b100\s*%\s*(?:de\s+|en\s+)?(?:remot[oa]|remote|teletrabaj\w*)\b"
        + @"|\b(?:teletrabajo|remot[oa]|remote)\s*:?\s*(?:al\s+)?100\s*%"
        + @"|\bfull(?:y)?[\s-]+remote\b|\bremote[\s-]only\b"
        + @"|\b(?:totalmente|completamente|integramente|entirely|completely)\s+(?:en\s+)?(?:remot[oa]|remote)\b"
        + @"|\b(?:teletrabajo|remoto)\s+(?:total|completo)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullRemoteRegex();

    // Piezas de RemoteOrHybridRegex.
    // Separador de alternativas: "/", "|", ",", ";", "&", "+", "o", "u", "y",
    // "e", "or", "and" (y ", o" / ", and").
    private const string AltSep = @"\s*(?:[/|;&+]|,(?:\s*(?:o|y|or|and)\b)?|\b(?:o|u|y|e|or|and)\b)\s*";

    // Solo los separadores que son claramente una ALTERNATIVA ("o", "or",
    // "/"). Con "y"/"and"/"," detrás de "híbrido" la frase suele describir el
    // propio híbrido ("modelo híbrido y teletrabajo flexible").
    private const string AltOrSep = @"\s*(?:[/|]|\b(?:o|u|or)\b)\s*";

    // Aclaración entre paréntesis tras una modalidad ("Remote (Spain) or
    // Hybrid (Barcelona)") o el cierre de "(Hybrid) or Remote". Acotada a 40
    // caracteres para no saltar frases enteras.
    private const string AltParen = @"(?:\s*\([^()\n]{0,40}\)|\))?";

    private const string ModeFiller = @"(?:(?:modelo|modalidad|trabajo|oficina|en|un|una|a|an)\s+){0,3}";

    private const string FullQualifier = @"(?:100\s*%\s*|fully[\s-]+|full[\s-]+|totalmente\s+|completamente\s+)";

    private const string AltRemoteWord = @"(?:en\s+)?(?:remot[oa]|remote|teletrabajo)\b";

    // Palabras que convierten "híbrido y remoto" en una lista de opciones.
    private const string OptionWord =
        @"\s+(?:options?|opciones|modalidades|modes?|positions?|roles?|possible|posibles?|available|disponibles?|segun|a\s+elegir)\b";

    // Lo que sigue al remoto y lo vuelve PARCIAL: "híbrido / teletrabajo 2
    // días", "híbrido o remoto los viernes"... Ahí el remoto es la parte en
    // casa del híbrido, no una alternativa. "100%" detrás sí es alternativa.
    private const string PartialTail =
        @"(?![\s:\-]*(?:(?:de|hasta|up\s+to|work|working|trabajo)\s+){0,2}"
        + @"(?:(?!100\s*%)[0-9]|(?:un|uno|una|dos|tres|cuatro|one|two|three|four|twice|once)\b|parcial|ocasional|puntual|partial|occasional|part\b"
        + @"|algun|ciert|vari[oa]s\b|determinad|some\b|a\s+few\b|certain\b|(?:los\s+)?(?:lunes|martes|miercoles|jueves|viernes)\b"
        + @"|on\s+(?:mon|tue|wed|thu|fri)|dias?\b|days?\b|the\s+rest\b|el\s+resto\b))";

    /// <summary>
    /// Remoto como ALTERNATIVA a lo híbrido: la oferta admite remoto completo.
    /// "Remoto o híbrido", "100% remoto o modelo híbrido", "Remote and hybrid
    /// options", "Modalidad remota e híbrida", "Remoto, híbrido o presencial",
    /// "Remote (Spain) or Hybrid (Barcelona)", "Híbrido / 100% remoto",
    /// "Hybrid and fully remote"... Un remoto PARCIAL no vale: "2 días
    /// remoto, híbrido" o "híbrido / teletrabajo 2 días" siguen siendo híbridos.
    /// </summary>
    [GeneratedRegex(
        // Remoto primero, con cualquier separador. Se ancla en la palabra
        // remota (un "100%"/"fully" delante no cambia si casa) y DESPUÉS se
        // mira hacia atrás que no sea un remoto parcial ("2 días en remoto,
        // híbrido..."): así el motor solo prueba el patrón donde hay "remot"
        // o "teletrabajo" y no en cada palabra.
        @"\b(?:remot[oa]|remote|teletrabajo)\b"
        + @"(?<!\b(?:parcial(?:mente)?|partial(?:ly)?|partly|part|semi|ocasional(?:mente)?|occasional(?:ly)?|dias?|days?)[\s-]+(?:(?:de|en)\s+)?(?:remot[oa]|remote|teletrabajo))"
        + @"(?:\s*:?\s*(?:al\s+)?100\s*%)?" + AltParen + AltSep + ModeFiller + HybridWord
        // Híbrido primero con "o" / "/": alternativa clara.
        + @"|\b(?:hibrid[oa]|hybrid)" + AltParen + AltOrSep + ModeFiller + FullQualifier + @"?" + AltRemoteWord + PartialTail
        // Híbrido primero con "y" / "and" / ",": solo si el remoto es
        // completo ("hybrid and fully remote") o se habla de opciones
        // ("hybrid and remote options").
        + @"|\b(?:hibrid[oa]|hybrid)" + AltParen + AltSep + ModeFiller
        + @"(?:" + FullQualifier + AltRemoteWord + @"|" + AltRemoteWord + OptionWord + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteOrHybridRegex();

    /// <summary>
    /// Híbrido o remoto PARCIAL: "híbrido", "semipresencial", "2 días de
    /// teletrabajo", "días en remoto", "teletrabajo 2 días", "teletrabajo dos
    /// veces por semana", "work from home twice a week", "remote 3 days a
    /// week", "3 días en oficina", "office attendance: 3 days", "teletrabajo
    /// 60%", "teletrabajo parcial/ocasional", "teletrabajar algunos días",
    /// "(viernes) teletrabajo (los viernes)", "remotely the rest of the week",
    /// "presencial con posibilidad de teletrabajo"...
    /// </summary>
    [GeneratedRegex(
        HybridWord
        + @"|\bsemi[\s-]?(?:presencial|remot[oa]|remote)\b"
        // "2 días (a la semana) de teletrabajo / en la oficina", "2 days remote",
        // "twice a week from home"
        + @"|\b" + DayFreq + @"(?:" + Sep + Filler + @"){0,6}" + Sep + @"(?:" + RemoteTarget + @"|" + OfficeTarget + @")\b"
        // "teletrabajo 2 días", "remote work 3 days a week", "presencialidad: 3
        // días", "office attendance: 3 days", "from home twice a week"
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:ly)?|from\s+home|desde\s+casa|wfh|presencial(?:idad)?|oficina|office|on[\s-]?site)"
        + @"(?:\s+(?:de|hasta|up\s+to|mas\s+de|more\s+than|work|working|trabajo|attendance|presence|asistencia|presencia|required|minimo|minimum|at\s+least|al\s+menos)){0,3}[\s:(\-]+" + DayFreq + @"\b"
        // Porcentaje PARCIAL de remoto (10-80 %): "teletrabajo 60%", "50% en
        // remoto". 90-99 % son visitas puntuales: ambiguo, se queda en remoto.
        // "Remote (20% travel)" habla de viajes, no de oficina.
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:ly)?)\s*[:(]?\s*(?:al\s+|del\s+)?(?:[1-7][0-9]|80)\s*%(?!\s*(?:de\s+)?(?:travel\w*|viajes?|desplazamientos?|bonus|variable))"
        + @"|\b(?:[1-7][0-9]|80)(?<![.,]\d\d)\s*%\s*(?:(?:de|del|en|of|the|time|tiempo)\s+){0,3}(?:teletrabaj\w*|remot[oa]|remote(?:ly)?)\b"
        // Sin cifra: "días de teletrabajo", "días en remoto", "remote days"
        + @"|\bdias?\s+(?:de\s+teletrabajo|de\s+trabajo\s+(?:en\s+)?remoto|de\s+remoto|en\s+remoto|remotos?)\b|\bremote\s+days\b"
        // "teletrabajar algunos días", "some days remote"
        + @"|\bteletrabaj\w*\s+(?:algunos|algun|ciertos|varios|determinados)\s+dias\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+(?:some|a\s+few|certain)\s+days\b|\b(?:some|a\s+few|certain)\s+days\s+(?:remote(?:ly)?|from\s+home)\b"
        // "teletrabajo los viernes", "(los) viernes (de) teletrabajo", "remote
        // on Fridays". "De lunes a viernes en remoto" es la semana entera: no.
        + @"|\bteletrabaj\w*\s+(?:los\s+)?(?:lunes|martes|miercoles|jueves|viernes)\b(?!\s+(?:a|al|hasta)\s+(?:lunes|martes|miercoles|jueves|viernes|sabado|domingo)\b)"
        + @"|\b(?:lunes|martes|miercoles|jueves|viernes)(?<!\b(?:a|al|hasta)\s+(?:lunes|martes|miercoles|jueves|viernes))\s+(?:de\s+teletrabajo|en\s+remoto|teletrabaj\w*|remoto)\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+on\s+(?:mondays?|tuesdays?|wednesdays?|thursdays?|fridays?)\b(?!\s*(?:-|to|through|thru|until)\s*(?:mon|tue|wed|thu|fri))"
        // "remotely the rest of the week", "teletrabajo el resto de la semana"
        + @"|\b(?:remote(?:ly)?|from\s+home|wfh)\s+(?:for\s+)?the\s+(?:rest|remainder)\s+of\s+the\s+week\b"
        + @"|\b(?:teletrabaj\w*|remoto|desde\s+casa)\s+el\s+resto\s+de\s+(?:la\s+semana|los\s+dias)\b"
        // ("(el) resto": empezar por "el" obligaría a probar el patrón en cada
        // palabra que empieza por "e", y no cambia el resultado.)
        + @"|\bresto\s+de\s+(?:la\s+semana|los\s+dias)\s+(?:en\s+remoto|teletrabaj\w*|desde\s+casa|remoto)\b"
        // "presencial con posibilidad de teletrabajo", "on-site with some remote work"
        + @"|\b(?:presencial|on[\s-]?site|in[\s-]office|office[\s-]based)\s+(?:con|with|y|and|\+)\s+(?:(?:posibilidad|opcion|option|possibility|flexibilidad|flexibility)\s+(?:de|a|of|for|to)\s+)?(?:algo\s+de\s+|some\s+|occasional\s+)?(?:teletrabaj\w*|remot[oa]|remote(?:ly)?|trabajo\s+en\s+remoto|work(?:ing)?\s+from\s+home)\b"
        + @"|\b(?:teletrabajo|remoto|remote)\s+(?:y|and|\+)\s+presencial\b"
        // "teletrabajo parcial/ocasional/puntual", "partially remote"; "remote
        // part-time" NO: es una jornada parcial en remoto.
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:\s+work(?:ing)?)?)\s*[:\-]?\s*(?:parcial(?:mente)?|ocasional(?:mente)?|puntual(?:mente)?|partial(?:ly)?|occasional(?:ly)?|part(?![\s-]*time))\b"
        + @"|\b(?:parcial(?:mente)?|ocasional(?:mente)?|puntual(?:mente)?|partial(?:ly)?|partly|part|parte|occasional(?:ly)?)[\s-]+(?:en\s+)?(?:teletrabaj\w*|remot[oa]|remote(?:ly)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HybridRegex();

    /// <summary>
    /// Remoto a secas: "remoto", "en remoto", "100% remoto", "teletrabajo",
    /// "remote", "fully remote", "remote-first", "work from home", "no
    /// presencial"... Solo se mira tras descartar lo híbrido y lo negado.
    /// </summary>
    [GeneratedRegex(
        RemoteWord
        + @"|\btele-?trabaj\w*"
        + @"|\b(?:work(?:ing)?|trabaj\w*)\s+(?:from|desde)\s+(?:home|casa|anywhere|cualquier\s+(?:lugar|sitio|parte))\b"
        + @"|\bwfh\b|\bhome[\s-]office\b|\btrabajo\s+a\s+distancia\b|\bno\s+presencial\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteRegex();

    /// <summary>
    /// Presencial: "presencial" (no "semipresencial" ni "entrevista
    /// presencial"), "on-site/onsite/on site", "in office", "office-based".
    /// </summary>
    [GeneratedRegex(
        @"(?<!\b(?:no|sin|semi|entrevistas?|formacion|reunion(?:es)?|eventos?|visitas?)[\s-]+)\bpresencial(?:es|idad|mente)?\b"
        + @"|\bon[\s-]?site\b(?![\s-]+(?:reliability|interviews?|visits?)\b)"
        + @"|\bin[\s-]office\b|\boffice[\s-]based\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnSiteRegex();

    // Separador de sedes en un campo corto: ";", "|" o " / " con espacios. Un
    // "/" pegado o entre cifras ("2/3 días", "días/semana") no separa sedes.
    [GeneratedRegex(@"\s*[;|]\s*|(?<!\d)\s+/\s+(?!\d)")]
    private static partial Regex SegmentSeparatorRegex();

    // Exige una letra tras "<" (o "</"), como HtmlText.TagRegex: las
    // descripciones llegan ya en texto plano, y un "<10 personas ... >3 años"
    // literal se comía con `<[^>]+>` la frase del modo que quedaba en medio.
    [GeneratedRegex(@"</?[a-zA-Z][^<>]*>")]
    private static partial Regex HtmlTagRegex();
}
