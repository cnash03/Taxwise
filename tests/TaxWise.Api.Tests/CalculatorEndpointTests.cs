using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Core;
using TaxWise.Core.Calculator;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Calculator;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>/api/calculator and /api/calculator/compare with a fake retriever and explainer.</summary>
public sealed class CalculatorEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubExplainer _explainer = new();
    private RetrievalResult _retrieval = new(0, [], 0, false);

    public CalculatorEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IRetriever>(new LambdaRetriever(() => _retrieval));
            s.AddSingleton<ICalcExplainer>(_explainer);
        }));
    }

    private static Chunk Chunk(int i) =>
        new($"i1040-ty2025-p{i}-001", "i1040", "Form 1040 Instructions (incl. Tax Table)", 2025, null, "Line 16", i, i, $"Sentence {i}. More.");

    [Fact]
    public async Task Calculate_ReturnsStepsAndCitedExplanation_WithAbsolutePdfUrls_AndStringEnums()
    {
        _retrieval = new RetrievalResult(8, Enumerable.Range(1, 4).Select(i => new RetrievedPassage(Chunk(i), 0.8)).ToList(), 0.8, true);
        _explainer.Draft = new DraftExplanation("answered", "The Tax Table gives $1,235 on $12,250.", [new("P1", "Sentence 1.")]).ToDraftAnswer();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Calculator,
            new CalcRequest(2025, 28_000m, 1_500m, FilingStatus.Single, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"filingStatus\":\"Single\"", await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<CalcResponse>())!;
        Assert.Equal(CalcResponse.AnswerKind, body.ExplanationKind);
        Assert.Equal(1_235m, body.Tax);
        Assert.Equal(265m, body.RefundOrOwed);
        Assert.Equal("Tax (from IRS Tax Table)", body.Steps[3].Label);
        Assert.Equal("http://localhost/api/sources/i1040/pdf", body.Sources[0].PdfUrl);
        Assert.False(string.IsNullOrEmpty(body.NotIncluded));
    }

    [Fact]
    public async Task Calculate_AcceptsStatusAsString()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/" + ApiRoutes.Calculator, JsonContent.Create(new
        {
            taxYear = 2027, wages = 20_000, federalWithholding = 0, filingStatus = "HeadOfHousehold", canBeClaimedAsDependent = false,
        }));

        var body = (await response.Content.ReadFromJsonAsync<CalcResponse>())!;
        Assert.Equal(FilingStatus.HeadOfHousehold, body.FilingStatus);
        Assert.True(body.IsEstimate);
        Assert.Equal(CalcResponse.FallbackKind, body.ExplanationKind); // fake retrieval is below the threshold
        Assert.Equal(0, _explainer.Calls);
    }

    [Theory]
    [InlineData(2024, 28_000, 0)]
    [InlineData(2028, 28_000, 0)]
    [InlineData(2025, -1, 0)]
    [InlineData(2025, 28_000, -5)]
    [InlineData(2025, 20_000_000, 0)]
    public async Task Calculate_InvalidRequest_Returns400(int year, int wages, int withholding)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Calculator,
            new CalcRequest(year, wages, withholding, FilingStatus.Single, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Calculate_UnknownFilingStatus_Returns400()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/" + ApiRoutes.Calculator, JsonContent.Create(new
        {
            taxYear = 2025, wages = 1, federalWithholding = 0, filingStatus = 9, canBeClaimedAsDependent = false,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(AnswererFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(AnswererFailure.Unavailable, HttpStatusCode.BadGateway)]
    public async Task Calculate_ExplainerFailure_MapsToStatus(AnswererFailure failure, HttpStatusCode expected)
    {
        _retrieval = new RetrievalResult(1, [new RetrievedPassage(Chunk(1), 0.9)], 0.9, true);
        _explainer.Failure = failure;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/" + ApiRoutes.Calculator, new CalcRequest(2025, 28_000m, 0, FilingStatus.Single, false));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Compare_ReturnsEveryYear_WithoutClaude()
    {
        using var client = _factory.CreateClient();

        var body = await (await client.PostAsJsonAsync("/" + ApiRoutes.CompareYears,
            new CalcRequest(0, 28_000m, 1_500m, FilingStatus.Single, false))).Content.ReadFromJsonAsync<CompareYearsResponse>();

        Assert.Equal(TaxYearRegistry.Supported, body!.Years.Select(y => y.TaxYear));
        Assert.Equal(1_235m, body.Years[0].Tax);
        Assert.Equal(0, _explainer.Calls);
    }

    private sealed class LambdaRetriever(Func<RetrievalResult> result) : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default) => Task.FromResult(result());
    }

    private sealed class StubExplainer : ICalcExplainer
    {
        public DraftAnswer? Draft { get; set; }
        public AnswererFailure? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<AnswererResult> ExplainAsync(CalcResult result, IReadOnlyList<Chunk> passages, bool plainLanguage,
            IReadOnlyList<string>? rejectedAmounts = null, CancellationToken ct = default)
        {
            Calls++;
            if (Failure is { } f) throw new AnswererException(f, "safe message");
            return Task.FromResult(new AnswererResult(Draft, false, new ModelCallInfo("stub", 1, 1, 1, 0, "end_turn")));
        }
    }
}
