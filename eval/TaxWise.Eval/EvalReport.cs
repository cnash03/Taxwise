using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxWise.Eval;

/// <summary>Everything one run produced: written as <c>eval/results/YYYY-MM-DD-{mode}.json</c> and re-read by <c>--rejudge</c>.</summary>
public sealed record EvalReport(
    string Mode,
    string Date,
    string AnswerModel,
    string JudgeModel,
    double ConfiguredThreshold,
    IReadOnlyList<QuestionResult> Results)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static EvalReport Load(string path) =>
        JsonSerializer.Deserialize<EvalReport>(File.ReadAllText(path), JsonOptions)
        ?? throw new FormatException($"{path} is not an eval report.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>Renders the markdown report: summary table, threshold sweep, judge spot-check list, per-question rows.</summary>
public static class ReportWriter
{
    private const int SpotCheckCount = 10;

    public static string ToMarkdown(EvalReport report, IReadOnlyList<GoldItem> gold)
    {
        var byId = gold.ToDictionary(g => g.Id);
        var results = report.Results;
        var baseline = report.Mode == EvalModes.Baseline;
        var sweep = baseline ? [] : Metrics.Sweep(results);
        var (chosen, meets) = baseline ? (Metrics.Score(results, 0), true) : Metrics.ChooseThreshold(sweep);
        var correctness = Metrics.ScoreCorrectness(results, byId, chosen.Threshold);

        var sb = new StringBuilder();
        sb.AppendLine($"# TaxWise eval · {report.Mode} · {report.Date}").AppendLine();
        sb.AppendLine($"Answer model `{report.AnswerModel}` · judge `{report.JudgeModel}` · {results.Count} questions " +
                      $"({results.Count(r => r.InScope)} in scope, {results.Count(r => !r.InScope)} out of scope).");
        if (baseline)
            sb.AppendLine("Baseline: same model, same questions, **no retrieval**. A refusal is a `declined` verdict from the judge.");
        sb.AppendLine();

        sb.AppendLine("## Summary").AppendLine();
        sb.AppendLine("| Metric | Value |").AppendLine("|---|---|");
        if (!baseline)
        {
            var hits = results.Where(r => r.HitAt4 is not null).ToList();
            var cites = results.Sum(r => r.CitationsTotal);
            sb.AppendLine($"| Retrieval hit@4 | {Pct(hits.Count(r => r.HitAt4 == true), hits.Count)} ({hits.Count(r => r.HitAt4 == true)}/{hits.Count}) |");
            sb.AppendLine($"| Citation validity (before the validator) | {Pct(results.Sum(r => r.CitationsValid), cites)} ({results.Sum(r => r.CitationsValid)}/{cites}) |");
            sb.AppendLine($"| Chosen `MinRerankScore` | **{F(chosen.Threshold)}**{(meets ? "" : " (no value kept false refusals ≤ 10%)")} · configured: {F(report.ConfiguredThreshold)} |");
        }
        var at = baseline ? "" : $" at {F(chosen.Threshold)}";
        sb.AppendLine($"| Out-of-scope refused (refusal recall){at} | {Pct(chosen.OutOfScopeRefused, chosen.OutOfScope)} ({chosen.OutOfScopeRefused}/{chosen.OutOfScope}) |");
        sb.AppendLine($"| In-scope wrongly refused{at} | {Pct(chosen.InScopeRefused, chosen.InScope)} ({chosen.InScopeRefused}/{chosen.InScope}) |");
        sb.AppendLine($"| Refusal precision{at} | {Pct(chosen.OutOfScopeRefused, chosen.OutOfScopeRefused + chosen.InScopeRefused)} |");
        sb.AppendLine($"| Answer correctness{at} | {Pct(correctness.Correct, correctness.Judged)} correct · {correctness.Partial} partial · " +
                      $"{correctness.Incorrect} incorrect · {correctness.Refused} refused (of {correctness.Judged} judged) |");
        if (correctness.NotJudged > 0)
            sb.AppendLine($"| Not judged yet | {correctness.NotJudged} in-scope questions have no approved `goldAnswer` |");
        var errors = results.Count(r => r.Outcome == Outcomes.Error);
        if (errors > 0) sb.AppendLine($"| Errors | {errors} (counted as refusals) |");
        var lat = results.Select(r => r.LatencyMs).Order().ToList();
        if (lat.Count > 0) sb.AppendLine($"| Latency per question (median / max) | {lat[lat.Count / 2]} ms / {lat[^1]} ms |");
        sb.AppendLine();

        if (!baseline)
        {
            sb.AppendLine("## Threshold sweep").AppendLine();
            sb.AppendLine("Each question was run once with the threshold off; a question counts as refused at a threshold when its top " +
                          "rerank score is below it or the pipeline refused anyway. Target: most out-of-scope refusals with in-scope " +
                          "false refusals ≤ 10%.").AppendLine();
            sb.AppendLine("| MinRerankScore | Out-of-scope refused | …before Claude is called | In-scope wrongly refused | Refusal precision |").AppendLine("|---|---|---|---|---|");
            foreach (var p in sweep)
                sb.AppendLine($"| {(p.Threshold == chosen.Threshold ? $"**{F(p.Threshold)}**" : F(p.Threshold))} | " +
                              $"{Pct(p.OutOfScopeRefused, p.OutOfScope)} ({p.OutOfScopeRefused}/{p.OutOfScope}) | {p.OutOfScopeBlocked}/{p.OutOfScope} | " +
                              $"{Pct(p.InScopeRefused, p.InScope)} ({p.InScopeRefused}/{p.InScope}) | {Pct(p.OutOfScopeRefused, p.OutOfScopeRefused + p.InScopeRefused)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## By persona").AppendLine();
        sb.AppendLine("| Persona | Questions | Refused | Correct / judged |").AppendLine("|---|---|---|---|");
        foreach (var group in results.GroupBy(r => r.Persona))
        {
            var list = group.ToList();
            var c = Metrics.ScoreCorrectness(list, byId, chosen.Threshold);
            sb.AppendLine($"| {group.Key} | {list.Count} | {list.Count(r => Metrics.IsRefused(r, chosen.Threshold))} | " +
                          (c.Judged > 0 ? $"{c.Correct}/{c.Judged}" : "–") + " |");
        }
        sb.AppendLine();

        var spot = SpotCheck(results);
        if (spot.Count > 0)
        {
            sb.AppendLine("## Judge spot-check (human)").AppendLine();
            sb.AppendLine("A person checks these verdicts against the answer and the gold answer, and ticks Agree or notes the disagreement.").AppendLine();
            sb.AppendLine("| Id | Verdict | Judge's reason | Agree? |").AppendLine("|---|---|---|---|");
            foreach (var r in spot)
                sb.AppendLine($"| {r.Id} | {r.Judge!.Verdict} | {Cell(r.Judge.Reason)} | ☐ |");
            sb.AppendLine();
        }

        sb.AppendLine("## Per question").AppendLine();
        sb.AppendLine(baseline
            ? "| Id | Persona | Year | In scope | Outcome | Verdict | Question |"
            : "| Id | Persona | Year | In scope | Top score | hit@4 | Outcome | Citations valid | Verdict | Top chunk | Question |");
        sb.AppendLine(baseline ? "|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var outcome = Metrics.IsRefused(r, chosen.Threshold) && r.Outcome == Outcomes.Answered && !r.PipelineRefused
                ? "below threshold" : r.Outcome;
            var verdict = r.Judge?.Verdict ?? "–";
            if (baseline)
                sb.AppendLine($"| {r.Id} | {r.Persona} | {r.TaxYear} | {YesNo(r.InScope)} | {outcome} | {verdict} | {Cell(r.Question)} |");
            else
                sb.AppendLine($"| {r.Id} | {r.Persona} | {r.TaxYear} | {YesNo(r.InScope)} | {F(r.TopScore)} | " +
                              $"{(r.HitAt4 is null ? "–" : YesNo(r.HitAt4.Value))} | {outcome} | " +
                              $"{(r.CitationsTotal == 0 ? "–" : $"{r.CitationsValid}/{r.CitationsTotal}")} | {verdict} | " +
                              $"{(r.Retrieved.Count > 0 ? $"{r.Retrieved[0].DocId} p{r.Retrieved[0].PageStart}" : "–")} | {Cell(r.Question)} |");
        }
        return sb.ToString();
    }

    /// <summary>Up to 10 judged in-scope questions spread evenly over the run, so the human spot-check isn't cherry-picked.</summary>
    public static IReadOnlyList<QuestionResult> SpotCheck(IReadOnlyList<QuestionResult> results)
    {
        var judged = results.Where(r => r.InScope && r.Judge is not null).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        if (judged.Count <= SpotCheckCount) return judged;
        return Enumerable.Range(0, SpotCheckCount).Select(i => judged[i * judged.Count / SpotCheckCount]).ToList();
    }

    private static string Pct(int n, int d) => d == 0 ? "–" : (100.0 * n / d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string F(double d) => d.ToString("0.00##", CultureInfo.InvariantCulture);
    private static string YesNo(bool b) => b ? "yes" : "no";
    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
