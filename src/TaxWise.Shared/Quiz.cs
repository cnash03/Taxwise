namespace TaxWise.Shared;

/// <summary>Body of <c>POST /api/quiz</c> (BUILD_PLAN §7, Phase 6).</summary>
/// <param name="Topic">The quiz topic: the section of the first passage cited in chat, e.g. "Scholarships and Fellowship Grants".</param>
/// <param name="ChunkIds">The chunks cited in chat under this topic, at most <see cref="QuizLimits.MaxChunkIds"/>.</param>
/// <param name="TaxYear">Used to fill up from the topic when fewer than <paramref name="Count"/> chunks were cited.</param>
public sealed record QuizRequest(
    string Topic,
    IReadOnlyList<string> ChunkIds,
    int TaxYear,
    bool PlainLanguage = false,
    int Count = QuizLimits.DefaultCount);

/// <summary>The questions that passed validation, in passage order. Can be shorter than the requested count, or empty.</summary>
public sealed record QuizResponse(string Topic, int TaxYear, IReadOnlyList<QuizItem> Items, string Disclaimer);

/// <summary>
/// One multiple-choice question written from a single passage. <see cref="Quote"/> is the sentence of
/// <see cref="Source"/> that answers it, copied from the passage text so the source panel can highlight it.
/// </summary>
public sealed record QuizItem(
    string ChunkId,
    string Question,
    IReadOnlyList<string> Options,  // exactly 4: A–D
    int CorrectIndex,               // 0–3
    string Explanation,
    string Quote,
    SourcePassage Source);

public static class QuizLimits
{
    public const int DefaultCount = 5;
    public const int MaxCount = 10;
    public const int MaxChunkIds = 10;
    public const int MaxTopicLength = 200;
}
