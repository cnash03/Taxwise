using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

/// <summary>
/// Reads the tax year printed on a publication's first page so ingestion can stop when the
/// downloaded edition doesn't match the manifest (BUILD_PLAN §4.1).
/// </summary>
public static partial class CoverYearDetector
{
    /// <returns>The detected year, or null when no rule matches.</returns>
    public static int? Detect(IReadOnlyList<LayoutBlock> firstPageBlocks)
    {
        var text = string.Join(' ', firstPageBlocks.Select(b => b.Text));

        // "For use in preparing 2025 Returns" (Pubs 17, 970, 519)
        var m = ForUseIn().Match(text);
        if (m.Success) return int.Parse(m.Groups[1].Value);

        // "Publication 501 (2025)", "Instructions for Form 1040 (2025)" (cover footers)
        m = TitleYear().Match(text);
        if (m.Success) return int.Parse(m.Groups[1].Value);

        // A large standalone year on the cover: "2025" (instructions), "TAX GUIDE 2025" (Pub 17)
        var big = firstPageBlocks
            .OrderByDescending(b => b.FontSize)
            .Select(b => StandaloneYear().Match(b.Text))
            .FirstOrDefault(x => x.Success);
        if (big is not null) return int.Parse(big.Groups[1].Value);

        return null;
    }

    [GeneratedRegex(@"for use in preparing\s+(20\d\d)\s+returns", RegexOptions.IgnoreCase)]
    private static partial Regex ForUseIn();

    [GeneratedRegex(@"(?:Publication\s+\d+[A-Z-]*|Instructions for Forms?\s+[\w-]+(?:\s+and\s+[\w-]+)?)\s+\((20\d\d)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleYear();

    [GeneratedRegex(@"(?:^|GUIDE\s+)(20\d\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneYear();
}
