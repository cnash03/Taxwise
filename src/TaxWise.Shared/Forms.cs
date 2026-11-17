namespace TaxWise.Shared;

/// <summary>
/// A sample form from <c>data/forms/*.json</c> (BUILD_PLAN §6). Every <see cref="FormBox.GoesTo"/> is human-verified
/// data; the model explains the mapping but never decides it.
/// </summary>
public sealed record FormSample(
    string FormId,              // "w2" | "1098t", used in URLs
    string Form,                // "W-2"
    string Title,               // "Wage and Tax Statement"
    int TaxYear,
    string IssuerLabel,         // "c · Employer name"
    string Issuer,
    IReadOnlyList<FormBox> Boxes);

/// <param name="Value">Display text: an amount, a state code or a checkbox state.</param>
/// <param name="Summary">One line for the "Next up" card.</param>
/// <param name="RetrievalQuery">What to search the IRS documents for when explaining this box.</param>
/// <param name="SourceDocs">Manifest doc IDs the search is limited to.</param>
public sealed record FormBox(
    string Id,
    string Label,
    string Value,
    string Summary,
    string GoesTo,
    string RetrievalQuery,
    IReadOnlyList<string> SourceDocs);

/// <summary>
/// <c>POST /api/forms/upload</c> takes one PDF as the raw body. It answers with a <see cref="FormSample"/> built on the
/// reviewed sample, so box clicks go through <c>/api/forms/explain</c> unchanged.
/// </summary>
public static class FormUploadLimits
{
    public const string ContentType = "application/pdf";
    public const long MaxBytes = 5 * 1024 * 1024;   // W-2 and 1098-T PDFs are usually under 500 KB
}

/// <summary>Body of <c>POST /api/forms/explain</c>.</summary>
public sealed record ExplainFormRequest(string Form, string BoxId, bool PlainLanguage);

/// <summary>
/// A cited explanation of one box, or a refusal. <see cref="GoesTo"/> always comes from the form data, never from the
/// model. Citation numbers refer to <see cref="Sources"/>.
/// </summary>
public sealed record ExplainFormResponse(
    string Kind,                            // "answer" | "refusal" (AskResponse.AnswerKind / RefusalKind)
    string FormId,
    string BoxId,
    string BoxLabel,
    string GoesTo,
    string Explanation,
    IReadOnlyList<CitationRef> Citations,
    IReadOnlyList<SourcePassage> Sources,
    int PassagesSearched, int PassagesKept,
    string Disclaimer,
    IReadOnlyList<HelpLink> Links);         // refusal only
