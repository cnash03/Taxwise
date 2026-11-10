using System.Text.RegularExpressions;

namespace TaxWise.Shared.Display;

/// <summary>Display text for sources and highlights in the chat UI (BUILD_PLAN §5, Phase 3).</summary>
public static partial class SourceLabels
{
    /// <summary>"Pub 970, Tax Benefits for Education" → "Pub 970". Titles without a comma are returned whole.</summary>
    public static string ShortTitle(string docTitle)
    {
        var comma = docTitle.IndexOf(',');
        return comma > 0 ? docTitle[..comma].Trim() : docTitle.Trim();
    }

    /// <summary>"Chapter 1. Scholarships, …" → "Ch. 1"; null or unnumbered chapters → null.</summary>
    public static string? ShortChapter(string? chapter)
    {
        if (string.IsNullOrWhiteSpace(chapter)) return null;
        var m = ChapterNumber().Match(chapter);
        return m.Success ? $"Ch. {m.Groups[1].Value}" : null;
    }

    /// <summary>Citation chip text, e.g. "1 · Pub 970 · Ch. 1 · p. 6 · TY 2025". Every source carries its tax year (BUILD_PLAN §10).</summary>
    public static string Chip(SourcePassage s)
    {
        var parts = new List<string> { s.Number.ToString(), ShortTitle(s.DocTitle) };
        if (ShortChapter(s.Chapter) is { } ch) parts.Add(ch);
        parts.Add($"p. {s.Page}");
        parts.Add($"TY {s.TaxYear}");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Splits a passage around its highlight sentence: (before, highlight, after). The API copies the highlight
    /// from the passage text, so it's an exact substring; when it is null or not found, everything is in "before".
    /// </summary>
    public static (string Before, string? Highlight, string After) SplitHighlight(string text, string? highlight)
    {
        if (string.IsNullOrEmpty(highlight)) return (text, null, "");
        var i = text.IndexOf(highlight, StringComparison.Ordinal);
        return i < 0 ? (text, null, "") : (text[..i], highlight, text[(i + highlight.Length)..]);
    }

    /// <summary>Conversation title: the first question, cut at a word boundary.</summary>
    public static string Truncate(string text, int max)
    {
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t.Length <= max) return t;
        var cut = t.LastIndexOf(' ', max - 1);
        return (cut > max / 2 ? t[..cut] : t[..(max - 1)]).TrimEnd(',', '.', ';', ':') + "…";
    }

    [GeneratedRegex(@"^\s*Chapter\s+(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterNumber();
}
