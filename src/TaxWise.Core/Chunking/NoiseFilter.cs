using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

/// <summary>Removes text that would pollute chunks: repeated running headers/footers and table-of-contents lines.</summary>
public static partial class NoiseFilter
{
    /// <summary>
    /// Drops short blocks whose text (digits ignored) repeats on many pages — running heads such as
    /// "Page 7 Publication 970 (2025)" — and table-of-contents blocks (dot leaders).
    /// </summary>
    public static IReadOnlyList<LayoutBlock> Apply(IReadOnlyList<LayoutBlock> blocks)
    {
        var pageCount = blocks.Select(b => b.Page).Distinct().Count();
        var threshold = Math.Max(3, (int)Math.Ceiling(pageCount * 0.2));

        // Keyed by font size too, and only text with letters: a big "1." chapter number must not be
        // mistaken for the small "1." list markers that repeat on every page.
        var repeated = blocks
            .Where(IsRunningHeadCandidate)
            .GroupBy(Key)
            .Where(g => g.Select(b => b.Page).Distinct().Count() >= threshold)
            .Select(g => g.Key)
            .ToHashSet();

        return blocks
            .Where(b => !(IsRunningHeadCandidate(b) && repeated.Contains(Key(b))))
            .Where(b => !IsTableOfContents(b))
            .Where(b => !Icon().IsMatch(b.Text.Trim()))
            .ToList();
    }

    private static bool IsRunningHeadCandidate(LayoutBlock b) => b.WordCount < 15 && b.Text.Any(char.IsLetter);

    private static (string, double) Key(LayoutBlock b) => (TextUtil.Fingerprint(b.Text), Math.Round(b.FontSize));

    public static bool IsTableOfContents(LayoutBlock block) =>
        block.Lines.Count(l => DotLeader().IsMatch(l)) * 2 > block.Lines.Count;

    [GeneratedRegex(@"(\.\s?){5,}")] private static partial Regex DotLeader();

    /// <summary>Margin icons in IRS publications ("TIP", "! CAUTION", "RECORDS").</summary>
    [GeneratedRegex(@"^(?:(?:!|TIP|CAUTION|RECORDS|ENCLOSURE|SUGGESTION)\s*)+$")] private static partial Regex Icon();
}
