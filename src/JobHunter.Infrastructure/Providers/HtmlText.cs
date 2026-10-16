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
    ///  1. Decodificar entidades: Greenhouse entrega el HTML ESCAPADO
    ///     ("&amp;lt;p&amp;gt;..."), así que hasta decodificar no hay etiquetas que quitar.
    ///  2. Quitar &lt;script&gt;/&lt;style&gt; con su contenido y comentarios.
    ///  3. Colapsar los saltos de línea del propio HTML (no significan nada)
    ///     y convertir los límites de bloque (br, p, li, div, títulos...) en
    ///     saltos; cada &lt;li&gt; empieza con "- " para conservar las listas.
    ///  4. Quitar el resto de etiquetas (las inline dejan un espacio para no
    ///     pegar palabras) y decodificar otra vez: lo que estaba doblemente
    ///     escapado ("&amp;amp;nbsp;") solo aparece tras la primera pasada.
    ///  5. Colapsar espacios por línea y eliminar líneas vacías.
    /// Devuelve null si no queda texto.
    /// </summary>
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(html);
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

    // \s menos el salto de línea: tabuladores, espacios y &nbsp; (U+00A0).
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespaceRegex();
}
