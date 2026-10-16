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
///     b. Las negaciones ("sin teletrabajo", "not remote") se borran. Un TOPE
///        al teletrabajo ("no se puede teletrabajar más de 2 días / todos los
///        días / los lunes / al 100%", "we don't offer remote work on
///        Mondays") también se borra, pero cuenta como señal híbrida.
///     c. Un remoto COMPLETO explícito que sobreviva ("100% remoto", "fully
///        remote") o el remoto como alternativa ("remoto o híbrido") es
///        remoto, aunque el texto también diga "híbrido" o restrinja el país.
///     d. Lo híbrido/parcial se comprueba ANTES que el remoto a secas.
///     e. Una negación de remoto sin señal híbrida es presencial, SALVO que
///        sobreviva una frase de remoto positiva y el campo no exija presencia
///        de forma explícita ("100% presencial", "fully on-site",
///        "presencialidad obligatoria"): entonces es remoto. "Remote role
///        within Spain. We cannot offer remote contracts in Portugal" o
///        "Puesto en remoto. Ayuda para coworking si no puedes teletrabajar
///        desde casa" niegan un remoto CONCRETO, no el del puesto. No se
///        intenta enumerar qué acota cada negación (países, regiones,
///        beneficios...): la lista nunca estaría completa, y equivocarse
///        hacia remoto lo corrige la IA, mientras que un falso
///        híbrido/presencial lo descarta el filtro de desplazamiento antes de
///        que la IA lo vea.
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

        // Las negaciones y los topes se borran del texto para que "sin
        // teletrabajo" no cuente después como remoto ni "no es híbrido" como
        // híbrido. Toda restricción geográfica y todo tope empiezan por una
        // negación, así que sin ella no hay nada más que buscar (y se ahorran
        // varias pasadas sobre descripciones largas).
        var negatesRemote = false;
        var capsRemote = false;
        var requiresOnSite = false;
        if (RemoteNeutralizerRegex().IsMatch(text))
        {
            // Las restricciones geográficas ("no remoto internacional", "no se
            // permite teletrabajar desde el extranjero") no niegan el remoto:
            // lo acotan. Se borran ANTES y sin contar como ninguna señal: así
            // "Presencial. No remoto internacional" sigue presencial y "No
            // remote work from outside the EU" a secas queda desconocido.
            text = GeoRestrictedRemoteRegex().Replace(text, " ");

            // Se mira ANTES de borrar: "100% presencial" también se neutraliza.
            requiresOnSite = ExplicitOnSiteRegex().IsMatch(text);

            // Las variables que captura la lambda se declaran aquí dentro para
            // que el cierre solo se reserve cuando hay algo que neutralizar.
            var caps = false;
            var negates = false;
            text = RemoteNeutralizerRegex().Replace(text, m =>
            {
                if (m.Groups["cap"].Success)
                {
                    caps = true;
                }
                else
                {
                    negates = true;
                }

                return " ";
            });
            capsRemote = caps;
            negatesRemote = negates;
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

        // Un tope ("no se puede teletrabajar más de 2 días") es la parte en
        // casa de un híbrido: como presencial, con AcceptOnSite=false se
        // descartaría una oferta híbrida válida.
        if (capsRemote || HybridRegex().IsMatch(text))
        {
            return WorkMode.Hybrid;
        }

        // Regla 2.e: la negación solo decide si no sobrevive un remoto
        // positivo, o si el campo exige presencia de forma explícita ("100%
        // presencial, quizá teletrabajo en el futuro" es presencial).
        if (negatesRemote)
        {
            return !requiresOnSite && RemoteRegex().IsMatch(text) ? WorkMode.Remote : WorkMode.OnSite;
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
    ///
    /// Cada racha de espacios queda en UNO, y la que contiene un salto de
    /// línea en un "\n" (del que depende el "Teletrabajo: No" de fin de
    /// línea). Los patrones encadenan separadores opcionales ("remoto : 100%",
    /// "remoto (Spain) o híbrido"), y JSearch y Lever mandan la descripción en
    /// bruto: con miles de espacios seguidos el motor probaba cada reparto
    /// posible de la racha entre ellos (segundos por oferta).
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
        var pendingSpace = false;
        var pendingNewLine = false;

        foreach (var c in decomposed)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                pendingNewLine |= c is '\n' or '\r';
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(pendingNewLine ? '\n' : ' ');
                pendingSpace = pendingNewLine = false;
            }

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

    // ── Negaciones y topes (se neutralizan antes de clasificar) ─────────

    // Tope DETRÁS de una negación de remoto: "no se puede teletrabajar MÁS
    // DE / MÁS QUE 2 días", "... todos los días", "... los lunes", "... al
    // 100%", "we don't offer remote work FOR MORE THAN 2 days / on Mondays /
    // full-time". No niega el remoto: dice CUÁNTO, y eso es un híbrido.
    // Empieza por \s+, así que el \w* de "teletrabaj\w*" no puede recortarse
    // para saltárselo. Un "100%" DELANTE ("no es (posible trabajar) 100% en
    // remoto") niega el modo completo, no lo acota: sigue siendo negación.
    private const string RemoteCap =
        @"\s+(?:(?:work(?:ing)?|trabajo|en\s+remoto|remotely|from\s+home|desde\s+casa)\s+)?"
        + @"(?:mas\s+(?:del?|que)|(?:for\s+)?more\s+than"
        + @"|(?:al|el)\s+100|100\s*%\s*(?:del?\s+(?:la\s+)?(?:tiempo|semana|jornada)|of\s+the\s+(?:time|week))"
        + @"|todos\s+los\s+dias|toda\s+la\s+(?:semana|jornada)|a\s+(?:tiempo|jornada)\s+complet[ao]|every\s+day|all\s+week|full[\s-]time"
        + @"|(?:los\s+|el\s+)?(?:lunes|martes|miercoles|jueves|viernes)|on\s+(?:mon|tue|wed|thu|fri)\w*)\b";

    // Negaciones que admiten un tope detrás (ver RemoteCap):
    //  - "no (se puede / se permite / hay...) teletrabajar", "sin
    //    (posibilidad de) teletrabajo", "no es posible trabajar en remoto";
    //  - "el teletrabajo no es posible", "Teletrabajo: no disponible";
    //  - "no remote", "no WFH", "no home office", "no work from home";
    //  - "remote work isn't possible", "working remotely is not allowed";
    //  - "we don't offer / cannot support / are unable to offer (fully) remote".
    private const string CappableNegation =
        @"(?:\b(?:no|ni|sin)\s+(?:(?:hay|es|se\s+ofrece|ofrecemos|se\s+permite|se\s+admite|admite|permite|es\s+posible|se\s+puede|puede|puedes|podras|incluye|existe|contempla|se\s+contempla)\s+)?(?:el\s+|la\s+)?(?:(?:posibilidad|opcion)\s+(?:de|a)\s+)?(?:100\s*%\s*(?:de\s+)?|totalmente\s+|completamente\s+)?(?:tele-?trabaj\w*|trabaj\w*\s+(?:100\s*%\s*|totalmente\s+|completamente\s+)?(?:en\s+)?remoto|trabaj\w*\s+a\s+distancia)\b"
        + @"|\b(?:teletrabajo|trabajo\s+(?:en\s+)?remoto|remoto)(?:\s*:\s*|\s+)no\s+(?:es\s+|esta\s+)?(?:disponible|posible|permitido|contemplado|aplica)\b"
        + @"|\b(?:no|sin)\s+(?:remote|wfh|home[\s-]office|work(?:ing)?\s+from\s+home)\b"
        + @"|\b(?:remote(?:\s+work(?:ing)?|\s+option)?|work(?:ing)?\s+(?:from\s+home|remotely))(?:\s*:\s*|\s+)(?:is\s+not|isn'?t|not)\s+(?:possible|available|allowed|offered|supported|an\s+option)\b"
        + @"|\b(?:do\s+not|don'?t|does\s+not|doesn'?t|cannot|can'?t|can\s+not|unable\s+to|not\s+able\s+to)\s+(?:offer|support|allow|provide)\s+(?:a\s+|any\s+)?(?:fully[\s-]+|full[\s-]+|100\s*%\s*)?remote\b)";

    // Negaciones que NO admiten tope: "no (es) (100%) remoto", "no hay opción
    // de 100% remoto", "Teletrabajo: No", "not (a) remote", "isn't fully
    // remote", "non-remote", "not eligible for fully remote", "can't be done
    // remotely", "remote applications will not be considered"... Un "no es
    // remoto al 100%" niega el modo, igual que "no es 100% remoto".
    // "Teletrabajo: No" exige fin de frase detrás: "Remote: no relocation
    // needed" no niega nada.
    private const string FixedRemoteNegation =
        @"\b(?:no|ni|sin)\s+(?:(?:es|sera|se\s+trata\s+de|hay|existe|ofrecemos|se\s+ofrece|contempla(?:mos)?|se\s+contempla|permite|permitimos|se\s+permite|admite|admitimos|se\s+admite|es\s+posible)\s+)?(?:(?:el|la|un|una)\s+)?(?:(?:posibilidad|opcion)\s+(?:de|a)\s+(?:(?:el|un)\s+)?)?(?:puesto\s+|posicion\s+|oferta\s+|trabajo\s+|modelo\s+|modalidad\s+)?(?:100\s*%\s*|totalmente\s+|completamente\s+)?(?:en\s+)?remot[oa]\b"
        + @"|\b(?:teletrabajo|teletrabajable|remot[oa]|remote(?:\s+work)?|trabajo\s+(?:en\s+)?remoto)\s*:\s*no\b(?=[^\S\n]*(?:[\n.,;:|/()!\-]|$))"
        + @"|\b(?:not|isn'?t|aren'?t)\s+(?:a\s+|an\s+)?(?:fully[\s-]+|full[\s-]+|100\s*%\s*|entirely\s+|completely\s+)?remote\b"
        + @"|\bnon[\s-]?remote\b"
        + @"|\bnot\s+(?:open\s+to|eligible\s+for|available\s+for|looking\s+for|offering|offered\s+as)\s+(?:a\s+)?(?:fully[\s-]+|full[\s-]+|100\s*%\s*|entirely\s+|completely\s+)?remote\b"
        + @"|\b(?:cannot|can'?t|can\s+not)\s+be\s+(?:done\s+|performed\s+|worked\s+)?remote(?:ly)?\b"
        + @"|\bremote\s+(?:applications?|applicants?|candidates?|candidacies)\s+(?:will\s+not|won'?t|would\s+not|are\s+not|aren'?t|is\s+not|isn'?t|cannot|can'?t|can\s+not)\s+(?:be\s+)?(?:considered|accepted|reviewed)\b";

    /// <summary>
    /// Negaciones del remoto: las <see cref="CappableNegation"/> que NO llevan
    /// un tope detrás ("no se puede teletrabajar más de 2 días" no niega el
    /// remoto: es un híbrido) y <see cref="FixedRemoteNegation"/>. Las que
    /// admiten tope van primero: "no hay trabajo remoto los viernes" casa
    /// también "no hay trabajo remoto" de las fijas, y debe leerse como tope.
    /// </summary>
    private const string NegatedRemotePhrase =
        @"(?:" + CappableNegation + @"(?!" + RemoteCap + @")|" + FixedRemoteNegation + @")";

    // Exigencia explícita de presencia: "100% presencial", "presencial al
    // 100%", "totalmente presencial", "fully on-site", "100% in-office".
    private const string ExplicitOnSitePhrase =
        @"(?:\b100\s*%|\btotalmente|\bcompletamente|\bintegramente)\s*presencial\b|\bpresencial\s*(?:al\s+)?100\s*%"
        + @"|(?:\b100\s*%|\bfully|\bentirely)\s*(?:on[\s-]?site|in[\s-]office|office[\s-]based)\b";

    // "Presencial obligatorio", "la presencialidad es obligatoria". No vale
    // para un evento ("formación presencial obligatoria") ni con una
    // frecuencia detrás: "presencialidad obligatoria una vez al trimestre" es
    // la visita de un puesto remoto.
    private const string MandatoryOnSitePhrase =
        @"\bpresencial(?:idad)?"
        + @"(?<!\b(?:entrevistas?|formacion|reunion(?:es)?|eventos?|visitas?|incorporacion|onboarding)\s+presencial(?:idad)?)"
        + @"\s+(?:es\s+)?obligatori[oa]\b"
        + @"(?![^.;\n]{0,50}?\b(?:\d|(?:un[oa]?|dos|tres|cuatro|algun\w*|ciert[oa]s|vez|veces|dias?|semana\w*|mes(?:es)?|mensual\w*|trimestr\w*|anual\w*|puntual\w*|ocasional\w*)\b))";

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
    /// Todo lo que se borra antes de clasificar: negaciones de remoto
    /// (<see cref="NegatedRemotePhrase"/>), topes al teletrabajo (una
    /// <see cref="CappableNegation"/> seguida de <see cref="RemoteCap"/>, que
    /// casa el grupo vacío "cap" y cuenta como señal híbrida) y exigencias
    /// explícitas de presencia (<see cref="ExplicitOnSitePhrase"/>). La
    /// negación con y sin tope va en una sola alternativa, con el grupo
    /// opcional, para no recorrer dos veces cada descripción buscándola; y va
    /// primero, como en <see cref="NegatedRemotePhrase"/>.
    /// </summary>
    [GeneratedRegex(
        CappableNegation + "(?:(?<cap>)(?=" + RemoteCap + "))?|" + FixedRemoteNegation + "|" + ExplicitOnSitePhrase,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteNeutralizerRegex();

    /// <summary>
    /// Exigencia explícita de presencia (<see cref="ExplicitOnSitePhrase"/>,
    /// <see cref="MandatoryOnSitePhrase"/>): con ella, un remoto que sobreviva
    /// a una negación no cuenta ("100% presencial, quizá teletrabajo en el
    /// futuro" es presencial).
    /// </summary>
    [GeneratedRegex(ExplicitOnSitePhrase + "|" + MandatoryOnSitePhrase, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOnSiteRegex();

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
        // "\s*(?::\s*)?" y no "\s*:?\s*": dos \s* seguidos se repartían una
        // racha de espacios de todas las formas posibles antes de fallar.
        + @"|\b(?:teletrabajo|remot[oa]|remote)\s*(?::\s*)?(?:al\s+)?100\s*%"
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

    // Tras "teletrabajo" (que a secas no es remoto completo) vale además la
    // coma de una lista ("Teletrabajo, híbrido o presencial"), pero no
    // "y"/"e": "Teletrabajo y modelo híbrido: 2 días en oficina" describe el
    // propio híbrido.
    private const string AltOrCommaSep = @"\s*(?:[/|]|,(?:\s*(?:o|u|or)\b)?|\b(?:o|u|or)\b)\s*";

    // "remoto 100%", "remote: 100%", "teletrabajo al 100%". "\s*(?::\s*)?" y
    // no "\s*:?\s*", por lo mismo que en FullRemoteRegex.
    private const string Full100Tail = @"\s*(?::\s*)?(?:al\s+)?100\s*%";

    // Aclaración entre paréntesis tras una modalidad ("Remote (Spain) or
    // Hybrid (Barcelona)") o el cierre de "(Hybrid) or Remote". Acotada a 40
    // caracteres para no saltar frases enteras.
    private const string AltParen = @"(?:\s*\([^()\n]{0,40}\)|\))?";

    private const string ModeFiller = @"(?:(?:modelo|modalidad|trabajo|oficina|en|un|una|a|an)\s+){0,3}";

    private const string FullQualifier = @"(?:100\s*%\s*|fully[\s-]+|full[\s-]+|totalmente\s+|completamente\s+)";

    private const string AltRemoteWord = @"(?:en\s+)?(?:remot[oa]|remote|teletrabajo)\b";

    // Lookbehind tras la palabra remota del "remoto primero": no vale un
    // remoto parcial ("2 días en remoto, híbrido...", "teletrabajo parcial").
    private const string RemoteFirstGuard =
        @"(?<!\b(?:parcial(?:mente)?|partial(?:ly)?|partly|part|semi|ocasional(?:mente)?|occasional(?:ly)?|dias?|days?)[\s-]+(?:(?:de|en)\s+)?(?:remot[oa]|remote|teletrabajo))";

    // Palabras que convierten "híbrido y remoto" en una lista de opciones.
    private const string OptionWord =
        @"\s+(?:options?|opciones|modalidades|modes?|positions?|roles?|possible|posibles?|available|disponibles?|segun|a\s+elegir)\b";

    // Lo que sigue al remoto y lo vuelve PARCIAL: "híbrido / teletrabajo 2
    // días", "híbrido o remoto los viernes", "... algunos días", "... part of
    // the week"... Ahí el remoto es la parte en casa del híbrido, no una
    // alternativa. "100%" detrás sí es alternativa, y una cantidad que no
    // cuenta días tampoco vuelve parcial nada: "Hybrid or remote - some
    // travel required", "one of the two", "part of a growing team", "a few
    // trips per year", "occasional travel".
    private const string PartialTail =
        @"(?![\s:\-]*(?:(?:de|hasta|up\s+to|work|working|trabajo)\s+){0,2}"
        + @"(?:(?!100\s*%)[0-9]|" + DayFreq
        + @"|(?:algun[oa]?s?|ciert[oa]s|vari[oa]s|determinad[oa]s|some|a\s+few|several|certain|un[oa]?|dos|tres|cuatro|one|two|three|four)\s+(?:dias?|days?|veces|times|jornadas?)\b"
        + @"|(?:parcial|ocasional|puntual)(?:mente)?\b(?!\s+(?:viajes?|visitas?|desplazamientos?)\b)"
        + @"|(?:partial|occasional)(?:ly)?\b(?!\s+(?:travel\w*|visits?|trips?)\b)"
        + @"|part\s+of\s+the\s+(?:week|time)\b|(?:los\s+)?(?:lunes|martes|miercoles|jueves|viernes)\b"
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
        // Remoto primero. Se ancla en la palabra remota (un "100%"/"fully"
        // delante no cambia si casa) y DESPUÉS se mira hacia atrás que no sea
        // un remoto parcial: así el motor solo prueba el patrón donde hay
        // "remot" o "teletrabajo" y no en cada palabra. "Remoto" admite
        // cualquier separador; "teletrabajo", solo los de alternativa salvo
        // que lleve "100%".
        @"\b(?:(?:remot[oa]|remote)\b" + RemoteFirstGuard + @"(?:" + Full100Tail + @")?" + AltParen + AltSep
        + @"|teletrabajo\b" + RemoteFirstGuard + @"(?:" + Full100Tail + AltParen + AltSep + @"|" + AltParen + AltOrCommaSep + @"))"
        + ModeFiller + HybridWord
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
        // Detrás del remoto, el porcentaje solo es tiempo si no sigue nada
        // ("Teletrabajo 60%.", "Remoto (60%)"), si sigue una palabra de tiempo
        // ("60% de la jornada", "50% of the time") o el resto del reparto
        // ("60% y 40% presencial"). "Remoto (70% backend, 30% frontend)" o
        // "Remote (20% travel)" reparten el trabajo, no la semana.
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:ly)?)\s*(?:[:(]\s*)?(?:al\s+|del\s+)?(?:[1-7][0-9]|80)\s*%"
        + @"(?=\s*(?:$|[).,;:/|+\n-]|(?:y|e|and)\s+\d{1,2}\s*%|(?:de|del)\s+(?:la\s+)?(?:jornada|tiempo|semana|horas)\b|of\s+(?:the\s+|my\s+|your\s+)?(?:time|week|working\s+(?:time|hours|week)|hours)\b|(?:time|tiempo)\b))"
        + @"|\b(?:[1-7][0-9]|80)(?<![.,]\d\d)\s*%\s*(?:(?:de|del|en|of|the|time|tiempo)\s+){0,3}(?:teletrabaj\w*|remot[oa]|remote(?:ly)?)\b"
        // Sin cifra: "días de teletrabajo", "días en remoto", "remote days"
        + @"|\bdias?\s+(?:de\s+teletrabajo|de\s+trabajo\s+(?:en\s+)?remoto|de\s+remoto|en\s+remoto|remotos?)\b|\bremote\s+days\b"
        // "teletrabajar algunos días", "some days remote"
        + @"|\bteletrabaj\w*\s+(?:algunos|algun|ciertos|varios|determinados)\s+dias\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+(?:some|a\s+few|certain)\s+days\b|\b(?:some|a\s+few|certain)\s+days\s+(?:remote(?:ly)?|from\s+home)\b"
        // "teletrabajo los viernes", "(los) viernes (de) teletrabajo", "remote
        // on Fridays". "De lunes a viernes en remoto" o "lunes-viernes remoto"
        // (guion o raya) es la semana entera: no.
        + @"|\bteletrabaj\w*\s+(?:los\s+)?(?:lunes|martes|miercoles|jueves|viernes)\b(?!(?:\s+(?:a|al|hasta)\s+|\s*[-\u2013\u2014]\s*)(?:lunes|martes|miercoles|jueves|viernes|sabado|domingo)\b)"
        + @"|\b(?:lunes|martes|miercoles|jueves|viernes)(?<!\b(?:(?:a|al|hasta)\s+|(?:lunes|martes|miercoles|jueves)\s*[-\u2013\u2014]\s*)(?:lunes|martes|miercoles|jueves|viernes))\s+(?:de\s+teletrabajo|en\s+remoto|teletrabaj\w*|remoto)\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+on\s+(?:mondays?|tuesdays?|wednesdays?|thursdays?|fridays?)\b(?!\s*(?:[-\u2013\u2014]|to|through|thru|until)\s*(?:mon|tue|wed|thu|fri))"
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
        // part-time" NO: es una jornada parcial en remoto. "\s*(?:[:\-]\s*)?"
        // y no "\s*[:\-]?\s*", por lo mismo que en FullRemoteRegex.
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:\s+work(?:ing)?)?)\s*(?:[:\-]\s*)?(?:parcial(?:mente)?|ocasional(?:mente)?|puntual(?:mente)?|partial(?:ly)?|occasional(?:ly)?|part(?![\s-]*time))\b"
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
