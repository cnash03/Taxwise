using TaxWise.Core;
using TaxWise.Core.Guardrails;

namespace TaxWise.Rag.Answering;

/// <summary>What happened on the model call, for the per-call log line (BUILD_PLAN §10).</summary>
public sealed record ModelCallInfo(string Model, long LatencyMs, long InputTokens, long OutputTokens, long CacheReadTokens, string StopReason);

/// <summary>
/// Either a <see cref="Draft"/> to validate, or <see cref="ModelRefused"/> when Claude stopped with
/// <c>stop_reason: refusal</c> (no content is read in that case).
/// </summary>
public sealed record AnswererResult(DraftAnswer? Draft, bool ModelRefused, ModelCallInfo Call);

public interface IAnswerer
{
    /// <param name="passages">Sent as P1..Pn in this order.</param>
    Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage,
        CancellationToken ct = default);
}

/// <summary>The model couldn't produce an answer. <see cref="Exception.Message"/> is safe to show users.</summary>
public sealed class AnswererException(AnswererFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public AnswererFailure Failure { get; } = failure;
}

public enum AnswererFailure
{
    /// <summary>429 from the API: the caller should try again later.</summary>
    RateLimited,
    /// <summary>5xx/network after one retry, another API error, or an unreadable response.</summary>
    Unavailable,
}
