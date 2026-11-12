using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;

namespace TaxWise.Eval;

public static class EvalModes
{
    public const string TaxWise = "taxwise";
    public const string Baseline = "baseline";
}

/// <summary>
/// Runs every gold question once (BUILD_PLAN §8). TaxWise mode mirrors <see cref="AskService"/> but ignores the
/// rerank threshold, so the threshold sweep can be scored afterwards from the saved top scores. Errors on one question
/// are recorded and don't stop the run.
/// </summary>
public sealed class EvalRunner(
    IRetriever retriever, IAnswerer answerer, IBaselineAnswerer baseline, IJudge judge, ILogger<EvalRunner> logger)
{
    public async Task<IReadOnlyList<QuestionResult>> RunAsync(string mode, IReadOnlyList<GoldItem> gold, int concurrency,
        IProgress<QuestionResult>? progress = null, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = gold.Select(async g =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var r = mode == EvalModes.Baseline ? await RunBaselineAsync(g, ct) : await RunTaxWiseAsync(g, ct);
                r = await JudgeAsync(r, g, ct);
                progress?.Report(r);
                return r;
            }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Judges an answered question when there's something to compare against: a gold answer (in scope), or, in baseline
    /// mode, an out-of-scope question (the judge decides whether the chatbot declined). Keeps any existing verdict.
    /// </summary>
    public async Task<QuestionResult> JudgeAsync(QuestionResult r, GoldItem g, CancellationToken ct = default)
    {
        if (r.Judge is not null || r.Outcome != Outcomes.Answered || r.AnswerText is null) return r;
        var baselineOos = !g.InScope && r.Retrieved.Count == 0;
        if (!g.HasGoldAnswer && !baselineOos) return r;
        try
        {
            var verdict = await judge.JudgeAsync(g.Question, g.TaxYear, g.InScope ? g.GoldAnswer : "", r.AnswerText, ct);
            return r with { Judge = verdict };
        }
        catch (AnswererException e)
        {
            logger.LogWarning("Judge failed on {Id}: {Error}", r.Id, e.Message);
            return r;
        }
    }

    private async Task<QuestionResult> RunTaxWiseAsync(GoldItem g, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var retrieval = await retriever.RetrieveAsync(g.Question, g.TaxYear, ct: ct);
            var retrieved = retrieval.Passages
                .Select(p => new RetrievedRef(p.Chunk.Id, p.Chunk.DocId, p.Chunk.PageStart, p.Chunk.PageEnd, p.RerankScore))
                .ToList();
            var hit = Metrics.HitAt4(g, retrieved);
            if (retrieved.Count == 0)
                return Result(g, 0, retrieved, hit, Outcomes.NoHits, 0, 0, null, sw);

            var chunks = retrieval.Passages.Select(p => p.Chunk).ToList();
            var texts = chunks.Select(c => c.Text).ToList();
            var answer = await answerer.AnswerAsync(g.Question, chunks, g.TaxYear, plainLanguage: false, ct);
            if (answer.ModelRefused || answer.Draft is null)
                return Result(g, retrieval.TopScore, retrieved, hit, Outcomes.ModelRefusal, 0, 0, null, sw);

            var (total, valid) = Metrics.CitationValidity(answer.Draft, texts);
            var validated = CitationValidator.Validate(answer.Draft, texts);
            var text = validated.IsRefusal ? null : string.Join("\n\n", validated.Paragraphs.Select(p => p.Text));
            return Result(g, retrieval.TopScore, retrieved, hit, validated.IsRefusal ? validated.Stats.Outcome : Outcomes.Answered,
                total, valid, text, sw);
        }
        catch (Exception e) when (e is AnswererException or HttpRequestException or Rag.Voyage.VoyageApiException
                                       or Grpc.Core.RpcException)
        {
            logger.LogWarning("Question {Id} failed: {Error}", g.Id, e.Message);
            return Result(g, 0, [], null, Outcomes.Error, 0, 0, null, sw, e.Message);
        }
    }

    private async Task<QuestionResult> RunBaselineAsync(GoldItem g, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (text, refused, _) = await baseline.AnswerAsync(g.Question, g.TaxYear, ct);
            return Result(g, 0, [], null, refused ? Outcomes.ModelRefusal : Outcomes.Answered, 0, 0, text, sw);
        }
        catch (AnswererException e)
        {
            logger.LogWarning("Question {Id} failed: {Error}", g.Id, e.Message);
            return Result(g, 0, [], null, Outcomes.Error, 0, 0, null, sw, e.Message);
        }
    }

    private static QuestionResult Result(GoldItem g, double topScore, IReadOnlyList<RetrievedRef> retrieved, bool? hit,
        string outcome, int citations, int valid, string? text, Stopwatch sw, string? error = null) =>
        new(g.Id, g.Persona, g.InScope, g.TaxYear, g.Question, topScore, retrieved, hit, outcome, citations, valid, text,
            null, sw.ElapsedMilliseconds, error);
}
