using TaxWise.Core.Chunking;
using static TaxWise.Core.Tests.Chunking.Blocks;

namespace TaxWise.Core.Tests.Chunking;

public sealed class HeuristicsTests
{
    [Fact]
    public void TableHeuristic_NeedsThreeNumericColumnsOnMostLines()
    {
        Assert.True(TableHeuristics.IsTable(["12,250 12,300 1,231 1,231", "12,300 12,350 1,237 1,237"]));
        Assert.False(TableHeuristics.IsTable(["For 2025, the standard deduction is $15,750 for single filers."]));
        Assert.False(TableHeuristics.IsTable(["Up to $2,500 credit", "40% of credit may be refundable"]));
    }

    [Fact]
    public void JoinLines_UndoesHyphenation()
    {
        Assert.Equal("aid in the pursuit of study or research.", TextUtil.JoinLines(["aid in the pursuit of study or re-", "search."]));
        Assert.Equal("Form W-2 Box 1", TextUtil.JoinLines(["Form W-2", "Box 1"]));
    }

    [Fact]
    public void TokenEstimate_IsWordsTimes133()
    {
        Assert.Equal(133, TextUtil.EstimateTokens(Words(100, "w")));
        Assert.Equal(0, TextUtil.EstimateTokens(""));
    }

    [Theory]
    [InlineData("For use in preparing", "2025 Returns", 2025)]
    [InlineData("Publication 501 (2025)", "Catalog Number 15000U", 2025)]
    [InlineData("Instructions for Form 1040 (2025) Catalog Number 24811V", "", 2025)]
    public void CoverYear_IsReadFromTheCover(string line1, string line2, int expected)
    {
        LayoutBlock[] cover = [Body(1, line1), Body(1, line2)];
        Assert.Equal(expected, CoverYearDetector.Detect(cover));
    }

    [Fact]
    public void CoverYear_FallsBackToLargestStandaloneYear()
    {
        LayoutBlock[] cover = [Body(1, "Section references are to the Internal Revenue Code. Changes for 2025."), Heading(1, "2026", 36)];
        Assert.Equal(2026, CoverYearDetector.Detect(cover));
    }

    [Fact]
    public void CoverYear_IsNullWhenNothingMatches() =>
        Assert.Null(CoverYearDetector.Detect([Body(1, "Contents")]));

    [Fact]
    public void PointIds_AreDeterministicGuids()
    {
        Assert.Equal(ChunkIds.ToPointId("p970-ty2025-p5-010"), ChunkIds.ToPointId("p970-ty2025-p5-010"));
        Assert.NotEqual(ChunkIds.ToPointId("p970-ty2025-p5-010"), ChunkIds.ToPointId("p970-ty2025-p5-011"));
    }
}
