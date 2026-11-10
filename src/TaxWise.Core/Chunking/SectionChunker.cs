using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

/// <summary>
/// Section-aware chunker for IRS publications and instructions (BUILD_PLAN §4.2 steps 3–4).
/// <list type="bullet">
/// <item>Chapter heading: a large "1." block followed by its title, or a large block starting "Chapter N".</item>
/// <item>Major heading: font clearly larger than the body median, ≤ 20 words.</item>
/// <item>Minor heading: bold and short (&lt; 12 words) at body size, or a paragraph's bold run-in heading.
/// Minor headings are break points only; <see cref="Chunk.Section"/> names chapters' major headings.</item>
/// </list>
/// Chapters are hard boundaries. Headings are soft ones: a new chunk starts at a heading once the current
/// chunk has reached the minimum size; otherwise the short section is merged with the next one and the
/// chunk's <see cref="Chunk.Section"/> names both. Without this, a quarter of all chunks were under 100 tokens.
/// Everything after an "Index" heading is skipped, and so are the IRS lookup tables (Tax Table, EIC Table):
/// pages of numbers that embed poorly and would tempt the model to read tax amounts off a retrieved row,
/// which the calculator owns (BUILD_PLAN §10). Skipping lasts until the next major heading or chapter.
/// </summary>
public static partial class SectionChunker
{
    private const double MajorHeadingRatio = 1.15;
    private const double ChapterNumberRatio = 1.5;
    private const string FrontMatterSection = "Introduction";

    public static IReadOnlyList<Chunk> Chunk(SourceDocument doc, IReadOnlyList<LayoutBlock> rawBlocks, ChunkingOptions? options = null)
    {
        options ??= ChunkingOptions.Default;
        var blocks = NoiseFilter.Apply(rawBlocks);
        var bodySize = BodyFontSize(blocks);

        var packed = new List<(string? Chapter, PackedText Text)>();
        var segment = new List<PackUnit>();
        string? chapter = null;
        var section = FrontMatterSection;
        var inIndex = false;
        var inLookupTable = false;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var words = block.WordCount;
            if (words == 0) continue;

            // Chapter: "1." in a big font, then the title in the same font — in the same block or the next ones.
            var number = ChapterNumber().Match(block.Text);
            if (number.Success && words <= 20 && block.FontSize >= bodySize * ChapterNumberRatio)
            {
                var rest = TakeSameStyleRun(blocks, ref i, block.FontSize, skipFirst: true);
                var title = string.Join(' ', new[] { number.Groups[2].Value, rest }.Where(x => x.Length > 0)).Trim();
                FlushSegment();
                inIndex = inLookupTable = false;
                chapter = $"Chapter {number.Groups[1].Value}. {title}".TrimEnd(' ', '.');
                section = title.Length > 0 ? title : chapter;
                continue;
            }
            if (ChapterWord().IsMatch(block.Text) && words <= 20 && block.FontSize >= bodySize * ChapterNumberRatio)
            {
                var title = TakeSameStyleRun(blocks, ref i, block.FontSize, skipFirst: false);
                FlushSegment();
                inIndex = inLookupTable = false;
                chapter = title;
                section = title;
                continue;
            }

            var isTable = TableHeuristics.IsTable(block.Lines);
            if (!isTable && words <= 20 && block.FontSize >= bodySize * MajorHeadingRatio && block.Text.Any(char.IsLetter))
            {
                var heading = TakeSameStyleRun(blocks, ref i, block.FontSize, skipFirst: false);
                if (heading.Equals("Index", StringComparison.OrdinalIgnoreCase)) inIndex = true;
                inLookupTable = LookupTable().IsMatch(heading);
                section = heading;
                if (!inIndex && !inLookupTable) segment.Add(new PackUnit(heading, block.Page, false, section, SoftBreak: true));
                continue;
            }
            if (inIndex || inLookupTable) continue;

            // Minor and run-in headings are soft break points but don't rename the section: labels stay
            // at the level a reader would look for ("Tax-Free Scholarships and Fellowship Grants").
            if (!isTable && block.IsBold && words < 12 && block.FontSize >= bodySize * 0.95)
            {
                if (block.Text.Equals("Index", StringComparison.OrdinalIgnoreCase))
                {
                    inIndex = true;
                    continue;
                }
                segment.Add(new PackUnit(block.Text, block.Page, false, section, SoftBreak: true));
                continue;
            }

            if (isTable)
            {
                var rows = string.Join('\n', block.Lines.Select(l => l.Trim()));
                // Consecutive table blocks on the same page are one table: keep them together.
                if (segment.Count > 0 && segment[^1] is { IsTable: true } prev && prev.Page == block.Page)
                    segment[^1] = prev with { Text = prev.Text + "\n" + rows };
                else
                    segment.Add(new PackUnit(rows, block.Page, true, section));
                continue;
            }

            if (block.RunInHeading is not null && block.FontSize >= bodySize * 0.95)
            {
                segment.Add(new PackUnit(block.Text, block.Page, false, section, SoftBreak: true));
                continue;
            }

            segment.Add(new PackUnit(block.Text, block.Page, false, section));
        }
        FlushSegment();

        return ChunkFactory.Build(doc, doc.TaxYear, packed);

        void FlushSegment()
        {
            if (segment.Count == 0) return;
            foreach (var p in ChunkPacker.Pack(segment, options, texts => string.Join('\n', texts)))
                packed.Add((chapter, p));
            segment.Clear();
        }
    }

    /// <summary>Joins the heading at <paramref name="i"/> with the following blocks in the same font size (wrapped headings).</summary>
    private static string TakeSameStyleRun(IReadOnlyList<LayoutBlock> blocks, ref int i, double fontSize, bool skipFirst)
    {
        var parts = new List<string>();
        if (!skipFirst) parts.Add(blocks[i].Text);
        while (i + 1 < blocks.Count
               && Math.Abs(blocks[i + 1].FontSize - fontSize) < 0.6
               && blocks[i + 1].Page == blocks[i].Page
               && blocks[i + 1].WordCount <= 20)
        {
            i++;
            parts.Add(blocks[i].Text);
        }
        return string.Join(' ', parts).Trim();
    }

    /// <summary>Median font size weighted by word count, so body text dominates headings and footnotes.</summary>
    public static double BodyFontSize(IReadOnlyList<LayoutBlock> blocks)
    {
        var bySize = blocks.Where(b => b.WordCount > 0).OrderBy(b => b.FontSize).ToList();
        var total = bySize.Sum(b => b.WordCount);
        var seen = 0;
        foreach (var b in bySize)
        {
            seen += b.WordCount;
            if (seen * 2 >= total) return b.FontSize;
        }
        return 10;
    }

    [GeneratedRegex(@"^(\d{1,2})\.?(?:\s+(.+))?$")] private static partial Regex ChapterNumber();
    [GeneratedRegex(@"^Chapter\s+\d+\b")] private static partial Regex ChapterWord();

    /// <summary>"2025 Tax Table", "2025 Earned Income Credit (EIC) Table", and Pub 17's "Sample Table" inside its Tax Table.</summary>
    [GeneratedRegex(@"^(?:\d{4} (?:Tax Table|Earned Income Credit \(EIC\) Table)\b|Sample Table$)")]
    private static partial Regex LookupTable();
}

internal static class ChunkFactory
{
    /// <summary>Turns packed text into chunks with deterministic, per-(doc, year) ordinal ids.</summary>
    public static IReadOnlyList<Chunk> Build(SourceDocument doc, int taxYear, IEnumerable<(string? Chapter, PackedText Text)> packed, int firstOrdinal = 1)
    {
        var ordinal = firstOrdinal;
        return packed
            .Where(p => !string.IsNullOrWhiteSpace(p.Text.Text))
            .Select(p => new Chunk(
                Chunk.MakeId(doc.DocId, taxYear, p.Text.PageStart, ordinal++),
                doc.DocId, doc.DocTitle, taxYear, p.Chapter,
                string.IsNullOrWhiteSpace(p.Text.Section) ? "Introduction" : p.Text.Section,
                p.Text.PageStart, p.Text.PageEnd, p.Text.Text,
                p.Text.Sections.Count > 1 ? p.Text.Sections : null))
            .ToList();
    }
}
