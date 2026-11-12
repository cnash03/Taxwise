using Microsoft.Extensions.Logging.Abstractions;
using TaxWise.Core;
using TaxWise.Core.Calculator;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Calculator;
using TaxWise.Rag.Tests.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Calculator;

public sealed class CalculatorServiceTests
{
    // Slide 11 scenario: TY2025, single, $28,000 wages, $1,500 withheld → taxable $12,250, tax $1,235, refund $265.
    private static readonly CalcRequest Slide11 = new(2025, 28_000m, 1_500m, FilingStatus.Single, false);
    private static readonly ITaxYearRules Rules2025 = TaxYearRegistry.Get(2025)!;

    private const string GoodText = "Your $15,750 standard deduction leaves $12,250 of taxable income, and the Tax Table tax for that row is $1,235. " +
                                    "Since $1,500 was withheld, you get $265 back.";

    private static RetrievalResult Kept(double score, int first) =>
        new(8, Enumerable.Range(first, 4).Select(i => new RetrievedPassage(TestChunks.Make(i), score)).ToList(), score, score >= 0.35);

    private static DraftAnswer Explained(string text, params (string P, string Quote)[] citations) =>
        new DraftExplanation(DraftAnswer.Answered, text, citations.Select(c => new DraftCitation(c.P, c.Quote)).ToList()).ToDraftAnswer();

    private static CalculatorService MakeService(IRetriever retriever, ICalcExplainer explainer) =>
        new(retriever, explainer, NullLogger<CalculatorService>.Instance);

    [Fact]
    public async Task ValidExplanation_IsReturnedWithCitations_AndInterleavedPassages()
    {
        // Deduction search returns chunks 1–4, tax search 11–14: P1..P4 = 1, 11, 2, 12.
        var retriever = new QueryRetriever(q => q.Contains("standard deduction") ? Kept(0.8, 1) : Kept(0.7, 11));
        var explainer = new ScriptedExplainer(Explained(GoodText, ("P2", "Passage 11 text.")));

        var response = await MakeService(retriever, explainer).CalculateAsync(Rules2025, Slide11);

        Assert.Equal(CalcResponse.AnswerKind, response.ExplanationKind);
        Assert.Equal(GoodText, response.Explanation);
        Assert.Equal([TestChunks.Make(1).Id, TestChunks.Make(11).Id, TestChunks.Make(2).Id, TestChunks.Make(12).Id],
            response.Sources.Select(s => s.ChunkId));
        var c = Assert.Single(response.Citations);
        Assert.Equal((2, "Passage 11 text."), (c.Number, c.Highlight));
        Assert.Equal(1_235m, response.Tax);
        Assert.Equal(265m, response.RefundOrOwed);
        Assert.True(response.IsRefund);
        Assert.False(response.IsEstimate);
        Assert.Null(response.EstimateNote);
        Assert.Equal(1, explainer.Calls);
        Assert.All(retriever.Calls, call => Assert.Equal(2025, call.TaxYear));
        Assert.All(retriever.Calls, call => Assert.Contains("i1040", call.DocIds!));
    }

    [Fact]
    public async Task MadeUpAmount_RetriesOnceWithTheRejectedAmounts_ThenAcceptsTheFix()
    {
        var explainer = new ScriptedExplainer(
            Explained("Your tax is $1,234.00.", ("P1", "Passage 1 text.")),
            Explained(GoodText, ("P1", "Passage 1 text.")));

        var response = await MakeService(new QueryRetriever(_ => Kept(0.8, 1)), explainer).CalculateAsync(Rules2025, Slide11);

        Assert.Equal(2, explainer.Calls);
        Assert.Equal(["$1,234.00"], explainer.Rejected[1]!);
        Assert.Null(explainer.Rejected[0]);
        Assert.Equal(CalcResponse.AnswerKind, response.ExplanationKind);
        Assert.Equal(GoodText, response.Explanation);
    }

    [Fact]
    public async Task MadeUpAmountTwice_ShowsTheCannedExplanation_AndKeepsTheNumbers()
    {
        var bad = Explained("The exact tax is $1,231.50.", ("P1", "Passage 1 text."));
        var explainer = new ScriptedExplainer(bad, bad);

        var response = await MakeService(new QueryRetriever(_ => Kept(0.8, 1)), explainer).CalculateAsync(Rules2025, Slide11);

        Assert.Equal(2, explainer.Calls);
        Assert.Equal(CalcResponse.FallbackKind, response.ExplanationKind);
        Assert.Empty(response.Citations);
        Assert.Empty(response.Sources);
        Assert.Contains("$1,235", response.Explanation);
        Assert.DoesNotContain("1,231.50", response.Explanation);
        Assert.Equal(1_235m, response.Tax);
        Assert.Equal(6, response.Steps.Count);
    }

    [Fact]
    public async Task NoSourcesAboveThreshold_SkipsClaude_AndUsesTheCannedExplanation()
    {
        var explainer = new ScriptedExplainer();

        var response = await MakeService(new QueryRetriever(_ => Kept(0.1, 1)), explainer).CalculateAsync(Rules2025, Slide11);

        Assert.Equal(0, explainer.Calls);
        Assert.Equal(CalcResponse.FallbackKind, response.ExplanationKind);
        Assert.Equal(1_235m, response.Tax);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsufficientSourcesOrModelRefusal_FallsBackWithoutRetry(bool refuse)
    {
        var explainer = refuse
            ? new ScriptedExplainer(refuse: true)
            : new ScriptedExplainer(new DraftExplanation(DraftAnswer.InsufficientSources, "", []).ToDraftAnswer());

        var response = await MakeService(new QueryRetriever(_ => Kept(0.8, 1)), explainer).CalculateAsync(Rules2025, Slide11);

        Assert.Equal(1, explainer.Calls);
        Assert.Equal(CalcResponse.FallbackKind, response.ExplanationKind);
    }

    [Fact]
    public async Task LaterYears_SearchTheWholeYear_AndAreLabelledEstimates()
    {
        var retriever = new QueryRetriever(_ => Kept(0.1, 1));

        var response = await MakeService(retriever, new ScriptedExplainer())
            .CalculateAsync(TaxYearRegistry.Get(2026)!, Slide11 with { TaxYear = 2026 });

        Assert.All(retriever.Calls, call => Assert.Equal((2026, (IReadOnlyCollection<string>?)null), (call.TaxYear, call.DocIds)));
        Assert.True(response.IsEstimate);
        Assert.Equal("Estimate: official 2026 Tax Table not yet published", response.EstimateNote);
    }

    [Fact]
    public void Compare_RunsEverySupportedYear_WithoutClaude()
    {
        var response = CalculatorService.Compare(Slide11 with { FederalWithholding = 0 });

        Assert.Equal([2025, 2026, 2027], response.Years.Select(y => y.TaxYear));
        Assert.Equal([15_750m, 16_100m, 16_600m], response.Years.Select(y => y.StandardDeduction));
        Assert.Equal(1_235m, response.Years[0].Tax);
        Assert.All(response.Years, y => Assert.False(y.IsRefund && y.RefundOrOwed > 0));
        Assert.Equal([false, true, true], response.Years.Select(y => y.IsEstimate));
    }

    [Fact]
    public void CannedExplanation_PassesTheDollarGuardrail()
    {
        foreach (var (wages, withheld, dependent) in new[] { (28_000m, 1_500m, false), (4_000m, 0m, true), (150_000m, 9_000m, false), (40_000m, 3_200m, false) })
        {
            var r = TaxCalculator.Calculate(Rules2025, wages, withheld, FilingStatus.Single, dependent);
            Assert.True(DollarAmountValidator.Validate(CalcNarrative.Canned(r), r.KnownAmounts).IsValid, CalcNarrative.Canned(r));
        }
    }

    private sealed class QueryRetriever(Func<string, RetrievalResult> respond) : IRetriever
    {
        public List<(string Query, int TaxYear, IReadOnlyCollection<string>? DocIds)> Calls { get; } = [];

        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default)
        {
            lock (Calls) Calls.Add((question, taxYear, docIds));
            return Task.FromResult(respond(question));
        }
    }

    /// <summary>Returns the drafts in order, one per call; records what each call was told was rejected.</summary>
    private sealed class ScriptedExplainer(params DraftAnswer[] drafts) : ICalcExplainer
    {
        private readonly bool _refuse;

        public ScriptedExplainer(bool refuse) : this() => _refuse = refuse;

        public int Calls => Rejected.Count;
        public List<IReadOnlyList<string>?> Rejected { get; } = [];

        public Task<AnswererResult> ExplainAsync(CalcResult result, IReadOnlyList<Chunk> passages, bool plainLanguage,
            IReadOnlyList<string>? rejectedAmounts = null, CancellationToken ct = default)
        {
            var draft = _refuse ? null : drafts[Rejected.Count];
            Rejected.Add(rejectedAmounts);
            return Task.FromResult(new AnswererResult(draft, _refuse, new ModelCallInfo("fake", 1, 1, 1, 0, _refuse ? "refusal" : "end_turn")));
        }
    }
}
