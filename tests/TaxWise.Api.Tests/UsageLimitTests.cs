using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Api.Tests;

/// <summary>The per-IP "UsageLimit" on the endpoints that call Claude.</summary>
public sealed class UsageLimitTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Factory(int permitLimit) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("UsageLimit:PermitLimit", permitLimit.ToString());
        // No passages, so /api/ask answers "not found" without calling Claude.
        b.ConfigureTestServices(s => s.AddSingleton<IRetriever>(new EmptyRetriever()));
    });

    [Fact]
    public async Task Ask_Returns429WithDetail_OnceTheLimitIsUsed()
    {
        using var client = Factory(permitLimit: 2).CreateClient();
        var request = new AskRequest("Is my scholarship taxable?", false, 2025);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ask", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ask", request)).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/ask", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("question limit", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_IsNeverLimited()
    {
        using var client = Factory(permitLimit: 1).CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task NoLimit_WhenPermitLimitIsZero()
    {
        using var client = Factory(permitLimit: 0).CreateClient();
        var request = new AskRequest("Is my scholarship taxable?", false, 2025);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ask", request)).StatusCode);
    }

    private sealed class EmptyRetriever : IRetriever
    {
        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
        string jurisdiction = TaxWise.Shared.Jurisdictions.Federal, CancellationToken ct = default) => Task.FromResult(new RetrievalResult(0, [], 0, false));
    }
}
