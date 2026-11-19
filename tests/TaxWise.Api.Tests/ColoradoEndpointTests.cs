using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>The jurisdiction contract of /api/ask, /api/ask/baseline and /api/knowledge-base (DECISIONS #23).</summary>
public sealed class ColoradoEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _sourcesDir = Path.Combine(Path.GetTempPath(), "taxwise-co-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingRetriever _retriever = new();
    private readonly RecordingAnswerer _answerer = new();
    private readonly CountingBaseline _baseline = new();

    public ColoradoEndpointTests(WebApplicationFactory<Program> factory)
    {
        Directory.CreateDirectory(Path.Combine(_sourcesDir, "pdf"));
        File.WriteAllText(Path.Combine(_sourcesDir, "manifest.json"), """
            { "documents": [
              { "docId": "p970", "title": "Pub 970, Tax Benefits for Education", "url": "https://example.test/p970.pdf", "taxYear": 2025, "kind": "publication" },
              { "docId": "co-dr0104", "title": "DR 0104 Book, 2025 Colorado Individual Income Tax Filing Guide",
                "url": "https://tax.example.test/Book104_2025.pdf", "taxYear": 2025, "kind": "instructions", "jurisdiction": "CO" }
            ] }
            """);

        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Sources:Directory", _sourcesDir);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IRetriever>(_retriever);
                s.AddSingleton<IAnswerer>(_answerer);
                s.AddSingleton<IBaselineAnswerer>(_baseline);
            });
        });
    }

    public void Dispose() => Directory.Delete(_sourcesDir, recursive: true);

    private static Chunk CoChunk() =>
        new("co-dr0104-ty2025-p4-001", "co-dr0104", "DR 0104 Book, 2025 Colorado Individual Income Tax Filing Guide", 2025, null,
            "Who Must File This Tax Return", 4, 4, "You must file a Colorado return. More.", Jurisdiction: Jurisdictions.Colorado);

    [Fact]
    public async Task Ask_WithoutJurisdiction_SearchesFederal()
    {
        using var client = _factory.CreateClient();

        // The JSON an App built before Colorado was added sends.
        var response = await client.PostAsync("/api/ask",
            new StringContent("""{"question":"Is my scholarship taxable?","plainLanguage":false,"taxYear":2025}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Jurisdictions.Federal, _retriever.LastJurisdiction);
    }

    [Fact]
    public async Task Ask_Colorado_SearchesColorado_AndReturnsDorSourcesWithTheirJurisdictionAndTaxYear()
    {
        _retriever.Result = new RetrievalResult(8, [new RetrievedPassage(CoChunk(), 0.9)], 0.9, true);
        _answerer.Draft = new DraftAnswer(DraftAnswer.Answered, [new("Yes.", [new("P1", "You must file a Colorado return.")])]);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest("Do I file in Colorado?", false, 2025, "co"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AskResponse>())!;
        Assert.Equal(Jurisdictions.Colorado, _retriever.LastJurisdiction);
        Assert.Equal(Jurisdictions.Colorado, _answerer.LastJurisdiction);
        var source = Assert.Single(body.Sources);
        Assert.Equal(Jurisdictions.Colorado, source.Jurisdiction);
        Assert.Equal(2025, source.TaxYear);
        Assert.Equal("http://localhost/api/sources/co-dr0104/pdf", source.PdfUrl);
        Assert.Equal(AskResponses.Disclaimer, body.Disclaimer);
    }

    [Fact]
    public async Task Ask_ColoradoRefusal_HasColoradoLinks()
    {
        _retriever.Result = new RetrievalResult(8, [new RetrievedPassage(CoChunk(), 0.1)], 0.1, false);
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/ask", new AskRequest("How do I stake crypto?", false, 2025, Jurisdictions.Colorado)))
            .Content.ReadFromJsonAsync<AskResponse>();

        Assert.Equal(AskResponse.RefusalKind, body!.Kind);
        Assert.Equal(0, _answerer.Calls);
        Assert.Contains(body.Links, l => l.Url == "https://www.colorado.gov/revenueonline/");
    }

    [Fact]
    public async Task Ask_UnknownJurisdiction_Returns400WithoutSearching()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask", new AskRequest("q", false, 2025, "TX"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_retriever.LastJurisdiction);
    }

    [Fact]
    public async Task Baseline_Colorado_Returns400WithoutCallingTheModel()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest("q", false, 2025, Jurisdictions.Colorado));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _baseline.Calls);
    }

    [Fact]
    public async Task KnowledgeBase_TagsEachDocumentWithItsJurisdiction()
    {
        using var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<KnowledgeBaseResponse>("/api/knowledge-base");

        Assert.Equal([("p970", "US"), ("co-dr0104", "CO")], body!.Documents.Select(d => (d.DocId, d.Jurisdiction)));
    }

    [Fact]
    public async Task SourcePdf_RedirectsColoradoDocsToTheDor()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/sources/co-dr0104/pdf");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://tax.example.test/Book104_2025.pdf", response.Headers.Location?.ToString());
    }

    private sealed class RecordingRetriever : IRetriever
    {
        public RetrievalResult Result { get; set; } = new(0, [], 0, false);
        public string? LastJurisdiction { get; private set; }

        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            string jurisdiction = Jurisdictions.Federal, CancellationToken ct = default)
        {
            LastJurisdiction = jurisdiction;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingAnswerer : IAnswerer
    {
        public DraftAnswer? Draft { get; set; }
        public int Calls { get; private set; }
        public string? LastJurisdiction { get; private set; }

        public Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage,
            string jurisdiction = Jurisdictions.Federal, CancellationToken ct = default)
        {
            Calls++;
            LastJurisdiction = jurisdiction;
            return Task.FromResult(new AnswererResult(Draft, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }

    private sealed class CountingBaseline : IBaselineAnswerer
    {
        public int Calls { get; private set; }

        public Task<BaselineResult> AnswerAsync(string question, int taxYear, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new BaselineResult("An answer.", false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
