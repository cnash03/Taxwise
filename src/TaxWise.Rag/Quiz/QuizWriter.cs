using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;

namespace TaxWise.Rag.Quiz;

/// <summary>
/// <see cref="Draft"/> is null when <see cref="ModelRefused"/>, or when the reply wasn't readable JSON
/// (<see cref="Unreadable"/>); an unreadable reply is retried like a failed check instead of failing the whole quiz.
/// </summary>
public sealed record QuizWriterResult(DraftQuizItem? Draft, bool ModelRefused, bool Unreadable, ModelCallInfo Call);

public interface IQuizWriter
{
    /// <param name="failedCheck">Set on the retry: the <see cref="QuizCheck.Outcome"/> the first question failed.</param>
    Task<QuizWriterResult> WriteAsync(string topic, Chunk passage, bool plainLanguage, string? failedCheck = null,
        CancellationToken ct = default);
}

/// <summary>Writes one question from one passage with the schema <c>{ question, options[4], correctIndex, explanation, quote }</c>.</summary>
public sealed class ClaudeQuizWriter(ClaudeJsonClient claude) : IQuizWriter
{
    public async Task<QuizWriterResult> WriteAsync(string topic, Chunk passage, bool plainLanguage, string? failedCheck = null,
        CancellationToken ct = default)
    {
        var call = await claude.CallAsync(QuizPrompt.SystemPrompt(plainLanguage),
            QuizPrompt.UserMessage(topic, passage, failedCheck), QuizPrompt.Schema, ct);
        if (call.Refused)
            return new QuizWriterResult(null, true, false, call.Call);

        try
        {
            return new QuizWriterResult(claude.Parse<DraftQuizItem>(call), false, false, call.Call);
        }
        catch (AnswererException)
        {
            // Parse already logged it. One bad reply costs one question, not the quiz.
            return new QuizWriterResult(null, false, true, call.Call);
        }
    }
}
