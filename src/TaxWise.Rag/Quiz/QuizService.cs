using Microsoft.Extensions.Logging;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.VectorStore;
using TaxWise.Shared;

namespace TaxWise.Rag.Quiz;

/// <summary>
/// Learning quizzes (BUILD_PLAN §7, Phase 6): look up the chunks cited in chat, fill up to the requested count with the
/// reranked neighbours of the topic, then write one question per passage, at most <see cref="MaxParallelCalls"/> Claude
/// calls at a time. Each question must pass <see cref="QuizItemValidator"/>; a failing one is regenerated once and then
/// dropped. PdfUrls in the response are relative; the API makes them absolute.
/// </summary>
public sealed class QuizService(IChunkStore store, IRetriever retriever, IQuizWriter writer, ILogger<QuizService> logger)
{
    public const int MaxParallelCalls = 5;
    private const int MaxAttempts = 2;
    private const string Unreadable = "unreadable_json";

    public async Task<QuizResponse> CreateAsync(QuizRequest request, CancellationToken ct = default)
    {
        var passages = await PassagesAsync(request, ct);

        // A failure that isn't about one question (rate limit, API down) stops the other calls too.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(MaxParallelCalls);
        var items = await Task.WhenAll(passages.Select(p => ItemAsync(request, p, gate, cts)));
        return new QuizResponse(request.Topic, request.TaxYear, items.OfType<QuizItem>().ToList(), AskResponses.Disclaimer);
    }

    /// <summary>The cited chunks in the order sent, then the topic's reranked neighbours that aren't already in, up to the count.</summary>
    private async Task<List<Chunk>> PassagesAsync(QuizRequest request, CancellationToken ct)
    {
        var ids = request.ChunkIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(QuizLimits.MaxChunkIds).ToList();
        var passages = (await store.GetAsync(ids, ct)).Take(request.Count).ToList();
        if (passages.Count >= request.Count) return passages;

        var found = passages.Count;
        var retrieval = await retriever.RetrieveAsync(request.Topic, request.TaxYear, ct: ct);
        foreach (var p in retrieval.Passages)
        {
            if (passages.Count >= request.Count) break;
            if (passages.All(c => c.Id != p.Chunk.Id)) passages.Add(p.Chunk);
        }
        logger.LogInformation("endpoint=quiz cited={Cited} found={Found} filledFromTopic={Filled} topScore={TopScore:F4} taxYear={TaxYear}",
            ids.Count, found, passages.Count - found, retrieval.TopScore, request.TaxYear);
        return passages;
    }

    private async Task<QuizItem?> ItemAsync(QuizRequest request, Chunk passage, SemaphoreSlim gate, CancellationTokenSource cts)
    {
        await gate.WaitAsync(cts.Token);
        try
        {
            string? failed = null;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var result = await writer.WriteAsync(request.Topic, passage, request.PlainLanguage, failed, cts.Token);
                if (result.ModelRefused)
                {
                    LogCall(result.Call, "model_refusal", attempt, passage, request);
                    return null;
                }

                var check = result.Draft is null ? new QuizCheck(Unreadable, null) : QuizItemValidator.Validate(result.Draft, passage.Text);
                LogCall(result.Call, check.Outcome, attempt, passage, request);
                if (check.IsValid)
                {
                    var d = result.Draft!;
                    return new QuizItem(passage.Id, d.Question!.Trim(), d.Options!.Select(o => o.Trim()).ToList(), d.CorrectIndex,
                        d.Explanation!.Trim(), check.Quote!, AskService.ToSource(1, passage));
                }
                failed = check.Outcome;
            }
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await cts.CancelAsync();
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    // One line per Claude call (BUILD_PLAN §10). The chunk ID names a public IRS passage; no topic or question text.
    private void LogCall(ModelCallInfo call, string outcome, int attempt, Chunk passage, QuizRequest request) =>
        logger.LogInformation(
            "endpoint=quiz model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} outputTokens={OutputTokens} " +
            "cacheReadTokens={CacheReadTokens} stopReason={StopReason} validator={Outcome} attempt={Attempt} chunkId={ChunkId} " +
            "taxYear={TaxYear} plain={Plain}",
            call.Model, call.LatencyMs, call.InputTokens, call.OutputTokens, call.CacheReadTokens, call.StopReason, outcome, attempt,
            passage.Id, request.TaxYear, request.PlainLanguage);
}
