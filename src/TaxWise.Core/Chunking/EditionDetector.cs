namespace TaxWise.Core.Chunking;

/// <summary>
/// Edition check for guides that are revised in place rather than published per tax year, such as the Colorado
/// DOR's Individual Income Tax Guide ("Revised January 2026" in every page footer). The manifest names the expected
/// edition; ingestion stops when the downloaded PDF doesn't carry it (BUILD_PLAN §4.1, DECISIONS #23).
/// </summary>
public static class EditionDetector
{
    /// <summary>The cover can be a bare title page, so the first few pages are searched.</summary>
    public const int PagesSearched = 3;

    /// <returns>True when <paramref name="edition"/> appears in the blocks, ignoring case and runs of whitespace.</returns>
    public static bool Contains(IReadOnlyList<LayoutBlock> blocks, string edition)
    {
        var wanted = Normalize(edition);
        return wanted.Length > 0 && blocks.Any(b => Normalize(b.Text).Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
