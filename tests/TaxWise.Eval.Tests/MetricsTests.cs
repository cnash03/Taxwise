using TaxWise.Core.Guardrails;

namespace TaxWise.Eval.Tests;

public class MetricsTests
{
    private static GoldItem Gold(string id = "q01", bool inScope = true, string[]? docs = null, int[]? pages = null, string gold = "") =>
        new(id, "scholarship", inScope, "Is my scholarship taxable?", docs ?? ["p970"], pages ?? [6], gold, gold == "" ? GoldItem.Pending : GoldItem.Approved);

    private static RetrievedRef Ref(string doc, int start, int end = 0) => new($"{doc}-p{start}", doc, start, end == 0 ? start : end, 0.5);

    internal static QuestionResult Result(string id, bool inScope, double top, string outcome = Outcomes.Answered, JudgeVerdict? judge = null) =>
        new(id, "p", inScope, 2025, "q", top, [], null, outcome, 0, 0, outcome == Outcomes.Answered ? "answer" : null, judge, 10);

    [Theory]
    [InlineData("p970", 6, 6, true)]   // exact page
    [InlineData("p970", 7, 7, true)]   // +1
    [InlineData("p970", 5, 5, true)]   // -1
    [InlineData("p970", 8, 9, false)]  // two pages away
    [InlineData("p970", 2, 4, false)]
    [InlineData("p970", 3, 5, true)]   // range ends within 1
    [InlineData("p17", 6, 6, false)]   // wrong doc
    public void HitAt4_checks_doc_and_page_within_one(string doc, int start, int end, bool expected) =>
        Assert.Equal(expected, Metrics.HitAt4(Gold(), [Ref(doc, start, end)]));

    [Fact]
    public void HitAt4_only_looks_at_the_top_four()
    {
        var refs = new[] { Ref("p17", 1), Ref("p17", 2), Ref("p17", 3), Ref("p17", 4), Ref("p970", 6) };
        Assert.False(Metrics.HitAt4(Gold(), refs));
    }

    [Fact]
    public void HitAt4_is_null_out_of_scope_and_any_page_when_no_pages_given()
    {
        Assert.Null(Metrics.HitAt4(Gold(inScope: false, docs: []), [Ref("p970", 6)]));
        Assert.True(Metrics.HitAt4(Gold(pages: []), [Ref("p970", 40)]));
    }

    [Fact]
    public void CitationValidity_counts_bad_ids_and_missing_quotes_before_the_validator_drops_them()
    {
        var draft = new DraftAnswer(DraftAnswer.Answered,
        [
            new DraftParagraph("A", [new DraftCitation("P1", "Scholarships can be tax free."), new DraftCitation("P7", "x")]),
            new DraftParagraph("B", [new DraftCitation("P2", "Not in the passage.")]),
        ]);
        var (total, valid) = Metrics.CitationValidity(draft, ["Scholarships can be tax free. More text.", "Other passage."]);
        Assert.Equal(3, total);
        Assert.Equal(1, valid);
    }

    [Fact]
    public void Sweep_counts_below_threshold_and_pipeline_refusals()
    {
        QuestionResult[] results =
        [
            Result("i1", true, 0.80),
            Result("i2", true, 0.32),                                     // refused from 0.35 up
            Result("i3", true, 0.90, ValidationStats.InsufficientSources), // always refused
            Result("o1", false, 0.10),
            Result("o2", false, 0.33),
            Result("o3", false, 0.60, judge: new JudgeVerdict(Verdicts.Declined, "")),
        ];
        var sweep = Metrics.Sweep(results);

        var at02 = sweep.Single(p => p.Threshold == 0.2);
        Assert.Equal((2, 1), (at02.OutOfScopeRefused, at02.InScopeRefused));
        var at035 = sweep.Single(p => p.Threshold == 0.35);
        Assert.Equal((3, 2), (at035.OutOfScopeRefused, at035.InScopeRefused));
        Assert.Equal(1.0, at035.OutOfScopeRefusalRate);
        Assert.Equal(0.6, at035.RefusalPrecision, 3);
        Assert.Equal(2, at035.OutOfScopeBlocked); // o3 was declined by the model, not blocked
    }

    [Fact]
    public void ChooseThreshold_maximizes_out_of_scope_refusal_within_ten_percent_false_refusals()
    {
        SweepPoint[] sweep =
        [
            new(0.2, 10, 5, 40, 0),
            new(0.3, 10, 8, 40, 2),
            new(0.35, 10, 8, 40, 4),  // same recall as 0.3, more false refusals
            new(0.4, 10, 9, 40, 6),   // 15%: over the bar
        ];
        var (point, meets) = Metrics.ChooseThreshold(sweep);
        Assert.True(meets);
        Assert.Equal(0.3, point.Threshold);
    }

    [Fact]
    public void ChooseThreshold_breaks_full_ties_toward_blocking_before_Claude()
    {
        SweepPoint[] sweep = [new(0.2, 10, 10, 40, 0, OutOfScopeBlocked: 2), new(0.35, 10, 10, 40, 0, OutOfScopeBlocked: 7), new(0.4, 10, 10, 40, 0, OutOfScopeBlocked: 7)];
        Assert.Equal(0.35, Metrics.ChooseThreshold(sweep).Point.Threshold);
    }

    [Fact]
    public void ChooseThreshold_falls_back_to_fewest_false_refusals_when_none_meet_the_bar()
    {
        SweepPoint[] sweep = [new(0.2, 10, 9, 10, 3), new(0.3, 10, 10, 10, 2)];
        var (point, meets) = Metrics.ChooseThreshold(sweep);
        Assert.False(meets);
        Assert.Equal(0.3, point.Threshold);
    }

    [Fact]
    public void ScoreCorrectness_counts_refusals_against_and_skips_questions_without_gold()
    {
        var gold = new Dictionary<string, GoldItem>
        {
            ["a"] = Gold("a", gold: "Yes."), ["b"] = Gold("b", gold: "No."), ["c"] = Gold("c", gold: "Maybe."), ["d"] = Gold("d"),
        };
        QuestionResult[] results =
        [
            Result("a", true, 0.9, judge: new JudgeVerdict(Verdicts.Correct, "")),
            Result("b", true, 0.9, judge: new JudgeVerdict(Verdicts.Partial, "")),
            Result("c", true, 0.1, judge: new JudgeVerdict(Verdicts.Correct, "")), // below the threshold: refused
            Result("d", true, 0.9),
        ];
        var c = Metrics.ScoreCorrectness(results, gold, 0.35);
        Assert.Equal(new Correctness(Judged: 3, Correct: 1, Partial: 1, Incorrect: 0, Refused: 1, NotJudged: 1), c);
        Assert.Equal(1 / 3.0, c.Rate, 3);
    }
}
