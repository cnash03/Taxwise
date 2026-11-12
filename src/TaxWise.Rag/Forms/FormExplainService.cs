using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Forms;

/// <summary>
/// "Explain my form" (BUILD_PLAN §6): retrieve with the box's query, limited to its source docs → refuse below the rerank
/// threshold <em>without</em> calling Claude → explain → validate citations. <see cref="ExplainFormResponse.GoesTo"/> is
/// always the human-verified value from the form data. Results are cached in memory per (form, box, plainLanguage).
/// PdfUrls in the response are relative; the API makes them absolute.
/// </summary>
public sealed class FormExplainService(IRetriever retriever, IFormExplainer explainer, ILogger<FormExplainService> logger)
{
    private readonly ConcurrentDictionary<(string Form, string Box, bool Plain), ExplainFormResponse> _cache = new();

    public async Task<ExplainFormResponse> ExplainAsync(FormSample form, FormBox box, bool plainLanguage, CancellationToken ct = default)
    {
        var key = (form.FormId.ToLowerInvariant(), box.Id.ToLowerInvariant(), plainLanguage);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var response = await ExplainUncachedAsync(form, box, plainLanguage, ct);
        // Answers and refusals are both kept; failures throw and are never cached.
        return _cache.GetOrAdd(key, response);
    }

    private async Task<ExplainFormResponse> ExplainUncachedAsync(FormSample form, FormBox box, bool plainLanguage, CancellationToken ct)
    {
        var retrieval = await retriever.RetrieveAsync(box.RetrievalQuery, form.TaxYear, box.SourceDocs, ct);
        if (!retrieval.MeetsThreshold)
        {
            logger.LogInformation(
                "endpoint=forms.explain outcome=refusal reason=below_threshold form={Form} box={Box} topScore={TopScore:F4} searched={Searched} claudeCalled=false",
                form.FormId, box.Id, retrieval.TopScore, retrieval.Searched);
            return Refusal(form, box, AskResponses.NotFoundText(form.TaxYear), retrieval.Searched, retrieval.Passages.Count);
        }

        var chunks = retrieval.Passages.Select(p => p.Chunk).ToList();
        var result = await explainer.ExplainAsync(form, box, chunks, plainLanguage, ct);
        if (result.ModelRefused || result.Draft is null)
        {
            LogCall(result.Call, "model_refusal", form, box, plainLanguage, retrieval.TopScore);
            return Refusal(form, box, AskResponses.CantHelpText, retrieval.Searched, chunks.Count);
        }

        var validated = CitationValidator.Validate(result.Draft, chunks.Select(c => c.Text).ToList());
        LogCall(result.Call, validated.Stats.Outcome, form, box, plainLanguage, retrieval.TopScore, validated.Stats);
        if (validated.IsRefusal)
            return Refusal(form, box, AskResponses.NotFoundText(form.TaxYear), retrieval.Searched, chunks.Count);

        var paragraph = validated.Paragraphs[0];
        return new ExplainFormResponse(AskResponse.AnswerKind, form.FormId, box.Id, box.Label, box.GoesTo, paragraph.Text,
            paragraph.Citations.Select(c => new CitationRef(c.PassageNumber, chunks[c.PassageNumber - 1].Id, c.Highlight)).ToList(),
            chunks.Select((c, i) => AskService.ToSource(i + 1, c)).ToList(),
            retrieval.Searched, chunks.Count, AskResponses.Disclaimer, []);
    }

    private static ExplainFormResponse Refusal(FormSample form, FormBox box, string text, int searched, int kept) =>
        new(AskResponse.RefusalKind, form.FormId, box.Id, box.Label, box.GoesTo, text, [], [], searched, kept,
            AskResponses.Disclaimer, AskResponses.HelpLinks);

    // One line per Claude call (BUILD_PLAN §10). Form and box IDs are public sample data, safe to log.
    private void LogCall(ModelCallInfo call, string outcome, FormSample form, FormBox box, bool plain, double topScore,
        ValidationStats? stats = null) =>
        logger.LogInformation(
            "endpoint=forms.explain model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens} " +
            "cacheReadTokens={CacheReadTokens} stopReason={StopReason} validator={Outcome} citationsRemoved={Removed} " +
            "highlightsMissing={Missing} form={Form} box={Box} topScore={TopScore:F4} taxYear={TaxYear} plain={Plain}",
            call.Model, call.LatencyMs, call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.StopReason, outcome,
            stats?.CitationsRemoved ?? 0, stats?.HighlightsMissing ?? 0, form.FormId, box.Id, topScore, form.TaxYear, plain);
}
