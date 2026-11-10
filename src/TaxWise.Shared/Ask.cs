namespace TaxWise.Shared;

/// <summary>Body of <c>POST /api/ask</c> (BUILD_PLAN §5.1).</summary>
public sealed record AskRequest(string Question, bool PlainLanguage, int TaxYear);

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

/// <summary><see cref="Page"/> is the physical PDF page, so <c>PdfUrl + "#page=" + Page</c> opens it.</summary>
public sealed record SourcePassage(int Number, string ChunkId, string DocId, string DocTitle,
    string? Chapter, string Section, int Page, int TaxYear, string Text, string PdfUrl);

public sealed record HelpLink(string Label, string Url);
