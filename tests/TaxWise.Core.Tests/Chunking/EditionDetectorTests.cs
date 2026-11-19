using TaxWise.Core.Chunking;

namespace TaxWise.Core.Tests.Chunking;

public sealed class EditionDetectorTests
{
    [Fact]
    public void FindsTheEditionInAFooter_IgnoringCaseAndLineBreaks()
    {
        var blocks = new[] { Blocks.Body(1, "Colorado Individual Income Tax Guide"), new LayoutBlock(2, ["1", "Revised  January", "2026"], 8, false) };

        Assert.True(EditionDetector.Contains(blocks, "revised january 2026"));
    }

    [Fact]
    public void ANewerRevision_DoesNotMatch()
    {
        var blocks = new[] { Blocks.Body(2, "Revised January 2027") };

        Assert.False(EditionDetector.Contains(blocks, "Revised January 2026"));
        Assert.False(EditionDetector.Contains(blocks, "   "));
    }
}
