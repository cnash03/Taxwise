using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Quiz;
using TaxWise.Rag.VectorStore;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>/api/quiz with a fake chunk store, retriever and quiz writer.</summary>
public sealed class QuizEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IReadOnlyList<Chunk> Stored = Enumerable.Range(1, 5)
        .Select(i => new Chunk($"p970-ty2025-p{i}-001", "p970", "Pub 970, Tax Benefits for Education", 2025,
            "Chapter 1. Scholarships, Fellowship Grants, Grants, and Tuition Reductions", "Scholarships", i, i, $"Sentence {i}. More."))
        .ToList();

    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubWriter _writer = new();

    public QuizEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IChunkStore>(new StubStore());
            s.AddSingleton<IRetriever>(new EmptyRetriever());
            s.AddSingleton<IQuizWriter>(_writer);
        }));
    }

    [Fact]
    public async Task Quiz_ReturnsValidatedItems_WithAbsolutePdfUrls()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Quiz,
            new QuizRequest("Scholarships", Stored.Select(c => c.Id).ToList(), 2025));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<QuizResponse>())!;
        Assert.Equal(5, body.Items.Count);
        var item = body.Items[0];
        Assert.Equal((Stored[0].Id, 1, "Sentence 1."), (item.ChunkId, item.CorrectIndex, item.Quote));
        Assert.Equal(4, item.Options.Count);
        Assert.Equal("http://localhost/api/sources/p970/pdf", item.Source.PdfUrl);
        Assert.Equal(2025, item.Source.TaxYear);
    }

    [Fact]
    public async Task Quiz_RateLimited_Returns429()
    {
        _writer.Failure = AnswererFailure.RateLimited;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Quiz, new QuizRequest("Scholarships", [Stored[0].Id], 2025, Count: 1));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    public static TheoryData<QuizRequest> InvalidRequests => new()
    {
        new QuizRequest(" ", ["a"], 2025),
        new QuizRequest(new string('x', QuizLimits.MaxTopicLength + 1), ["a"], 2025),
        new QuizRequest("Scholarships", Enumerable.Range(0, QuizLimits.MaxChunkIds + 1).Select(i => $"id{i}").ToList(), 2025),
        new QuizRequest("Scholarships", ["a"], 2025, Count: 0),
        new QuizRequest("Scholarships", ["a"], 2025, Count: QuizLimits.MaxCount + 1),
        new QuizRequest("Scholarships", ["a"], 2024),
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Quiz_InvalidRequest_Returns400_WithoutCallingClaude(QuizRequest request)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Quiz, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _writer.Calls);
    }

    private sealed class StubStore : IChunkStore
    {
        public Task<IReadOnlyList<Chunk>> GetAsync(IReadOnlyList<string> chunkIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Chunk>>(Stored.Where(c => chunkIds.Contains(c.Id)).ToList());

        public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int taxYear, int limit,
            IReadOnlyCollection<string>? docIds = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnsureCollectionAsync(bool recreate, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertAsync(IReadOnlyList<Chunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string docId, int taxYear, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ulong> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class EmptyRetriever : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default) => Task.FromResult(new RetrievalResult(0, [], 0, false));
    }

    private sealed class StubWriter : IQuizWriter
    {
        private int _calls;
        public AnswererFailure? Failure { get; set; }
        public int Calls => _calls;

        public Task<QuizWriterResult> WriteAsync(string topic, Chunk passage, bool plainLanguage, string? failedCheck = null,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            var draft = new DraftQuizItem("Which is right?", ["W", "X", "Y", "Z"], 1, "The passage says so.", $"Sentence {passage.PageStart}.");
            return Task.FromResult(new QuizWriterResult(draft, false, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
