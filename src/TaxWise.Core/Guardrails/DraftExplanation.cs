using System.Text.Json.Serialization;

namespace TaxWise.Core.Guardrails;

/// <summary>Claude's structured explanation of one form box, as the form JSON schema shapes it (BUILD_PLAN §6). Not yet trusted.</summary>
public sealed record DraftExplanation(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("explanation")] string? Explanation,
    [property: JsonPropertyName("citations")] IReadOnlyList<DraftCitation>? Citations)
{
    /// <summary>
    /// The same checks as a chat answer: the explanation is a one-paragraph draft, so it is dropped (→ refusal) when
    /// none of its citations survive <see cref="CitationValidator"/>.
    /// </summary>
    public DraftAnswer ToDraftAnswer() =>
        new(Status, string.IsNullOrWhiteSpace(Explanation) ? [] : [new DraftParagraph(Explanation.Trim(), Citations ?? [])]);
}
