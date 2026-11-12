using System.Globalization;
using System.Text.RegularExpressions;

namespace TaxWise.Core.Guardrails;

/// <summary><see cref="Text"/> is the amount as written ("$1,235.00"); <see cref="Value"/> is what it parses to.</summary>
public sealed record DollarAmount(string Text, decimal Value);

/// <summary><see cref="Unexpected"/> lists every amount (as written) that isn't one of the known numbers.</summary>
public sealed record DollarCheck(bool IsValid, IReadOnlyList<string> Unexpected);

/// <summary>
/// The calculator explanation's dollar guardrail (BUILD_PLAN §7.2): the model may only repeat numbers the code
/// computed. Every <c>$X</c> and every bare number with comma thousands must equal, to the cent, one of the known
/// amounts (step table, bracket breakdown, thresholds). Plain numbers without a <c>$</c> or a comma (years, line
/// numbers, percentages) are ignored.
/// </summary>
public static partial class DollarAmountValidator
{
    public static DollarCheck Validate(string text, IEnumerable<decimal> knownAmounts)
    {
        var known = knownAmounts.ToHashSet();
        var unexpected = Extract(text).Where(a => !known.Contains(a.Value)).Select(a => a.Text).ToList();
        return new DollarCheck(unexpected.Count == 0, unexpected);
    }

    public static IReadOnlyList<DollarAmount> Extract(string text) =>
        Amount().Matches(text)
            .Where(m => m.Groups["dollar"].Success || m.Groups["int"].Value.Contains(','))
            .Select(m => new DollarAmount(m.Value,
                decimal.Parse(m.Groups["int"].Value.Replace(",", "") + m.Groups["frac"].Value, CultureInfo.InvariantCulture)))
            .ToList();

    // "$1,235.00", "$ 900", "$12" (from "$12k"), "11,925". A trailing sentence period isn't taken as a decimal point.
    [GeneratedRegex(@"(?<![\w.,])(?<dollar>\$\s?)?(?<int>\d{1,3}(?:,\d{3})+|\d+)(?<frac>\.\d+)?")]
    private static partial Regex Amount();
}
