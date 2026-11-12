namespace TaxWise.Eval;

/// <summary>A chunk among the top 4 after reranking.</summary>
public sealed record RetrievedRef(string ChunkId, string DocId, int PageStart, int PageEnd, double Score);

/// <summary>The judge's verdict: one of <see cref="Verdicts"/>.</summary>
public sealed record JudgeVerdict(string Verdict, string Reason);

public static class Verdicts
{
    public const string Correct = "correct";
    public const string Partial = "partial";
    public const string Incorrect = "incorrect";
    public const string Declined = "declined";
}

/// <summary>What the pipeline did with a question, before any threshold is applied.</summary>
public static class Outcomes
{
    public const string Answered = "answered";
    public const string NoHits = "no_hits";
    public const string ModelRefusal = "model_refusal";
    public const string Error = "error";
    // Plus the validator outcomes ValidationStats.InsufficientSources and AllParagraphsDropped.
}

/// <summary>
/// One gold question run once, with the rerank threshold <em>off</em>: Claude is called even when the top score is
/// low, so every threshold in the sweep can be scored from this one record. In baseline mode there's no retrieval
/// (<see cref="Retrieved"/> is empty) and a refusal is a "declined" verdict from the judge.
/// </summary>
public sealed record QuestionResult(
    string Id,
    string Persona,
    bool InScope,
    int TaxYear,
    string Question,
    double TopScore,
    IReadOnlyList<RetrievedRef> Retrieved,
    bool? HitAt4,
    string Outcome,
    int CitationsTotal,
    int CitationsValid,
    string? AnswerText,
    JudgeVerdict? Judge,
    long LatencyMs,
    string? Error = null)
{
    /// <summary>True when the pipeline itself refused (or failed), whatever the threshold.</summary>
    public bool PipelineRefused => Outcome != Outcomes.Answered || Judge?.Verdict == Verdicts.Declined;
}
