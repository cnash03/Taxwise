using TaxWise.Core.Guardrails;

namespace TaxWise.Eval;

/// <summary>Refusal rates at one <c>MinRerankScore</c>. Rates are 0–1; a set with no questions gives 0.</summary>
public sealed record SweepPoint(
    double Threshold,
    int OutOfScope, int OutOfScopeRefused,
    int InScope, int InScopeRefused,
    int OutOfScopeBlocked = 0)
{
    // OutOfScopeBlocked: out-of-scope questions refused by the threshold itself, before Claude would be called.

    /// <summary>Refusal recall: the share of out-of-scope questions refused.</summary>
    public double OutOfScopeRefusalRate => Rate(OutOfScopeRefused, OutOfScope);
    /// <summary>The share of in-scope questions wrongly refused.</summary>
    public double InScopeFalseRefusalRate => Rate(InScopeRefused, InScope);
    /// <summary>Refusal precision: of everything refused, the share that was out of scope.</summary>
    public double RefusalPrecision => Rate(OutOfScopeRefused, OutOfScopeRefused + InScopeRefused);

    internal static double Rate(int n, int d) => d == 0 ? 0 : (double)n / d;
}

/// <summary>Answer correctness at one threshold, over in-scope questions that have a gold answer.</summary>
public sealed record Correctness(int Judged, int Correct, int Partial, int Incorrect, int Refused, int NotJudged)
{
    /// <summary>Correct / judged. A refused in-scope question counts against it.</summary>
    public double Rate => SweepPoint.Rate(Correct, Judged);
}

/// <summary>The pure scoring functions for the eval report (BUILD_PLAN §8 metrics table).</summary>
public static class Metrics
{
    public static readonly double[] SweepThresholds = [0.2, 0.3, 0.35, 0.4, 0.5];
    public const double MaxFalseRefusalRate = 0.10;
    public const int PageTolerance = 1;

    /// <summary>
    /// Retrieval hit@4: at least one retrieved chunk is from an expected doc and its page range comes within
    /// ±<see cref="PageTolerance"/> of an expected page. No expected pages means any page of an expected doc counts.
    /// Null when the question has no expected docs (out of scope).
    /// </summary>
    public static bool? HitAt4(GoldItem gold, IReadOnlyList<RetrievedRef> retrieved)
    {
        if (gold.ExpectedDocs.Count == 0) return null;
        return retrieved.Take(4).Any(r =>
            gold.ExpectedDocs.Contains(r.DocId, StringComparer.OrdinalIgnoreCase) &&
            (gold.ExpectedPages.Count == 0 ||
             gold.ExpectedPages.Any(p => r.PageStart - PageTolerance <= p && p <= r.PageEnd + PageTolerance)));
    }

    /// <summary>
    /// Citations in Claude's draft <em>before</em> the validator drops any: (total, valid). A citation is valid when
    /// its passage is one that was sent and its quote is found in that passage.
    /// </summary>
    public static (int Total, int Valid) CitationValidity(DraftAnswer draft, IReadOnlyList<string> passageTexts)
    {
        int total = 0, valid = 0;
        foreach (var c in draft.Paragraphs.SelectMany(p => p.Citations))
        {
            total++;
            var n = CitationValidator.ParsePassage(c.Passage, passageTexts.Count);
            if (n is not null && CitationValidator.FindQuote(passageTexts[n.Value - 1], c.Quote) is not null) valid++;
        }
        return (total, valid);
    }

    /// <summary>Refused at this threshold: below it (Claude wouldn't have been called), or the pipeline refused anyway.</summary>
    public static bool IsRefused(QuestionResult r, double threshold) => r.PipelineRefused || r.TopScore < threshold;

    public static SweepPoint Score(IReadOnlyList<QuestionResult> results, double threshold)
    {
        var oos = results.Where(r => !r.InScope).ToList();
        var ins = results.Where(r => r.InScope).ToList();
        return new SweepPoint(threshold,
            oos.Count, oos.Count(r => IsRefused(r, threshold)),
            ins.Count, ins.Count(r => IsRefused(r, threshold)),
            oos.Count(r => r.TopScore < threshold));
    }

    public static IReadOnlyList<SweepPoint> Sweep(IReadOnlyList<QuestionResult> results, IEnumerable<double>? thresholds = null) =>
        (thresholds ?? SweepThresholds).Select(t => Score(results, t)).ToList();

    /// <summary>
    /// The threshold that refuses the most out-of-scope questions while wrongly refusing at most 10% of in-scope ones;
    /// ties go to fewer false refusals. When no point meets the 10% bar, the one with the fewest
    /// false refusals is returned and <c>MeetsTarget</c> is false. Remaining ties go to the threshold that blocks more
    /// out-of-scope questions before Claude is called (cheaper, and doesn't lean on the model to refuse).
    /// </summary>
    public static (SweepPoint Point, bool MeetsTarget) ChooseThreshold(IReadOnlyList<SweepPoint> sweep)
    {
        var ok = sweep.Where(p => p.InScopeFalseRefusalRate <= MaxFalseRefusalRate + 1e-9).ToList();
        if (ok.Count > 0)
            return (ok.OrderByDescending(p => p.OutOfScopeRefusalRate)
                      .ThenBy(p => p.InScopeFalseRefusalRate)
                      .ThenByDescending(p => p.OutOfScopeBlocked)
                      .ThenBy(p => p.Threshold).First(), true);
        return (sweep.OrderBy(p => p.InScopeFalseRefusalRate).ThenByDescending(p => p.OutOfScopeBlocked)
                     .ThenBy(p => p.Threshold).First(), false);
    }

    /// <summary>Answer correctness at a threshold, over in-scope questions with a gold answer.</summary>
    public static Correctness ScoreCorrectness(IReadOnlyList<QuestionResult> results, IReadOnlyDictionary<string, GoldItem> gold,
        double threshold)
    {
        int judged = 0, correct = 0, partial = 0, incorrect = 0, refused = 0, notJudged = 0;
        foreach (var r in results.Where(r => r.InScope))
        {
            if (!gold.TryGetValue(r.Id, out var g) || !g.HasGoldAnswer) { notJudged++; continue; }
            if (IsRefused(r, threshold)) { judged++; refused++; continue; }
            switch (r.Judge?.Verdict)
            {
                case Verdicts.Correct: judged++; correct++; break;
                case Verdicts.Partial: judged++; partial++; break;
                case Verdicts.Incorrect: judged++; incorrect++; break;
                default: notJudged++; break;
            }
        }
        return new Correctness(judged, correct, partial, incorrect, refused, notJudged);
    }
}
