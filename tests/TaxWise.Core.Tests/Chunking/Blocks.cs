using TaxWise.Core.Chunking;

namespace TaxWise.Core.Tests.Chunking;

/// <summary>Builders for a small synthetic page model.</summary>
internal static class Blocks
{
    public static readonly SourceDocument Pub970 = new("p970", "Pub 970, Tax Benefits for Education", 2025);

    public static LayoutBlock Body(int page, int words, string tag = "w") => new(page, [Words(words, tag)], 10, false);

    public static LayoutBlock Body(int page, string text) => new(page, [text], 10, false);

    public static LayoutBlock Heading(int page, string text, double size = 14) => new(page, [text], size, true);

    public static LayoutBlock Bold(int page, string text) => new(page, [text], 10, true);

    /// <summary>Distinct words ("a0 a1 a2 …") so overlap and ordering are checkable.</summary>
    public static string Words(int count, string tag) => string.Join(' ', Enumerable.Range(0, count).Select(i => $"{tag}{i}"));

    public static LayoutBlock Table(int page, int rows) =>
        new(page, Enumerable.Range(0, rows).Select(r => $"{r * 50:N0} {r * 50 + 50:N0} {r * 5:N0} {r * 6:N0}").ToList(), 8, false);
}
