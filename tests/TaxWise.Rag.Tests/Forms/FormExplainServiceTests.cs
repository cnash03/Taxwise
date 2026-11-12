using Microsoft.Extensions.Logging.Abstractions;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Forms;
using TaxWise.Rag.Tests.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Forms;

public sealed class FormExplainServiceTests
{
    private static readonly FormBox Box1 = new("1", "Wages, tips, other compensation", "$28,000.00", "Your pay.",
        "Form 1040, line 1a", "Form W-2 box 1 wages", ["iw2", "i1040"]);

    private static readonly FormSample W2 = new("w2", "W-2", "Wage and Tax Statement", 2025, "c · Employer name", "Sample Employer Inc.", [Box1]);

    private static RetrievalResult Kept(double score) =>
        new(8, Enumerable.Range(1, 4).Select(i => new RetrievedPassage(TestChunks.Make(i), score)).ToList(), score, score >= 0.35);

    private static FormExplainService MakeService(IRetriever retriever, IFormExplainer explainer) =>
        new(retriever, explainer, NullLogger<FormExplainService>.Instance);

    [Fact]
    public async Task BelowThreshold_RefusesWithoutCallingClaude_ButStillSaysWhereItGoes()
    {
        var explainer = new FakeExplainer(Explained("x", ("P1", "Passage 1 text.")));
        var service = MakeService(new RecordingRetriever(Kept(0.1)), explainer);

        var response = await service.ExplainAsync(W2, Box1, plainLanguage: false);

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Equal(0, explainer.Calls);
        Assert.Equal("Form 1040, line 1a", response.GoesTo);
        Assert.StartsWith("I don't know.", response.Explanation);
        Assert.Empty(response.Citations);
        Assert.NotEmpty(response.Links);
    }

    [Fact]
    public async Task Answer_SearchesTheBoxQueryInItsSourceDocs_AndValidatesCitations()
    {
        var retriever = new RecordingRetriever(Kept(0.8));
        var explainer = new FakeExplainer(Explained("Box 1 is your taxable pay.", ("P2", "Passage 2 text."), ("P9", "nope"), ("P3", "not in it")));
        var service = MakeService(retriever, explainer);

        var response = await service.ExplainAsync(W2, Box1, plainLanguage: true);

        Assert.Equal(("Form W-2 box 1 wages", 2025), (retriever.Last.Query, retriever.Last.TaxYear));
        Assert.Equal(["iw2", "i1040"], retriever.Last.DocIds!);
        Assert.True(explainer.LastPlain);
        Assert.Equal(AskResponse.AnswerKind, response.Kind);
        Assert.Equal("Box 1 is your taxable pay.", response.Explanation);
        Assert.Equal([2, 3], response.Citations.Select(c => c.Number));
        Assert.Equal("Passage 2 text.", response.Citations[0].Highlight);
        Assert.Null(response.Citations[1].Highlight);
        Assert.Equal(TestChunks.Make(2).Id, response.Citations[0].ChunkId);
        Assert.Equal(4, response.Sources.Count);
        Assert.Equal("api/sources/p970/pdf", response.Sources[0].PdfUrl);
        Assert.Equal(AskResponses.Disclaimer, response.Disclaimer);
    }

    [Fact]
    public async Task GoesTo_AlwaysComesFromTheFormData()
    {
        var explainer = new FakeExplainer(Explained("It goes on Schedule C, line 99.", ("P1", "Passage 1 text.")));
        var response = await MakeService(new RecordingRetriever(Kept(0.8)), explainer).ExplainAsync(W2, Box1, false);

        Assert.Equal("Form 1040, line 1a", response.GoesTo);
    }

    [Theory]
    [InlineData(DraftAnswer.InsufficientSources)]
    [InlineData("no_valid_citations")]
    public async Task InsufficientSourcesOrNoValidCitation_IsARefusal(string status)
    {
        var draft = status == DraftAnswer.InsufficientSources
            ? new DraftExplanation(DraftAnswer.InsufficientSources, "", []).ToDraftAnswer()
            : Explained("Uncited claim.", ("P7", "x"));
        var response = await MakeService(new RecordingRetriever(Kept(0.8)), new FakeExplainer(draft)).ExplainAsync(W2, Box1, false);

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Empty(response.Sources);
    }

    [Fact]
    public async Task ModelRefusal_ReturnsCantHelp()
    {
        var response = await MakeService(new RecordingRetriever(Kept(0.8)), new FakeExplainer(null, refuse: true)).ExplainAsync(W2, Box1, false);

        Assert.Equal(AskResponse.RefusalKind, response.Kind);
        Assert.Equal(AskResponses.CantHelpText, response.Explanation);
    }

    [Fact]
    public async Task RepeatRequests_AreCachedPerFormBoxAndMode()
    {
        var retriever = new RecordingRetriever(Kept(0.8));
        var explainer = new FakeExplainer(Explained("Taxable pay.", ("P1", "Passage 1 text.")));
        var service = MakeService(retriever, explainer);

        var first = await service.ExplainAsync(W2, Box1, false);
        var again = await service.ExplainAsync(W2 with { FormId = "W2" }, Box1, false);
        await service.ExplainAsync(W2, Box1, true);

        Assert.Same(first, again);
        Assert.Equal(2, explainer.Calls);
        Assert.Equal(2, retriever.Calls);
    }

    [Fact]
    public async Task Failures_AreNotCached()
    {
        var explainer = new FakeExplainer(Explained("Taxable pay.", ("P1", "Passage 1 text."))) { Throw = true };
        var service = MakeService(new RecordingRetriever(Kept(0.8)), explainer);

        await Assert.ThrowsAsync<AnswererException>(() => service.ExplainAsync(W2, Box1, false));
        explainer.Throw = false;
        var response = await service.ExplainAsync(W2, Box1, false);

        Assert.Equal(AskResponse.AnswerKind, response.Kind);
        Assert.Equal(2, explainer.Calls);
    }

    [Fact]
    public void DraftExplanation_BecomesOneParagraph_OrNoneWhenEmpty()
    {
        var one = new DraftExplanation("answered", "  Text.  ", [new DraftCitation("P1", "q")]).ToDraftAnswer();
        var none = new DraftExplanation("answered", " ", null).ToDraftAnswer();

        Assert.Equal("Text.", Assert.Single(one.Paragraphs).Text);
        Assert.Empty(none.Paragraphs);
    }

    private static DraftAnswer Explained(string text, params (string Passage, string Quote)[] citations) =>
        new DraftExplanation(DraftAnswer.Answered, text, citations.Select(c => new DraftCitation(c.Passage, c.Quote)).ToList()).ToDraftAnswer();

    private sealed class RecordingRetriever(RetrievalResult result) : IRetriever
    {
        public int Calls { get; private set; }
        public (string Query, int TaxYear, IReadOnlyCollection<string>? DocIds) Last { get; private set; }

        public Task<RetrievalResult> RetrieveAsync(string question, int taxYear, IReadOnlyCollection<string>? docIds = null,
            CancellationToken ct = default)
        {
            Calls++;
            Last = (question, taxYear, docIds);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeExplainer(DraftAnswer? draft, bool refuse = false) : IFormExplainer
    {
        public int Calls { get; private set; }
        public bool LastPlain { get; private set; }
        public bool Throw { get; set; }

        public Task<AnswererResult> ExplainAsync(FormSample form, FormBox box, IReadOnlyList<Chunk> passages, bool plainLanguage,
            CancellationToken ct = default)
        {
            Calls++;
            LastPlain = plainLanguage;
            if (Throw) throw new AnswererException(AnswererFailure.Unavailable, "safe");
            var call = new ModelCallInfo("fake", 1, 10, 10, 0, refuse ? "refusal" : "end_turn");
            return Task.FromResult(new AnswererResult(refuse ? null : draft, refuse, call));
        }
    }
}
