namespace TaxWise.Core.Chunking;

/// <summary>A piece of text the packer places whole into a chunk.</summary>
/// <param name="SoftBreak">True when the unit starts a minor (bold run-in) heading: the packer may
/// start a new chunk here once the current one has reached the minimum size.</param>
internal sealed record PackUnit(string Text, int Page, bool IsTable, string Section, bool SoftBreak = false);

internal sealed record PackedText(string Section, string Text, int PageStart, int PageEnd);

/// <summary>
/// Greedy packer for one section: fills chunks up to <see cref="ChunkingOptions.MaxTokens"/>, never
/// splits a table, and starts each size-driven continuation with an overlap of the previous chunk's tail.
/// Callers pass one section at a time, so chunks never cross a section or chapter boundary.
/// </summary>
internal static class ChunkPacker
{
    public static List<PackedText> Pack(IEnumerable<PackUnit> units, ChunkingOptions options, Func<IEnumerable<string>, string> join)
    {
        var result = new List<PackedText>();
        var current = new List<PackUnit>();
        var currentTokens = 0;
        string? overlap = null;
        var overlapPage = 0;

        foreach (var unit in Expand(units, options))
        {
            var tokens = TextUtil.EstimateTokens(unit.Text);
            if (current.Count > 0)
            {
                var softBreak = unit.SoftBreak && currentTokens >= options.MinTokens;
                var full = currentTokens + tokens > options.MaxTokens;
                if (softBreak || full)
                {
                    var emitted = Emit();
                    var last = current[^1];
                    // Overlap only when we split mid-section for size, never after a table or into a new subsection.
                    if (!softBreak && !last.IsTable && !unit.IsTable && unit.Section == last.Section)
                    {
                        overlap = TextUtil.TailTokens(emitted.Text, options.OverlapTokens);
                        overlapPage = last.Page;
                    }
                    else overlap = null;

                    current.Clear();
                    currentTokens = overlap is null ? 0 : TextUtil.EstimateTokens(overlap);
                }
            }
            current.Add(unit);
            currentTokens += tokens;
        }
        if (current.Count > 0) Emit();
        return result;

        PackedText Emit()
        {
            var texts = current.Select(u => u.Text);
            var body = join(texts);
            var text = overlap is null ? body : overlap + " " + body;
            var pageStart = overlap is null ? current[0].Page : Math.Min(overlapPage, current[0].Page);
            var packed = new PackedText(SectionLabel(current), text, pageStart, current.Max(u => u.Page));
            result.Add(packed);
            return packed;
        }
    }

    /// <summary>"Reminders · Introduction" when short sections were merged into one chunk (at most three names).</summary>
    private static string SectionLabel(List<PackUnit> units)
    {
        var names = units.Select(u => u.Section).Distinct().ToList();
        return names.Count <= 3 ? string.Join(" · ", names) : string.Join(" · ", names.Take(3)) + " …";
    }

    /// <summary>Splits oversized prose units so each fits in a chunk with room for overlap. Tables stay whole.</summary>
    private static IEnumerable<PackUnit> Expand(IEnumerable<PackUnit> units, ChunkingOptions options)
    {
        var limit = options.MaxTokens - options.OverlapTokens;
        foreach (var unit in units)
        {
            if (unit.IsTable || TextUtil.EstimateTokens(unit.Text) <= limit)
            {
                yield return unit;
                continue;
            }
            var first = true;
            foreach (var piece in TextUtil.SplitByTokens(unit.Text, limit))
            {
                yield return unit with { Text = piece, SoftBreak = first && unit.SoftBreak };
                first = false;
            }
        }
    }
}
