using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Voyage;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Answering;

public sealed class AskPipelineTests
{
    private static readonly IOptions<RetrievalOptions> Retrieval =
        Options.Create(new RetrievalOptions { VectorTopK = 8, RerankTopK = 4, MinRerankScore = 0.35 });

    private static Retriever MakeRetriever(FakeVoyage voyage, FakeStore store) => new(voyage, store, Retrieval);

    private static AskService MakeService(IRetriever retriever, IAnswerer answerer) =>
        new(retriever, answerer, NullLogger<AskService>.Instance);

    [Fact]
    public async Task Retriever_EmbedsAsQuery_SearchesTheYear_AndKeepsTopFourBestFirst()
    {
        var chunks = Enumerable.Range(1, 8).Select(i => TestChunks.Make(i)).ToList();
        var voyage = new FakeVoyage(0.1, 0.9, 0.2, 0.8, 0.3, 0.7, 0.05, 0.6);
        var store = new FakeStore(chunks);

        var result = await MakeRetriever(voyage, store).RetrieveAsync("q", 2025);

        Assert.Equal([VoyageInputType.Query], voyage.EmbedCalls);
        Assert.Equal((2025, 8), Assert.Single(store.Searches));
        Assert.Equal(8, result.Searched);
        Assert.Equal([chunks[1].Id, chunks[3].Id, chunks[5].Id, chunks[7].Id], result.Passages.Select(p => p.Chunk.Id));
        Assert.True(result.MeetsThreshold);
        Assert.Equal(0.9, result.TopScore);
    }

    [Fact]
    public async Task Retriever_BelowMinRerankScore_DoesNotMeetThreshold()
    {
        var store = new FakeStore(Enumerable.Range(1, 8).Select(i => TestChunks.Make(i)).ToList());

        var result = await MakeRetriever(new FakeVoyage(0.2, 0.34, 0.1), store).RetrieveAsync("How do I stake crypto for yield?", 2025);

        Assert.False(result.MeetsThreshold);
    }

    [Fact]
    public async Task BelowThreshold_RefusesWithoutCallingClaude()
    {
        // Acceptance: an off-topic question is refused and Claude is never called.
        var store = new FakeStore(Enumerable.Range(1, 8).Select(i => TestChunks.Make(i)).ToList());
        var answerer = new FakeAnswerer(null);
        var service = MakeService(MakeRetriever(new FakeVoyage(0.2, 0.1), store), answerer);

        var response = await service.AskAsync(new AskRequest("How do I stake crypto for yield?", false, 2025));

        Assert.Equal(0, answerer.Calls);
        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Contains("tax year 2025", Assert.Single(response.Paragraphs).Text);
        Assert.Empty(Assert.Single(response.Paragraphs).Citations);
        Assert.Empty(response.Sources);
        Assert.NotEmpty(response.Links);
        Assert.Equal(AskResponses.Disclaimer, response.Disclaimer);
    }

    [Fact]
    public async Task NoHitsForTheYear_RefusesWithoutCallingClaude()
    {
        var answerer = new FakeAnswerer(null);
        var service = MakeService(MakeRetriever(new FakeVoyage(), new FakeStore([])), answerer);

        var response = await service.AskAsync(new AskRequest("Anything", false, 2027));

        Assert.Equal(0, answerer.Calls);
        Assert.Equal(AskResponse.RefusalKind, response.Kind);
    }

    [Fact]
    public async Task Answer_MapsPassageNumbersToChunks_AndDropsCitationsOutsideOneToFour()
    {
        var chunks = Enumerable.Range(1, 4).Select(i => TestChunks.Make(i, $"Sentence {i}. More text.")).ToList();
        var retrieval = new RetrievalResult(8, chunks.Select(c => new RetrievedPassage(c, 0.9)).ToList(), 0.9, true);
        var draft = new DraftAnswer(DraftAnswer.Answered,
        [
            new("First.", [new("P2", "Sentence 2."), new("P7", "x")]),
            new("Second.", [new("P4", "not there")]),
        ], ["Follow up?"]);

        var response = await MakeService(new FakeRetriever(retrieval), new FakeAnswerer(draft))
            .AskAsync(new AskRequest("Is my scholarship taxable?", false, 2025));

        Assert.Equal(AskResponse.AnswerKind, response.Kind);
        Assert.Equal((8, 4), (response.PassagesSearched, response.PassagesKept));
        Assert.Equal([1, 2, 3, 4], response.Sources.Select(s => s.Number));
        Assert.All(response.Paragraphs.SelectMany(p => p.Citations), c => Assert.InRange(c.Number, 1, 4));

        var first = Assert.Single(response.Paragraphs[0].Citations);
        Assert.Equal(new CitationRef(2, chunks[1].Id, "Sentence 2."), first);
        Assert.Null(Assert.Single(response.Paragraphs[1].Citations).Highlight);

        var source = response.Sources[1];
        Assert.Equal((chunks[1].Id, "p970", 2, 2025), (source.ChunkId, source.DocId, source.Page, source.TaxYear));
        Assert.Equal("api/sources/p970/pdf", source.PdfUrl);
        Assert.Equal(["Follow up?"], response.SuggestedFollowUps);
    }

    [Fact]
    public async Task ModelRefusal_ReturnsCantHelpRefusal()
    {
        var chunks = new[] { TestChunks.Make(1) };
        var retrieval = new RetrievalResult(1, [new RetrievedPassage(chunks[0], 0.9)], 0.9, true);

        var response = await MakeService(new FakeRetriever(retrieval), new FakeAnswerer(null, refuse: true))
            .AskAsync(new AskRequest("q", false, 2025));

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Equal(AskResponses.CantHelpText, Assert.Single(response.Paragraphs).Text);
    }

    [Fact]
    public async Task InsufficientSources_ReturnsNotFoundRefusal()
    {
        var retrieval = new RetrievalResult(1, [new RetrievedPassage(TestChunks.Make(1), 0.9)], 0.9, true);
        var draft = new DraftAnswer(DraftAnswer.InsufficientSources, []);

        var response = await MakeService(new FakeRetriever(retrieval), new FakeAnswerer(draft))
            .AskAsync(new AskRequest("q", false, 2026));

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Equal(AskResponses.NotFoundText(2026), Assert.Single(response.Paragraphs).Text);
    }
}
