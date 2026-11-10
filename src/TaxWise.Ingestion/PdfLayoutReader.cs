using System.Text.RegularExpressions;
using TaxWise.Core.Chunking;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace TaxWise.Ingestion;

/// <summary>
/// Turns a PDF into <see cref="LayoutBlock"/>s with PdfPig (BUILD_PLAN §4.2 step 2): nearest-neighbour
/// words, Docstrum blocks and reading-order detection so two-column pages read left column first.
/// </summary>
public static partial class PdfLayoutReader
{
    /// <summary>Footer band (page number, running chapter title, catalog number) as a share of page height.</summary>
    private const double FooterBand = 0.075;

    public sealed record Result(IReadOnlyList<LayoutBlock> Blocks, IReadOnlyList<LayoutBlock> FirstPage, int PageCount);

    public static Result Read(string path)
    {
        using var document = PdfDocument.Open(path);
        var blocks = new List<LayoutBlock>();
        var firstPage = new List<LayoutBlock>();

        foreach (var page in document.GetPages())
        {
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
            var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
            foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(textBlocks))
            {
                // Printer's proof banner ("Userid: CPM … MUST be removed before printing") sits above the page box.
                if (block.BoundingBox.Bottom > page.Height) continue;

                var split = SplitByStyle(page.Number, block);
                if (page.Number == 1) firstPage.AddRange(split);
                if (block.BoundingBox.Top < page.Height * FooterBand) continue;
                blocks.AddRange(split);
            }
        }
        return new Result(blocks, firstPage, document.NumberOfPages);
    }

    /// <summary>
    /// Docstrum can glue a heading to the paragraph under it. Splitting a block wherever the line style
    /// (size, all-bold) changes gives the heading detector clean blocks. A line that only starts bold
    /// (a run-in heading) counts as regular, so its paragraph stays in one piece.
    /// </summary>
    private static IEnumerable<LayoutBlock> SplitByStyle(int pageNumber, TextBlock block)
    {
        var run = new List<(TextLine Line, double Size, bool Bold)>();
        foreach (var line in block.TextLines)
        {
            var letters = line.Words.SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
            if (letters.Count == 0) continue;
            var style = (Size: Math.Round(Median(letters.Select(l => l.PointSize)) * 2) / 2, Bold: letters.All(IsBold));
            if (run.Count > 0 && (run[^1].Size != style.Size || run[^1].Bold != style.Bold))
            {
                yield return ToBlock(pageNumber, run);
                run.Clear();
            }
            run.Add((line, style.Size, style.Bold));
        }
        if (run.Count > 0) yield return ToBlock(pageNumber, run);
    }

    private static LayoutBlock ToBlock(int pageNumber, List<(TextLine Line, double Size, bool Bold)> run) =>
        new(pageNumber, run.Select(r => r.Line.Text).ToList(), run[0].Size, run[0].Bold,
            run[0].Bold ? null : RunInHeading(run.SelectMany(r => r.Line.Words)));

    /// <summary>"Candidate for a degree. You are a candidate…" → "Candidate for a degree." (bold words, ending in a period or colon).</summary>
    private static string? RunInHeading(IEnumerable<Word> words)
    {
        var lead = words.TakeWhile(w => w.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).All(IsBold)).Take(13).ToList();
        if (lead.Count is 0 or > 12) return null;
        var text = string.Join(' ', lead.Select(w => w.Text)).Trim();
        return text.EndsWith('.') || text.EndsWith(':') ? text : null;
    }

    private static bool IsBold(Letter letter) => BoldFont().IsMatch(letter.FontName ?? "");

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    // IRS fonts: HelveticaWorld-Bold, HelveticaLTStd-Blk, HelveticaNeueLTStd-Bd/BdCn/BdOu, Helvetica-Black.
    [GeneratedRegex(@"Bold|Black|Blk|Heavy|Semibold|Demi|-Bd", RegexOptions.IgnoreCase)]
    private static partial Regex BoldFont();
}
