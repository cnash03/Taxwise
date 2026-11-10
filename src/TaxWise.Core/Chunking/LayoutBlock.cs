namespace TaxWise.Core.Chunking;

/// <summary>
/// A block of text from a PDF page, in reading order. Produced by the PDF parser
/// (TaxWise.Ingestion) and consumed by the chunkers, which stay free of any PDF library.
/// </summary>
/// <param name="Page">1-based physical page index (what <c>#page=N</c> opens).</param>
/// <param name="Lines">The block's text lines, top to bottom.</param>
/// <param name="FontSize">Median font size of the block's letters.</param>
/// <param name="IsBold">True when every line is set in a bold/black font.</param>
/// <param name="RunInHeading">Leading bold words of a regular paragraph, e.g. "Candidate for a degree." —
/// the IRS's run-in subheading style. Null when the paragraph doesn't start with one.</param>
public sealed record LayoutBlock(int Page, IReadOnlyList<string> Lines, double FontSize, bool IsBold, string? RunInHeading = null)
{
    public int WordCount => Lines.Sum(TextUtil.CountWords);

    /// <summary>Lines joined into running text, undoing end-of-line hyphenation.</summary>
    public string Text => TextUtil.JoinLines(Lines);
}

/// <summary>The document a chunker is working on.</summary>
public sealed record SourceDocument(string DocId, string DocTitle, int TaxYear);
