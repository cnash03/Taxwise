using TaxWise.Core.Chunking;

namespace TaxWise.Core.Tests.Chunking;

public sealed class RevProcChunkerTests
{
    private static readonly SourceDocument RevProc = new("rp-26-35", "Rev. Proc. 2026-35, TY2027 inflation adjustments", 2027);

    // Shaped like the real document: a table of contents that repeats the headings, then the body.
    private static readonly LayoutBlock[] Document =
    [
        new(1, ["SECTION 1. PURPOSE", "SECTION 3. 2027 ADJUSTED ITEMS", "SECTION 4. EFFECTIVE DATE"], 12, false),
        new(1, [".01 Tax Rate Tables . . . . . . . . . . . 1(j)(2)(A)-(D)", ".15 Standard Deduction . . . . . . . . . . 63"], 12, false),
        new(2, ["SECTION 1. PURPOSE", "This revenue procedure sets forth inflation-adjusted items for 2027."], 12, false),
        new(4, ["SECTION 3. 2027 ADJUSTED ITEMS",
                ".01 Tax Rate Tables. For taxable years beginning in 2027, the tax rate tables under",
                "§ 1 are as follows:",
                "TABLE 1 - Section 1(j)(2)(A) – Married Individuals Filing Joint Returns",
                "If Taxable Income Is: The Tax Is:",
                "Not over $25,600 10% of the taxable income",
                "TABLE 3 - Section 1(j)(2)(C) – Unmarried Individuals",
                "If Taxable Income Is: The Tax Is:",
                "Not over $12,800 10% of the taxable income"], 12, false),
        new(6, [".15 Standard Deduction.",
                "(1) In general. For taxable years beginning in 2027, the standard deduction amounts",
                "under § 63(c)(2) are as follows: Unmarried Individuals $16,600"], 12, false),
        new(30, ["SECTION 4. EFFECTIVE DATE", "This revenue procedure applies to taxable years beginning in 2027."], 12, false),
    ];

    private static IReadOnlyList<Chunk> Chunks() => RevProcChunker.Chunk(RevProc, Document, [new RevProcSection(3, 2027)]);

    [Fact]
    public void EachRateTable_IsItsOwnChunk()
    {
        var tables = Chunks().Where(c => c.Section.StartsWith(".01", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, tables.Count);
        Assert.Contains("Not over $25,600", tables[0].Text);
        Assert.Contains("are as follows:", tables[0].Text); // lead-in goes with the first table
        Assert.Contains("Not over $12,800", tables[1].Text);
        Assert.DoesNotContain("$25,600", tables[1].Text);
        Assert.Equal(".01 Tax Rate Tables > TABLE 3 - Section 1(j)(2)(C) – Unmarried Individuals", tables[1].Section);
    }

    [Fact]
    public void Subsection_IsOneChunk_WithItsNumberInTheSection()
    {
        var chunk = Assert.Single(Chunks(), c => c.Section == ".15 Standard Deduction");
        Assert.Contains("$16,600", chunk.Text);
        Assert.Equal(6, chunk.PageStart);
        Assert.Equal("Section 3. 2027 Adjusted Items", chunk.Chapter);
    }

    [Fact]
    public void OnlyTheConfiguredSection_IsIngested_FromTheBodyNotTheContents()
    {
        var chunks = Chunks();
        Assert.All(chunks, c => Assert.Equal(2027, c.TaxYear));
        Assert.DoesNotContain(chunks, c => c.Text.Contains("EFFECTIVE DATE") || c.Text.Contains("PURPOSE"));
        Assert.DoesNotContain(chunks, c => c.PageStart == 1);
    }

    [Fact]
    public void SectionsCanBeTaggedWithDifferentYears()
    {
        var chunks = RevProcChunker.Chunk(RevProc, Document, [new RevProcSection(3, 2027), new RevProcSection(1, 2026)]);
        Assert.Contains(chunks, c => c.TaxYear == 2026 && c.Text.Contains("sets forth"));
        Assert.Equal(chunks.Count, chunks.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void MissingSection_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RevProcChunker.Chunk(RevProc, Document, [new RevProcSection(9, 2027)]));
}
