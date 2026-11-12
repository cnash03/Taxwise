using TaxWise.Core;
using TaxWise.Core.Guardrails;

namespace TaxWise.Rag.Answering;

/// <summary>Answers a chat question from P1..Pn with the answer schema (BUILD_PLAN §5.1 "Claude call").</summary>
public sealed class ClaudeAnswerer(ClaudeJsonClient claude) : IAnswerer
{
    public async Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage,
        CancellationToken ct = default)
    {
        var result = await claude.CallAsync(
            AnswerPrompt.SystemPrompt(taxYear, plainLanguage), AnswerPrompt.UserMessage(question, passages), AnswerPrompt.Schema, ct);
        if (result.Refused)
            return new AnswererResult(null, true, result.Call);

        var draft = claude.Parse<DraftAnswer>(result);
        return new AnswererResult(draft with { Paragraphs = draft.Paragraphs ?? [] }, false, result.Call);
    }
}
