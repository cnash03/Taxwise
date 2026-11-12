using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Forms;

public interface IFormExplainer
{
    /// <summary>The explanation comes back as a one-paragraph <see cref="DraftAnswer"/> so the chat validator applies unchanged.</summary>
    /// <param name="passages">Sent as P1..Pn in this order.</param>
    Task<AnswererResult> ExplainAsync(FormSample form, FormBox box, IReadOnlyList<Chunk> passages, bool plainLanguage,
        CancellationToken ct = default);
}

/// <summary>Explains one box from P1..Pn with the form schema <c>{ status, explanation, citations[] }</c> (BUILD_PLAN §6).</summary>
public sealed class ClaudeFormExplainer(ClaudeJsonClient claude) : IFormExplainer
{
    public async Task<AnswererResult> ExplainAsync(FormSample form, FormBox box, IReadOnlyList<Chunk> passages, bool plainLanguage,
        CancellationToken ct = default)
    {
        var result = await claude.CallAsync(
            FormPrompt.SystemPrompt(form.TaxYear, plainLanguage), FormPrompt.UserMessage(form, box, passages), FormPrompt.Schema, ct);
        if (result.Refused)
            return new AnswererResult(null, true, result.Call);

        return new AnswererResult(claude.Parse<DraftExplanation>(result).ToDraftAnswer(), false, result.Call);
    }
}
