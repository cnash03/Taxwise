using System.Text.Json.Serialization;

namespace TaxWise.Core.Guardrails;

/// <summary>Claude's structured answer, exactly as the answer JSON schema shapes it (BUILD_PLAN §5.1). Not yet trusted.</summary>
public sealed record DraftAnswer(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("paragraphs")] IReadOnlyList<DraftParagraph> Paragraphs,
    [property: JsonPropertyName("suggestedFollowUps")] IReadOnlyList<string>? SuggestedFollowUps = null)
{
    public const string Answered = "answered";
    public const string InsufficientSources = "insufficient_sources";
}

public sealed record DraftParagraph(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("citations")] IReadOnlyList<DraftCitation> Citations);

/// <summary><see cref="Passage"/> is a label like "P2"; <see cref="Quote"/> should be a sentence copied from it.</summary>
public sealed record DraftCitation(
    [property: JsonPropertyName("passage")] string Passage,
    [property: JsonPropertyName("quote")] string Quote);
