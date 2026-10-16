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
/// Clasificar mal como <see cref="WorkMode.Remote"/> es el error caro: la
/// oferta suma +30 en la puntuación y se salta el filtro de desplazamiento.
/// Por eso un texto que menciona "teletrabajo" no basta: una oferta típica
/// española dice "modelo híbrido con 2 días de teletrabajo", y antes salía
/// como remota porque "teletrabajo" se buscaba primero sobre todo el texto.
///
/// Reglas:
///  1. Prioridad por CAMPO: título &gt; pista estructurada del provider
///     (<c>extra</c>, p. ej. el "type" de Jooble) &gt; localización &gt;
///     descripción. Los campos cortos los rellena la empresa a propósito; la
///     descripción es texto libre que mezcla cultura de empresa, beneficios y
///     tecnologías ("cloud híbrido", "acceso remoto"...). Gana el primer
///     campo que dé un modo concreto.
///  2. Dentro de un mismo texto: las negaciones ("sin teletrabajo", "not
///     remote") se neutralizan antes de buscar nada, y lo híbrido/parcial se
///     comprueba ANTES que el remoto a secas. Una negación de remoto sin
///     señal híbrida es <see cref="WorkMode.OnSite"/>. Única excepción:
///     "remoto o híbrido" es remoto, porque la oferta admite remoto completo.
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
        string?[] fieldsByPriority = [title, extra, location, description];

        foreach (var field in fieldsByPriority)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var mode = Classify(field);
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

    /// <summary>Clasifica UN texto suelto (un único campo).</summary>
    private static WorkMode Classify(string raw)
    {
        var text = Normalize(raw);

        // Las negaciones se borran del texto para que "sin teletrabajo" no
        // cuente después como remoto ni "no es híbrido" como híbrido.
        var negatesRemote = NegatedRemoteRegex().IsMatch(text);
        text = NegatedHybridRegex().Replace(NegatedRemoteRegex().Replace(text, " "), " ");

        // "Remoto o híbrido" es una alternativa: la oferta ADMITE remoto
        // completo, así que no debe acabar como híbrida (y descartada por el
        // filtro de desplazamiento) por mencionar también "híbrido".
        if (RemoteOrHybridRegex().IsMatch(text))
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
    /// nunca casaría con "hibrido".
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
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
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
        + @"|(?:un|uno|dos|tres|cuatro|one|two|three|four)(?:\s+(?:o|y|a|or|to)\s+(?:dos|tres|cuatro|two|three|four))?)";

    private const string Days = @"(?:dias?|days?)";

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
    // "hybrid cloud", "hybrid mobile apps"... aparecen en descripciones de
    // puestos 100% remotos y no dicen nada del modo de trabajo. Solo singular:
    // "entornos híbridos" es casi siempre tecnología.
    private const string HybridWord =
        @"(?<!\b(?:cloud|nube|entorno|aplicacion|app|arquitectura|infraestructura|solucion|sistema|vehiculo|coche|motor|desarrollo|integracion|red|privad[oa]|public[oa])\s+(?:(?:e|y|o)\s+)?)"
        + @"\b(?:hibrid[oa]|hybrid)\b"
        + @"(?![\s/-]+(?:(?:and|or|y|o)[\s/-]+)?(?:multi\w*|cloud|nube|apps?|applications?|mobile|infrastructure|environments?|architectures?|solutions?|search|integrations?|vehicles?|cars?|it|networks?|deployments?|kubernetes|data|storage|engines?|on[\s-]?prem\w*)\b)";

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
    /// Negaciones de remoto y exigencias explícitas de presencia: "no (es)
    /// (100%) remoto", "sin (posibilidad de) teletrabajo", "no hay
    /// teletrabajo", "Teletrabajo: No", "not (a) remote", "no remote",
    /// "remote work is not possible", "100% presencial"...
    /// </summary>
    [GeneratedRegex(
        @"\b(?:no|ni|sin)\s+(?:es\s+|sera\s+|se\s+trata\s+de\s+)?(?:un\s+puesto\s+|una\s+posicion\s+|una\s+oferta\s+|un\s+trabajo\s+|trabajo\s+)?(?:100\s*%\s*|totalmente\s+|completamente\s+)?(?:en\s+)?remot[oa]\b"
        + @"|\b(?:no|ni|sin)\s+(?:(?:hay|se\s+ofrece|ofrecemos|se\s+permite|se\s+admite|admite|permite|es\s+posible|incluye|existe|contempla|se\s+contempla)\s+)?(?:el\s+|la\s+)?(?:(?:posibilidad|opcion)\s+(?:de|a)\s+)?(?:teletrabaj\w*|trabaj\w*\s+(?:en\s+)?remoto|trabaj\w*\s+a\s+distancia)"
        + @"|\b(?:teletrabajo|teletrabajable|remot[oa]|remote(?:\s+work)?|trabajo\s+(?:en\s+)?remoto)\s*:\s*no\b"
        + @"|\b(?:teletrabajo|trabajo\s+(?:en\s+)?remoto)\s+no\s+(?:disponible|posible|permitido|contemplado)\b"
        + @"|\bnot\s+(?:a\s+|an\s+)?(?:fully\s+|100\s*%\s*|entirely\s+|completely\s+)?remote\b"
        + @"|\bno\s+(?:remote|wfh)\b"
        + @"|\bnon[\s-]?remote\b"
        + @"|\bremote\s+(?:work(?:ing)?\s+|option\s+)?(?:is\s+)?not\s+(?:possible|available|allowed|offered|supported|an\s+option)\b"
        + @"|\bnot\s+(?:open\s+to|eligible\s+for|available\s+for|offering|offered\s+as)\s+(?:a\s+)?remote\b"
        + @"|\b(?:cannot|can't|can\s+not)\s+be\s+(?:done\s+|performed\s+|worked\s+)?remote(?:ly)?\b"
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

    /// <summary>"Remoto o híbrido", "Híbrido / 100% remoto", "remote or hybrid".</summary>
    [GeneratedRegex(
        @"\b(?:100\s*%\s*)?(?:en\s+)?(?:remot[oa]|remote|teletrabajo)\s*(?:/|\||\bo\b|\bor\b)\s*" + HybridWord
        + @"|\b(?:hibrid[oa]|hybrid)\s*(?:/|\||\bo\b|\bor\b)\s*(?:100\s*%\s*|fully\s+|full[\s-]|totalmente\s+|completamente\s+)?(?:en\s+)?(?:remot[oa]|remote|teletrabajo)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteOrHybridRegex();

    /// <summary>
    /// Híbrido o remoto PARCIAL: "híbrido", "semipresencial", "2 días de
    /// teletrabajo", "días en remoto", "teletrabajo 2 días", "remote 3 days a
    /// week", "3 días en oficina", "teletrabajo parcial/ocasional",
    /// "teletrabajar algunos días", "teletrabajo los viernes", "presencial
    /// con posibilidad de teletrabajo"...
    /// </summary>
    [GeneratedRegex(
        HybridWord
        + @"|\bsemi[\s-]?(?:presencial|remot[oa]|remote)\b"
        // "2 días (a la semana) de teletrabajo / en la oficina", "2 days remote"
        + @"|\b" + DayCount + @"\s+" + Days + @"(?:" + Sep + Filler + @"){0,6}" + Sep + @"(?:" + RemoteTarget + @"|" + OfficeTarget + @")\b"
        // "teletrabajo 2 días", "remote work 3 days a week", "presencialidad: 3 días"
        + @"|\b(?:teletrabaj\w*|remot[oa]|remote(?:ly)?|from\s+home|desde\s+casa|wfh|presencial(?:idad)?|oficina|office|on[\s-]?site)(?:\s+(?:de|hasta|up\s+to|work|working|trabajo))*[\s:(\-]+" + DayCount + @"\s+" + Days + @"\b"
        // Sin cifra: "días de teletrabajo", "días en remoto", "remote days"
        + @"|\bdias?\s+(?:de\s+teletrabajo|de\s+trabajo\s+(?:en\s+)?remoto|de\s+remoto|en\s+remoto|remotos?)\b|\bremote\s+days\b"
        // "teletrabajar algunos días", "some days remote"
        + @"|\bteletrabaj\w*\s+(?:algunos|algun|ciertos|varios|determinados)\s+dias\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+(?:some|a\s+few|certain)\s+days\b|\b(?:some|a\s+few|certain)\s+days\s+(?:remote(?:ly)?|from\s+home)\b"
        // "teletrabajo los viernes", "viernes de teletrabajo", "remote on Fridays"
        + @"|\bteletrabaj\w*\s+(?:los\s+)?(?:lunes|martes|miercoles|jueves|viernes)\b"
        + @"|\b(?:lunes|martes|miercoles|jueves|viernes)\s+(?:de\s+teletrabajo|en\s+remoto)\b"
        + @"|\b(?:remote(?:ly)?|from\s+home)\s+on\s+(?:mondays?|tuesdays?|wednesdays?|thursdays?|fridays?)\b"
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

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
