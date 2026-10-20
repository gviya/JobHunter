using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Infrastructure.Providers;

/// <summary>
/// Horquilla salarial anual en EUR. Cualquiera de los extremos puede faltar:
/// "Hasta 45.000 €" solo da el máximo y "Desde 35.000 €", el mínimo.
/// </summary>
internal readonly record struct SalaryRange(decimal? Min, decimal? Max);

/// <summary>
/// Convierte el salario en texto libre de los agregadores (el "salary" de
/// Jooble) en una horquilla anual en EUR. Admite los formatos habituales en
/// español e inglés: "30.000 € - 40.000 €", "30000-40000 EUR",
/// "30k - 40k €", "Hasta 45.000 €", "Desde 35.000€", "35.000 €/año",
/// "40.000 € brutos/año"...
///
/// Ante la duda devuelve null en vez de adivinar: un salario mal leído
/// distorsiona la puntuación (que compara el techo de la horquilla con el
/// mínimo/objetivo del perfil), mientras que "sin salario" solo resta un
/// poco. Por eso se descartan:
///  - Importes por mes/día/hora/semana ("2.500 €/mes", "15 €/hora"):
///    pasarlos a anual exigiría suponer pagas y jornada.
///  - Otras monedas ($, £, USD...): el perfil está en EUR. Sin moneda se
///    asume EUR, porque Jooble se consulta en su portal español.
///  - Importes fuera de un rango anual plausible: un "2.500 €" sin periodo es
///    casi seguro mensual.
///  - Más de dos importes o dos importes sin un conector de rango entre
///    ellos ("35.000 € + 5.000 € de bonus"): no se sabe cuál es el salario.
/// </summary>
internal static partial class SalaryText
{
    // Rango anual plausible: por debajo, el importe es casi seguro mensual
    // (o una beca/media jornada que no compite con el resto); por encima,
    // una errata o un número que no es el salario.
    private const decimal MinPlausibleAnnual = 10_000m;
    private const decimal MaxPlausibleAnnual = 500_000m;

    // "30-40k €": el sufijo del segundo importe se aplica al primero solo si
    // el primero, por sí solo, es demasiado pequeño para ser un salario.
    private const decimal SharedMultiplierThreshold = 1_000m;

    public static SalaryRange? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = Normalize(text);

        if (NonAnnualPeriodRegex().IsMatch(normalized) || ForeignCurrencyRegex().IsMatch(normalized))
        {
            return null;
        }

        // "en 14 pagas" confirma que es anual, pero su número no es un importe.
        normalized = ExtraPaymentsRegex().Replace(normalized, " ");

        var amounts = new List<Amount>();
        foreach (Match match in AmountRegex().Matches(normalized))
        {
            if (ReadAmount(match) is not { } amount)
            {
                return null;
            }

            amounts.Add(amount);
        }

        var range = amounts.Count switch
        {
            1 => SingleAmount(normalized, amounts[0]),
            2 => TwoAmounts(normalized, amounts[0], amounts[1]),
            _ => null,
        };

        return range is { } r && IsPlausible(r.Min) && IsPlausible(r.Max) ? r : null;
    }

    /// <summary>
    /// Unifica los espacios Unicode (el &amp;nbsp; antes de "€", el espacio fino
    /// como separador de miles...) en espacios normales y los guiones
    /// tipográficos en "-", para que las expresiones regulares no tengan que
    /// contemplar cada variante.
    /// </summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            sb.Append(c switch
            {
                _ when char.IsWhiteSpace(c) => ' ',
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
                _ => c,
            });
        }

        return sb.ToString().Trim();
    }

    private static Amount? ReadAmount(Match match)
    {
        var groupSeparator = match.Groups["sep"];
        var decimalSeparator = match.Groups["dec"];

        // "30.000.50": el mismo carácter no puede separar miles y decimales.
        if (groupSeparator.Success && decimalSeparator.Success && groupSeparator.Value == decimalSeparator.Value)
        {
            return null;
        }

        var integerDigits = groupSeparator.Success
            ? match.Groups["int"].Value.Replace(groupSeparator.Value, string.Empty, StringComparison.Ordinal)
            : match.Groups["int"].Value;
        var number = decimalSeparator.Success
            ? $"{integerDigits}.{match.Groups["frac"].Value}"
            : integerDigits;

        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var hasMultiplier = match.Groups["mult"].Success;

        return new Amount(hasMultiplier ? value * 1000 : value, hasMultiplier, match.Index, match.Index + match.Length);
    }

    private static SalaryRange SingleAmount(string text, Amount amount)
    {
        if (UpperBoundPrefixRegex().IsMatch(text[..amount.Start]))
        {
            return new SalaryRange(null, amount.Value);
        }

        if (LowerBoundPrefixRegex().IsMatch(text[..amount.Start]) || OpenEndedSuffixRegex().IsMatch(text[amount.End..]))
        {
            return new SalaryRange(amount.Value, null);
        }

        // Un importe suelto ("35.000 € al año") es a la vez suelo y techo.
        return new SalaryRange(amount.Value, amount.Value);
    }

    private static SalaryRange? TwoAmounts(string text, Amount first, Amount second)
    {
        if (!RangeConnectorRegex().IsMatch(text[first.End..second.Start]))
        {
            return null;
        }

        var low = !first.HasMultiplier && second.HasMultiplier && first.Value < SharedMultiplierThreshold
            ? first.Value * 1000
            : first.Value;

        return low <= second.Value ? new SalaryRange(low, second.Value) : null;
    }

    private static bool IsPlausible(decimal? value)
        => value is not { } v || v is >= MinPlausibleAnnual and <= MaxPlausibleAnnual;

    /// <summary>Importe leído del texto, ya multiplicado por su sufijo ("k", "mil").</summary>
    private readonly record struct Amount(decimal Value, bool HasMultiplier, int Start, int End);

    // Un importe: miles agrupados con ".", "," o espacio (siempre el mismo
    // separador, por eso la referencia a "sep") o dígitos seguidos; luego
    // decimales opcionales (1-2 dígitos) y el sufijo "k"/"mil". Los
    // lookaround impiden empezar o acabar a mitad de un número, así que
    // "30.000,500" no se lee como 30.000 (no casa ningún importe). Se usa
    // [0-9] y no \d, que en .NET admite dígitos de cualquier alfabeto que
    // decimal.Parse no entiende.
    [GeneratedRegex(
        @"(?<![0-9.,])(?<int>[0-9]{1,3}(?<sep>[., ])[0-9]{3}(?:\k<sep>[0-9]{3})*|[0-9]+)(?:(?<dec>[.,])(?<frac>[0-9]{1,2}))?(?![.,]?[0-9])(?:\s*(?<mult>k|mil)\b)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    // Periodos que no son anuales. "h"/"hr"/"mo" son las abreviaturas de
    // "15 €/h" o "$4k/mo".
    [GeneratedRegex(
        @"\b(?:mes|meses|mensual|mensuales|month|months|monthly|mo|hora|horas|hour|hours|hourly|hr|h|d[ií]a|d[ií]as|diario|diarios|day|days|daily|semana|semanas|semanal|semanales|week|weeks|weekly)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonAnnualPeriodRegex();

    [GeneratedRegex(
        @"[$£¥₹]|\b(?:usd|gbp|chf|pln|mxn|ars|cop|clp|brl|inr|cad|aud|d[oó]lar|d[oó]lares|dollars?|libras?|pounds?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForeignCurrencyRegex();

    [GeneratedRegex(@"\b[0-9]{1,2}\s*pagas\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExtraPaymentsRegex();

    // Lo único que puede haber entre los dos importes de una horquilla: un
    // conector ("-", "/", "a", "y", "to", "hasta"...) rodeado, como mucho, de
    // la moneda o "brutos" ("30.000 € brutos - 40.000 €", "30/35k").
    [GeneratedRegex(
        @"^\s*(?:(?:€|eur|euros|brutos?|gross)\s*)*(?:-|/|a|y|to|and|hasta)\s*(?:(?:€|eur|euros)\s*)*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangeConnectorRegex();

    [GeneratedRegex(
        @"\b(?:hasta|up\s+to|m[aá]x|m[aá]ximo|maximum|menos\s+de|less\s+than|under)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpperBoundPrefixRegex();

    [GeneratedRegex(
        @"\b(?:desde|from|a\s+partir\s+de|m[ií]n|m[ií]nimo|minimum|starting\s+(?:at|from)|m[aá]s\s+de|more\s+than|over)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LowerBoundPrefixRegex();

    // "35k+ €" o "35.000 €+": "de 35.000 en adelante".
    [GeneratedRegex(@"^\s*(?:(?:€|eur|euros)\s*)?\+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenEndedSuffixRegex();
}
