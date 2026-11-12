using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaxWise.Core;

namespace TaxWise.Rag.Answering;

/// <summary>Builds the Claude prompt from the embedded templates in Prompts/ (BUILD_PLAN §5.1).</summary>
public static partial class AnswerPrompt
{
    private static readonly string SystemTemplate = Resource("answer.system.md");
    private static readonly string DefaultStyle = Resource("writing-style.default.md").Trim();
    private static readonly string PlainStyle = Resource("writing-style.plain.md").Trim();

    /// <summary>The answer JSON schema, as sent in <c>output_config.format</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Resource("answer.schema.json"))!;

    /// <summary>Same text for every request with the same year and mode, so the prefix stays cacheable.</summary>
    public static string SystemPrompt(int taxYear, bool plainLanguage) =>
        SystemTemplate
            .Replace("{TaxYear}", taxYear.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{WritingStyle}", WritingStyle(plainLanguage))
            .TrimEnd();

    /// <summary>The default or plain-language style paragraph (BUILD_PLAN §5.2); the passages and facts stay the same.</summary>
    internal static string WritingStyle(bool plainLanguage) => plainLanguage ? PlainStyle : DefaultStyle;

    /// <summary>The passages as P1..Pn blocks, then the question.</summary>
    public static string UserMessage(string question, IReadOnlyList<Chunk> passages)
    {
        var sb = new StringBuilder();
        AppendPassages(sb, passages);
        sb.Append("<question>").Append(Body(question.Trim())).Append("</question>");
        return sb.ToString();
    }

    /// <summary>One <c>&lt;passage id="Pn" source="…"&gt;</c> block per chunk, in order.</summary>
    internal static void AppendPassages(StringBuilder sb, IReadOnlyList<Chunk> passages)
    {
        for (var i = 0; i < passages.Count; i++)
        {
            var c = passages[i];
            sb.Append($"<passage id=\"P{i + 1}\" source=\"{Attr(SourceLabel(c))}\">\n")
              .Append(Body(c.Text))
              .Append("\n</passage>\n\n");
        }
    }

    /// <summary>"Pub 970 · Ch. 1 · p. 7 · TY 2025".</summary>
    public static string SourceLabel(Chunk c)
    {
        var parts = new List<string> { Chunk.ShortTitle(c.DocTitle) };
        if (ShortChapter(c.Chapter) is { } ch) parts.Add(ch);
        parts.Add($"p. {c.PageStart}");
        parts.Add($"TY {c.TaxYear}");
        return string.Join(" · ", parts);
    }

    /// <summary>"Chapter 1. Scholarships, Fellowship Grants…" → "Ch. 1"; other chapter names pass through.</summary>
    public static string? ShortChapter(string? chapter)
    {
        if (string.IsNullOrWhiteSpace(chapter)) return null;
        var m = ChapterNumber().Match(chapter);
        return m.Success ? $"Ch. {m.Groups[1].Value}" : chapter.Trim();
    }

    // Passage text goes in verbatim (Claude must quote it exactly); only a literal closing tag is defused.
    internal static string Body(string s) => s.Replace("</passage>", "</ passage>").Replace("</question>", "</ question>");
    internal static string Attr(string s) => s.Replace("\"", "'");

    internal static string Resource(string name)
    {
        using var stream = typeof(AnswerPrompt).Assembly.GetManifestResourceStream($"TaxWise.Rag.Prompts.{name}")
                           ?? throw new InvalidOperationException($"Missing embedded prompt {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    [GeneratedRegex(@"^\s*Chapter\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterNumber();
}
