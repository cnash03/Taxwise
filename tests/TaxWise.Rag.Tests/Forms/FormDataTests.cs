using TaxWise.Rag.Forms;
using TaxWise.Rag.Sources;

namespace TaxWise.Rag.Tests.Forms;

/// <summary>The committed sample forms in data/forms (BUILD_PLAN §6). The goesTo values themselves are human-checked.</summary>
public sealed class FormDataTests
{
    private static readonly FormLibrary Library = new(RepoPaths.Forms);

    [Fact]
    public void BothSamples_HaveTheBoxesThePlanLists()
    {
        Assert.Equal(["1098t", "w2"], Library.Forms.Select(f => f.FormId));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "12", "15", "16", "17"], Library.Find("w2")!.Boxes.Select(b => b.Id));
        Assert.Equal(["1", "4", "5", "6", "7", "8", "9"], Library.Find("1098T")!.Boxes.Select(b => b.Id));
    }

    [Fact]
    public void EveryBox_IsComplete_AndSearchesOnlyTaxYearDocsInTheManifest()
    {
        var manifest = Manifest.Load(RepoPaths.Manifest).Documents.ToDictionary(d => d.DocId);
        foreach (var form in Library.Forms)
        {
            // Explain my form stays on TY2025 until the 2026 form instructions are out (BUILD_PLAN §1a).
            Assert.Equal(2025, form.TaxYear);
            Assert.False(string.IsNullOrWhiteSpace(form.Form));
            Assert.False(string.IsNullOrWhiteSpace(form.Issuer));
            Assert.Equal(form.Boxes.Count, form.Boxes.Select(b => b.Id).Distinct().Count());

            foreach (var box in form.Boxes)
            {
                var where = $"{form.FormId} box {box.Id}";
                Assert.False(string.IsNullOrWhiteSpace(box.Label), where);
                Assert.False(string.IsNullOrWhiteSpace(box.Value), where);
                Assert.False(string.IsNullOrWhiteSpace(box.Summary), where);
                Assert.False(string.IsNullOrWhiteSpace(box.GoesTo), where);
                Assert.False(string.IsNullOrWhiteSpace(box.RetrievalQuery), where);
                Assert.NotEmpty(box.SourceDocs);
                Assert.All(box.SourceDocs, d => Assert.True(manifest.TryGetValue(d, out var doc) && doc.TaxYear == form.TaxYear,
                    $"{where}: source doc '{d}' isn't a TY{form.TaxYear} manifest document"));
            }
        }
    }

    [Fact]
    public void FindBox_IgnoresCase_AndReturnsNullForUnknownBoxes()
    {
        var w2 = Library.Find("W2")!;

        Assert.Equal("Form 1040, line 1a", FormLibrary.FindBox(w2, "1")!.GoesTo);
        Assert.Null(FormLibrary.FindBox(w2, "99"));
        Assert.Null(Library.Find("1099-nec"));
    }
}
