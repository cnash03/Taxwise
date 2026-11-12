using System.Text.RegularExpressions;

namespace TaxWise.Shared.Display;

/// <summary>Contents of <c>data/glossary.json</c>: human-reviewed term definitions for plain-language mode (BUILD_PLAN §5.2).</summary>
public sealed record GlossaryFile(int TaxYear, IReadOnlyList<GlossaryTerm> Terms);

/// <summary>One term. <see cref="ChunkId"/> is the IRS passage the definition paraphrases.</summary>
public sealed record GlossaryTerm(string Term, IReadOnlyList<string>? Aliases, string Definition, string ChunkId, bool Reviewed)
{
    public IEnumerable<string> Spellings => Aliases is null ? [Term] : Aliases.Prepend(Term);
}

/// <summary>A run of answer text: plain, or a glossary term to wrap in a tooltip.</summary>
public sealed record TextSegment(string Text, GlossaryTerm? Term = null);

/// <summary>
/// Deterministic term marking (BUILD_PLAN §5.2): wraps the first case-insensitive, whole-word match of each glossary
/// term in an answer. The LLM never chooses or writes definitions.
/// </summary>
public sealed class GlossaryMarker
{
    private readonly List<(GlossaryTerm Term, Regex Pattern)> _terms;

    public GlossaryMarker(IEnumerable<GlossaryTerm> terms)
    {
        _terms = terms
            .Select(t => (t, new Regex(
                // Longest spelling first so "tax credit" wins over "credit" at the same position.
                @"(?<![\w-])(?:" + string.Join("|", t.Spellings.OrderByDescending(s => s.Length).Select(Regex.Escape)) + @")(?![\w-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .ToList();
    }

    /// <summary>
    /// Splits <paramref name="text"/> into segments. Terms already in <paramref name="used"/> (marked in an earlier
    /// paragraph of the same answer) are skipped; terms marked here are added to it.
    /// Matches are taken left to right, longest first at the same position, so "standard deduction" isn't split
    /// into "deduction" and "earned income credit" beats "earned income".
    /// </summary>
    public IReadOnlyList<TextSegment> Mark(string text, ISet<GlossaryTerm> used)
    {
        var candidates = new List<(int Start, int Length, GlossaryTerm Term)>();
        foreach (var (term, pattern) in _terms)
        {
            if (used.Contains(term)) continue;
            // Every match, not just the first: the first may sit inside a longer term.
            foreach (Match m in pattern.Matches(text))
                candidates.Add((m.Index, m.Length, term));
        }

        var chosen = new List<(int Start, int Length, GlossaryTerm Term)>();
        var end = 0;
        foreach (var c in candidates.OrderBy(c => c.Start).ThenByDescending(c => c.Length))
        {
            if (c.Start < end || chosen.Any(x => x.Term == c.Term)) continue;
            chosen.Add(c);
            end = c.Start + c.Length;
        }

        var segments = new List<TextSegment>();
        var pos = 0;
        foreach (var (start, length, term) in chosen)
        {
            if (start > pos) segments.Add(new TextSegment(text[pos..start]));
            segments.Add(new TextSegment(text.Substring(start, length), term));
            used.Add(term);
            pos = start + length;
        }
        if (pos < text.Length) segments.Add(new TextSegment(text[pos..]));
        return segments;
    }
}
