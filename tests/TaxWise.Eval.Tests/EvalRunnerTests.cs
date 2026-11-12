using Microsoft.Extensions.Logging.Abstractions;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;

namespace TaxWise.Eval.Tests;

public class EvalRunnerTests
{
    private static readonly ModelCallInfo Call = new("fake", 1, 1, 1, 0, "end_turn");

    private static Chunk Chunk(string doc, int page, string text) =>
        new($"{doc}-ty2025-p{page}-001", doc, "Pub 970, Tax Benefits for Education", 2025, null, "S", page, page, text);

    private sealed class FakeRetriever(double top, params Chunk[] chunks) : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default) =>
            Task.FromResult(new RetrievalResult(8, chunks.Select(c => new RetrievedPassage(c, top)).ToList(), top, top >= 0.35));
    }

    private sealed class FakeAnswerer(DraftAnswer draft) : IAnswerer
    {
        public int Calls;
        public Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new AnswererResult(draft, false, Call));
        }
    }

    private sealed class FakeBaseline(string answer) : IBaselineAnswerer
    {
        public Task<(string? Answer, bool Refused, ModelCallInfo Call)> AnswerAsync(string question, int taxYear, CancellationToken ct = default) =>
            Task.FromResult<(string?, bool, ModelCallInfo)>((answer, false, Call));
    }

    private sealed class FakeJudge(string verdict) : IJudge
    {
        public List<string> GoldSeen { get; } = [];
        public Task<JudgeVerdict> JudgeAsync(string question, int taxYear, string goldAnswer, string candidate, CancellationToken ct = default)
        {
            lock (GoldSeen) GoldSeen.Add(goldAnswer);
            return Task.FromResult(new JudgeVerdict(verdict, "because"));
        }
    }

    private static readonly DraftAnswer Draft = new(DraftAnswer.Answered,
        [new DraftParagraph("It's tax free.", [new DraftCitation("P1", "Scholarships can be tax free."), new DraftCitation("P9", "nope")])]);

    private static EvalRunner Runner(IRetriever r, IAnswerer a, IJudge j, string baseline = "Sure, here's how.") =>
        new(r, a, new FakeBaseline(baseline), j, NullLogger<EvalRunner>.Instance);

    [Fact]
    public async Task TaxWise_mode_calls_Claude_even_below_the_threshold_and_records_metrics()
    {
        var answerer = new FakeAnswerer(Draft);
        var judge = new FakeJudge(Verdicts.Correct);
        var runner = Runner(new FakeRetriever(0.1, Chunk("p970", 6, "Scholarships can be tax free.")), answerer, judge);
        GoldItem[] gold =
        [
            new("q01", "scholarship", true, "Q?", ["p970"], [6], "Yes, if it pays tuition.", GoldItem.Approved),
            new("q03", "scholarship", true, "Q3?", ["p970"], [6], "Drafted, not approved.", GoldItem.Draft), // not judged
            new("q02", "scholarship", true, "Q2?", ["p970"], [6]), // no gold answer yet: not judged
        ];

        var results = await runner.RunAsync(EvalModes.TaxWise, gold, concurrency: 2);

        Assert.Equal(3, answerer.Calls);
        var r = results.Single(x => x.Id == "q01");
        Assert.Equal(Outcomes.Answered, r.Outcome);
        Assert.Equal(0.1, r.TopScore);
        Assert.True(r.HitAt4);
        Assert.Equal((2, 1), (r.CitationsTotal, r.CitationsValid));
        Assert.Equal(Verdicts.Correct, r.Judge?.Verdict);
        Assert.True(Metrics.IsRefused(r, 0.35));
        Assert.False(Metrics.IsRefused(r, 0.05));
        Assert.Null(results.Single(x => x.Id == "q02").Judge);
        Assert.Null(results.Single(x => x.Id == "q03").Judge);
    }

    [Fact]
    public async Task Baseline_mode_judges_out_of_scope_answers_with_an_empty_reference()
    {
        var judge = new FakeJudge(Verdicts.Declined);
        var runner = Runner(new FakeRetriever(0.9), new FakeAnswerer(Draft), judge);

        var results = await runner.RunAsync(EvalModes.Baseline,
            [new GoldItem("o01", "out-of-scope", false, "How do I stake crypto?", [], [])], concurrency: 1);

        Assert.Equal([""], judge.GoldSeen);
        Assert.True(results[0].PipelineRefused);
        Assert.Empty(results[0].Retrieved);
    }

    [Fact]
    public async Task Rejudge_adds_a_verdict_once_a_gold_answer_exists_and_keeps_existing_ones()
    {
        var judge = new FakeJudge(Verdicts.Partial);
        var runner = Runner(new FakeRetriever(0.9), new FakeAnswerer(Draft), judge);
        var unjudged = MetricsTests.Result("q01", true, 0.9);
        var gold = new GoldItem("q01", "p", true, "Q?", ["p970"], [6], "Now written.", GoldItem.Approved);

        var judged = await runner.JudgeAsync(unjudged, gold);
        var again = await runner.JudgeAsync(judged with { Judge = new JudgeVerdict(Verdicts.Correct, "kept") }, gold);

        Assert.Equal(Verdicts.Partial, judged.Judge?.Verdict);
        Assert.Equal("kept", again.Judge?.Reason);
        Assert.Single(judge.GoldSeen);
    }

    [Fact]
    public void Markdown_report_includes_sweep_and_chosen_threshold()
    {
        var report = new EvalReport(EvalModes.TaxWise, "2026-11-12", "m", "j", 0.35,
            [MetricsTests.Result("q01", true, 0.8), MetricsTests.Result("o01", false, 0.25)]);
        var md = ReportWriter.ToMarkdown(report,
            [new GoldItem("q01", "scholarship", true, "Q?", ["p970"], [6]), new GoldItem("o01", "out-of-scope", false, "Q?", [], [])]);

        Assert.Contains("## Threshold sweep", md);
        Assert.Contains("| Chosen `MinRerankScore` | **0.30**", md);
        Assert.Contains("| o01 |", md);
    }
}
