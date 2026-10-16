using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Convierte el HTML de las descripciones en texto plano legible. Lo
/// necesitan la puntuación (la longitud de la descripción mide la calidad
/// de la información, y la maquetación la inflaba) y el prompt de la IA
/// (las etiquetas son tokens desperdiciados y empeoran el análisis).
/// </summary>
internal static partial class HtmlText
{
    /// <summary>
    /// Pasos, en este orden:
    ///  1. Solo si la entrada es HTML ESCAPADO entero, decodificar entidades:
    ///     Greenhouse lo entrega así ("&amp;lt;p&amp;gt;..."), y hasta decodificar no
    ///     hay etiquetas que quitar. En el HTML normal (Lever, Jooble, Adzuna)
    ///     no: ahí "&amp;lt;T&amp;gt;" o "&amp;lt;juan@acme.es&amp;gt;" es TEXTO escapado, y
    ///     decodificarlo antes de quitar etiquetas lo convertiría en una
    ///     "etiqueta" que el paso 4 borraría.
    ///  2. Quitar &lt;script&gt;/&lt;style&gt; con su contenido y comentarios.
    ///  3. Colapsar los saltos de línea del propio HTML (no significan nada)
    ///     y convertir los límites de bloque (br, p, li, div, títulos...) en
    ///     saltos; cada &lt;li&gt; empieza con "- " para conservar las listas.
    ///  4. Quitar el resto de etiquetas (las inline dejan un espacio para no
    ///     pegar palabras) y decodificar: en el HTML normal es la única
    ///     pasada; en el escapado, la segunda, porque lo que estaba doblemente
    ///     escapado ("&amp;amp;nbsp;") solo aparece tras la primera.
    ///  5. Colapsar espacios por línea y eliminar líneas vacías.
    /// Devuelve null si no queda texto.
    /// </summary>
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = DecodeIfEscapedHtml(html);
        text = ScriptOrStyleRegex().Replace(text, " ");
        text = CommentRegex().Replace(text, " ");
        text = WhitespaceRegex().Replace(text, " ");
        text = ListItemRegex().Replace(text, "\n- ");
        text = BlockTagRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);

        return JoinLines(text);
    }

    /// <summary>
    /// Decodifica la entrada solo cuando es HTML escapado entero: no trae
    /// ninguna etiqueta real y, al decodificarla, aparecen elementos HTML.
    /// Basta una etiqueta real para saber que el marcado está sin escapar y
    /// que cualquier "&amp;lt;" es texto. Sin etiquetas, se mira qué sale al
    /// decodificar: un texto plano con "&amp;lt;senior&amp;gt;" o "Task&amp;lt;T&amp;gt;" no
    /// esconde elementos HTML, así que se deja para la decodificación final.
    /// </summary>
    private static string DecodeIfEscapedHtml(string html)
    {
        if (TagRegex().IsMatch(html))
        {
            return html;
        }

        var decoded = WebUtility.HtmlDecode(html);
        return HtmlElementRegex().IsMatch(decoded) ? decoded : html;
    }

    /// <summary>
    /// Limpia cada línea y descarta las vacías. Una viñeta que se ha quedado
    /// sola ("&lt;li&gt;&lt;p&gt;texto&lt;/p&gt;&lt;/li&gt;" deja "-" y el texto en la línea
    /// siguiente) se une con su texto.
    /// </summary>
    private static string? JoinLines(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingBullet = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = HorizontalWhitespaceRegex().Replace(rawLine, " ").Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line == "-")
            {
                pendingBullet = true;
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(pendingBullet && !line.StartsWith("- ", StringComparison.Ordinal) ? $"- {line}" : line);
            pendingBullet = false;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<li\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"</?(br|p|div|li|ul|ol|h[1-6]|tr|table|section|article|header|footer|blockquote|hr)\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    // Exige una letra tras "<" (o "</"): así un "&lt; 40k" del texto, ya
    // decodificado, no se toma por etiqueta y no se come lo que sigue.
    [GeneratedRegex(@"</?[a-zA-Z][^<>]*>")]
    private static partial Regex TagRegex();

    // Elementos HTML que de verdad usan las fuentes, con el nombre seguido de
    // espacio, "/" o ">": así "<T>", "<IActionResult>" o "<juan@acme.es>"
    // (texto) no cuentan como marcado escapado. Solo decide si decodificar
    // primero; quitar etiquetas sigue siendo cosa de TagRegex.
    [GeneratedRegex(@"</?(a|b|i|u|em|strong|span|font|small|code|pre|img|br|p|div|li|ul|ol|h[1-6]|table|tr|td|th|section|article|header|footer|blockquote|hr)(?=[\s/>])", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlElementRegex();

    // \s menos el salto de línea: tabuladores, espacios y &nbsp; (U+00A0).
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespaceRegex();
}
