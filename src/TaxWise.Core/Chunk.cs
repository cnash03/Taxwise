namespace TaxWise.Core;

/// <summary>One retrievable passage from an IRS document (BUILD_PLAN §4.2 step 5).</summary>
public sealed record Chunk(
    string Id,            // deterministic: $"{docId}-ty{taxYear}-p{pageStart}-{ordinal:D3}"
    string DocId, string DocTitle, int TaxYear,
    string? Chapter, string Section,
    int PageStart, int PageEnd,
    string Text,          // without the breadcrumb prefix
    IReadOnlyList<string>? Sections = null) // every section in a merged chunk, in order; null when it's just Section
{
    /// <summary><see cref="Sections"/>, or just <see cref="Section"/> when the chunk covers one section.</summary>
    public IReadOnlyList<string> AllSections => Sections is { Count: > 0 } ? Sections : [Section];

    /// <summary>Builds the deterministic chunk id.</summary>
    public static string MakeId(string docId, int taxYear, int pageStart, int ordinal) =>
        $"{docId}-ty{taxYear}-p{pageStart}-{ordinal:D3}";

    /// <summary>"Pub 970 > Chapter 1. Scholarships… > Tax-Free Scholarships": names the main section, for display.</summary>
    public string Breadcrumb => MakeBreadcrumb(Section);

    /// <summary>
    /// The text that gets embedded: a breadcrumb naming every merged section ("… > Reminders · Introduction"),
    /// so each section's name still helps retrieval, then a blank line and the chunk text.
    /// </summary>
    public string EmbeddingText => $"{MakeBreadcrumb(string.Join(" · ", AllSections))}\n\n{Text}";

    private string MakeBreadcrumb(string section) =>
        string.Join(" > ", new[] { ShortTitle(DocTitle), Chapter, section }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>"Pub 970, Tax Benefits for Education" → "Pub 970".</summary>
    public static string ShortTitle(string docTitle)
    {
        var comma = docTitle.IndexOf(',');
        return comma > 0 ? docTitle[..comma] : docTitle;
    }
}

/// <summary>A chunk returned by a vector search, with its similarity score.</summary>
public sealed record ScoredChunk(Chunk Chunk, double Score);
