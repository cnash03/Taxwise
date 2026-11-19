namespace TaxWise.Shared;

/// <summary>
/// Body of <c>POST /api/ask</c> (BUILD_PLAN §5.1). <see cref="Jurisdiction"/> is "US" (federal, the default when it's
/// missing or null) or "CO" (Colorado); see <see cref="Jurisdictions"/>.
/// </summary>
public sealed record AskRequest(string Question, bool PlainLanguage, int TaxYear, string? Jurisdiction = Jurisdictions.Federal);

/// <summary>An answer or a refusal. Citation numbers always refer to <see cref="Sources"/> (1–4).</summary>
public sealed record AskResponse(
    string Kind,                            // "answer" | "refusal"
    IReadOnlyList<AnswerParagraph> Paragraphs,
    IReadOnlyList<SourcePassage> Sources,   // the passages sent to Claude, for the right-hand panel
    int PassagesSearched, int PassagesKept, // "Searched 8 passages, kept the 4 most relevant"
    IReadOnlyList<string> SuggestedFollowUps,
    string Disclaimer,
    IReadOnlyList<HelpLink> Links)          // refusal only: IRS.gov, Free File, VITA
{
    public const string AnswerKind = "answer";
    public const string RefusalKind = "refusal";
}

public sealed record AnswerParagraph(string Text, IReadOnlyList<CitationRef> Citations);

/// <summary><see cref="Highlight"/> is the exact sentence in the passage text to mark, or null when Claude's quote didn't match.</summary>
public sealed record CitationRef(int Number, string ChunkId, string? Highlight);

/// <summary>
/// <see cref="Page"/> is the physical PDF page, so <c>PdfUrl + "#page=" + Page</c> opens it.
/// <see cref="Jurisdiction"/> says who published it ("US" = IRS, "CO" = Colorado DOR).
/// </summary>
public sealed record SourcePassage(int Number, string ChunkId, string DocId, string DocTitle,
    string? Chapter, string Section, int Page, int TaxYear, string Text, string PdfUrl,
    string Jurisdiction = Jurisdictions.Federal);

public sealed record HelpLink(string Label, string Url);
