using TaxWise.Core.Chunking;
using static TaxWise.Core.Tests.Chunking.Blocks;

namespace TaxWise.Core.Tests.Chunking;

public sealed class SectionChunkerTests
{
    [Fact]
    public void ChapterNumberAndTitle_SetChapterAndSection()
    {
        var chunks = SectionChunker.Chunk(Pub970,
        [
            Heading(4, "1.", 18), Heading(4, "Scholarships, Fellowship", 18), Heading(4, "Grants", 18),
            Body(4, 200),
        ]);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Chapter 1. Scholarships, Fellowship Grants", chunk.Chapter);
        Assert.Equal("Scholarships, Fellowship Grants", chunk.Section);
    }

    [Fact]
    public void ChapterNumberAndTitleInOneBlock_IsDetected()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970, [Heading(4, "2. American Opportunity Credit", 18), Body(4, 200)]));
        Assert.Equal("Chapter 2. American Opportunity Credit", chunk.Chapter);
    }

    [Fact]
    public void ChapterWordHeading_IsDetected()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970, [Heading(1, "Chapter 3 Dependents", 18), Body(1, 200)]));
        Assert.Equal("Chapter 3 Dependents", chunk.Chapter);
    }

    [Fact]
    public void LargerFontHeading_BecomesSection()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970,
            [Heading(5, "Tax-Free Scholarships and Fellowship Grants"), Body(5, 300)]));
        Assert.Equal("Tax-Free Scholarships and Fellowship Grants", chunk.Section);
        Assert.StartsWith("Tax-Free Scholarships", chunk.Text);
    }

    [Fact]
    public void BoldBodySizeHeading_IsNotAMajorHeading()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970,
            [Heading(5, "Qualified Expenses"), Body(5, 50), Bold(5, "Room and board"), Body(5, 50)]));
        Assert.Equal("Qualified Expenses", chunk.Section);
    }

    [Fact]
    public void Chunks_NeverCrossAChapterBoundary()
    {
        var chunks = SectionChunker.Chunk(Pub970,
        [
            Heading(4, "1.", 18), Heading(4, "Scholarships", 18), Body(4, 60, "a"),
            Heading(9, "2.", 18), Heading(9, "American Opportunity Credit", 18), Body(9, 60, "b"),
        ]);

        Assert.Equal(2, chunks.Count);
        Assert.DoesNotContain("b0", chunks[0].Text);
        Assert.DoesNotContain("a0", chunks[1].Text);
        Assert.Equal(["Chapter 1. Scholarships", "Chapter 2. American Opportunity Credit"], chunks.Select(c => c.Chapter));
    }

    [Fact]
    public void LongSection_SplitsWithinMaxTokens_WithOverlap()
    {
        var blocks = new List<LayoutBlock> { Heading(5, "Scholarships") };
        for (var i = 0; i < 10; i++) blocks.Add(Body(5 + i / 3, 100, $"p{i}x"));

        var chunks = SectionChunker.Chunk(Pub970, blocks);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.InRange(TextUtil.EstimateTokens(c.Text), 1, ChunkingOptions.Default.MaxTokens));
        // The second chunk opens with the last 60 tokens (45 words) of the first.
        var tail = TextUtil.TailTokens(chunks[0].Text, 60);
        Assert.Equal(45, TextUtil.CountWords(tail));
        Assert.StartsWith(tail, chunks[1].Text);
        Assert.All(chunks, c => Assert.Equal("Scholarships", c.Section));
    }

    [Fact]
    public void Overlap_CarriesThePageItCameFrom()
    {
        var chunks = SectionChunker.Chunk(Pub970,
            [Heading(5, "Scholarships"), Body(5, 300, "a"), Body(5, 200, "b"), Body(6, 300, "c")]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(5, chunks[1].PageStart);
        Assert.Equal(6, chunks[1].PageEnd);
    }

    [Fact]
    public void Table_IsNeverSplit_EvenWhenLargerThanMax()
    {
        var table = Table(80, rows: 150); // 600 numbers ≈ 800 tokens
        var chunks = SectionChunker.Chunk(Pub970, [Heading(80, "Tax Table"), Body(80, 300), table, Body(81, 100)]);

        var rows = table.Lines.Select(l => l.Trim()).ToList();
        var holder = Assert.Single(chunks, c => c.Text.Contains(rows[0]));
        Assert.Contains(string.Join('\n', rows), holder.Text);
    }

    [Fact]
    public void ShortSections_MergeIntoOneChunk_AndListBothSections()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970,
            [Heading(4, "Reminders", 17), Body(4, 40), Heading(4, "Introduction", 17), Body(4, 40)]));
        Assert.Equal(["Reminders", "Introduction"], chunk.AllSections);
        Assert.Contains("> Reminders · Introduction\n\n", chunk.EmbeddingText);
    }

    [Fact]
    public void MergedChunk_IsNamedAfterTheSectionWithTheMostText()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970,
            [Heading(5, "Scholarships", 17), Body(5, 20), Heading(5, "Tax-Free Scholarships", 17), Body(5, 200)]));
        Assert.Equal("Tax-Free Scholarships", chunk.Section);
        Assert.Equal("Pub 970 > Tax-Free Scholarships", chunk.Breadcrumb);
    }

    [Fact]
    public void SingleSectionChunk_HasNoSectionsList()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970, [Heading(5, "Scholarships", 17), Body(5, 200)]));
        Assert.Null(chunk.Sections);
        Assert.Equal(["Scholarships"], chunk.AllSections);
    }

    [Theory]
    [InlineData("2025 Tax Table CAUTION !")]
    [InlineData("2025 Earned Income Credit (EIC) Table Caution. This is not a tax table.")]
    [InlineData("Sample Table")]
    public void LookupTables_AreSkippedUntilTheNextMajorHeading(string tableHeading)
    {
        var chunks = SectionChunker.Chunk(Pub970,
        [
            Heading(60, "Line 27", 17), Body(60, 400, "a"),
            Heading(68, tableHeading, 17), Body(68, "See the instructions for line 16."), Table(68, 40), Table(69, 40),
            Heading(80, "2025 Tax Computation Worksheet", 17), Body(80, 400, "b"),
        ]);

        Assert.DoesNotContain(chunks, c => c.AllSections.Contains(tableHeading) || c.Text.Contains("line 16") || c.Text.Contains("1,000 1,050"));
        Assert.Contains(chunks, c => c.Section == "Line 27");
        Assert.Contains(chunks, c => c.Section == "2025 Tax Computation Worksheet" && c.Text.Contains("b399"));
    }

    [Fact]
    public void LookupTableSkip_EndsAtTheNextChapter()
    {
        var chunks = SectionChunker.Chunk(Pub970,
            [Heading(68, "2025 Tax Table", 17), Table(68, 40), Heading(80, "2.", 18), Heading(80, "Credits", 18), Body(80, 100, "c")]);
        var chunk = Assert.Single(chunks);
        Assert.Equal("Chapter 2. Credits", chunk.Chapter);
    }

    [Fact]
    public void HeadingAfterMinimumSize_StartsANewChunkWithoutOverlap()
    {
        var chunks = SectionChunker.Chunk(Pub970,
            [Heading(4, "Reminders", 17), Body(4, 300, "a"), Heading(5, "Introduction", 17), Body(5, 100, "b")]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Introduction", chunks[1].Section);
        Assert.StartsWith("Introduction", chunks[1].Text);
        Assert.DoesNotContain("a299", chunks[1].Text);
    }

    [Fact]
    public void Ids_AreDeterministic_AndFollowTheFormat()
    {
        LayoutBlock[] blocks = [Heading(5, "Scholarships"), Body(5, 600, "a"), Body(7, 600, "b")];

        var first = SectionChunker.Chunk(Pub970, blocks).Select(c => c.Id).ToList();
        var second = SectionChunker.Chunk(Pub970, blocks).Select(c => c.Id).ToList();

        Assert.Equal(first, second);
        Assert.Equal("p970-ty2025-p5-001", first[0]);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public void IndexAtTheEnd_IsSkipped()
    {
        var chunks = SectionChunker.Chunk(Pub970,
            [Heading(1, "Scholarships"), Body(1, 100), Heading(80, "Index", 17), Body(80, "Room and board 5, 7")]);
        Assert.DoesNotContain(chunks, c => c.Text.Contains("Room and board 5"));
    }

    [Fact]
    public void EveryChunk_HasSectionPagesAndTaxYear()
    {
        var chunks = SectionChunker.Chunk(Pub970, [Body(1, 100), Heading(2, "Scholarships"), Body(2, 900)]);

        Assert.All(chunks, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Section));
            Assert.True(c.PageStart >= 1 && c.PageEnd >= c.PageStart);
            Assert.Equal(2025, c.TaxYear);
        });
        Assert.StartsWith("Introduction", chunks[0].Section); // front matter before the first heading
    }

    [Fact]
    public void EmbeddingText_IsPrefixedWithBreadcrumb()
    {
        var chunk = Assert.Single(SectionChunker.Chunk(Pub970,
            [Heading(4, "1.", 18), Heading(4, "Scholarships", 18), Heading(5, "Tax-Free Grants"), Body(5, 100)]));
        Assert.StartsWith("Pub 970 > Chapter 1. Scholarships > Tax-Free Grants\n\n", chunk.EmbeddingText);
        Assert.DoesNotContain("Pub 970 >", chunk.Text);
    }
}
