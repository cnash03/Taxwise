using TaxWise.Core;
using TaxWise.Core.Calculator;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;

namespace TaxWise.Rag.Calculator;

public interface ICalcExplainer
{
    /// <summary>The explanation comes back as a one-paragraph <see cref="DraftAnswer"/> so the chat validator applies unchanged.</summary>
    /// <param name="passages">Sent as P1..Pn in this order.</param>
    /// <param name="rejectedAmounts">Set on the stricter retry after the dollar guardrail failed.</param>
    Task<AnswererResult> ExplainAsync(CalcResult result, IReadOnlyList<Chunk> passages, bool plainLanguage,
        IReadOnlyList<string>? rejectedAmounts = null, CancellationToken ct = default);
}

/// <summary>Explains a finished calculation from P1..Pn with the schema <c>{ status, explanation, citations[] }</c> (BUILD_PLAN §7.2).</summary>
public sealed class ClaudeCalcExplainer(ClaudeJsonClient claude) : ICalcExplainer
{
    public async Task<AnswererResult> ExplainAsync(CalcResult result, IReadOnlyList<Chunk> passages, bool plainLanguage,
        IReadOnlyList<string>? rejectedAmounts = null, CancellationToken ct = default)
    {
        var call = await claude.CallAsync(CalcPrompt.SystemPrompt(result.TaxYear, plainLanguage),
            CalcPrompt.UserMessage(result, passages, rejectedAmounts), CalcPrompt.Schema, ct);
        if (call.Refused)
            return new AnswererResult(null, true, call.Call);

        return new AnswererResult(claude.Parse<DraftExplanation>(call).ToDraftAnswer(), false, call.Call);
    }
}
