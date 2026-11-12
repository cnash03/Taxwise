using Microsoft.Extensions.Logging;
using TaxWise.Core;
using TaxWise.Core.Calculator;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;
using TaxWise.Shared.Display;

namespace TaxWise.Rag.Calculator;

/// <summary>
/// The scenario calculator (BUILD_PLAN §7.2): compute with <see cref="TaxCalculator"/>, then explain the fixed numbers
/// from the selected year's standard deduction and tax table passages. The explanation must pass the citation check
/// and the dollar guardrail. A guardrail failure gets one stricter retry; after that, or when there are no usable
/// sources, the response carries a canned explanation built from the numbers. The numbers are never affected.
/// PdfUrls in the response are relative; the API makes them absolute.
/// </summary>
public sealed class CalculatorService(IRetriever retriever, ICalcExplainer explainer, ILogger<CalculatorService> logger)
{
    /// <summary>For 2025, search the Form 1040 instructions and the deduction guides, not the student pubs. Other years only have their Rev. Proc.</summary>
    private static readonly string[] Docs2025 = ["i1040", "p501", "p17", "rp-25-32"];
    private const int PassagesPerQuery = 2;

    /// <param name="rules">From <see cref="TaxYearRegistry"/>; the API rejects unsupported years first.</param>
    public async Task<CalcResponse> CalculateAsync(ITaxYearRules rules, CalcRequest request, CancellationToken ct = default)
    {
        var result = TaxCalculator.Calculate(rules, request.Wages, request.FederalWithholding, request.FilingStatus,
            request.CanBeClaimedAsDependent);

        var (passages, topScore) = await RetrieveAsync(result, ct);
        if (passages.Count == 0)
        {
            logger.LogInformation(
                "endpoint=calculator outcome=fallback reason=below_threshold topScore={TopScore:F4} taxYear={TaxYear} claudeCalled=false",
                topScore, result.TaxYear);
            return Response(result, CalcResponse.FallbackKind, CalcNarrative.Canned(result), [], []);
        }

        IReadOnlyList<string>? rejected = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var call = await explainer.ExplainAsync(result, passages, request.PlainLanguage, rejected, ct);
            if (call.ModelRefused || call.Draft is null)
            {
                LogCall(call.Call, "model_refusal", attempt, result, request.PlainLanguage, topScore);
                break;
            }

            var validated = CitationValidator.Validate(call.Draft, passages.Select(c => c.Text).ToList());
            if (validated.IsRefusal)
            {
                LogCall(call.Call, validated.Stats.Outcome, attempt, result, request.PlainLanguage, topScore, validated.Stats);
                break;
            }

            var paragraph = validated.Paragraphs[0];
            var dollars = DollarAmountValidator.Validate(paragraph.Text, result.KnownAmounts);
            if (dollars.IsValid)
            {
                LogCall(call.Call, validated.Stats.Outcome, attempt, result, request.PlainLanguage, topScore, validated.Stats);
                return Response(result, CalcResponse.AnswerKind, paragraph.Text,
                    paragraph.Citations.Select(c => new CitationRef(c.PassageNumber, passages[c.PassageNumber - 1].Id, c.Highlight)).ToList(),
                    passages.Select((c, i) => AskService.ToSource(i + 1, c)).ToList());
            }

            rejected = dollars.Unexpected;
            LogCall(call.Call, "dollar_mismatch", attempt, result, request.PlainLanguage, topScore, validated.Stats, dollars.Unexpected.Count);
            if (attempt == 2)
            {
                // An incident: the model made up an amount twice. Only the made-up amounts are logged, never the inputs.
                logger.LogWarning("endpoint=calculator incident=dollar_guardrail_failed taxYear={TaxYear} unexpected={Unexpected}",
                    result.TaxYear, string.Join(" ", dollars.Unexpected));
            }
        }

        return Response(result, CalcResponse.FallbackKind, CalcNarrative.Canned(result), [], []);
    }

    /// <summary>The same inputs for every supported year, numbers only (the "Compare years" toggle; no Claude call).</summary>
    public static CompareYearsResponse Compare(CalcRequest request) =>
        new(TaxYearRegistry.Supported
                .Select(y => TaxCalculator.Calculate(TaxYearRegistry.Get(y)!, request.Wages, request.FederalWithholding,
                    request.FilingStatus, request.CanBeClaimedAsDependent))
                .Select(r => new YearResult(r.TaxYear, r.StandardDeduction, r.TaxableIncome, r.Tax, Math.Abs(r.RefundOrOwed),
                    r.IsRefund, r.IsEstimate))
                .ToList(),
            CalcLabels.NotIncluded, AskResponses.Disclaimer);

    /// <summary>
    /// Two searches in the selected year: one for the standard deduction, one for how the tax is figured. The best two
    /// passages of each search that clears the rerank threshold are interleaved (P1 deduction, P2 tax, …).
    /// </summary>
    private async Task<(List<Chunk> Passages, double TopScore)> RetrieveAsync(CalcResult r, CancellationToken ct)
    {
        var docs = r.TaxYear == 2025 ? Docs2025 : null;
        var deductionQuery = r.CanBeClaimedAsDependent
            ? "standard deduction for a dependent: earned income plus amount, minimum amount"
            : $"standard deduction amount for {CalcLabels.FilingStatus(r.FilingStatus).ToLowerInvariant()}";
        var taxQuery = r.Breakdown.Method == TaxBreakdown.WorksheetMethod
            ? $"Tax Computation Worksheet tax rate table for {CalcLabels.FilingStatus(r.FilingStatus).ToLowerInvariant()} taxable income over $100,000"
            : r.TaxYear == 2025
                ? "how to use the Tax Table to find your tax from taxable income on line 15"
                : $"tax rate table for {CalcLabels.FilingStatus(r.FilingStatus).ToLowerInvariant()}: tax on taxable income";

        var searches = await Task.WhenAll(
            retriever.RetrieveAsync(deductionQuery, r.TaxYear, docs, ct),
            retriever.RetrieveAsync(taxQuery, r.TaxYear, docs, ct));

        var lists = searches.Where(s => s.MeetsThreshold).Select(s => s.Passages.Take(PassagesPerQuery).ToList()).ToList();
        var passages = new List<Chunk>();
        for (var i = 0; i < PassagesPerQuery; i++)
            foreach (var list in lists)
                if (i < list.Count && passages.All(p => p.Id != list[i].Chunk.Id))
                    passages.Add(list[i].Chunk);
        return (passages, searches.Max(s => s.TopScore));
    }

    private static CalcResponse Response(CalcResult r, string kind, string explanation, IReadOnlyList<CitationRef> citations,
        IReadOnlyList<SourcePassage> sources) =>
        new(r.TaxYear, r.FilingStatus, r.CanBeClaimedAsDependent, r.Steps, r.Breakdown, r.Tax, Math.Abs(r.RefundOrOwed), r.IsRefund,
            r.IsEstimate, r.IsEstimate ? CalcLabels.EstimateNote(r.TaxYear) : null, kind, explanation, citations, sources,
            CalcLabels.NotIncluded, AskResponses.Disclaimer);

    // One line per Claude call (BUILD_PLAN §10). No wages or withholding: those are the student's own numbers.
    private void LogCall(ModelCallInfo call, string outcome, int attempt, CalcResult r, bool plain, double topScore,
        ValidationStats? stats = null, int dollarsRejected = 0) =>
        logger.LogInformation(
            "endpoint=calculator model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens} " +
            "cacheReadTokens={CacheReadTokens} stopReason={StopReason} validator={Outcome} attempt={Attempt} " +
            "citationsRemoved={Removed} highlightsMissing={Missing} dollarsRejected={DollarsRejected} topScore={TopScore:F4} " +
            "taxYear={TaxYear} method={Method} plain={Plain}",
            call.Model, call.LatencyMs, call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.StopReason, outcome, attempt,
            stats?.CitationsRemoved ?? 0, stats?.HighlightsMissing ?? 0, dollarsRejected, topScore, r.TaxYear, r.Breakdown.Method, plain);
}
