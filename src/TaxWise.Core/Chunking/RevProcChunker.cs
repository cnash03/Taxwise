using System.Globalization;
using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

/// <summary>Which numbered section of a Rev. Proc. to ingest, and the tax year its amounts apply to.</summary>
public sealed record RevProcSection(int Number, int TaxYear);

/// <summary>
/// Chunker for inflation-adjustment Rev. Procs (BUILD_PLAN §4.1): ingest only the configured sections,
/// one chunk per subsection (".15 Standard Deduction"), and each "TABLE n" rate table as its own chunk.
/// </summary>
public static partial class RevProcChunker
{
    public static IReadOnlyList<Chunk> Chunk(SourceDocument doc, IReadOnlyList<LayoutBlock> rawBlocks,
        IReadOnlyList<RevProcSection> sections, ChunkingOptions? options = null)
    {
        options ??= ChunkingOptions.Default;
        var lines = NoiseFilter.Apply(rawBlocks)
            .SelectMany(b => b.Lines.Select(l => (b.Page, Text: l.Trim())))
            .Where(l => l.Text.Length > 0)
            .ToList();

        var sectionStarts = lines
            .Select((l, idx) => (Match: SectionHeading().Match(l.Text), idx))
            .Where(x => x.Match.Success)
            .Select(x => (Number: int.Parse(x.Match.Groups[1].Value), Title: x.Match.Groups[2].Value.Trim(), Index: x.idx))
            .ToList();

        var chunks = new List<Chunk>();
        foreach (var wanted in sections)
        {
            // The table of contents repeats every heading; the body copy is the last occurrence.
            var start = sectionStarts.LastOrDefault(s => s.Number == wanted.Number);
            if (start == default)
                throw new InvalidOperationException($"{doc.DocId}: SECTION {wanted.Number} not found.");
            var end = sectionStarts.Where(s => s.Index > start.Index).Select(s => s.Index).DefaultIfEmpty(lines.Count).Min();

            var bodyStart = start.Index + 1;
            var title = start.Title;
            // Wrapped all-caps heading: "SECTION 3. 2025 ADJUSTED ITEMS AS MODIFIED, SUPERSEDED OR" / "SUPPLEMENTED BY THE OBBBA".
            while (bodyStart < end && IsAllCaps(lines[bodyStart].Text) && !Subsection().IsMatch(lines[bodyStart].Text))
                title += " " + lines[bodyStart++].Text;
            var chapter = $"Section {wanted.Number}. {ToTitle(title)}";

            var packed = new List<(string?, PackedText)>();
            foreach (var (label, subLines) in SplitSubsections(lines, bodyStart, end))
                packed.AddRange(ChunkSubsection(label, subLines, options).Select(p => ((string?)chapter, p)));

            var firstOrdinal = chunks.Count(c => c.TaxYear == wanted.TaxYear) + 1;
            chunks.AddRange(ChunkFactory.Build(doc, wanted.TaxYear, packed, firstOrdinal));
        }
        return chunks;
    }

    private static IEnumerable<(string Label, List<(int Page, string Text)> Lines)> SplitSubsections(
        List<(int Page, string Text)> lines, int from, int to)
    {
        var label = "Introduction";
        var current = new List<(int, string)>();
        for (var i = from; i < to; i++)
        {
            var m = Subsection().Match(lines[i].Text);
            if (m.Success)
            {
                if (current.Count > 0) yield return (label, current);
                label = $".{m.Groups[1].Value} {SubsectionTitle(m.Groups[2].Value)}";
                current = [];
            }
            current.Add(lines[i]);
        }
        if (current.Count > 0) yield return (label, current);
    }

    private static IEnumerable<PackedText> ChunkSubsection(string label, List<(int Page, string Text)> lines, ChunkingOptions options)
    {
        var tableStarts = lines.Select((l, i) => (l, i)).Where(x => TableHeading().IsMatch(x.l.Text)).Select(x => x.i).ToList();
        if (tableStarts.Count == 0)
        {
            var units = lines.Select(l => new PackUnit(l.Text, l.Page, false, label));
            foreach (var p in ChunkPacker.Pack(units, options, TextUtil.JoinLines))
                yield return p;
            yield break;
        }

        // One chunk per rate table; the subsection's lead-in sentence goes with the first table.
        for (var t = 0; t < tableStarts.Count; t++)
        {
            var from = t == 0 ? 0 : tableStarts[t];
            var to = t + 1 < tableStarts.Count ? tableStarts[t + 1] : lines.Count;
            var slice = lines.GetRange(from, to - from);
            var text = string.Join('\n', slice.Select(l => l.Text));
            var section = $"{label} > {lines[tableStarts[t]].Text}";
            yield return new PackedText(section, [section], text, slice.Min(l => l.Page), slice.Max(l => l.Page));
        }
    }

    /// <summary>"Tax Rate Tables. For taxable years beginning in 2027, …" → "Tax Rate Tables".</summary>
    private static string SubsectionTitle(string rest)
    {
        var period = rest.IndexOf(". ", StringComparison.Ordinal);
        var title = (period > 0 ? rest[..period] : rest).Trim().TrimEnd('.');
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 12 ? string.Join(' ', words.Take(12)) + "…" : title;
    }

    private static bool IsAllCaps(string s) => s.Any(char.IsLetter) && !s.Any(char.IsLower);

    private static string ToTitle(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    [GeneratedRegex(@"^SECTION\s+(\d+)\.\s*(.*)$")] private static partial Regex SectionHeading();
    [GeneratedRegex(@"^\.(\d{2})\s+(.+)$")] private static partial Regex Subsection();
    [GeneratedRegex(@"^TABLE\s+\d+\b")] private static partial Regex TableHeading();
}
