using System.Text.Json;
using TaxWise.Rag.Sources;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Sources;

/// <summary>data/sources/manifest.json and the manifest fields added for Colorado (DECISIONS #23).</summary>
public sealed class ManifestTests
{
    private static readonly Manifest Real = Manifest.Load(RepoPaths.Manifest);

    private static ManifestEntry Parse(string json) =>
        JsonSerializer.Deserialize<Manifest>($$"""{ "documents": [ {{json}} ] }""", Manifest.JsonOptions)!.Documents.Single();

    [Fact]
    public void IrsEntries_AreFederal_AndColoradoEntriesAreDorDocuments()
    {
        Assert.All(Real.Documents.Where(d => !d.DocId.StartsWith("co-", StringComparison.Ordinal)),
            d => Assert.Equal(Jurisdictions.Federal, d.JurisdictionCode));

        var colorado = Real.Documents.Where(d => d.JurisdictionCode == Jurisdictions.Colorado).ToList();
        Assert.Equal(["co-dr0104", "co-guide"], colorado.Select(d => d.DocId));
        Assert.All(colorado, d =>
        {
            Assert.StartsWith("https://tax.colorado.gov/", d.Url);
            Assert.Equal(2025, d.TaxYear);
            Assert.Equal(64, d.Sha256.Length);
        });
    }

    [Fact]
    public void DrBook_IngestsOnlyItsInstructionPages()
    {
        var book = Real.Documents.Single(d => d.DocId == "co-dr0104");

        Assert.Equal((4, 33), (book.FirstPage, book.LastPage));
        Assert.False(book.IncludesPage(3));
        Assert.True(book.IncludesPage(4));
        Assert.True(book.IncludesPage(33));
        Assert.False(book.IncludesPage(34)); // the tax table
    }

    [Fact]
    public void Guide_IsCheckedByItsEditionText()
    {
        Assert.Equal("Revised January 2026", Real.Documents.Single(d => d.DocId == "co-guide").Edition);
    }

    [Fact]
    public void NewFields_ParseFromJson_AndAreOptional()
    {
        var co = Parse("""
            { "docId": "x", "title": "X", "url": "https://e.test/x.pdf", "taxYear": 2025, "kind": "publication",
              "jurisdiction": "co", "edition": "Revised January 2026", "firstPage": 2, "lastPage": 9 }
            """);
        Assert.Equal(Jurisdictions.Colorado, co.JurisdictionCode);
        Assert.Equal("Revised January 2026", co.Edition);
        Assert.True(co.IncludesPage(9));
        Assert.False(co.IncludesPage(10));

        var plain = Parse("""{ "docId": "p17", "title": "Pub 17", "url": "https://e.test/p17.pdf", "taxYear": 2025, "kind": "publication" }""");
        Assert.Equal(Jurisdictions.Federal, plain.JurisdictionCode);
        Assert.Null(plain.Edition);
        Assert.True(plain.IncludesPage(1));
        Assert.True(plain.IncludesPage(500));
    }

    [Fact]
    public void UnknownJurisdiction_Throws()
    {
        var entry = Parse("""{ "docId": "x", "title": "X", "url": "https://e.test/x.pdf", "taxYear": 2025, "kind": "publication", "jurisdiction": "TX" }""");

        Assert.Throws<InvalidOperationException>(() => entry.JurisdictionCode);
    }
}
