using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Forms;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>/api/forms/{form} and /api/forms/explain with a fake retriever/explainer and a temp forms folder.</summary>
public sealed class FormsEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _formsDir = Path.Combine(Path.GetTempPath(), "taxwise-forms-tests-" + Guid.NewGuid().ToString("N"));
    private readonly StubExplainer _explainer = new();
    private RetrievalResult _retrieval = new(0, [], 0, false);

    public FormsEndpointTests(WebApplicationFactory<Program> factory)
    {
        Directory.CreateDirectory(_formsDir);
        File.WriteAllText(Path.Combine(_formsDir, "w2-sample.json"), """
            { "formId": "w2", "form": "W-2", "title": "Wage and Tax Statement", "taxYear": 2025,
              "issuerLabel": "c · Employer name", "issuer": "Sample Employer Inc.",
              "boxes": [ { "id": "1", "label": "Wages, tips, other compensation", "value": "$28,000.00", "summary": "Your pay.",
                           "goesTo": "Form 1040, line 1a", "retrievalQuery": "W-2 box 1", "sourceDocs": ["iw2"] } ] }
            """);

        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Forms:Directory", _formsDir);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IRetriever>(new LambdaRetriever(() => _retrieval));
                s.AddSingleton<IFormExplainer>(_explainer);
            });
        });
    }

    public void Dispose() => Directory.Delete(_formsDir, recursive: true);

    private static Chunk Chunk(int i) =>
        new($"iw2-ty2025-p{i}-001", "iw2", "General Instructions for Forms W-2 and W-3", 2025, null, "Box 1—Wages, tips, other compensation",
            i, i, $"Sentence {i}. More.");

    [Fact]
    public async Task GetForm_ReturnsTheSample_And404sUnknownForms()
    {
        using var client = _factory.CreateClient();

        var form = await client.GetFromJsonAsync<FormSample>("/api/forms/W2");
        Assert.Equal("W-2", form!.Form);
        Assert.Equal("Form 1040, line 1a", Assert.Single(form.Boxes).GoesTo);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/forms/1099-nec")).StatusCode);
    }

    [Fact]
    public async Task Explain_ReturnsCitedExplanation_WithGoesToFromData_AndAbsolutePdfUrls()
    {
        _retrieval = new RetrievalResult(8, Enumerable.Range(1, 4).Select(i => new RetrievedPassage(Chunk(i), 0.8)).ToList(), 0.8, true);
        _explainer.Draft = new DraftExplanation("answered", "Your taxable pay.", [new("P1", "Sentence 1."), new("P5", "x")]).ToDraftAnswer();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.ExplainForm, new ExplainFormRequest("w2", "1", false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ExplainFormResponse>())!;
        Assert.Equal("answer", body.Kind);
        Assert.Equal("Form 1040, line 1a", body.GoesTo);
        Assert.Equal("Sentence 1.", Assert.Single(body.Citations).Highlight);
        Assert.Equal("http://localhost/api/sources/iw2/pdf", body.Sources[0].PdfUrl);
    }

    [Fact]
    public async Task Explain_BelowThreshold_ReturnsRefusalWithoutCallingClaude()
    {
        _retrieval = new RetrievalResult(8, [new RetrievedPassage(Chunk(1), 0.1)], 0.1, false);
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/" + ApiRoutes.ExplainForm, new ExplainFormRequest("w2", "1", true)))
            .Content.ReadFromJsonAsync<ExplainFormResponse>();

        Assert.Equal("refusal", body!.Kind);
        Assert.Equal("Form 1040, line 1a", body.GoesTo);
        Assert.Equal(0, _explainer.Calls);
    }

    [Theory]
    [InlineData("w2", "", HttpStatusCode.BadRequest)]
    [InlineData("1099-nec", "1", HttpStatusCode.NotFound)]
    [InlineData("w2", "99", HttpStatusCode.NotFound)]
    public async Task Explain_InvalidRequest_ReturnsProblem(string form, string box, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.ExplainForm, new ExplainFormRequest(form, box, false));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, _explainer.Calls);
    }

    [Theory]
    [InlineData(AnswererFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(AnswererFailure.Unavailable, HttpStatusCode.BadGateway)]
    public async Task Explain_ExplainerFailure_MapsToStatus(AnswererFailure failure, HttpStatusCode expected)
    {
        _retrieval = new RetrievalResult(1, [new RetrievedPassage(Chunk(1), 0.9)], 0.9, true);
        _explainer.Failure = failure;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.ExplainForm, new ExplainFormRequest("w2", "1", false));

        Assert.Equal(expected, response.StatusCode);
    }

    private sealed class LambdaRetriever(Func<RetrievalResult> result) : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default) => Task.FromResult(result());
    }

    private sealed class StubExplainer : IFormExplainer
    {
        public DraftAnswer? Draft { get; set; }
        public AnswererFailure? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<AnswererResult> ExplainAsync(FormSample form, FormBox box, IReadOnlyList<Chunk> passages, bool plainLanguage,
            CancellationToken ct = default)
        {
            Calls++;
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            return Task.FromResult(new AnswererResult(Draft, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
