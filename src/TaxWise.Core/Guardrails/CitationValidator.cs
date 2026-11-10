using System.Text;

namespace TaxWise.Core.Guardrails;

/// <summary>A paragraph that survived validation; every one has at least one citation.</summary>
public sealed record ValidParagraph(string Text, IReadOnlyList<ValidCitation> Citations);

/// <summary><see cref="PassageNumber"/> is 1-based (P1 → 1). <see cref="Highlight"/> is copied from the passage text, or null.</summary>
public sealed record ValidCitation(int PassageNumber, string? Highlight);

public sealed record ValidatedAnswer(
    bool IsRefusal,
    IReadOnlyList<ValidParagraph> Paragraphs,
    IReadOnlyList<string> SuggestedFollowUps,
    ValidationStats Stats);

/// <summary>What the validator removed, for the per-call log line (BUILD_PLAN §10).</summary>
public sealed record ValidationStats(string Outcome, int CitationsRemoved, int HighlightsMissing, int ParagraphsDropped)
{
    public const string Ok = "ok";
    public const string InsufficientSources = "insufficient_sources";
    public const string AllParagraphsDropped = "all_paragraphs_dropped";
}

/// <summary>
/// Checks Claude's draft against the passages actually sent (BUILD_PLAN §5.1 "Validation"):
/// citations to unknown passages are removed, quotes that aren't in the passage lose their highlight,
/// paragraphs left without a citation are dropped, and nothing left (or insufficient_sources) means a refusal.
/// </summary>
public static class CitationValidator
{
    public const int MaxFollowUps = 2;

    /// <param name="passageTexts">The passage texts in the order sent: index 0 is P1.</param>
    public static ValidatedAnswer Validate(DraftAnswer draft, IReadOnlyList<string> passageTexts)
    {
        if (draft.Status != DraftAnswer.Answered)
            return Refusal(ValidationStats.InsufficientSources, 0, 0, 0);

        int removed = 0, missing = 0, dropped = 0;
        var paragraphs = new List<ValidParagraph>();
        foreach (var p in draft.Paragraphs)
        {
            var citations = new List<ValidCitation>();
            foreach (var c in p.Citations)
            {
                var number = ParsePassage(c.Passage, passageTexts.Count);
                if (number is null) { removed++; continue; }

                var highlight = FindQuote(passageTexts[number.Value - 1], c.Quote);
                var existing = citations.FindIndex(x => x.PassageNumber == number);
                if (existing >= 0)
                {
                    // One chip per passage per paragraph; prefer a citation whose quote matched.
                    if (citations[existing].Highlight is null && highlight is not null) citations[existing] = new(number.Value, highlight);
                    continue;
                }
                if (highlight is null) missing++;
                citations.Add(new ValidCitation(number.Value, highlight));
            }

            if (citations.Count == 0 || string.IsNullOrWhiteSpace(p.Text)) { dropped++; continue; }
            paragraphs.Add(new ValidParagraph(p.Text.Trim(), citations));
        }

        if (paragraphs.Count == 0)
            return Refusal(ValidationStats.AllParagraphsDropped, removed, missing, dropped);

        var followUps = (draft.SuggestedFollowUps ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Take(MaxFollowUps).ToList();
        return new ValidatedAnswer(false, paragraphs, followUps,
            new ValidationStats(ValidationStats.Ok, removed, missing, dropped));
    }

    /// <summary>"P3" → 3 when 1 ≤ 3 ≤ <paramref name="passageCount"/>; anything else → null.</summary>
    public static int? ParsePassage(string? label, int passageCount)
    {
        var s = label?.Trim();
        if (s is not { Length: >= 2 } || (s[0] != 'P' && s[0] != 'p')) return null;
        return int.TryParse(s.AsSpan(1), out var n) && n >= 1 && n <= passageCount ? n : null;
    }

    /// <summary>
    /// Finds <paramref name="quote"/> in <paramref name="passage"/> after normalizing whitespace, quote marks,
    /// dashes and ligatures, and returns the matching span of the <em>original</em> passage text, or null.
    /// </summary>
    public static string? FindQuote(string passage, string? quote)
    {
        var q = Normalize(quote ?? "").Text.Trim().Trim('"', '\'').Trim();
        if (q.Length == 0) return null;

        var (text, starts, ends) = Normalize(passage);
        foreach (var candidate in new[] { q, q.TrimEnd('.', ' ') })
        {
            if (candidate.Length == 0) continue;
            var i = text.IndexOf(candidate, StringComparison.Ordinal);
            if (i < 0) i = text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return passage[starts[i]..ends[i + candidate.Length - 1]];
        }
        return null;
    }

    /// <summary>Normalized text plus, for each normalized char, the original span [start, end) it came from.</summary>
    private static (string Text, List<int> Starts, List<int> Ends) Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var starts = new List<int>(s.Length);
        var ends = new List<int>(s.Length);
        var pendingSpace = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (c == '­') continue; // soft hyphen

            var mapped = c switch
            {
                '‘' or '’' or '‚' or '‛' or '′' => "'",
                '“' or '”' or '„' or '″' => "\"",
                '‐' or '‑' or '‒' or '–' or '—' or '−' => "-",
                _ => char.IsSurrogate(c) ? c.ToString() : c.ToString().Normalize(NormalizationForm.FormKC), // "ﬁ" → "fi"
            };

            if (pendingSpace)
            {
                sb.Append(' ');
                starts.Add(i); ends.Add(i);
                pendingSpace = false;
            }
            foreach (var m in mapped)
            {
                sb.Append(m);
                starts.Add(i); ends.Add(i + 1);
            }
        }
        return (sb.ToString(), starts, ends);
    }

    private static ValidatedAnswer Refusal(string outcome, int removed, int missing, int dropped) =>
        new(true, [], [], new ValidationStats(outcome, removed, missing, dropped));
}
