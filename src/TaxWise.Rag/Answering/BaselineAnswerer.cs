using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaxWise.Shared;

namespace TaxWise.Rag.Answering;

/// <summary><see cref="Answer"/> is null when <see cref="Refused"/> (stop_reason "refusal").</summary>
public sealed record BaselineResult(string? Answer, bool Refused, ModelCallInfo Call);

public interface IBaselineAnswerer
{
    /// <summary>Same model, same question, no passages (BUILD_PLAN §8 "Baseline").</summary>
    Task<BaselineResult> AnswerAsync(string question, int taxYear, CancellationToken ct = default);
}

/// <summary>
/// A plain chatbot: the answer model with a one-line system prompt and no retrieval. Shared by the eval's
/// <c>--mode baseline</c> and <c>POST /api/ask/baseline</c>, so the in-app comparison shows what the eval measured.
/// </summary>
public sealed class ClaudeBaselineAnswerer(ClaudeJsonClient claude) : IBaselineAnswerer
{
    private sealed record Reply([property: JsonPropertyName("answer")] string Answer);

    public async Task<BaselineResult> AnswerAsync(string question, int taxYear, CancellationToken ct = default)
    {
        var result = await claude.CallAsync(BaselinePrompt.SystemPrompt(taxYear), question.Trim(), BaselinePrompt.Schema, ct);
        if (result.Refused) return new BaselineResult(null, true, result.Call);
        return new BaselineResult(claude.Parse<Reply>(result).Answer, false, result.Call);
    }
}

/// <summary>The baseline prompt, embedded from Prompts/ like the answer prompt and byte-stable per year.</summary>
public static class BaselinePrompt
{
    private static readonly string SystemTemplate = AnswerPrompt.Resource("baseline.system.md").TrimEnd();

    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnswerPrompt.Resource("baseline.schema.json"))!;

    public static string SystemPrompt(int taxYear) =>
        SystemTemplate.Replace("{TaxYear}", taxYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// The "plain chatbot" side of the in-app comparison. No retrieval, no citation validator and no rerank threshold:
/// it shows what TaxWise's guardrails add. The answer is labelled so it's never mistaken for a TaxWise answer.
/// </summary>
public sealed class BaselineService(IBaselineAnswerer answerer, ILogger<BaselineService> logger)
{
    public async Task<BaselineResponse> AskAsync(AskRequest request, CancellationToken ct = default)
    {
        var result = await answerer.AnswerAsync(request.Question, request.TaxYear, ct);
        var call = result.Call;
        var refused = result.Refused || string.IsNullOrWhiteSpace(result.Answer);
        // Same per-call log line as /api/ask (BUILD_PLAN §10). No question or answer text.
        logger.LogInformation(
            "endpoint=ask-baseline model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens} " +
            "cacheReadTokens={CacheReadTokens} stopReason={StopReason} outcome={Outcome} taxYear={TaxYear}",
            call.Model, call.LatencyMs, call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.StopReason,
            refused ? "model_refusal" : "answered", request.TaxYear);

        return refused
            ? new BaselineResponse(BaselineResponse.RefusalKind, AskResponses.CantHelpText, BaselineResponse.PlainChatbotLabel, AskResponses.Disclaimer)
            : new BaselineResponse(BaselineResponse.AnswerKind, result.Answer!.Trim(), BaselineResponse.PlainChatbotLabel, AskResponses.Disclaimer);
    }
}
