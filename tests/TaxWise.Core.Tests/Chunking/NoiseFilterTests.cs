using TaxWise.Core.Chunking;
using static TaxWise.Core.Tests.Chunking.Blocks;

namespace TaxWise.Core.Tests.Chunking;

public sealed class NoiseFilterTests
{
    [Fact]
    public void RepeatedRunningHead_IsRemoved_ButBodyIsKept()
    {
        var blocks = Enumerable.Range(1, 10)
            .SelectMany(p => new[] { Body(p, $"Page {p} Publication 970 (2025)"), Body(p, 50, $"p{p}x") })
            .ToList();

        var kept = NoiseFilter.Apply(blocks);

        Assert.Equal(10, kept.Count);
        Assert.DoesNotContain(kept, b => b.Text.StartsWith("Page"));
    }

    [Fact]
    public void LargeChapterNumber_IsKept_EvenThoughSmallListNumbersRepeat()
    {
        var blocks = Enumerable.Range(1, 10).Select(p => Body(p, "1.")).Append(Heading(4, "1.", 18)).ToList();
        Assert.Contains(NoiseFilter.Apply(blocks), b => b.FontSize == 18);
    }

    [Theory]
    [InlineData("TIP")]
    [InlineData("! CAUTION")]
    [InlineData("RECORDS")]
    public void MarginIcons_AreRemoved(string icon) =>
        Assert.Empty(NoiseFilter.Apply([Body(1, icon)]));

    [Fact]
    public void TableOfContents_IsRemoved()
    {
        var toc = new LayoutBlock(1, ["What’s New . . . . . . . . . 1", "Reminders . . . . . . . . . 2"], 9, false);
        Assert.Empty(NoiseFilter.Apply([toc]));
    }
}
