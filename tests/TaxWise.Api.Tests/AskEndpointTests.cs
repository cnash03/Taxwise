using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>/api/ask, /api/sources and /api/knowledge-base with fake retriever/answerer and a temp sources folder.</summary>
public sealed class AskEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _sourcesDir = Path.Combine(Path.GetTempPath(), "taxwise-api-tests-" + Guid.NewGuid().ToString("N"));
    private readonly StubAnswerer _answerer = new();
    private RetrievalResult _retrieval = new(0, [], 0, false);

    public AskEndpointTests(WebApplicationFactory<Program> factory)
    {
        Directory.CreateDirectory(Path.Combine(_sourcesDir, "pdf"));
        File.WriteAllText(Path.Combine(_sourcesDir, "manifest.json"), """
            { "documents": [
              { "docId": "p970", "title": "Pub 970, Tax Benefits for Education", "url": "https://example.test/p970.pdf", "taxYear": 2025, "kind": "publication" },
              { "docId": "rp-25-32", "title": "Rev. Proc. 2025-32", "url": "https://example.test/rp.pdf", "taxYear": 2026, "kind": "rev-proc",
                "revProcSections": [ { "number": 4, "taxYear": 2026 }, { "number": 3, "taxYear": 2025 } ] }
            ] }
            """);
        File.WriteAllBytes(Path.Combine(_sourcesDir, "pdf", "p970.pdf"), "%PDF-1.4 fake"u8.ToArray());

        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Sources:Directory", _sourcesDir);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IRetriever>(new LambdaRetriever(() => _retrieval));
                s.AddSingleton<IAnswerer>(_answerer);
            });
        });
    }

    public void Dispose() => Directory.Delete(_sourcesDir, recursive: true);

    private static Chunk Chunk(int i) =>
        new($"p970-ty2025-p{i}-001", "p970", "Pub 970, Tax Benefits for Education", 2025, "Chapter 1. Scholarships", "Tax-Free Scholarships",
            i, i, $"Sentence {i}. More.");

    [Fact]
    public async Task Ask_ReturnsAnswerWithAbsolutePdfUrlsAndCitationsInRange()
    {
        _retrieval = new RetrievalResult(8, Enumerable.Range(1, 4).Select(i => new RetrievedPassage(Chunk(i), 0.8)).ToList(), 0.8, true);
        _answerer.Draft = new DraftAnswer(DraftAnswer.Answered, [new("Yes, partly.", [new("P1", "Sentence 1."), new("P6", "x")])]);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest("Is my scholarship taxable?", false, 2025));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AskResponse>())!;
        Assert.Equal("answer", body.Kind);
        Assert.Equal(1, _answerer.Calls);
        Assert.All(body.Paragraphs.SelectMany(p => p.Citations), c => Assert.InRange(c.Number, 1, 4));
        Assert.Equal("Sentence 1.", Assert.Single(body.Paragraphs[0].Citations).Highlight);
        Assert.Equal("http://localhost/api/sources/p970/pdf", body.Sources[0].PdfUrl);
    }

    [Fact]
    public async Task Ask_BelowThreshold_ReturnsRefusalWithoutCallingClaude()
    {
        _retrieval = new RetrievalResult(8, [new RetrievedPassage(Chunk(1), 0.1)], 0.1, false);
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/ask", new AskRequest("How do I stake crypto for yield?", false, 2025)))
            .Content.ReadFromJsonAsync<AskResponse>();

        Assert.Equal("refusal", body!.Kind);
        Assert.Equal(0, _answerer.Calls);
    }

    [Theory]
    [InlineData("", 2025)]
    [InlineData("Is my scholarship taxable?", 2019)]
    public async Task Ask_InvalidRequest_Returns400(string question, int year)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest(question, false, year));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _answerer.Calls);
    }

    [Theory]
    [InlineData(AnswererFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(AnswererFailure.Unavailable, HttpStatusCode.BadGateway)]
    public async Task Ask_AnswererFailure_MapsToStatus(AnswererFailure failure, HttpStatusCode expected)
    {
        _retrieval = new RetrievalResult(1, [new RetrievedPassage(Chunk(1), 0.9)], 0.9, true);
        _answerer.Failure = failure;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest("q", false, 2025));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SourcePdf_StreamsKnownDoc_And404sOtherwise()
    {
        using var client = _factory.CreateClient();

        var ok = await client.GetAsync("/api/sources/p970/pdf");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sources/rp-25-32/pdf")).StatusCode); // in manifest, not downloaded
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sources/..%2Fmanifest/pdf")).StatusCode);
    }

    [Fact]
    public async Task KnowledgeBase_ListsDocsWithTheirTaxYears()
    {
        using var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<KnowledgeBaseResponse>("/api/knowledge-base");

        Assert.Equal(["p970", "rp-25-32"], body!.Documents.Select(d => d.DocId));
        Assert.Equal([2026, 2025], body.Documents[1].TaxYears);
        Assert.Equal([2025, 2026, 2027], body.SupportedTaxYears);
        Assert.Equal([2025], body.FullKnowledgeBaseYears);
    }

    private sealed class LambdaRetriever(Func<RetrievalResult> result) : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
        CancellationToken ct = default) => Task.FromResult(result());
    }

    private sealed class StubAnswerer : IAnswerer
    {
        public DraftAnswer? Draft { get; set; }
        public AnswererFailure? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage, CancellationToken ct = default)
        {
            Calls++;
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            return Task.FromResult(new AnswererResult(Draft, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
