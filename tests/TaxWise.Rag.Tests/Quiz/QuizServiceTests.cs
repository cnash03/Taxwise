using Microsoft.Extensions.Logging.Abstractions;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Quiz;
using TaxWise.Rag.Tests.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Quiz;

public sealed class QuizServiceTests
{
    private static readonly IReadOnlyList<Chunk> Stored = Enumerable.Range(1, 12).Select(i => TestChunks.Make(i)).ToList();

    private static QuizRequest Request(params int[] cited) =>
        new("Scholarships and Fellowship Grants", cited.Select(i => TestChunks.Make(i).Id).ToList(), 2025);

    private static RetrievalResult Neighbours(params int[] chunks) =>
        new(8, chunks.Select(i => new RetrievedPassage(TestChunks.Make(i), 0.6)).ToList(), 0.6, true);

    /// <summary>A question whose quote is the first sentence of <see cref="TestChunks.Make"/>'s text.</summary>
    private static DraftQuizItem Good(Chunk c) =>
        new($"Question about {c.Id}?", ["A", "B", "C", "D"], 2, "Because the passage says so.", $"Passage {c.PageStart} text.");

    private static QuizService MakeService(IRetriever retriever, IQuizWriter writer) =>
        new(new FakeStore(Stored), retriever, writer, NullLogger<QuizService>.Instance);

    [Fact]
    public async Task FiveCitedChunks_GiveFiveItems_InCitedOrder_WithoutRetrieval()
    {
        var writer = new ScriptedWriter(Good);
        var retriever = new CountingRetriever(Neighbours(11));

        var response = await MakeService(retriever, writer).CreateAsync(Request(3, 1, 4, 2, 5));

        Assert.Equal([3, 1, 4, 2, 5], response.Items.Select(i => i.Source.Page));
        Assert.All(response.Items, i => Assert.Equal(i.Source.ChunkId, i.ChunkId));
        var first = response.Items[0];
        Assert.Equal((2, "Passage 3 text.", 1), (first.CorrectIndex, first.Quote, first.Source.Number));
        Assert.Equal(0, retriever.Calls);
        Assert.Equal(5, writer.Calls);
        Assert.Equal(AskResponses.Disclaimer, response.Disclaimer);
    }

    [Fact]
    public async Task FewerThanFiveCited_FillsFromTheTopic_SkippingDuplicates()
    {
        var retriever = new CountingRetriever(Neighbours(2, 9, 10, 11));

        var response = await MakeService(retriever, new ScriptedWriter(Good)).CreateAsync(Request(1, 2));

        Assert.Equal([1, 2, 9, 10, 11], response.Items.Select(i => i.Source.Page));
        Assert.Equal(("Scholarships and Fellowship Grants", 2025), retriever.LastQuery);
    }

    [Fact]
    public async Task MoreThanCountCited_UsesTheFirstOnes_AndUnknownIdsAreSkipped()
    {
        var request = Request(1, 2, 3, 4, 5, 6, 7) with { ChunkIds = ["missing", .. Request(1, 2, 3, 4, 5, 6, 7).ChunkIds] };

        var response = await MakeService(new CountingRetriever(Neighbours()), new ScriptedWriter(Good)).CreateAsync(request);

        Assert.Equal([1, 2, 3, 4, 5], response.Items.Select(i => i.Source.Page));
    }

    [Fact]
    public async Task FailingItem_IsRegeneratedOnce_WithTheFailedCheck()
    {
        var writer = new ScriptedWriter((c, attempt) => attempt == 1 && c.PageStart == 2
            ? Good(c) with { Quote = "Not in the passage." }
            : Good(c));

        var response = await MakeService(new CountingRetriever(Neighbours()), writer).CreateAsync(Request(1, 2, 3, 4, 5));

        Assert.Equal(5, response.Items.Count);
        Assert.Equal(6, writer.Calls);
        Assert.Equal(QuizCheck.QuoteNotFound, Assert.Single(writer.Retries).FailedCheck);
    }

    [Fact]
    public async Task ItemFailingTwice_IsDropped_AndUnreadableOrRefusedItemsToo()
    {
        var writer = new ScriptedWriter((c, _) => c.PageStart switch
        {
            1 => Good(c) with { Options = ["Same", "same", "Other", "Last"] },
            2 => null,          // unreadable JSON: retried, then dropped
            3 => Refuse,        // model refusal: not retried
            _ => Good(c),
        });

        var response = await MakeService(new CountingRetriever(Neighbours()), writer).CreateAsync(Request(1, 2, 3, 4, 5));

        Assert.Equal([4, 5], response.Items.Select(i => i.Source.Page));
        Assert.Equal(2 + 2 + 1 + 1 + 1, writer.Calls);
    }

    [Fact]
    public async Task AtMostFiveCallsRunAtOnce()
    {
        var writer = new ScriptedWriter(Good) { Delay = TimeSpan.FromMilliseconds(40) };

        var response = await MakeService(new CountingRetriever(Neighbours()), writer)
            .CreateAsync(Request(1, 2, 3, 4, 5, 6, 7, 8) with { Count = 8 });

        Assert.Equal(8, response.Items.Count);
        Assert.Equal(QuizService.MaxParallelCalls, writer.MaxConcurrent);
    }

    [Fact]
    public async Task ApiFailure_FailsTheQuiz()
    {
        var writer = new ScriptedWriter((c, _) => c.PageStart == 3
            ? throw new AnswererException(AnswererFailure.RateLimited, "busy")
            : Good(c));

        var e = await Assert.ThrowsAsync<AnswererException>(() =>
            MakeService(new CountingRetriever(Neighbours()), writer).CreateAsync(Request(1, 2, 3, 4, 5)));
        Assert.Equal(AnswererFailure.RateLimited, e.Failure);
    }

    // Sentinel for "the model refused" in a script.
    private static readonly DraftQuizItem Refuse = new("refuse", [], 0, "", "");

    private sealed class ScriptedWriter(Func<Chunk, int, DraftQuizItem?> script) : IQuizWriter
    {
        private int _calls, _running, _maxConcurrent;

        public ScriptedWriter(Func<Chunk, DraftQuizItem> script) : this((c, _) => script(c)) { }

        public TimeSpan Delay { get; init; }
        public int Calls => _calls;
        public int MaxConcurrent => _maxConcurrent;
        public List<(string ChunkId, string FailedCheck)> Retries { get; } = [];

        public async Task<QuizWriterResult> WriteAsync(string topic, Chunk passage, bool plainLanguage, string? failedCheck = null,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var running = Interlocked.Increment(ref _running);
            lock (Retries)
            {
                _maxConcurrent = Math.Max(_maxConcurrent, running);
                if (failedCheck is not null) Retries.Add((passage.Id, failedCheck));
            }
            try
            {
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
                var draft = script(passage, failedCheck is null ? 1 : 2);
                var call = new ModelCallInfo("fake", 1, 10, 10, 0, ReferenceEquals(draft, Refuse) ? "refusal" : "end_turn");
                return ReferenceEquals(draft, Refuse)
                    ? new QuizWriterResult(null, true, false, call)
                    : new QuizWriterResult(draft, false, draft is null, call);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private sealed class CountingRetriever(RetrievalResult result) : IRetriever
    {
        public int Calls { get; private set; }
        public (string Query, int TaxYear)? LastQuery { get; private set; }

        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default)
        {
            Calls++;
            LastQuery = (question, taxYear);
            return Task.FromResult(result);
        }
    }
}
