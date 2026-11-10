using TaxWise.Shared;
using TaxWise.Shared.Display;

namespace TaxWise.Core.Tests.Display;

public sealed class SourceLabelsTests
{
    private static SourcePassage Source(string title, string? chapter, int page = 6) =>
        new(1, "p970-ty2025-p6-012", "p970", title, chapter, "Scholarships", page, 2025, "text", "http://x/pdf");

    [Fact]
    public void Chip_MatchesMockupFormat() =>
        Assert.Equal("1 · Pub 970 · Ch. 1 · p. 6 · TY 2025",
            SourceLabels.Chip(Source("Pub 970, Tax Benefits for Education", "Chapter 1. Scholarships, Fellowship Grants, Grants, and Tuition Reductions")));

    [Fact]
    public void Chip_OmitsMissingChapter() =>
        Assert.Equal("1 · Form 1040 Instructions (incl. Tax Table) · p. 40 · TY 2025",
            SourceLabels.Chip(Source("Form 1040 Instructions (incl. Tax Table)", null, 40)));

    [Theory]
    [InlineData("Chapter 12. Other Itemized Deductions", "Ch. 12")]
    [InlineData("Filing Information", null)]
    [InlineData("", null)]
    public void ShortChapter(string chapter, string? expected) => Assert.Equal(expected, SourceLabels.ShortChapter(chapter));

    [Fact]
    public void SplitHighlight_FindsExactSentence()
    {
        var (before, mark, after) = SourceLabels.SplitHighlight("One. Two is here. Three.", "Two is here.");
        Assert.Equal(("One. ", "Two is here.", " Three."), (before, mark, after));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not in the passage.")]
    public void SplitHighlight_NoMatch_LeavesTextUnmarked(string? highlight) =>
        Assert.Equal(("One. Two.", (string?)null, ""), SourceLabels.SplitHighlight("One. Two.", highlight));

    [Fact]
    public void Truncate_CutsAtWordBoundary()
    {
        Assert.Equal("Is my scholarship taxable?", SourceLabels.Truncate("Is my   scholarship taxable?", 40));
        Assert.Equal("My scholarship paid my tuition and…",
            SourceLabels.Truncate("My scholarship paid my tuition and also gave me $1,200 for housing.", 36));
    }
}
