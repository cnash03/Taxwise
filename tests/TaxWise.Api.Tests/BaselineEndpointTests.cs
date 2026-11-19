using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>/api/ask/baseline (the "plain chatbot" side of the comparison) with a fake baseline answerer.</summary>
public sealed class BaselineEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubBaseline _baseline = new();
    private readonly ThrowingRetriever _retriever = new();

    public BaselineEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IBaselineAnswerer>(_baseline);
            s.AddSingleton<IRetriever>(_retriever);
        }));

    [Fact]
    public async Task Baseline_ReturnsLabelledAnswerAndDisclaimer_WithoutRetrieval()
    {
        _baseline.Answer = "  Yes, the housing part is taxable.  ";
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest("Is my scholarship taxable?", true, 2027));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<BaselineResponse>())!;
        Assert.Equal(BaselineResponse.AnswerKind, body.Kind);
        Assert.Equal("Yes, the housing part is taxable.", body.Text);
        Assert.Equal("Plain chatbot — no sources, may be wrong or out of date", body.Label);
        Assert.Equal(AskResponses.Disclaimer, body.Disclaimer);
        Assert.Equal([("Is my scholarship taxable?", 2027)], _baseline.Calls);
        Assert.Equal(0, _retriever.Calls);
    }

    [Fact]
    public async Task Baseline_ModelRefusal_ReturnsRefusalKind()
    {
        _baseline.Refuse = true;
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest("q", false, 2025)))
            .Content.ReadFromJsonAsync<BaselineResponse>();

        Assert.Equal(BaselineResponse.RefusalKind, body!.Kind);
        Assert.Equal(AskResponses.CantHelpText, body.Text);
        Assert.Equal(BaselineResponse.PlainChatbotLabel, body.Label);
    }

    [Theory]
    [InlineData("", 2025)]
    [InlineData("Is my scholarship taxable?", 2019)]
    public async Task Baseline_InvalidRequest_Returns400WithoutCallingClaude(string question, int year)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest(question, false, year));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_baseline.Calls);
    }

    [Fact]
    public async Task Baseline_TooLongQuestion_Returns400()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest(new string('a', AskLimits.MaxQuestionLength + 1), false, 2025));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_baseline.Calls);
    }

    [Theory]
    [InlineData(AnswererFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(AnswererFailure.Unavailable, HttpStatusCode.BadGateway)]
    public async Task Baseline_AnswererFailure_MapsToStatus(AnswererFailure failure, HttpStatusCode expected)
    {
        _baseline.Failure = failure;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/ask/baseline", new AskRequest("q", false, 2025));

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains("safe message", await response.Content.ReadAsStringAsync());
    }

    private sealed class StubBaseline : IBaselineAnswerer
    {
        public string Answer { get; set; } = "An answer.";
        public bool Refuse { get; set; }
        public AnswererFailure? Failure { get; set; }
        public List<(string Question, int TaxYear)> Calls { get; } = [];

        public Task<BaselineResult> AnswerAsync(string question, int taxYear, CancellationToken ct = default)
        {
            Calls.Add((question, taxYear));
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            return Task.FromResult(new BaselineResult(Refuse ? null : Answer, Refuse, new ModelCallInfo("stub", 1, 1, 1, 0, Refuse ? "refusal" : "end_turn")));
        }
    }

    // The baseline must never search the knowledge base.
    private sealed class ThrowingRetriever : IRetriever
    {
        public int Calls { get; private set; }

        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            string jurisdiction = TaxWise.Shared.Jurisdictions.Federal, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("The baseline must not retrieve.");
        }
    }
}
